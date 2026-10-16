using F1.Core.Sim;

namespace F1.Game.Core;

public enum SessionMode { TimeTrial, Race }
public enum WeekendFormat { RaceOnly, OneShotQualifying, ShortQualifying, FullQualifying }
public enum WeatherMode { Dry, Wet, Dynamic }
public enum DamageMode { Off, Visual, Full }
public enum StewardsMode { Off, Lenient, Strict }

/// <summary>Everything chosen in the Race / Time Trial setup screens.</summary>
public sealed class SessionConfig
{
    public SessionMode Mode { get; set; } = SessionMode.TimeTrial;
    public string TeamId { get; set; } = "mclaren";
    /// <summary>Which of the team's two seats the player takes (0 or 1); that driver sits out.</summary>
    public int Seat { get; set; }
    public string Compound { get; set; } = "soft";
    public CarSetup Setup { get; set; } = CarSetup.Balanced();

    // race options
    public WeekendFormat Format { get; set; } = WeekendFormat.RaceOnly;
    public int RaceLaps { get; set; } = 5;
    public WeatherMode Weather { get; set; } = WeatherMode.Dry;
    /// <summary>0..110, 100 ≈ real-world pace.</summary>
    public double AiDifficulty { get; set; } = 90;
    public bool FormationLap { get; set; }
    public bool SafetyCar { get; set; } = true;
    public DamageMode Damage { get; set; } = DamageMode.Full;
    public StewardsMode Stewards { get; set; } = StewardsMode.Strict;
    public double TyreWearMultiplier { get; set; } = 1.0;
    public double FuelMultiplier { get; set; } = 1.0;
    public bool MechanicalFailures { get; set; }
    public bool ParcFerme { get; set; } = true;
    /// <summary>Grid slot for the player when qualifying is skipped (-1 = random).</summary>
    public int StartPosition { get; set; } = -1;

    // time trial options
    public bool ShowGhost { get; set; } = true;
}
