namespace F1.Core.Sim;

/// <summary>Driver-adjustable car setup. Ranges follow the usual F1-game conventions.</summary>
public sealed class CarSetup
{
    /// <summary>Wing levels 1..50 (25 = baseline downforce).</summary>
    public int FrontWing { get; set; } = 26;
    public int RearWing { get; set; } = 23;
    /// <summary>Differential locking on/off throttle, 10..100 %.</summary>
    public int DiffOnThrottle { get; set; } = 60;
    public int DiffOffThrottle { get; set; } = 55;
    /// <summary>Front brake bias, 50..70 %.</summary>
    public double BrakeBias { get; set; } = 56;
    /// <summary>Brake pressure 80..100 %.</summary>
    public int BrakePressure { get; set; } = 100;
    /// <summary>Ride height steps 1..11 (lower = more floor downforce, more bottoming).</summary>
    public int RideHeightFront { get; set; } = 3;
    public int RideHeightRear { get; set; } = 6;
    /// <summary>Spring stiffness steps 1..41.</summary>
    public int SpringFront { get; set; } = 21;
    public int SpringRear { get; set; } = 18;
    /// <summary>Anti-roll bar steps 1..21.</summary>
    public int ArbFront { get; set; } = 11;
    public int ArbRear { get; set; } = 9;
    /// <summary>Camber (degrees, negative) and toe (degrees).</summary>
    public double CamberFront { get; set; } = -3.2;
    public double CamberRear { get; set; } = -1.7;
    public double ToeFront { get; set; } = 0.05;
    public double ToeRear { get; set; } = 0.2;
    /// <summary>Starting tyre pressures (psi).</summary>
    public double PressureFront { get; set; } = 23.0;
    public double PressureRear { get; set; } = 21.5;

    public CarSetup Clone() => (CarSetup)MemberwiseClone();

    public static CarSetup Balanced() => new();

    public static CarSetup LowDownforce() => new() { FrontWing = 18, RearWing = 14, RideHeightFront = 4, RideHeightRear = 7 };

    public static CarSetup HighDownforce() => new() { FrontWing = 34, RearWing = 32, ArbFront = 12, ArbRear = 8 };

    public static CarSetup Wet() => new()
    {
        FrontWing = 36, RearWing = 36, DiffOnThrottle = 45, DiffOffThrottle = 50, BrakeBias = 54,
        RideHeightFront = 6, RideHeightRear = 8, SpringFront = 12, SpringRear = 10, ArbFront = 7, ArbRear = 6,
    };

    // Physical values derived from the steps --------------------------------------------------------

    public double FrontSpringRate => 160_000 + (SpringFront - 1) * 4_500;   // N/m at the wheel
    public double RearSpringRate => 150_000 + (SpringRear - 1) * 4_500;
    public double FrontArbRate => 30_000 + (ArbFront - 1) * 6_000;          // N/m of roll difference
    public double RearArbRate => 25_000 + (ArbRear - 1) * 6_000;
    public double FrontRideHeight => 0.020 + RideHeightFront * 0.003;        // m of floor clearance
    public double RearRideHeight => 0.035 + RideHeightRear * 0.004;

    /// <summary>Floor (ground-effect) downforce multiplier: lower ride height = more downforce.</summary>
    public double FloorFactor => 1.04 - (FrontRideHeight + RearRideHeight - 0.075) * 0.9;

    /// <summary>Lateral grip multiplier from camber (≈ -3.5° front / -2° rear is optimal for cornering).</summary>
    public double CamberGrip(bool front) => 1 - Math.Abs((front ? CamberFront : CamberRear) - (front ? -3.5 : -2.0)) * 0.012;

    /// <summary>Extra tyre wear from aggressive camber.</summary>
    public double CamberWear(bool front) => 1 + Math.Max(0, -(front ? CamberFront : CamberRear) - (front ? 3.0 : 1.5)) * 0.12;
}
