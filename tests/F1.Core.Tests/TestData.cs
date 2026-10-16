using F1.Core.Data;
using F1.Core.Track;

namespace F1.Core.Tests;

/// <summary>Locates the repository's game data files from the test output directory.</summary>
public static class TestData
{
    public static string RepoRoot { get; } = FindRoot();

    public static string GameData(params string[] parts) =>
        Path.Combine(new[] { RepoRoot, "game", "data" }.Concat(parts).ToArray());

    private static readonly Lazy<TrackData> _albertPark = new(() =>
        Json.Deserialize<TrackData>(File.ReadAllText(GameData("tracks", "albert_park", "track.json"))));

    public static TrackData AlbertPark => _albertPark.Value;

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Formula1.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
