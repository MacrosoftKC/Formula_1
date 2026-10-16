using F1.Core.Data;
using F1.Core.Track;
using Godot;

namespace F1.Game.Core;

/// <summary>Loads the JSON data files that ship under res://data.</summary>
public static class DataFiles
{
    public const string TeamsPath = "res://data/teams_2025.json";
    public const string RegulationsPath = "res://data/regulations_2025.json";
    public const string AlbertParkPath = "res://data/tracks/albert_park/track.json";

    public static T Load<T>(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new System.IO.FileNotFoundException($"Could not read {path}: {FileAccess.GetOpenError()}");
        return F1.Core.Data.Json.Deserialize<T>(text);
    }

    public static TeamsFile LoadTeams() => Load<TeamsFile>(TeamsPath);
    public static RegulationsFile LoadRegulations() => Load<RegulationsFile>(RegulationsPath);
    public static TrackData LoadTrack(string path = AlbertParkPath) => Load<TrackData>(path);
}
