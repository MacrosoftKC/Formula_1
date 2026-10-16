namespace F1.Core.Data;

/// <summary>Grid definition loaded from data/teams_2025.json. Edit that file to change names, colours or performance.</summary>
public sealed class TeamsFile
{
    public int Season { get; set; }
    public string Event { get; set; } = "";
    public List<TeamData> Teams { get; set; } = new();

    public IEnumerable<(TeamData Team, DriverData Driver)> AllDrivers() =>
        Teams.SelectMany(t => t.Drivers.Select(d => (t, d)));
}

public sealed class TeamData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string PowerUnit { get; set; } = "";
    /// <summary>Livery colours as #RRGGBB.</summary>
    public string Primary { get; set; } = "#FFFFFF";
    public string Secondary { get; set; } = "#000000";
    public string Accent { get; set; } = "#FF0000";
    public string Rim { get; set; } = "#202020";
    /// <summary>Optional per-part livery colours; null falls back to Primary/Secondary/Accent.</summary>
    public LiveryParts Livery { get; set; } = new();
    public CarPerformance Car { get; set; } = new();
    public List<DriverData> Drivers { get; set; } = new();
}

/// <summary>Optional colour overrides for individual car parts (#RRGGBB).</summary>
public sealed class LiveryParts
{
    public string? NoseTip { get; set; }
    public string? Sidepod { get; set; }
    public string? EngineCover { get; set; }
    public string? Airbox { get; set; }
    public string? FrontWing { get; set; }
    public string? Endplates { get; set; }
    public string? RearWing { get; set; }
    public string? Halo { get; set; }
    public string? Text { get; set; }
}

/// <summary>Relative car performance multipliers (1.0 = reference car).</summary>
public sealed class CarPerformance
{
    /// <summary>Power unit output multiplier.</summary>
    public double Power { get; set; } = 1.0;
    /// <summary>Downforce multiplier (cornering grip from aero).</summary>
    public double Downforce { get; set; } = 1.0;
    /// <summary>Drag multiplier (lower is faster on straights).</summary>
    public double Drag { get; set; } = 1.0;
    /// <summary>Tyre wear multiplier (lower is kinder to tyres).</summary>
    public double TyreWear { get; set; } = 1.0;
    /// <summary>Chance-free running, 0-100. Used for optional mechanical failures.</summary>
    public double Reliability { get; set; } = 95;
    /// <summary>Average pit stop stationary time in seconds.</summary>
    public double PitStopTime { get; set; } = 2.5;
}

public sealed class DriverData
{
    public int Number { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Code { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string Helmet { get; set; } = "#FFFFFF";
    public DriverRatings Ratings { get; set; } = new();

    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>Driver skill ratings, 0-100.</summary>
public sealed class DriverRatings
{
    public double Pace { get; set; } = 80;
    public double Consistency { get; set; } = 80;
    public double Racecraft { get; set; } = 80;
    public double TyreManagement { get; set; } = 80;
    public double Wet { get; set; } = 80;
    public double Starts { get; set; } = 80;
}
