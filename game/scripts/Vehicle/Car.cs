using F1.Core.Data;
using F1.Core.Sim;
using F1.Game.Core;
using F1.Game.Track;
using Godot;

namespace F1.Game.Vehicle;

/// <summary>Per-wheel simulation state.</summary>
public struct Wheel
{
    public Vector3 Hardpoint;      // local, top of the suspension travel
    public bool Front, Left;
    public float Radius;
    public double Omega;           // rad/s, positive = rolling forwards
    public double Compression, PrevCompression;
    public double Fz, Fx, Fy;
    public double SlipRatio, SlipAngle;
    public double SteerAngle;
    public double SpinAngle;       // visual rotation
    public double CenterDistance;  // hardpoint → wheel centre, for visuals
    public double BrakeTorque, DriveTorque;
    public double SurfaceHint;
    public bool Grounded;
    public bool Locked;
    public SurfaceInfo Surface;
    public Vector3 Contact;
}

/// <summary>
/// A Formula 1 car simulated as a rigid body with four raycast wheels on the analytic track surface,
/// combined-slip tyres, aero, a hybrid power unit, gearbox, differential and brakes.
/// Forward is -Z in the car's local frame, X is right, Y is up.
/// </summary>
public partial class Car : RigidBody3D
{
    public const int FL = 0, FR = 1, RL = 2, RR = 3;
    public const float FrontAxleZ = -1.8f, RearAxleZ = 1.8f;
    public const float FrontTrackHalf = 0.80f, RearTrackHalf = 0.78f;
    public const float WheelRadius = 0.36f;
    public const float HardpointHeight = 0.47f;
    public const double RestLength = 0.12, MaxDroop = 0.15, BumpStop = 0.055;
    public const double MaxSteerAngle = 0.33;
    public const double DryMass = 798;
    public const double WheelInertia = 1.25;
    public const uint CollisionLayerCars = 1 << 2;

    // ---- identity --------------------------------------------------------------------------------
    public TeamData Team { get; private set; } = null!;
    public DriverData DriverInfo { get; private set; } = null!;
    public int Index { get; set; }
    public bool IsPlayer { get; set; }

    // ---- systems --------------------------------------------------------------------------------
    public TrackSurface Track { get; private set; } = null!;
    public PowerUnit PU { get; private set; } = null!;
    public Gearbox Box { get; } = new();
    public AeroModel Aero { get; } = new();
    public BrakeSystem Brakes { get; } = new();
    public TyreState[] Tyres { get; } = new TyreState[4];
    public CarSetup Setup { get; private set; } = new();
    public DrivingAssists Assists { get; set; } = new();
    public Wheel[] Wheels { get; } = new Wheel[4];
    public IDriver? Driver { get; set; }
    public CarInputs Inputs;
    public CarModel? Model { get; private set; }

    // ---- environment set by the session each tick ------------------------------------------------
    public float Slipstream { get; set; }
    public float DirtyAir { get; set; }
    public float Wetness { get; set; }
    public double AmbientC { get; set; } = 22;
    public double TrackTempC { get; set; } = 35;
    public double TyreWearMultiplier { get; set; } = 1.0;
    /// <summary>Race control allows the driver to open DRS (in a zone, within the gap, DRS enabled).</summary>
    public bool DrsAllowed { get; set; }
    /// <summary>Held on the grid / in the garage: brakes on, no movement.</summary>
    public bool Held { get; set; }
    public bool Retired { get; set; }

    // ---- telemetry ------------------------------------------------------------------------------
    public float SpeedMs { get; private set; }
    public float SpeedKmh => SpeedMs * Units.MsToKmh;
    public float Rpm { get; private set; } = (float)PowerUnit.IdleRpm;
    public int Gear => Box.Gear;
    public float ThrottleApplied { get; private set; }
    public float BrakeApplied { get; private set; }
    public float SteerApplied { get; private set; }
    public float ClutchEngagement { get; private set; } = 1;
    public bool DrsOpen { get; private set; }
    public bool PitLimiter { get; private set; }
    public double TrackS { get; private set; }
    public double TrackD { get; private set; }
    public bool InPitLane { get; private set; }
    /// <summary>Number of wheels outside the white lines (for track limits).</summary>
    public int WheelsOffTrack { get; private set; }
    public float LateralG { get; private set; }
    public float LongitudinalG { get; private set; }
    public double OdometerM { get; private set; }
    /// <summary>Diagnostics: drag force, total tyre longitudinal force, axle torque (N, N, N·m).</summary>
    public double DebugDrag, DebugFx, DebugAxle;

    private double _sHint = -1;
    private Vector3 _prevVelocity;
    private double _launchTimer;
    private double _pitLimiterKmh = 80;
    private double _fuelMassTimer;

    public void Configure(TrackSurface track, TeamData team, DriverData driver, CarSetup setup, TyreCompound compound, double fuelKg, double tyreTempC = 70)
    {
        Track = track;
        Team = team;
        DriverInfo = driver;
        Name = $"Car_{driver.Code}";
        Setup = setup.Clone();
        PU = new PowerUnit(fuelKg) { PowerFactor = team.Car.Power };
        for (int i = 0; i < 4; i++) Tyres[i] = new TyreState(compound, tyreTempC);
        _pitLimiterKmh = track.Data.PitLane.SpeedLimitKmh;
        ApplySetup();
    }

    public void ApplySetup()
    {
        Aero.ApplyWings(Setup.FrontWing, Setup.RearWing, Team.Car.Downforce, Team.Car.Drag);
        Brakes.Bias = Setup.BrakeBias / 100.0;
        for (int i = 0; i < 4; i++) Tyres[i].PressurePsi = i < 2 ? Setup.PressureFront : Setup.PressureRear;
    }

    public override void _Ready()
    {
        Mass = (float)(DryMass + PU.FuelKg);
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0, 0.28f, 0.18f);
        Inertia = new Vector3(1150, 1250, 280);
        ContinuousCd = true;
        CanSleep = false;
        // Replace (not combine with) the project's default damping: aero drag is modelled explicitly.
        LinearDampMode = DampMode.Replace;
        LinearDamp = 0;
        AngularDampMode = DampMode.Replace;
        AngularDamp = 0.05f;
        CollisionLayer = CollisionLayerCars;
        CollisionMask = NatureBuilderLayers.Ground | NatureBuilderLayers.Walls | CollisionLayerCars;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.4f, Bounce = 0.1f };
        ContactMonitor = true;
        MaxContactsReported = 4;

        var body = new CollisionShape3D { Name = "Chassis", Shape = new BoxShape3D { Size = new Vector3(1.6f, 0.62f, 4.9f) } };
        body.Position = new Vector3(0, 0.39f, 0.15f);
        AddChild(body);
        var wings = new CollisionShape3D { Name = "Wings", Shape = new BoxShape3D { Size = new Vector3(1.95f, 0.3f, 5.6f) } };
        wings.Position = new Vector3(0, 0.3f, 0.1f);
        AddChild(wings);

        Vector3[] hp =
        {
            new(-FrontTrackHalf, HardpointHeight, FrontAxleZ), new(FrontTrackHalf, HardpointHeight, FrontAxleZ),
            new(-RearTrackHalf, HardpointHeight, RearAxleZ), new(RearTrackHalf, HardpointHeight, RearAxleZ),
        };
        for (int i = 0; i < 4; i++)
            Wheels[i] = new Wheel { Hardpoint = hp[i], Front = i < 2, Left = i % 2 == 0, Radius = WheelRadius, SurfaceHint = -1, CenterDistance = RestLength };

        Model = new CarModel { Name = "Model" };
        AddChild(Model);
        Model.Build(Team, DriverInfo);
        _prevVelocity = LinearVelocity;
    }

    /// <summary>Teleports the car (e.g. to a grid slot) and resets its motion.</summary>
    public void PlaceAt(Transform3D t)
    {
        GlobalTransform = t;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        for (int i = 0; i < 4; i++) { Wheels[i].Omega = 0; Wheels[i].SurfaceHint = -1; Wheels[i].Compression = Wheels[i].PrevCompression = 0.008; }
        _sHint = -1;
        Box.SetGear(1);
        _prevVelocity = Vector3.Zero;
        PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.Transform, t);
    }

    /// <summary>
    /// Puts a beached/stuck car back on the track at its current distance, pointing the right way
    /// (AI recovery; the player's equivalent is the Time Trial reset).
    /// </summary>
    public void RecoverToTrack(double lateral = 0)
    {
        var t = Track.TrackTransform(TrackS, lateral, 0.02f);
        PlaceAt(t);
        SetVelocity(8);
    }

    public void SetVelocity(float speed)
    {
        var fwd = -GlobalBasis.Z;
        LinearVelocity = fwd * speed;
        for (int i = 0; i < 4; i++) Wheels[i].Omega = speed / WheelRadius;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Track == null) return;
        double dt = delta;
        Inputs.ClearEdges();
        if (Driver != null && !Retired) Driver.Drive(this, dt, ref Inputs);
        if (Retired) { Inputs = default; Inputs.Brake = 0.4f; }
        Simulate(dt);
    }

    private void Simulate(double dt)
    {
        var xf = GlobalTransform;
        var basis = xf.Basis.Orthonormalized();
        var up = basis.Y;
        var forward = -basis.Z;
        var comWorld = xf * CenterOfMass;
        var vel = LinearVelocity;
        SpeedMs = vel.Length();
        float vForward = vel.Dot(forward);

        // body position on the track
        var info = Track.Query(xf.Origin, ref _sHint);
        TrackS = info.S;
        TrackD = info.D;
        InPitLane = info.InPitLane;

        HandleDriverControls();

        // ---- controls with assists ----
        double throttle = Math.Clamp(Inputs.Throttle, 0, 1);
        double brake = Math.Clamp(Inputs.Brake, 0, 1) * Setup.BrakePressure / 100.0;
        if (Held) { throttle = Math.Min(throttle, 1); brake = 1; }

        if (PitLimiter)
        {
            double limit = _pitLimiterKmh - 0.8;
            double over = SpeedKmh - limit;
            if (over > -3) throttle = Math.Min(throttle, Math.Clamp(-over * 0.25, 0, 1));
        }
        if (PU.FuelKg <= 0) throttle = 0;

        // traction control: trim throttle when the rears spin up
        if (Assists.TractionControl != DrivingAssists.TractionLevel.Off && Box.Gear > 0)
        {
            double target = Assists.TractionControl == DrivingAssists.TractionLevel.Full ? 0.05 : 0.08;
            double spin = Math.Max(Wheels[RL].SlipRatio, Wheels[RR].SlipRatio);
            double floor = SpeedMs < 8 ? 0.35 : 0.05; // never strangle the engine when pulling away
            if (spin > target) throttle *= Math.Clamp(1 - (spin - target) * 9, floor, 1);
        }

        double steer = Math.Clamp(Inputs.Steer, -1, 1) * MaxSteerAngle;
        ThrottleApplied = (float)throttle;
        BrakeApplied = (float)brake;
        SteerApplied = Inputs.Steer;

        // ---- DRS ----
        bool wantDrs = Inputs.Drs || (Assists.DrsAssist && DrsAllowed);
        if (DrsOpen && (brake > 0.05 || !DrsAllowedOrStillOpen())) DrsOpen = false;
        else if (!DrsOpen && wantDrs && DrsAllowed && brake < 0.05) DrsOpen = true;

        // ---- gearbox ----
        Box.Update(dt);
        if (Assists.AutoGearbox) AutoShift(throttle);
        else
        {
            if (Inputs.ShiftUp) Box.ShiftUp();
            if (Inputs.ShiftDown && CanDownshift()) Box.ShiftDown();
        }

        // ---- aero ----
        var (dfFront, dfRear, drag) = Aero.Forces(Math.Max(0, vForward), DrsOpen, Slipstream, DirtyAir, Setup.FloorFactor);
        ApplyForce(-up * (float)dfFront, xf.Basis * new Vector3(0, 0.1f, FrontAxleZ));
        ApplyForce(-up * (float)dfRear, xf.Basis * new Vector3(0, 0.1f, RearAxleZ));
        if (SpeedMs > 0.5f) ApplyCentralForce(-vel.Normalized() * (float)drag);
        DebugDrag = drag;

        // ---- drivetrain torque to the rear axle ----
        double rearOmega = (Wheels[RL].Omega + Wheels[RR].Omega) * 0.5;
        double ratio = Box.OverallRatio();
        double wheelRpm = Math.Abs(rearOmega * ratio) * 60 / Math.Tau;
        double clutch = ClutchFor(throttle, wheelRpm, dt);
        ClutchEngagement = (float)clutch;

        double engineRpm;
        if (Box.Gear == 0 || clutch < 0.999 || wheelRpm < PowerUnit.IdleRpm * 0.95)
        {
            double free = PowerUnit.IdleRpm + throttle * (Held ? 7000 : 8500);
            double target = Math.Max(free * (1 - clutch) + wheelRpm * clutch, Math.Max(wheelRpm, PowerUnit.IdleRpm));
            engineRpm = Rpm + (target - Rpm) * Math.Min(1, dt * 12);
        }
        else engineRpm = wheelRpm;
        engineRpm = Math.Clamp(engineRpm, 2500, PU.RevLimit + 50);
        Rpm = (float)engineRpm;

        double omegaEngine = Math.Max(engineRpm * Math.Tau / 60, 300);
        bool straight = Math.Abs(steer) < 0.03;
        double mguK = PU.UpdateErs(dt, throttle, brake, SpeedMs, engineRpm, straight);
        double crank = PU.IceTorque(engineRpm, throttle) + Math.Max(0, mguK) / omegaEngine;
        if (Box.Shifting) crank *= 0.7;
        PU.BurnFuel(dt, throttle, engineRpm);
        double axleTorque = Box.Gear == 0 || Held ? 0 : crank * ratio * 0.93 * clutch;
        DebugAxle = axleTorque;
        double reflectedInertia = clutch > 0.99 && Box.Gear != 0 ? PowerUnit.EngineInertia * ratio * ratio * 0.5 : 0;

        // limited-slip differential
        double lockFrac = (throttle > 0.1 ? Setup.DiffOnThrottle : Setup.DiffOffThrottle) / 100.0;
        double split = Math.Clamp(-(Wheels[RL].Omega - Wheels[RR].Omega) * 2500, -lockFrac * Math.Abs(axleTorque) * 0.5 - 150, lockFrac * Math.Abs(axleTorque) * 0.5 + 150);
        Wheels[RL].DriveTorque = axleTorque * 0.5 + split;
        Wheels[RR].DriveTorque = axleTorque * 0.5 - split;
        Wheels[FL].DriveTorque = Wheels[FR].DriveTorque = 0;

        // ---- wheels ----
        for (int i = 0; i < 4; i++) SuspensionContact(i, xf, basis, up, dt);
        double Comp(int i) => Wheels[i].Grounded ? Wheels[i].Compression : 0;
        double arbF = (Comp(FL) - Comp(FR)) * Setup.FrontArbRate;
        double arbR = (Comp(RL) - Comp(RR)) * Setup.RearArbRate;

        int off = 0;
        for (int i = 0; i < 4; i++)
        {
            ref var w = ref Wheels[i];
            if (w.Grounded) w.Fz = Math.Max(0, w.Fz + (i == FL ? arbF : i == FR ? -arbF : i == RL ? arbR : -arbR));
            if (w.Grounded && w.Fz > 0) ApplyForce(up * (float)w.Fz, w.Contact - xf.Origin);
            if (i < 2) w.SteerAngle = steer;
            TyreForces(ref w, i, xf, basis, comWorld, brake, dt, i >= 2 ? reflectedInertia : 0);
            if (w.Grounded && !w.Surface.OnTrack && !w.Surface.InPitLane) off++;
        }
        WheelsOffTrack = off;
        DebugFx = Wheels[0].Fx + Wheels[1].Fx + Wheels[2].Fx + Wheels[3].Fx;

        // ---- telemetry ----
        var accel = (vel - _prevVelocity) / (float)dt;
        _prevVelocity = vel;
        LateralG = accel.Dot(basis.X) / 9.81f;
        LongitudinalG = accel.Dot(forward) / 9.81f;
        OdometerM += SpeedMs * dt;

        _fuelMassTimer += dt;
        if (_fuelMassTimer > 1.0) { _fuelMassTimer = 0; Mass = (float)(DryMass + PU.FuelKg); }

        Model?.UpdateVisuals(this, dt);
    }

    private bool DrsAllowedOrStillOpen() => DrsAllowed || DrsOpen;

    private void HandleDriverControls()
    {
        if (Inputs.TogglePitLimiter) PitLimiter = !PitLimiter;
        if (Assists.PitLimiterAssist) PitLimiter = InPitLane && Track.Data.PitLane != null && IsInSpeedLimitZone();
        if (Inputs.ErsModeDelta != 0)
            PU.Mode = (ErsMode)Math.Clamp((int)PU.Mode + Inputs.ErsModeDelta, (int)ErsMode.None, (int)ErsMode.Hotlap);
        PU.OvertakeActive = Inputs.Overtake && PU.StoreJ > 0;
        if (Inputs.BrakeBiasDelta != 0) { Setup.BrakeBias = Math.Clamp(Setup.BrakeBias + Inputs.BrakeBiasDelta, 50, 70); Brakes.Bias = Setup.BrakeBias / 100.0; }
        if (Inputs.DiffDelta != 0) Setup.DiffOnThrottle = Math.Clamp(Setup.DiffOnThrottle + Inputs.DiffDelta * 5, 10, 100);
        if (Inputs.FuelMixDelta != 0) PU.Mix = (FuelMix)Math.Clamp((int)PU.Mix + Inputs.FuelMixDelta, 0, 2);
        if (Inputs.EngineBrakingDelta != 0) PU.EngineBraking = Math.Clamp(PU.EngineBraking + Inputs.EngineBrakingDelta, 1, 11);
    }

    /// <summary>True between the pit lane speed-limit lines.</summary>
    public bool IsInSpeedLimitZone()
    {
        var p = Track.ProjectPit(GlobalPosition);
        var pit = Track.Data.PitLane;
        return p.S >= pit.LimitStart && p.S <= pit.LimitEnd && Math.Abs(p.D) < pit.Width;
    }

    private double ClutchFor(double throttle, double wheelRpm, double dt)
    {
        if (Held) { _launchTimer = 0; return 0; }
        if (!Assists.AutoClutch && Inputs.Clutch > 0.02f) return 1 - Inputs.Clutch;
        if (Box.Gear == 0) return 0;
        if (wheelRpm > 4500 || SpeedMs > 6) { _launchTimer = 0; return 1; }
        if (throttle < 0.02 && SpeedMs < 1) { _launchTimer = 0; return 0.0; }
        _launchTimer += dt;
        return Math.Clamp(0.35 + _launchTimer * 1.6, 0, 1);
    }

    private bool CanDownshift()
    {
        if (Box.Gear <= 1) return Box.Gear > -1 && SpeedMs < 2;
        double rpmAfter = Math.Abs((Wheels[RL].Omega + Wheels[RR].Omega) * 0.5 * Box.OverallRatio(Box.Gear - 1)) * 60 / Math.Tau;
        return rpmAfter < PU.RevLimit - 300;
    }

    private void AutoShift(double throttle)
    {
        if (Box.Shifting) return;
        if (Box.Gear <= 0) { Box.SetGear(1); return; }
        // shift on road speed, not wheel speed, so wheelspin doesn't run the car up the gears
        double roadOmega = Math.Max(0, LinearVelocity.Dot(-GlobalBasis.Z)) / WheelRadius;
        double rearOmega = Math.Min(Math.Abs((Wheels[RL].Omega + Wheels[RR].Omega) * 0.5), roadOmega * 1.08);
        double rpmNow = rearOmega * Box.OverallRatio() * 60 / Math.Tau;
        if (rpmNow > 11900 && Box.Gear < Box.TopGear && ClutchEngagement > 0.99) { Box.ShiftUp(); return; }
        if (Box.Gear > 1)
        {
            double rpmLower = rearOmega * Box.OverallRatio(Box.Gear - 1) * 60 / Math.Tau;
            double threshold = throttle > 0.5 ? 9800 : 8800;
            if (rpmNow < threshold && rpmLower < 11600) Box.ShiftDown();
        }
    }

    private void SuspensionContact(int i, Transform3D xf, Basis basis, Vector3 up, double dt)
    {
        ref var w = ref Wheels[i];
        var hp = xf * w.Hardpoint;
        w.Surface = Track.Query(hp, ref w.SurfaceHint);
        double groundY = w.Surface.Height;
        if (w.Surface.Bump > 0)
            groundY += w.Surface.Bump * Math.Sin(w.Surface.S * 9.1 + i * 1.7) * Math.Sin(w.Surface.S * 3.7);
        double upY = Math.Max(0.25, up.Y);
        double t = (hp.Y - groundY) / upY;         // distance from the hardpoint down to the ground along -up
        double center = t - w.Radius;
        w.PrevCompression = w.Compression;
        if (center > MaxDroop)
        {
            w.Grounded = false;
            w.Fz = 0;
            w.Compression = 0;
            w.CenterDistance = MaxDroop;
            w.Contact = hp - up * (float)(MaxDroop + w.Radius);
            return;
        }
        w.Grounded = true;
        w.CenterDistance = Math.Max(center, RestLength - BumpStop - 0.02);
        double x = RestLength - center;
        w.Compression = x;
        double k = w.Front ? Setup.FrontSpringRate : Setup.RearSpringRate;
        double c = w.Front ? 9000 : 8500;
        double velocity = (x - w.PrevCompression) / dt;
        double force = k * x + c * velocity;
        if (x > BumpStop) force += (x - BumpStop) * 2_500_000;
        w.Fz = Math.Max(0, force);
        w.Contact = hp - up * (float)t;
    }

    private void TyreForces(ref Wheel w, int i, Transform3D xf, Basis basis, Vector3 comWorld, double brakePedal, double dt, double extraInertia)
    {
        double inertia = WheelInertia + extraInertia;
        double brakeTorque = Brakes.Torque(i, brakePedal);
        if (Held) brakeTorque = 8000;

        if (!w.Grounded)
        {
            w.Fx = w.Fy = 0; w.SlipRatio = 0; w.SlipAngle = 0;
            w.Omega += dt * w.DriveTorque / inertia;
            double db = dt * brakeTorque / inertia;
            w.Omega = Math.Abs(w.Omega) <= db ? 0 : w.Omega - Math.Sign(w.Omega) * db;
            w.SpinAngle += w.Omega * dt;
            return;
        }

        var n = Vector3.Up;
        var fwd = -basis.Z;
        if (w.SteerAngle != 0) fwd = fwd.Rotated(basis.Y, (float)w.SteerAngle);
        fwd = (fwd - n * fwd.Dot(n)).Normalized();
        var right = fwd.Cross(n).Normalized();

        var vContact = LinearVelocity + AngularVelocity.Cross(w.Contact - comWorld);
        double vLong = vContact.Dot(fwd);
        double vLat = vContact.Dot(right);

        double vDen = Math.Max(Math.Abs(vLong), 3.0);
        double slipRatio = (w.Omega * w.Radius - vLong) / vDen;
        double slipAngle = Math.Atan2(vLat, Math.Max(Math.Abs(vLong), 1.0));
        if (vLong < -0.5) slipAngle = -slipAngle; // reversing
        w.SlipRatio = slipRatio;
        w.SlipAngle = slipAngle;

        var tyre = Tyres[i];
        double camber = Setup.CamberGrip(w.Front);
        double mu = TyreModel.LoadedMu(tyre.Compound.PeakMu, w.Fz) * tyre.Grip(Wetness) * w.Surface.Grip * camber;
        var (fx, fy) = TyreModel.Forces(slipRatio, slipAngle, w.Fz, mu);

        // low-speed lateral damping so the car can rest without jitter
        if (Math.Abs(vLong) < 2.0)
        {
            double share = Mass * 0.25;
            double fyStatic = Math.Clamp(-vLat * share / Math.Max(dt * 8, 0.02), -mu * w.Fz, mu * w.Fz);
            double blend = Math.Abs(vLong) / 2.0;
            fy = fy * blend + fyStatic * (1 - blend);
        }

        // rolling resistance (large on gravel)
        double roll = -Math.Sign(vLong) * w.Fz * w.Surface.Rolling;
        if (Math.Abs(vLong) < 0.3) roll = -vLong * w.Fz * w.Surface.Rolling / 0.3;

        w.Fx = fx; w.Fy = fy;
        var force = fwd * (float)(fx + roll) + right * (float)fy;
        ApplyForce(force, w.Contact - xf.Origin);

        // ABS: back off the brake when the wheel locks
        if (Assists.Abs && slipRatio < -0.09 && Math.Abs(vLong) > 3) brakeTorque *= Math.Clamp(1 - (-slipRatio - 0.09) * 6, 0.45, 1);

        // wheel spin: tyre-force term integrated implicitly so stiff tyres stay stable at 240 Hz
        double slope = TyreModel.LongitudinalSlope(slipRatio, slipAngle, w.Fz, mu) * w.Radius / vDen;
        double net = w.DriveTorque - fx * w.Radius;
        double omega = w.Omega + dt * net / inertia / (1 + dt * slope * w.Radius / inertia);
        double dBrake = dt * brakeTorque / inertia;
        if (Math.Abs(omega) <= dBrake) { omega = 0; w.Locked = Math.Abs(vLong) > 2; }
        else { omega -= Math.Sign(omega) * dBrake; w.Locked = false; }
        w.Omega = omega;
        w.SpinAngle += omega * dt;
        w.BrakeTorque = brakeTorque;

        // tyre and brake temperatures, wear
        double slipPower = Math.Abs(fx * (w.Omega * w.Radius - vLong)) + Math.Abs(fy * vLat);
        double wear = TyreWearMultiplier * Team.Car.TyreWear * Setup.CamberWear(w.Front);
        tyre.Update(dt, slipPower, w.Fz * Math.Abs(vLong), SpeedMs, AmbientC, TrackTempC, wear, Wetness);
        Brakes.UpdateTemp(i, brakeTorque * Math.Abs(w.Omega), SpeedMs, dt, AmbientC);
    }

    /// <summary>Instantly fits fresh tyres and refuels (pit stop or reset).</summary>
    public void FitTyres(TyreCompound compound, double blanketC = 70)
    {
        for (int i = 0; i < 4; i++)
        {
            Tyres[i].Fit(compound, blanketC);
            Tyres[i].PressurePsi = i < 2 ? Setup.PressureFront : Setup.PressureRear;
        }
    }
}

/// <summary>Collision layer constants shared by world and cars.</summary>
public static class NatureBuilderLayers
{
    public const uint Ground = 1 << 0;
    public const uint Walls = 1 << 1;
}
