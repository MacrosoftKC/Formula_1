namespace F1.Core.Sim;

/// <summary>Tyre compound characteristics (dry slicks C3-C5 nominated for Albert Park, plus wet-weather tyres).</summary>
public sealed record TyreCompound(
    string Id,
    string Name,
    /// <summary>Peak friction coefficient on dry asphalt at the reference load.</summary>
    double PeakMu,
    /// <summary>Grip multiplier on a fully wet track.</summary>
    double WetGrip,
    double OptimalMinC,
    double OptimalMaxC,
    /// <summary>Wear per joule of slip energy (multiplied by team/driver factors).</summary>
    double WearRate,
    /// <summary>Surface heat gain per joule of slip energy.</summary>
    double HeatRate,
    bool Slick)
{
    public static readonly TyreCompound Soft = new("soft", "Soft", 2.0, 0.35, 85, 105, 1.25e-8, 1.0, true);
    public static readonly TyreCompound Medium = new("medium", "Medium", 1.97, 0.33, 95, 115, 0.82e-8, 0.9, true);
    public static readonly TyreCompound Hard = new("hard", "Hard", 1.94, 0.31, 105, 130, 0.58e-8, 0.8, true);
    public static readonly TyreCompound Intermediate = new("intermediate", "Intermediate", 1.7, 0.82, 55, 85, 1.1e-8, 0.85, false);
    public static readonly TyreCompound Wet = new("wet", "Wet", 1.56, 0.9, 45, 75, 1.0e-8, 0.75, false);

    public static readonly TyreCompound[] All = { Soft, Medium, Hard, Intermediate, Wet };

    public static TyreCompound ById(string id) =>
        All.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException($"Unknown compound {id}");
}

/// <summary>
/// Simplified Pacejka "magic formula" with the similarity method for combined slip: longitudinal and lateral slip
/// are normalised by their peak values and share one friction curve, then the force is split by slip direction.
/// </summary>
public static class TyreModel
{
    /// <summary>Peak slip ratio and slip angle (radians) of a modern F1 slick.</summary>
    public const double PeakSlipRatio = 0.10;
    public const double PeakSlipAngle = 0.105; // ~6 degrees

    // Normalised curve f(s) = sin(C·atan(B·s)) with its peak at s = 1 and ~85% grip when fully sliding.
    private const double C = 1.35;
    private static readonly double B = Math.Tan(Math.PI / (2 * C));

    public static double Curve(double s) => Math.Sin(C * Math.Atan(B * s));

    /// <summary>Reference vertical load for the load-sensitivity model (N).</summary>
    public const double ReferenceLoad = 3500;
    public const double LoadSensitivity = 0.11;

    /// <summary>Friction coefficient after load sensitivity (grip per newton falls as load rises).</summary>
    public static double LoadedMu(double peakMu, double fz)
    {
        double k = 1 - LoadSensitivity * (fz / ReferenceLoad - 1);
        return peakMu * Math.Clamp(k, 0.7, 1.15);
    }

    /// <summary>
    /// Combined tyre forces. Positive slip ratio = driving (wheel faster than ground), positive slip angle = contact
    /// patch sliding to the right. Returns Fx along the wheel heading and Fy to the right (it opposes the sliding).
    /// </summary>
    public static (double Fx, double Fy) Forces(double slipRatio, double slipAngle, double fz, double mu)
    {
        if (fz <= 0) return (0, 0);
        double sx = slipRatio / PeakSlipRatio;
        double sy = Math.Tan(Math.Clamp(slipAngle, -1.4, 1.4)) / Math.Tan(PeakSlipAngle);
        double s = Math.Sqrt(sx * sx + sy * sy);
        if (s < 1e-9) return (0, 0);
        double f = mu * fz * Curve(s);
        return (f * sx / s, -f * sy / s);
    }

    /// <summary>Longitudinal stiffness dFx/dκ at the given state (finite difference), used for stable wheel-spin integration.</summary>
    public static double LongitudinalSlope(double slipRatio, double slipAngle, double fz, double mu)
    {
        const double eps = 1e-3;
        var a = Forces(slipRatio - eps, slipAngle, fz, mu).Fx;
        var b = Forces(slipRatio + eps, slipAngle, fz, mu).Fx;
        return Math.Max(0, (b - a) / (2 * eps));
    }
}

/// <summary>Temperature and wear state of one tyre.</summary>
public sealed class TyreState
{
    public TyreCompound Compound { get; private set; }
    public double SurfaceTemp { get; set; }
    public double CoreTemp { get; set; }
    /// <summary>0 = new, 1 = completely worn.</summary>
    public double Wear { get; set; }
    public double PressurePsi { get; set; } = 23;
    public bool Punctured { get; set; }
    public int LapsUsed { get; set; }

    public TyreState(TyreCompound compound, double startTempC = 70)
    {
        Compound = compound;
        SurfaceTemp = CoreTemp = startTempC;
    }

    public void Fit(TyreCompound compound, double blanketTempC, double wear = 0)
    {
        Compound = compound;
        SurfaceTemp = CoreTemp = blanketTempC;
        Wear = wear;
        Punctured = false;
        LapsUsed = 0;
    }

    /// <summary>
    /// Advances the thermal and wear model. <paramref name="slipPower"/> is |Fx·vslipX| + |Fy·vslipY| in watts,
    /// <paramref name="loadPower"/> the rolling (flex) heating Fz·v, <paramref name="speed"/> the car speed in m/s.
    /// </summary>
    public void Update(double dt, double slipPower, double loadPower, double speed, double ambientC, double trackC, double wearMultiplier, double wetness)
    {
        const double surfaceCapacity = 9000;  // J/K
        const double coreCapacity = 42000;    // J/K
        const double surfaceToCore = 220;     // W/K
        // Calibrated so a pushing lap holds the surface around 95-110 °C with 20-25 °C ambient.
        double heatIn = slipPower * Compound.HeatRate * 0.45 + loadPower * 0.005;
        double airCool = (SurfaceTemp - ambientC) * (8 + 1.2 * speed) * (1 + wetness * 2.5);
        double trackExchange = (SurfaceTemp - trackC) * 8;
        double toCore = (SurfaceTemp - CoreTemp) * surfaceToCore;
        SurfaceTemp += dt * (heatIn - airCool - trackExchange - toCore) / surfaceCapacity;
        CoreTemp += dt * (toCore - (CoreTemp - ambientC) * 6) / coreCapacity;
        SurfaceTemp = Math.Clamp(SurfaceTemp, ambientC - 5, 180);
        CoreTemp = Math.Clamp(CoreTemp, ambientC - 5, 160);

        // Overheated tyres wear faster; slicks on a wet track barely wear but intermediates on a dry track do.
        double tempFactor = 1 + Math.Max(0, EffectiveTemp - Compound.OptimalMaxC) * 0.03;
        double dryAbuse = !Compound.Slick ? 1 + (1 - wetness) * 2.5 : 1;
        Wear = Math.Min(1, Wear + slipPower * dt * Compound.WearRate * wearMultiplier * tempFactor * dryAbuse);
    }

    public double EffectiveTemp => SurfaceTemp * 0.6 + CoreTemp * 0.4;

    /// <summary>Grip multiplier from temperature (1 inside the optimum window).</summary>
    public double TemperatureGrip
    {
        get
        {
            double t = EffectiveTemp;
            if (t < Compound.OptimalMinC) return Math.Max(0.8, 1 - (Compound.OptimalMinC - t) * 0.004);
            if (t > Compound.OptimalMaxC) return Math.Max(0.84, 1 - (t - Compound.OptimalMaxC) * 0.004);
            return 1;
        }
    }

    /// <summary>Grip multiplier from wear: gentle linear drop, then the "cliff" past ~70%.</summary>
    public double WearGrip
    {
        get
        {
            if (Wear < 0.7) return 1 - Wear * 0.075;
            return Math.Max(0.55, 1 - 0.7 * 0.075 - (Wear - 0.7) * 1.1);
        }
    }

    /// <summary>Pressure away from the 23 psi optimum costs a little grip.</summary>
    public double PressureGrip => 1 - Math.Min(0.06, Math.Abs(PressurePsi - 23) * 0.006);

    public double Grip(double wetness)
    {
        if (Punctured) return 0.25;
        double weather = Compound.Slick
            ? 1 - wetness * (1 - Compound.WetGrip)
            : 1 - (1 - wetness) * 0.04 - wetness * (1 - Compound.WetGrip) * 0.3;
        return TemperatureGrip * WearGrip * PressureGrip * weather;
    }
}
