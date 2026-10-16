using F1.Core.Data;
using F1.Core.Racing;
using F1.Game.Core;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.AI;

/// <summary>
/// AI driver: follows the racing line with pure-pursuit steering and a speed target from the line's speed profile
/// (with braking anticipation), scaled by difficulty and the driver's ratings. Drives the same car physics through
/// the same inputs as the player. Racecraft (traffic, overtaking, defending, strategy) builds on <see cref="LateralTarget"/>
/// and <see cref="SpeedCap"/>, which the race layer sets every tick.
/// </summary>
public class AIDriver : IDriver
{
    public RacingLine Line { get; }
    public DriverData Profile { get; }
    /// <summary>Fraction of the theoretical line speed the driver commits to (difficulty × pace rating).</summary>
    public double Pace { get; set; }
    /// <summary>Extra lateral offset from the racing line requested by racecraft (overtake/defend/avoid), metres.</summary>
    public double LateralOffset { get; set; }
    /// <summary>Absolute speed limit imposed by racecraft or race control (SC/VSC/pit limiter), m/s.</summary>
    public double SpeedCap { get; set; } = double.MaxValue;
    /// <summary>When set, the driver steers to this absolute lateral offset instead of the racing line (pit lane, grid).</summary>
    public double? LateralTarget { get; set; }
    public bool UseDrs { get; set; } = true;
    public bool Enabled { get; set; } = true;
    /// <summary>Speed target from the last tick (diagnostics/HUD).</summary>
    public double LastTarget { get; private set; }

    private readonly double[] _learn;          // per-sample speed scaling learned from sliding
    private double _steerFiltered;
    private double _throttle;
    private double _mistakeTimer, _mistakeAmount;
    private readonly Random _rng;

    public AIDriver(RacingLine line, DriverData profile, double difficulty, int seed)
    {
        Line = line;
        Profile = profile;
        _rng = new Random(seed);
        // start a little under the point-mass envelope (load transfer costs grip); learning refines it lap by lap
        _learn = Enumerable.Repeat(0.95, line.Offset.Length).ToArray();
        // difficulty 100 ≈ real-world pace; ratings spread the field by a few tenths
        double ratingFactor = 1 - (100 - profile.Ratings.Pace) * 0.0012;
        Pace = Math.Clamp(0.87 + difficulty / 100.0 * 0.12, 0.8, 1.02) * ratingFactor;
    }

    /// <summary>Seconds spent nearly stationary while trying to drive (stuck in gravel or against a wall).</summary>
    public double StuckTime { get; private set; }
    /// <summary>Allow automatic recovery to the track after being stuck (marshals push the car back).</summary>
    public bool AutoRecover { get; set; } = true;

    public virtual void Drive(Car car, double dt, ref CarInputs inputs)
    {
        if (!Enabled) { inputs.Throttle = 0; inputs.Brake = 1; inputs.Steer = 0; return; }
        if (car.SpeedMs < 2 && !car.Held && SpeedCap > 5) StuckTime += dt; else StuckTime = 0;
        if (AutoRecover && StuckTime > 4)
        {
            car.RecoverToTrack(Line.OffsetAt(car.TrackS));
            StuckTime = 0;
        }
        double s = car.TrackS;
        double v = car.SpeedMs;
        var center = Line.Center;

        // ---- steering: pure pursuit on the (offset) racing line ----
        double look = Math.Clamp(6 + v * 0.42, 8, 42);
        double sAhead = s + look;
        double latAhead = (LateralTarget ?? Line.OffsetAt(sAhead) + LateralOffset);
        double halfL = center.Sample(car.Track.Data.WidthLeft, sAhead) - 1.1;
        double halfR = center.Sample(car.Track.Data.WidthRight, sAhead) - 1.1;
        if (LateralTarget == null) latAhead = Math.Clamp(latAhead, -halfR, halfL);
        var target2 = center.ToWorld(sAhead, latAhead);
        var targetWorld = Units.ToWorld(target2, car.GlobalPosition.Y);
        var local = car.GlobalTransform.AffineInverse() * targetWorld; // forward is -Z
        double ld = Math.Max(1, new Vector2(local.X, local.Z).Length());
        double curvature = -2 * local.X / (ld * ld);                   // +ve = left
        double steerAngle = Math.Atan(curvature * 3.6);
        // add the line curvature feed-forward for stability at speed
        steerAngle += Math.Atan(Line.CurvatureAt(s + v * 0.15) * 3.6) * 0.3;
        if (_mistakeTimer > 0) { _mistakeTimer -= dt; steerAngle += _mistakeAmount; }
        // oversteer: counter-steer when the rear slides more than the front
        double af = (car.Wheels[Car.FL].SlipAngle + car.Wheels[Car.FR].SlipAngle) * 0.5;
        double ar = (car.Wheels[Car.RL].SlipAngle + car.Wheels[Car.RR].SlipAngle) * 0.5;
        if (v > 8 && Math.Abs(ar) > 0.09 && Math.Abs(ar) > Math.Abs(af))
            steerAngle -= 0.9 * (Math.Abs(ar) - Math.Abs(af)) * Math.Sign(ar);
        // understeer: don't keep winding on lock (in the same direction) past the front tyres' peak slip angle
        double current = _steerFiltered * Car.MaxSteerAngle;
        if (v > 15 && Math.Abs(af) > 0.12 && Math.Sign(steerAngle) == Math.Sign(current) && Math.Abs(steerAngle) > Math.Abs(current))
            steerAngle = current;
        double steer = Math.Clamp(steerAngle / Car.MaxSteerAngle, -1, 1);
        _steerFiltered += (steer - _steerFiltered) * Math.Min(1, dt * 18);
        inputs.Steer = (float)_steerFiltered;

        // ---- speed target with braking anticipation ----
        double target = TargetSpeed(s, v);
        target = Math.Min(target, SpeedCap);
        double err = target - v;
        if (err > 0)
        {
            // squeeze the throttle on progressively (no snap oversteer on corner exit)
            double want = Math.Clamp(0.55 + err * 0.35, 0, 1);
            inputs.Throttle = (float)Math.Min(want, _throttle + dt * (v < 20 ? 2.5 : 4.0));
            inputs.Brake = 0;
        }
        else
        {
            inputs.Throttle = (float)Math.Clamp(1 + err * 0.6, 0, 1) * (err > -1.5 ? 1 : 0);
            inputs.Brake = (float)Math.Clamp(-err * 0.11, 0, 1);
        }
        // cut the throttle while badly sideways
        double slide = Math.Max(Math.Abs(car.Wheels[Car.RL].SlipAngle), Math.Abs(car.Wheels[Car.RR].SlipAngle));
        if (slide > 0.16 && v > 12) inputs.Throttle *= 0.4f;
        LastTarget = target;
        _throttle = inputs.Throttle;

        LearnFromSliding(car, s);
        MaybeMakeMistake(car, dt);

        inputs.Drs = UseDrs && car.DrsAllowed && Math.Abs(Line.CurvatureAt(s + 60)) < 1.0 / 900;
        if (car.Gear <= 0) inputs.ShiftUp = true;
    }

    /// <summary>Speed the driver aims for now: the lowest speed it must reach ahead, reachable with its braking.</summary>
    protected double TargetSpeed(double s, double v)
    {
        // braking capacity grows with downforce: use the point-mass grip envelope, with a skill-dependent margin
        double margin = 0.8 + Profile.Ratings.Pace * 0.0012;
        double best = double.MaxValue;
        double horizon = Math.Max(60, v * v / (2 * 22) + 40);
        for (double x = 0; x <= horizon; x += 4)
        {
            int idx = Line.Center.NearestIndex(s + x);
            double vt = Line.Speed[idx] * Pace * _learn[idx];
            double decel = Math.Min(Perf.GripAccel((vt + v) * 0.5), Perf.MaxBrakeG * PerformanceModel.G) * margin;
            best = Math.Min(best, Math.Sqrt(vt * vt + 2 * decel * Math.Max(0, x - v * 0.06)));
        }
        return best;
    }

    private static readonly PerformanceModel Perf = new();

    private void LearnFromSliding(Car car, double s)
    {
        int idx = Line.Center.NearestIndex(s);
        double frontSlip = Math.Max(Math.Abs(car.Wheels[Car.FL].SlipAngle), Math.Abs(car.Wheels[Car.FR].SlipAngle));
        double rearSlip = Math.Max(Math.Abs(car.Wheels[Car.RL].SlipAngle), Math.Abs(car.Wheels[Car.RR].SlipAngle));
        bool offTrack = car.WheelsOffTrack >= 2;
        if (car.SpeedMs < 15) return;
        if (frontSlip > 0.16 || rearSlip > 0.16 || offTrack)
        {
            // over the limit here: lower the target for the stretch leading into this point
            for (int k = -25; k <= 6; k++)
            {
                int j = ((idx + k) % _learn.Length + _learn.Length) % _learn.Length;
                _learn[j] = Math.Max(0.84, _learn[j] - (offTrack ? 0.002 : 0.0006));
            }
        }
        else if (frontSlip < 0.115 && rearSlip < 0.115)
        {
            for (int k = -6; k <= 0; k++)
            {
                int j = ((idx + k) % _learn.Length + _learn.Length) % _learn.Length;
                _learn[j] = Math.Min(1.06, _learn[j] + 0.00012);
            }
        }
    }

    private void MaybeMakeMistake(Car car, double dt)
    {
        if (_mistakeTimer > 0 || car.SpeedMs < 30) return;
        double perSecond = (100 - Profile.Ratings.Consistency) * 0.00004;
        if (_rng.NextDouble() < perSecond * dt)
        {
            _mistakeTimer = 0.25 + _rng.NextDouble() * 0.3;
            _mistakeAmount = (_rng.NextDouble() - 0.5) * 0.02;
        }
    }
}
