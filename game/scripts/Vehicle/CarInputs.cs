namespace F1.Game.Vehicle;

/// <summary>Everything a driver (human or AI) can do in the cockpit during one physics tick.</summary>
public struct CarInputs
{
    /// <summary>0..1</summary>
    public float Throttle;
    /// <summary>0..1</summary>
    public float Brake;
    /// <summary>-1 (full right) .. +1 (full left).</summary>
    public float Steer;
    /// <summary>0 (engaged) .. 1 (fully disengaged), manual clutch only.</summary>
    public float Clutch;
    public bool ShiftUp;
    public bool ShiftDown;
    /// <summary>Driver is pressing the DRS button (held).</summary>
    public bool Drs;
    /// <summary>Toggle the pit-lane speed limiter this tick.</summary>
    public bool TogglePitLimiter;
    /// <summary>Hold for maximum ERS deployment (overtake button).</summary>
    public bool Overtake;
    public int ErsModeDelta;
    public int BrakeBiasDelta;
    public int DiffDelta;
    public int FuelMixDelta;
    public int EngineBrakingDelta;

    public void ClearEdges()
    {
        ShiftUp = ShiftDown = TogglePitLimiter = false;
        ErsModeDelta = BrakeBiasDelta = DiffDelta = FuelMixDelta = EngineBrakingDelta = 0;
    }
}

/// <summary>Driving aids applied inside the car's control loop.</summary>
public sealed class DrivingAssists
{
    public enum TractionLevel { Off, Medium, Full }

    public TractionLevel TractionControl = TractionLevel.Off;
    public bool Abs;
    public bool AutoGearbox;
    public bool AutoClutch = true;
    public bool PitLimiterAssist;
    public bool ErsAssist;
    public bool DrsAssist;

    public static DrivingAssists None() => new();
}

/// <summary>Anything that produces <see cref="CarInputs"/> each physics tick.</summary>
public interface IDriver
{
    void Drive(Car car, double dt, ref CarInputs inputs);
}
