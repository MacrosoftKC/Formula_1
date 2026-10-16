namespace TrackBuilder;

/// <summary>
/// Source description of Albert Park. The circuit is a temporary street circuit, so OpenStreetMap has no
/// highway=raceway; the lap is assembled from the public park roads it runs on, in race (clockwise) order.
/// </summary>
public static class AlbertPark
{
    public const string Id = "albert_park";
    public const string Name = "Albert Park Circuit";
    public const string Location = "Melbourne, Australia";
    public const double OfficialLength = 5278.0;
    public const int RaceLaps = 58;
    public const double PitSpeedLimitKmh = 80;

    public const double OriginLat = -37.8497;
    public const double OriginLon = 144.968;

    /// <summary>Start/finish line position on the main straight (Aughtie Drive).</summary>
    public const double StartLat = -37.849757;
    public const double StartLon = 144.968644;

    public const long LakeRelationId = 346429;

    public const string RoadsQuery =
        "[out:json][timeout:90];(way[\"highway\"](-37.8640,144.9560,-37.8380,144.9800);" +
        "way[\"natural\"=\"water\"](-37.8640,144.9560,-37.8380,144.9800);" +
        "relation[\"natural\"=\"water\"](-37.8640,144.9560,-37.8380,144.9800);" +
        "way[\"leisure\"](-37.8640,144.9560,-37.8380,144.9800););out tags geom;";

    public const string LakeQuery = "[out:json][timeout:60];relation(346429);way(r);out geom;";

    /// <summary>OSM way IDs forming one lap, starting at the south-east end of the main straight heading north-west.</summary>
    public static readonly long[] LapWays =
    {
        784076847,  // Aughtie Drive – main straight (start/finish)
        987030977,  // Aughtie Drive – Turns 1-2
        784076843,  // Aughtie Drive
        784076839,  // Aughtie Drive – run to Turn 3 and the western section
        987030969,  // Aughtie Drive
        784076845,  // Aughtie Drive
        784087844,  // Aughtie Drive (single carriageway piece)
        784087849,  // Aughtie Drive (single carriageway piece)
        784077758,  // Aughtie Drive
        987030966,  // Aughtie Drive
        784083210,  // Albert Road Drive – northern section
        987030982,  // Albert Road Drive
        1050371045, // Albert Road Drive link onto Lakeside Drive
        1013633269, // Lakeside Drive
        1050342733, // Lakeside Drive
        1021617920, // Lakeside Drive
        1021617874, // Lakeside Drive
        15856988,   // Lakeside Drive – fast lakeside section
        987030967,  // Lakeside Drive
        987030968,  // Lakeside Drive
        784071296,  // Lakeside Drive
        979723479,  // Lakeside Drive
        979723477,  // Lakeside Drive
        1253159342, // Lakeside Drive
        979723478,  // Lakeside Drive
        1126808201, // Lakeside Drive
        987030972,  // Lakeside Drive
        987030985,  // Ross Gregory Drive – final corners
        987030974,  // Aughtie Drive – onto the main straight
        987030973,  // Aughtie Drive
    };

    /// <summary>
    /// Approximate apex of each official corner (2022+ numbering) in the unscaled local frame. Detected curvature peaks are
    /// matched to these; anything unmatched (road kinks, the flat-out section where the old T9/T10 chicane was) becomes a bend.
    /// </summary>
    public static readonly (int Number, double X, double Y)[] OfficialApexes =
    {
        (1, -176, 232), (2, -159, 352), (3, -514, 835), (4, -456, 857), (5, -379, 1088), (6, -5, 1306), (7, 40, 1261),
        (8, 209, 1216), (9, 477, 182), (10, 596, 177), (11, 935, -407), (12, 699, -465), (13, 538, -263), (14, 425, -353),
    };

    /// <summary>
    /// Extra local smoothing where the public-road junction geometry is tighter than the re-profiled circuit
    /// (centre in the unscaled local frame, influence radius and Gaussian sigma in metres).
    /// </summary>
    public static readonly (double X, double Y, double Radius, double Sigma)[] LocalSmoothing =
    {
        (-5, 1306, 90, 12),   // T6, widened by 7.5 m in 2021
        (-400, 990, 75, 10),  // junction wiggle between T4 and T5
        (935, -407, 80, 9),   // T11, entry straightened and widened
        (-176, 232, 50, 6),   // T1
        (538, -263, 40, 5),   // T13
    };

    /// <summary>
    /// Minimum centreline radius (m) around corners built on tight public-road junctions, so apex speeds match the
    /// re-profiled circuit rather than the road (centre in the unscaled local frame, influence radius, minimum radius).
    /// </summary>
    public static readonly (double X, double Y, double Radius, double MinRadius)[] MinimumRadius =
    {
        (-176, 232, 70, 30),    // T1
        (-514, 835, 60, 24),    // T3
        (-379, 1088, 60, 32),   // T5
        (-5, 1306, 110, 55),    // T6 (widened by 7.5 m in 2021, now a fast corner)
        (935, -407, 100, 36),   // T11 (entry straightened in 2021)
        (699, -465, 80, 36),    // T12
        (538, -263, 60, 25),    // T13
        (425, -353, 80, 34),    // T14
    };

    /// <summary>Extra width (metres) from the 2021 re-profiling, keyed by 2022+ corner number.</summary>
    public static readonly Dictionary<int, double> CornerWidening = new()
    {
        [1] = 2.5, [3] = 4.0, [6] = 7.5, [11] = 3.5, [13] = 3.5,
    };

    public const double BaseWidth = 12.5;
    public const double MainStraightWidth = 15.0;

    /// <summary>Lateral offset (right of the track, negative) of the pit lane fast-lane centre on the pit straight.</summary>
    public const double PitLaneOffset = -19.0;
    public const double PitBoxOffset = -5.5;
    public const double PitLaneWidth = 12.0;
}
