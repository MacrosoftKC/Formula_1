namespace F1.Core.Sim;

public enum ErsMode { None, Medium, Hotlap, Overtake }
public enum FuelMix { Lean, Standard, Rich }

/// <summary>
/// 1.6 L V6 turbo-hybrid: ICE torque curve, MGU-K deployment/harvest with the 2025 energy limits, and fuel flow.
/// Energy in joules, power in watts, torque in N·m.
/// </summary>
public sealed class PowerUnit
{
    // ICE torque curve (crank). Peak ≈ 560 kW around 11,500 rpm.
    private static readonly double[] CurveRpm = { 0, 4000, 6000, 8000, 10000, 10500, 11500, 12500, 13500, 15000 };
    private static readonly double[] CurveNm = { 120, 220, 300, 380, 470, 490, 465, 420, 370, 300 };

    public const double IdleRpm = 4000;
    public const double EngineInertia = 0.06; // kg·m², reflected through the gearbox
    public double RevLimit { get; init; } = 15000;
    public double PowerFactor { get; init; } = 1.0;

    // ERS (MGU-K) — 2025: 120 kW, 4 MJ store, 4 MJ/lap deploy, 2 MJ/lap harvest from the K.
    public double MguKMaxW { get; init; } = 120_000;
    public double StoreMaxJ { get; init; } = 4_000_000;
    public double DeployPerLapJ { get; init; } = 4_000_000;
    public double HarvestKPerLapJ { get; init; } = 2_000_000;
    /// <summary>MGU-H power available to drive the MGU-K directly at high rpm/full throttle.</summary>
    public double MguHDirectW { get; init; } = 60_000;

    public double StoreJ { get; set; }
    public double DeployedThisLapJ { get; private set; }
    public double HarvestedKThisLapJ { get; private set; }
    public ErsMode Mode { get; set; } = ErsMode.Medium;
    public bool OvertakeActive { get; set; }

    // Fuel — 110 kg max, 100 kg/h flow limit above 10,500 rpm.
    public double FuelKg { get; set; }
    public double FuelFlowMaxKgPerS { get; init; } = 100.0 / 3600.0;
    public FuelMix Mix { get; set; } = FuelMix.Standard;
    public double FuelUseMultiplier { get; set; } = 1.0;

    /// <summary>Engine-braking setting 1..11 (higher = more engine braking off throttle).</summary>
    public int EngineBraking { get; set; } = 6;

    /// <summary>Last computed MGU-K output (+ deploying, − harvesting), watts.</summary>
    public double MguKPowerW { get; private set; }

    public PowerUnit(double fuelKg, double storeFraction = 1.0)
    {
        FuelKg = fuelKg;
        StoreJ = StoreMaxJ * storeFraction;
    }

    public double StoreFraction => StoreJ / StoreMaxJ;
    public double DeployRemainingThisLapJ => Math.Max(0, DeployPerLapJ - DeployedThisLapJ);

    public void NewLap()
    {
        DeployedThisLapJ = 0;
        HarvestedKThisLapJ = 0;
    }

    private static double Interp(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        for (int i = 1; i < xs.Length; i++)
            if (x <= xs[i])
            {
                double t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
                return ys[i - 1] + (ys[i] - ys[i - 1]) * t;
            }
        return ys[^1];
    }

    private double MixPower => Mix switch { FuelMix.Lean => 0.975, FuelMix.Rich => 1.012, _ => 1.0 };
    private double MixFlow => Mix switch { FuelMix.Lean => 0.92, FuelMix.Rich => 1.05, _ => 1.0 };

    /// <summary>ICE crank torque at the given rpm and throttle (negative = engine braking).</summary>
    public double IceTorque(double rpm, double throttle)
    {
        if (FuelKg <= 0) return -EngineBrakingTorque(rpm);
        if (rpm >= RevLimit) return -EngineBrakingTorque(rpm) * 0.5; // limiter cut
        double full = Interp(CurveRpm, CurveNm, rpm) * PowerFactor * MixPower;
        double drive = full * Math.Clamp(throttle, 0, 1);
        double braking = EngineBrakingTorque(rpm) * (1 - Math.Clamp(throttle * 4, 0, 1));
        return drive - braking;
    }

    private double EngineBrakingTorque(double rpm) => (18 + EngineBraking * 6.5) * Math.Clamp(rpm / 10000, 0.3, 1.3);

    /// <summary>
    /// MGU-K power decision for this step. Positive = deploy (adds drive), negative = harvest (adds braking torque
    /// on the rear axle). <paramref name="brakeDemand"/> is the brake pedal (0..1).
    /// </summary>
    public double UpdateErs(double dt, double throttle, double brakeDemand, double speedMs, double rpm, bool drsOrStraight)
    {
        double power = 0;
        var mode = OvertakeActive ? ErsMode.Overtake : Mode;
        double speedKmh = speedMs * 3.6;

        if (brakeDemand > 0.05 && speedKmh > 60)
        {
            // regenerative braking through the MGU-K
            double harvestLeft = HarvestKPerLapJ - HarvestedKThisLapJ;
            if (harvestLeft > 0 && StoreJ < StoreMaxJ)
            {
                power = -Math.Min(MguKMaxW, MguKMaxW * brakeDemand * 1.6);
                double e = Math.Min(-power * dt, Math.Min(harvestLeft, StoreMaxJ - StoreJ));
                StoreJ += e; HarvestedKThisLapJ += e;
                power = -e / dt;
            }
        }
        else if (throttle > 0.85 && speedKmh > 40)
        {
            double cap = mode switch
            {
                ErsMode.None => 0,
                ErsMode.Medium => speedKmh > 120 ? MguKMaxW * 0.7 : MguKMaxW * 0.3,
                ErsMode.Hotlap => MguKMaxW,
                ErsMode.Overtake => MguKMaxW,
                _ => 0,
            };
            // The MGU-H can feed the MGU-K directly (not limited per lap and not through the battery).
            double hDirect = mode == ErsMode.None ? 0 : MguHDirectW * Math.Clamp((rpm - 8000) / 3500, 0, 1);
            double fromBattery = Math.Max(0, cap - hDirect);
            // Medium keeps a reserve so the battery lasts the lap; Overtake may drain it.
            double reserve = mode == ErsMode.Overtake ? 0 : mode == ErsMode.Medium ? StoreMaxJ * 0.25 : StoreMaxJ * 0.05;
            if (StoreJ <= reserve || DeployRemainingThisLapJ <= 0) fromBattery = 0;
            double e = Math.Min(fromBattery * throttle * dt, Math.Min(StoreJ, DeployRemainingThisLapJ));
            StoreJ -= e; DeployedThisLapJ += e;
            power = dt > 0 ? e / dt + Math.Min(cap, hDirect) * throttle : 0;
        }
        else if (throttle > 0.2 && mode != ErsMode.Overtake)
        {
            // MGU-H / part-throttle harvesting is not limited per lap
            double harvest = 28_000 * Math.Clamp(rpm / 12000, 0.2, 1.0) * (mode == ErsMode.None ? 1.6 : mode == ErsMode.Hotlap ? 0.4 : 1.0);
            double e = Math.Min(harvest * dt, StoreMaxJ - StoreJ);
            StoreJ += e;
        }

        if (drsOrStraight && mode == ErsMode.None && throttle > 0.95)
        {
            // "None" still harvests through the MGU-H at full throttle on the straights
            StoreJ = Math.Min(StoreMaxJ, StoreJ + 30_000 * dt);
        }

        MguKPowerW = power;
        return power;
    }

    /// <summary>Burns fuel for this step and returns kg used.</summary>
    public double BurnFuel(double dt, double throttle, double rpm)
    {
        if (FuelKg <= 0) return 0;
        double flow = FuelFlowMaxKgPerS * Math.Clamp(throttle, 0.03, 1) * Math.Clamp(rpm / 10500, 0.25, 1.0) * MixFlow * FuelUseMultiplier;
        double used = Math.Min(FuelKg, flow * dt);
        FuelKg -= used;
        return used;
    }
}

/// <summary>Eight-speed seamless-shift gearbox.</summary>
public sealed class Gearbox
{
    public double[] Ratios { get; init; } = { 4.52, 3.75, 3.11, 2.58, 2.14, 1.775, 1.47, 1.22 };
    public double FinalDrive { get; init; } = 4.0;
    public double ReverseRatio { get; init; } = 4.0;
    public double UpshiftTime { get; init; } = 0.035;
    public double DownshiftTime { get; init; } = 0.055;

    /// <summary>-1 = reverse, 0 = neutral, 1..8 forward.</summary>
    public int Gear { get; private set; } = 1;
    public double ShiftTimer { get; private set; }
    public bool Shifting => ShiftTimer > 0;
    public int TopGear => Ratios.Length;

    public double OverallRatio(int gear) => gear switch
    {
        0 => 0,
        < 0 => -ReverseRatio * FinalDrive,
        _ => Ratios[Math.Min(gear, Ratios.Length) - 1] * FinalDrive,
    };

    public double OverallRatio() => OverallRatio(Gear);

    public bool ShiftUp()
    {
        if (Shifting || Gear >= TopGear) return false;
        Gear++;
        ShiftTimer = UpshiftTime;
        return true;
    }

    public bool ShiftDown()
    {
        if (Shifting || Gear <= -1) return false;
        Gear--;
        ShiftTimer = DownshiftTime;
        return true;
    }

    public void SetGear(int gear)
    {
        Gear = Math.Clamp(gear, -1, TopGear);
        ShiftTimer = 0;
    }

    public void Update(double dt) => ShiftTimer = Math.Max(0, ShiftTimer - dt);
}

/// <summary>Aerodynamic coefficients and their modifiers (DRS, slipstream, dirty air, damage).</summary>
public sealed class AeroModel
{
    public const double AirDensity = 1.19;

    /// <summary>Total downforce coefficient × area (m²) at the reference wing levels.</summary>
    public double ClA { get; set; } = 5.1;
    /// <summary>Drag coefficient × area (m²).</summary>
    public double CdA { get; set; } = 1.2;
    /// <summary>Fraction of downforce on the front axle.</summary>
    public double FrontBalance { get; set; } = 0.43;

    public double DrsClReduction { get; init; } = 0.55;
    public double DrsCdReduction { get; init; } = 0.15;

    /// <summary>Front wing damage 0..1 (1 = wing gone).</summary>
    public double FrontWingDamage { get; set; }
    public double RearWingDamage { get; set; }

    /// <summary>Applies wing levels from the setup (1..50 each, 25 = baseline).</summary>
    public void ApplyWings(int frontWing, int rearWing, double downforceFactor, double dragFactor)
    {
        double f = (frontWing - 25) / 25.0, r = (rearWing - 25) / 25.0;
        double frontCl = 5.1 * 0.43 * (1 + 0.18 * f);
        double rearCl = 5.1 * 0.57 * (1 + 0.22 * r);
        ClA = (frontCl + rearCl) * downforceFactor;
        FrontBalance = frontCl / (frontCl + rearCl);
        CdA = (1.2 + 0.05 * f + 0.11 * r) * dragFactor;
    }

    /// <summary>
    /// Returns (front downforce, rear downforce, drag) in newtons.
    /// <paramref name="slipstream"/> 0..1 cuts drag, <paramref name="dirtyAir"/> 0..1 cuts downforce,
    /// <paramref name="rideHeightFactor"/> models the floor's ground effect (≈1 at normal ride height).
    /// </summary>
    public (double Front, double Rear, double Drag) Forces(double speed, bool drsOpen, double slipstream, double dirtyAir, double rideHeightFactor = 1.0)
    {
        double q = 0.5 * AirDensity * speed * speed;
        double front = ClA * FrontBalance * (1 - FrontWingDamage * 0.85);
        double rear = ClA * (1 - FrontBalance) * (1 - RearWingDamage * 0.7);
        double cd = CdA;
        if (drsOpen) { rear -= DrsClReduction; cd -= DrsCdReduction; }
        double airLoss = 1 - 0.14 * Math.Clamp(dirtyAir, 0, 1);
        double floor = Math.Clamp(rideHeightFactor, 0.8, 1.1);
        front *= airLoss * floor; rear *= airLoss * floor;
        cd *= 1 - 0.3 * Math.Clamp(slipstream, 0, 1);
        return (q * front, q * Math.Max(0, rear), q * cd);
    }
}

/// <summary>Carbon brakes: torque from pedal and bias, with temperature-dependent efficiency.</summary>
public sealed class BrakeSystem
{
    /// <summary>Total brake torque at full pedal (N·m, all four wheels).</summary>
    public double MaxTorque { get; init; } = 17_500;
    /// <summary>Front share, 0.5..0.7 (F1 cars typically run ~56%).</summary>
    public double Bias { get; set; } = 0.56;
    public double[] Temps { get; } = { 400, 400, 300, 300 }; // FL FR RL RR, °C

    public double Efficiency(int wheel)
    {
        double t = Temps[wheel];
        if (t < 350) return 0.82 + 0.18 * Math.Clamp((t - 100) / 250, 0, 1);
        if (t > 1050) return Math.Max(0.6, 1 - (t - 1050) * 0.0015);
        return 1;
    }

    /// <summary>Requested brake torque at a wheel (0..3) for the pedal position.</summary>
    public double Torque(int wheel, double pedal)
    {
        double share = wheel < 2 ? Bias * 0.5 : (1 - Bias) * 0.5;
        return MaxTorque * share * Math.Clamp(pedal, 0, 1) * Efficiency(wheel);
    }

    public void UpdateTemp(int wheel, double brakePowerW, double speed, double dt, double ambient)
    {
        const double capacity = 2200; // J/K per corner (disc + pads)
        double cooling = (Temps[wheel] - ambient) * (2.5 + 0.55 * speed);
        Temps[wheel] += dt * (brakePowerW * 0.6 - cooling) / capacity;
        Temps[wheel] = Math.Clamp(Temps[wheel], ambient, 1300);
    }
}
