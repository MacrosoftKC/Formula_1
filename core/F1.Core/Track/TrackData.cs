using F1.Core.Geometry;

namespace F1.Core.Track;

/// <summary>
/// Serialised circuit description produced by tools/TrackBuilder and loaded by the game.
/// All per-sample arrays are indexed like <see cref="Centerline"/> (uniform spacing, closed loop,
/// sample 0 on the start/finish line, ordered in race direction). Lateral offsets are positive to the left.
/// </summary>
public sealed class TrackData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string Attribution { get; set; } = "";

    public double OfficialLength { get; set; }
    public int RaceLaps { get; set; }
    public bool Clockwise { get; set; }

    /// <summary>Geographic origin of the local metric frame and the uniform scale applied to match the official length.</summary>
    public double OriginLat { get; set; }
    public double OriginLon { get; set; }
    public double Scale { get; set; } = 1.0;

    public double SampleSpacing { get; set; }
    public List<Vec2> Centerline { get; set; } = new();
    public List<double> WidthLeft { get; set; } = new();
    public List<double> WidthRight { get; set; } = new();

    /// <summary>Distance from centreline to the barrier on each side.</summary>
    public List<double> BarrierLeft { get; set; } = new();
    public List<double> BarrierRight { get; set; } = new();

    /// <summary>
    /// One char per sample: G = grass, R = gravel, A = asphalt run-off (pit wall section), P = pit lane branch
    /// (no barrier on this side; the pit lane has its own walls).
    /// </summary>
    public string RunoffLeft { get; set; } = "";
    public string RunoffRight { get; set; } = "";

    /// <summary>One char per sample: K = kerb on that edge, '-' = none.</summary>
    public string KerbLeft { get; set; } = "";
    public string KerbRight { get; set; } = "";

    /// <summary>Officially numbered corners (2022+ layout numbering).</summary>
    public List<Corner> Corners { get; set; } = new();
    /// <summary>Unnumbered bends and kinks (taken flat out or nearly so). Number is 0.</summary>
    public List<Corner> Bends { get; set; } = new();
    /// <summary>Track distance where each sector starts. Sector 1 starts at 0.</summary>
    public List<double> SectorStarts { get; set; } = new();
    public List<DrsZone> DrsZones { get; set; } = new();
    public List<GridSlot> Grid { get; set; } = new();
    public PitLaneData PitLane { get; set; } = new();
    public double SafetyCarLine1 { get; set; }
    public double SafetyCarLine2 { get; set; }
    public List<MarshalPost> MarshalPosts { get; set; } = new();

    /// <summary>Water body rings (first is the outer shoreline of the lake).</summary>
    public List<List<Vec2>> Lake { get; set; } = new();
}

public sealed class Corner
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public double EntryS { get; set; }
    public double ApexS { get; set; }
    public double ExitS { get; set; }
    /// <summary>+1 left-hander, -1 right-hander.</summary>
    public int Direction { get; set; }
    public double MinRadius { get; set; }
    /// <summary>Total heading change through the corner, degrees.</summary>
    public double Angle { get; set; }
}

public sealed class DrsZone
{
    public int Number { get; set; }
    public double DetectionS { get; set; }
    public double ActivationS { get; set; }
    public double EndS { get; set; }
}

public sealed class GridSlot
{
    public int Position { get; set; }
    public double S { get; set; }
    public double D { get; set; }
}

public sealed class MarshalPost
{
    public int Number { get; set; }
    public double S { get; set; }
    /// <summary>+1 left side, -1 right side.</summary>
    public int Side { get; set; }
    /// <summary>Index of the marshalling sector this post controls (flags are shown per sector).</summary>
    public int Sector { get; set; }
}

public sealed class PitLaneData
{
    public double SpeedLimitKmh { get; set; } = 80;
    /// <summary>Track distance where the pit lane leaves the circuit.</summary>
    public double EntryS { get; set; }
    /// <summary>Track distance where the pit lane rejoins the circuit.</summary>
    public double ExitS { get; set; }
    /// <summary>Pit lane centreline, sampled uniformly, from the entry branch to the exit merge.</summary>
    public List<Vec2> Path { get; set; } = new();
    /// <summary>Matching track distance for each pit path sample (wraps past the line).</summary>
    public List<double> TrackS { get; set; } = new();
    public double Width { get; set; } = 12;
    /// <summary>Pit-lane distance (along <see cref="Path"/>) where the speed limit starts / ends.</summary>
    public double LimitStart { get; set; }
    public double LimitEnd { get; set; }
    /// <summary>Pit-lane distance of each team's box (front of the car).</summary>
    public List<double> Boxes { get; set; } = new();
    /// <summary>Lateral offset of the working lane relative to the pit path (towards the garages).</summary>
    public double BoxOffset { get; set; }
    /// <summary>Pit-lane distances of the entry and exit lines (crossing them is an infringement).</summary>
    public double EntryLineEnd { get; set; }
    public double ExitLineStart { get; set; }
}
