using F1.Core.Geometry;
using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>
/// Low-poly surroundings: the Melbourne CBD and Southbank skyline to the north (landmarks at their real positions
/// relative to the park), St Kilda Road towers to the east, houses to the west and south, and Port Phillip Bay.
/// Positions are in the track's local frame (metres east/north of the park origin).
/// </summary>
public static class CityBuilder
{
    private static readonly Color[] Facades =
    {
        new(0.78f, 0.79f, 0.82f), new(0.62f, 0.66f, 0.72f), new(0.85f, 0.82f, 0.74f), new(0.45f, 0.52f, 0.62f),
        new(0.7f, 0.72f, 0.7f), new(0.35f, 0.4f, 0.5f), new(0.9f, 0.9f, 0.88f), new(0.55f, 0.58f, 0.6f),
    };

    // Beach line along Port Phillip Bay (south-west of the park).
    private static readonly Vec2 BeachA = new(-1460, 40), BeachB = new(400, -1300);

    private static bool OnBaySide(Vec2 p)
    {
        var dir = (BeachB - BeachA).Normalized();
        return Vec2.Cross(dir, p - BeachA) < 0; // right of A→B = south-west
    }

    public static void Build(Node3D parent, TrackSurface ts, SceneryMask mask, int seed = 1996)
    {
        var root = new Node3D { Name = "City" };
        parent.AddChild(root);
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        BuildBay(root);
        BuildSkyline(root, rng);
        BuildStKildaRoad(root, rng);
        BuildHouses(root, mask, rng);
    }

    private static void BuildBay(Node3D root)
    {
        var dir = (BeachB - BeachA).Normalized();
        var sw = new Vec2(dir.Y, -dir.X); // perpendicular towards the bay
        var a = BeachA - dir * 7000; var b = BeachB + dir * 7000;
        var m = new LowPolyMesh();
        m.QuadUp(Units.ToWorld(a, 0.02f), Units.ToWorld(b, 0.02f), Units.ToWorld(b + sw * 9000, 0.02f), Units.ToWorld(a + sw * 9000, 0.02f), Colors.White);
        root.AddChild(new MeshInstance3D { Name = "PortPhillipBay", Mesh = m.ToMesh(Materials.Water), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // sand strip
        var sand = new LowPolyMesh();
        sand.QuadUp(Units.ToWorld(a - sw * 35, 0.015f), Units.ToWorld(b - sw * 35, 0.015f), Units.ToWorld(b + sw * 2, 0.015f), Units.ToWorld(a + sw * 2, 0.015f), new Color(0.9f, 0.84f, 0.66f));
        root.AddChild(sand.ToInstance("Beach", castShadows: false));
    }

    private static void Tower(LowPolyMesh m, Vec2 at, float w, float d, float h, Color c, float rot, int setbacks = 0)
    {
        var basis = new Basis(Vector3.Up, rot);
        var pos = Units.ToWorld(at);
        float y = 0;
        float segH = h / (setbacks + 1);
        for (int k = 0; k <= setbacks; k++)
        {
            float shrink = 1f - 0.16f * k;
            m.Box(pos + new Vector3(0, y + segH * 0.5f, 0), new Vector3(w * 0.5f * shrink, segH * 0.5f, d * 0.5f * shrink), basis, k % 2 == 0 ? c : c.Lightened(0.05f));
            y += segH;
        }
    }

    private static void BuildSkyline(Node3D root, RandomNumberGenerator rng)
    {
        var m = new LowPolyMesh();
        // Landmarks
        var gold = new Color(0.86f, 0.71f, 0.32f);
        var blueGlass = new Color(0.28f, 0.34f, 0.46f);
        // Eureka Tower: slim dark-blue tower with a gold crown
        Tower(m, new Vec2(-320, 3140), 38, 44, 265, blueGlass, 0.1f);
        m.Box(Units.ToWorld(new Vec2(-320, 3140)) + new Vector3(0, 281, 0), new Vector3(17, 16, 20), new Basis(Vector3.Up, 0.1f), gold);
        // Australia 108: tallest, bronze, with the wider "starburst" section near the top
        Tower(m, new Vec2(-80, 2874), 40, 34, 230, new Color(0.62f, 0.55f, 0.45f), 0.3f);
        m.Box(Units.ToWorld(new Vec2(-80, 2874)) + new Vector3(0, 245, 0), new Vector3(28, 15, 22), new Basis(Vector3.Up, 0.3f), gold.Darkened(0.1f));
        Tower(m, new Vec2(-80, 2874) + new Vec2(4, 0), 30, 26, 317, new Color(0.62f, 0.55f, 0.45f), 0.3f);
        // Rialto Towers: dark glass twin
        Tower(m, new Vec2(-880, 3450), 42, 42, 251, new Color(0.18f, 0.24f, 0.36f), 0.0f, 2);
        Tower(m, new Vec2(-850, 3420), 38, 30, 185, new Color(0.2f, 0.26f, 0.38f), 0.0f, 1);
        // 120 Collins Street with its spire
        Tower(m, new Vec2(220, 3905), 44, 44, 220, new Color(0.7f, 0.68f, 0.64f), 0.12f, 2);
        m.Prism(Units.ToWorld(new Vec2(220, 3905)) + new Vector3(0, 220, 0), 1.5f, 0.2f, 45, 4, new Color(0.85f, 0.85f, 0.85f));
        // Arts Centre spire
        m.Prism(Units.ToWorld(new Vec2(53, 3190)) + new Vector3(0, 0, 0), 22, 0.5f, 162, 8, new Color(0.85f, 0.86f, 0.9f));
        // Prima Pearl
        Tower(m, new Vec2(-700, 2640), 34, 34, 254, new Color(0.74f, 0.76f, 0.8f), 0.4f);

        // Generic CBD / Southbank towers, denser and taller towards the centre of the grid
        var cbd = new Vec2(-430, 3990);
        for (int i = 0; i < 170; i++)
        {
            var p = new Vec2(rng.RandfRange(-1800, 1000), rng.RandfRange(2600, 4800));
            double dc = Vec2.Distance(p, cbd);
            float maxH = (float)Math.Max(45, 230 - dc * 0.11);
            float h = rng.RandfRange(30, maxH);
            float w = rng.RandfRange(18, 42), d = rng.RandfRange(18, 42);
            Tower(m, p, w, d, h, Facades[rng.RandiRange(0, Facades.Length - 1)], rng.RandfRange(-0.15f, 0.15f) + 0.08f, h > 120 ? rng.RandiRange(0, 2) : 0);
        }
        root.AddChild(new MeshInstance3D { Name = "Skyline", Mesh = m.ToMesh(Materials.VertexColor), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    private static void BuildStKildaRoad(Node3D root, RandomNumberGenerator rng)
    {
        var m = new LowPolyMesh();
        var a = new Vec2(1290, -600); var b = new Vec2(690, 1500);
        for (int i = 0; i < 38; i++)
        {
            var p = Vec2.Lerp(a, b, i / 37.0) + new Vec2(rng.RandfRange(60, 160), rng.RandfRange(-20, 20));
            Tower(m, p, rng.RandfRange(22, 40), rng.RandfRange(22, 40), rng.RandfRange(25, 105), Facades[rng.RandiRange(0, Facades.Length - 1)], -0.28f);
        }
        root.AddChild(m.ToInstance("StKildaRoad", castShadows: false));
    }

    private static void BuildHouses(Node3D root, SceneryMask mask, RandomNumberGenerator rng)
    {
        var placements = new List<(Transform3D, Color)>();
        Color[] walls = { new(0.95f, 0.92f, 0.85f), new(0.8f, 0.55f, 0.42f), new(0.9f, 0.9f, 0.9f), new(0.85f, 0.78f, 0.62f), new(0.7f, 0.74f, 0.78f) };
        const float Grid = 22f;
        for (float y = -1800; y < 1700; y += Grid)
            for (float x = -2200; x < 1400; x += Grid)
            {
                var p = new Vec2(x + rng.RandfRange(-4, 4), y + rng.RandfRange(-4, 4));
                if (OnBaySide(p)) continue;
                if (mask.WaterAt(p) != SceneryMask.Land || mask.Blocked(p)) continue;
                float dist = mask.TrackDistance(p);
                if (dist < 300 || dist > 1600) continue;          // the park around the circuit stays open
                if (x > 600 && y > -700) continue;                // golf course / St Kilda Road strip
                if (rng.Randf() > 0.55f) continue;
                float rot = -0.62f + (rng.Randf() < 0.5f ? 0 : Mathf.Pi / 2);
                var basis = new Basis(Vector3.Up, rot).Scaled(new Vector3(rng.RandfRange(0.8f, 1.3f), rng.RandfRange(0.8f, 1.5f), rng.RandfRange(0.8f, 1.4f)));
                placements.Add((new Transform3D(basis, Units.ToWorld(p)), walls[rng.RandiRange(0, walls.Length - 1)]));
            }
        if (placements.Count == 0) return;
        var house = new LowPolyMesh();
        house.Box(new Vector3(0, 2.5f, 0), new Vector3(4.5f, 2.5f, 6f), Colors.White);
        var roof = new Color(0.62f, 0.3f, 0.24f);
        var r0 = new Vector3(-4.9f, 5f, -6.4f); var r1 = new Vector3(4.9f, 5f, -6.4f);
        var r2 = new Vector3(4.9f, 5f, 6.4f); var r3 = new Vector3(-4.9f, 5f, 6.4f);
        var ridgeA = new Vector3(0, 7.6f, -6.4f); var ridgeB = new Vector3(0, 7.6f, 6.4f);
        house.QuadFacing(r1, ridgeA, ridgeB, r2, new Vector3(1, 1, 0), roof);
        house.QuadFacing(r0, ridgeA, ridgeB, r3, new Vector3(-1, 1, 0), roof.Darkened(0.1f));
        house.Tri(r0, ridgeA, r1, Colors.White.Darkened(0.05f));
        house.Tri(r3, r2, ridgeB, Colors.White.Darkened(0.05f));
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = house.ToMesh(Materials.VertexColorDoubleSided) };
        mm.InstanceCount = placements.Count;
        for (int i = 0; i < placements.Count; i++) { mm.SetInstanceTransform(i, placements[i].Item1); mm.SetInstanceColor(i, placements[i].Item2); }
        root.AddChild(new MultiMeshInstance3D { Name = "Houses", Multimesh = mm, VisibilityRangeEnd = 2500, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }
}
