using System.Text.Json;
using F1.Core.Geometry;

namespace TrackBuilder;

public sealed record OsmWay(long Id, Dictionary<string, string> Tags, List<(double Lat, double Lon)> Geometry);

/// <summary>Minimal reader for Overpass "out tags geom" JSON plus an equirectangular projection to local metres.</summary>
public static class Osm
{
    public static List<OsmWay> ReadWays(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var ways = new List<OsmWay>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (el.GetProperty("type").GetString() != "way" || !el.TryGetProperty("geometry", out var geom)) continue;
            var tags = new Dictionary<string, string>();
            if (el.TryGetProperty("tags", out var t))
                foreach (var p in t.EnumerateObject()) tags[p.Name] = p.Value.GetString() ?? "";
            var pts = geom.EnumerateArray().Select(g => (g.GetProperty("lat").GetDouble(), g.GetProperty("lon").GetDouble())).ToList();
            ways.Add(new OsmWay(el.GetProperty("id").GetInt64(), tags, pts));
        }
        return ways;
    }

    /// <summary>Projects WGS84 to a local tangent plane (X east, Y north) around the origin.</summary>
    public static Vec2 Project(double lat, double lon, double lat0, double lon0)
    {
        // WGS84 metres per degree at the origin latitude.
        double phi = lat0 * Math.PI / 180;
        double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2 * phi) + 1.175 * Math.Cos(4 * phi);
        double mPerDegLon = 111412.84 * Math.Cos(phi) - 93.5 * Math.Cos(3 * phi);
        return new Vec2((lon - lon0) * mPerDegLon, (lat - lat0) * mPerDegLat);
    }

    /// <summary>Downloads an Overpass query result to <paramref name="cachePath"/> unless it is already cached.</summary>
    public static async Task EnsureCachedAsync(string cachePath, string query)
    {
        if (File.Exists(cachePath)) return;
        string[] endpoints =
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.private.coffee/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
        };
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(150) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("lowpoly-f1-trackbuilder/0.1");
        foreach (var ep in endpoints)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    var body = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) });
                    var resp = await http.PostAsync(ep, body);
                    if (resp.IsSuccessStatusCode)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                        await File.WriteAllTextAsync(cachePath, await resp.Content.ReadAsStringAsync());
                        Console.WriteLine($"  fetched {Path.GetFileName(cachePath)} from {ep}");
                        return;
                    }
                    Console.WriteLine($"  {ep} -> {(int)resp.StatusCode}, retrying");
                }
                catch (Exception ex) { Console.WriteLine($"  {ep} -> {ex.Message}"); }
                await Task.Delay(TimeSpan.FromSeconds(5 * (1 << attempt)));
            }
        }
        throw new InvalidOperationException($"Could not download OSM data for {cachePath}; Overpass is unavailable.");
    }

    /// <summary>Joins multipolygon member ways into closed rings by matching endpoints.</summary>
    public static List<List<Vec2>> AssembleRings(IEnumerable<List<Vec2>> parts, double snap = 0.5)
    {
        var pool = parts.Where(p => p.Count >= 2).Select(p => p.ToList()).ToList();
        var rings = new List<List<Vec2>>();
        while (pool.Count > 0)
        {
            var ring = pool[0];
            pool.RemoveAt(0);
            bool grew = true;
            while (Vec2.Distance(ring[0], ring[^1]) > snap && grew)
            {
                grew = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    var p = pool[i];
                    if (Vec2.Distance(ring[^1], p[0]) <= snap) ring.AddRange(p.Skip(1));
                    else if (Vec2.Distance(ring[^1], p[^1]) <= snap) { p.Reverse(); ring.AddRange(p.Skip(1)); }
                    else continue;
                    pool.RemoveAt(i);
                    grew = true;
                    break;
                }
            }
            if (Vec2.Distance(ring[0], ring[^1]) <= snap) ring.RemoveAt(ring.Count - 1);
            if (ring.Count >= 3) rings.Add(ring);
        }
        return rings;
    }
}
