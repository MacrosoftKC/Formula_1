using System.Text.Json;
using F1.Core.Data;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.Session;

/// <summary>One recorded pose of a lap: time into the lap, distance into the lap, position and rotation.</summary>
public readonly record struct GhostSample(float T, float D, Vector3 P, Quaternion Q);

/// <summary>A lap stored for the PB ghost and the live delta.</summary>
public sealed class GhostLap
{
    public List<GhostSample> Samples { get; } = new();

    /// <summary>Time at which the ghost had covered <paramref name="distance"/> metres of the lap (for the live delta).</summary>
    public double? TimeAtDistance(double distance)
    {
        if (Samples.Count < 2 || distance < Samples[0].D) return null;
        int lo = 0, hi = Samples.Count - 1;
        if (distance >= Samples[hi].D) return null;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Samples[mid].D <= distance) lo = mid; else hi = mid; }
        var a = Samples[lo]; var b = Samples[hi];
        double t = (distance - a.D) / Math.Max(1e-6, b.D - a.D);
        return a.T + (b.T - a.T) * t;
    }

    public Transform3D? PoseAtTime(double time)
    {
        if (Samples.Count < 2 || time < 0 || time > Samples[^1].T) return null;
        int lo = 0, hi = Samples.Count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Samples[mid].T <= time) lo = mid; else hi = mid; }
        var a = Samples[lo]; var b = Samples[hi];
        float t = (float)((time - a.T) / Math.Max(1e-6, b.T - a.T));
        return new Transform3D(new Basis(a.Q.Slerp(b.Q, t)), a.P.Lerp(b.P, t));
    }

    public float[][] ToArray() => Samples.Select(s => new[] { s.T, s.D, s.P.X, s.P.Y, s.P.Z, s.Q.X, s.Q.Y, s.Q.Z, s.Q.W }).ToArray();

    public static GhostLap FromArray(float[][] data)
    {
        var g = new GhostLap();
        foreach (var a in data)
            if (a.Length == 9) g.Samples.Add(new GhostSample(a[0], a[1], new Vector3(a[2], a[3], a[4]), new Quaternion(a[5], a[6], a[7], a[8])));
        return g;
    }
}

/// <summary>Personal-best record for a track.</summary>
public sealed class TrackRecord
{
    public double BestLap { get; set; }
    public double[] Sectors { get; set; } = new double[3];
    public string TeamId { get; set; } = "";
    public string Compound { get; set; } = "";
    public string Date { get; set; } = "";
    public float[][] Ghost { get; set; } = Array.Empty<float[]>();
}

/// <summary>Time Trial records in user://records.json.</summary>
public static class Records
{
    public const string Path = "user://records.json";

    public static Dictionary<string, TrackRecord> Load()
    {
        try
        {
            if (FileAccess.FileExists(Path))
                return JsonSerializer.Deserialize<Dictionary<string, TrackRecord>>(FileAccess.GetFileAsString(Path)) ?? new();
        }
        catch (Exception e) { GD.PushWarning($"records.json unreadable: {e.Message}"); }
        return new();
    }

    public static void Save(Dictionary<string, TrackRecord> records)
    {
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(records));
    }
}

/// <summary>Translucent car replaying a ghost lap.</summary>
public partial class GhostCar : Node3D
{
    public GhostLap? Lap { get; set; }

    public void Build(TeamData team, DriverData driver)
    {
        var model = new CarModel { Name = "GhostModel" };
        AddChild(model);
        model.Build(team, driver);
        foreach (var node in model.FindChildren("*", "GeometryInstance3D", true, false))
            if (node is GeometryInstance3D g)
            {
                g.Transparency = 0.6f;
                g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
    }

    public void ShowAt(double lapTime)
    {
        var pose = Lap?.PoseAtTime(lapTime);
        Visible = pose.HasValue;
        if (pose.HasValue) GlobalTransform = pose.Value;
    }
}
