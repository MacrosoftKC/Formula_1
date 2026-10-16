namespace F1.Core.Data;

/// <summary>Penalties the stewards can hand out.</summary>
public enum PenaltyKind
{
    None,
    Warning,
    Time5,
    Time10,
    DriveThrough,
    StopGo10,
    Disqualification,
}

/// <summary>Sporting and technical numbers loaded from data/regulations_2025.json.</summary>
public sealed class RegulationsFile
{
    public int Season { get; set; }
    public List<int> Points { get; set; } = new();
    public bool FastestLapPoint { get; set; }
    public double ClassificationPercent { get; set; } = 90;
    public double MaxRaceHours { get; set; } = 2;
    public TechnicalRules Car { get; set; } = new();
    public ErsRules Ers { get; set; } = new();
    public DrsRules Drs { get; set; } = new();
    public TyreRules Tyres { get; set; } = new();
    public PitRules Pit { get; set; } = new();
    public TrackLimitRules TrackLimits { get; set; } = new();
    public SafetyCarRules SafetyCar { get; set; } = new();
    public QualifyingRules Qualifying { get; set; } = new();
    public StartRules Start { get; set; } = new();
    public PenaltyTable Penalties { get; set; } = new();
}

public sealed class TechnicalRules
{
    public double MinimumMassKg { get; set; } = 800;
    public double FuelMaxKg { get; set; } = 110;
    public double FuelFlowMaxKgPerHour { get; set; } = 100;
    public double RevLimit { get; set; } = 15000;
    public int ForwardGears { get; set; } = 8;
}

public sealed class ErsRules
{
    public double MguKMaxKw { get; set; } = 120;
    public double StoreMaxMj { get; set; } = 4.0;
    public double DeployPerLapMj { get; set; } = 4.0;
    public double HarvestPerLapMj { get; set; } = 2.0;
}

public sealed class DrsRules
{
    /// <summary>DRS is enabled once this many laps have been completed after the start or a restart.</summary>
    public int EnabledAfterLaps { get; set; } = 2;
    public double DetectionGapSeconds { get; set; } = 1.0;
    public bool DisabledWhenWetDeclared { get; set; } = true;
}

public sealed class TyreCompoundRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Pirelli { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";
    public bool Dry { get; set; } = true;
}

public sealed class TyreRules
{
    public List<TyreCompoundRule> Compounds { get; set; } = new();
    public bool MandatoryTwoDryCompounds { get; set; } = true;
    public double BlanketTempC { get; set; } = 70;
    public int SetsPerWeekend { get; set; } = 13;
}

public sealed class PitRules
{
    /// <summary>Speeding by up to this many km/h earns <see cref="SpeedingMinorPenalty"/>; above it <see cref="SpeedingMajorPenalty"/>.</summary>
    public double SpeedingMinorMaxKmh { get; set; } = 15;
    public PenaltyKind SpeedingMinorPenalty { get; set; } = PenaltyKind.Time5;
    public PenaltyKind SpeedingMajorPenalty { get; set; } = PenaltyKind.DriveThrough;
    public double SpeedingToleranceKmh { get; set; } = 0.5;
}

public sealed class TrackLimitRules
{
    /// <summary>Strike number at which the black-and-white flag is shown.</summary>
    public int BlackAndWhiteAtStrike { get; set; } = 3;
    /// <summary>Strike number from which each further offence is penalised.</summary>
    public int PenaltyFromStrike { get; set; } = 4;
    public PenaltyKind Penalty { get; set; } = PenaltyKind.Time5;
    /// <summary>All four wheels beyond the white line by more than this (m) counts as leaving the track.</summary>
    public double WheelTolerance { get; set; } = 0.0;
}

public sealed class SafetyCarRules
{
    public double VscDeltaFactor { get; set; } = 1.4;
    public double ScDeltaFactor { get; set; } = 1.35;
    public double MaxGapCarLengths { get; set; } = 10;
    public bool LappedCarsMayUnlap { get; set; } = true;
    public double VscEndingWarningSeconds { get; set; } = 12;
    public double ScSpeedKmh { get; set; } = 160;
}

public sealed class QualifyingRules
{
    public double Q1Minutes { get; set; } = 18;
    public double Q2Minutes { get; set; } = 15;
    public double Q3Minutes { get; set; } = 12;
    public int EliminatedAfterQ1 { get; set; } = 5;
    public int EliminatedAfterQ2 { get; set; } = 5;
    public double Rule107Percent { get; set; } = 107;
}

public sealed class StartRules
{
    public double LightIntervalSeconds { get; set; } = 1.0;
    public double LightsOutMinDelay { get; set; } = 0.2;
    public double LightsOutMaxDelay { get; set; } = 3.0;
    /// <summary>Movement (m) before lights out that counts as a jump start.</summary>
    public double JumpStartDistance { get; set; } = 0.5;
}

/// <summary>Baseline penalties from the FIA 2025 Formula 1 penalty guidelines (race column).</summary>
public sealed class PenaltyTable
{
    public PenaltyKind JumpStart { get; set; } = PenaltyKind.Time10;
    public PenaltyKind CausingCollision { get; set; } = PenaltyKind.Time10;
    public PenaltyKind ForcingOffTrack { get; set; } = PenaltyKind.Time10;
    public PenaltyKind LeavingTrackGainingAdvantage { get; set; } = PenaltyKind.Time10;
    public PenaltyKind YellowFlagOvertake { get; set; } = PenaltyKind.Time10;
    public PenaltyKind DoubleYellowOvertake { get; set; } = PenaltyKind.StopGo10;
    public PenaltyKind IgnoringBlueFlags { get; set; } = PenaltyKind.Time5;
    public int BlueFlagsBeforePenalty { get; set; } = 3;
    public PenaltyKind SafetyCarOvertake { get; set; } = PenaltyKind.Time10;
    public PenaltyKind VscSpeeding { get; set; } = PenaltyKind.Time5;
    public PenaltyKind UnsafeRelease { get; set; } = PenaltyKind.Time5;
    public PenaltyKind PitEntryLine { get; set; } = PenaltyKind.Time5;
    public PenaltyKind PitExitLine { get; set; } = PenaltyKind.Time5;
    public PenaltyKind RedLightAtPitExit { get; set; } = PenaltyKind.StopGo10;
    public PenaltyKind TwoCompoundRule { get; set; } = PenaltyKind.Disqualification;
    /// <summary>Drive-throughs and stop-go penalties must be served within this many laps.</summary>
    public int ServeWithinLaps { get; set; } = 3;
}
