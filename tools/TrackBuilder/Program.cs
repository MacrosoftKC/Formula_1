using F1.Core.Data;
using TrackBuilder;

// Builds game/data/tracks/albert_park/track.json from cached OpenStreetMap data.
// Usage: dotnet run --project tools/TrackBuilder [-- <repoRoot>]

string root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
string cache = Path.Combine(root, "tools", "TrackBuilder", "osm_cache");
string roadsPath = Path.Combine(cache, "albert_park_raw.json");
string lakePath = Path.Combine(cache, "albert_park_lake.json");

Console.WriteLine("Albert Park track builder");
await Osm.EnsureCachedAsync(roadsPath, AlbertPark.RoadsQuery);
await Osm.EnsureCachedAsync(lakePath, AlbertPark.LakeQuery);

var processor = new TrackProcessor(Osm.ReadWays(roadsPath), Osm.ReadWays(lakePath));
var track = processor.Build();
foreach (var line in processor.Log) Console.WriteLine("  " + line);

string outDir = Path.Combine(root, "game", "data", "tracks", AlbertPark.Id);
Directory.CreateDirectory(outDir);
string outPath = Path.Combine(outDir, "track.json");
File.WriteAllText(outPath, Json.Serialize(track));
Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length / 1024} KB)");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Formula1.sln"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run from inside the repository or pass the repo root.");
}
