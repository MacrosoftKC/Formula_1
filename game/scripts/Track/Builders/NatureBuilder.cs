using F1.Core.Geometry;
using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Faceted grass ground and instanced low-poly trees (eucalypts, round trees, palms, conifers).</summary>
public static class NatureBuilder
{
    public static readonly Color Grass = new(0.42f, 0.72f, 0.2f);
    public const uint CollisionLayerGround = 1 << 0;

    public static (Vector2 Min, Vector2 Max) Bounds(TrackSurface ts, float margin)
    {
        var pts = ts.Data.Centerline;
        float minX = (float)pts.Min(p => p.X) - margin, maxX = (float)pts.Max(p => p.X) + margin;
        float minY = (float)pts.Min(p => p.Y) - margin, maxY = (float)pts.Max(p => p.Y) + margin;
        return (new Vector2(minX, minY), new Vector2(maxX, maxY));
    }

    public static void BuildGround(Node3D parent, TrackSurface ts)
    {
        var root = new Node3D { Name = "Ground" };
        parent.AddChild(root);
        var (min, max) = Bounds(ts, 900);
        const float Cell = 40f;
        int nx = (int)Math.Ceiling((max.X - min.X) / Cell), ny = (int)Math.Ceiling((max.Y - min.Y) / Cell);
        const int ChunkCells = 16;
        for (int cy = 0; cy < ny; cy += ChunkCells)
            for (int cx = 0; cx < nx; cx += ChunkCells)
            {
                var m = new LowPolyMesh();
                for (int y = cy; y < Math.Min(ny, cy + ChunkCells); y++)
                    for (int x = cx; x < Math.Min(nx, cx + ChunkCells); x++)
                    {
                        float x0 = min.X + x * Cell, y0 = min.Y + y * Cell;
                        var a = Units.ToWorld(new Vec2(x0, y0)); var b = Units.ToWorld(new Vec2(x0 + Cell, y0));
                        var c = Units.ToWorld(new Vec2(x0 + Cell, y0 + Cell)); var d = Units.ToWorld(new Vec2(x0, y0 + Cell));
                        float v = Hash(x, y) * 0.07f - 0.035f;
                        var g1 = Grass.Lightened(Math.Max(0, v)).Darkened(Math.Max(0, -v));
                        var g2 = g1.Darkened(0.025f + Hash(y, x) * 0.02f);
                        m.TriUp(a, b, c, g1);
                        m.TriUp(a, c, d, g2);
                    }
                root.AddChild(m.ToInstance($"Ground_{cx}_{cy}", castShadows: false));
            }

        // distant ground to the horizon
        var far = new LowPolyMesh();
        float F = 12000f;
        far.QuadUp(new Vector3(-F, -0.05f, -F), new Vector3(F, -0.05f, -F), new Vector3(F, -0.05f, F), new Vector3(-F, -0.05f, F), Grass.Darkened(0.12f));
        root.AddChild(far.ToInstance("FarGround", castShadows: false));

        var body = new StaticBody3D { Name = "GroundCollision", CollisionLayer = CollisionLayerGround, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(Vector3.Up, 0) } });
        body.PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f };
        root.AddChild(body);
    }

    /// <summary>Scatters trees across the park, avoiding the circuit corridor, the lake and reserved areas.</summary>
    public static void BuildTrees(Node3D parent, TrackSurface ts, SceneryMask mask, int seed = 2025)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var (min, max) = Bounds(ts, 420);
        var types = new[] { EucalyptusMesh(), RoundTreeMesh(), PalmMesh(), ConiferMesh() };
        var placements = types.Select(_ => new List<(Transform3D T, Color C)>()).ToArray();
        const float Grid = 13f;

        for (float y = min.Y; y < max.Y; y += Grid)
            for (float x = min.X; x < max.X; x += Grid)
            {
                var p = new Vec2(x + rng.RandfRange(-5, 5), y + rng.RandfRange(-5, 5));
                byte water = mask.WaterAt(p);
                if (water == SceneryMask.Water) continue;
                bool onIsland = water == SceneryMask.Island;
                if (mask.Blocked(p)) continue;
                float shore = mask.ShoreDistance(p);
                if (shore < 6) continue;

                float distTrack = mask.TrackDistance(p);
                float density = distTrack < 250 ? 0.42f : distTrack < 400 ? 0.22f : 0.0f;
                if (shore < 35) density = 0.1f;           // keep the lake visible from the track
                if (onIsland) density = 0.7f;
                if (rng.Randf() > density) continue;

                int type;
                float r01 = rng.Randf();
                if (shore < 60 && r01 < 0.55f) type = 2;   // palms along the lake
                else if (r01 < 0.45f) type = 0;            // eucalypts
                else if (r01 < 0.88f) type = 1;            // round trees
                else type = 3;                             // conifers

                float scale = rng.RandfRange(0.8f, 1.35f);
                var basis = new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(scale, scale * rng.RandfRange(0.9f, 1.15f), scale));
                var tint = new Color(rng.RandfRange(0.85f, 1.1f), rng.RandfRange(0.9f, 1.1f), rng.RandfRange(0.85f, 1.0f));
                placements[type].Add((new Transform3D(basis, Units.ToWorld(p, onIsland ? 0.06f : 0f)), tint));
            }

        var root = new Node3D { Name = "Trees" };
        parent.AddChild(root);
        string[] names = { "Eucalypts", "RoundTrees", "Palms", "Conifers" };
        for (int t = 0; t < types.Length; t++)
        {
            if (placements[t].Count == 0) continue;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = types[t] };
            mm.InstanceCount = placements[t].Count;
            for (int i = 0; i < placements[t].Count; i++)
            {
                mm.SetInstanceTransform(i, placements[t][i].T);
                mm.SetInstanceColor(i, placements[t][i].C);
            }
            root.AddChild(new MultiMeshInstance3D { Name = names[t], Multimesh = mm, VisibilityRangeEnd = 1500 });
        }
    }

    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private static readonly Color Trunk = new(0.45f, 0.36f, 0.28f);
    private static readonly Color PaleTrunk = new(0.78f, 0.74f, 0.66f);

    private static ArrayMesh EucalyptusMesh()
    {
        var m = new LowPolyMesh();
        m.Prism(Vector3.Zero, 0.35f, 0.22f, 6.5f, 5, PaleTrunk);
        var leaf = new Color(0.38f, 0.56f, 0.27f);
        m.Blob(new Vector3(0.2f, 8.2f, 0.1f), new Vector3(3.6f, 2.6f, 3.4f), leaf, 3, 6, 1);
        m.Blob(new Vector3(-1.8f, 6.9f, 0.8f), new Vector3(2.4f, 1.8f, 2.3f), leaf.Darkened(0.05f), 3, 5, 2);
        m.Blob(new Vector3(1.6f, 7.0f, -1.2f), new Vector3(2.2f, 1.7f, 2.2f), leaf.Lightened(0.04f), 3, 5, 3);
        return m.ToMesh(Materials.VertexColor);
    }

    private static ArrayMesh RoundTreeMesh()
    {
        var m = new LowPolyMesh();
        m.Prism(Vector3.Zero, 0.3f, 0.22f, 3.2f, 5, Trunk);
        m.Blob(new Vector3(0, 5.0f, 0), new Vector3(3.2f, 3.0f, 3.2f), new Color(0.4f, 0.66f, 0.24f), 3, 7, 4);
        return m.ToMesh(Materials.VertexColor);
    }

    private static ArrayMesh PalmMesh()
    {
        var m = new LowPolyMesh();
        m.Prism(Vector3.Zero, 0.32f, 0.2f, 8.5f, 5, new Color(0.55f, 0.45f, 0.33f));
        var top = new Vector3(0, 8.5f, 0);
        var frond = new Color(0.3f, 0.6f, 0.22f);
        for (int k = 0; k < 7; k++)
        {
            float a = Mathf.Tau * k / 7;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = dir.Cross(Vector3.Up) * 0.55f;
            var mid = top + dir * 2.0f + new Vector3(0, 0.5f, 0);
            var tip = top + dir * 3.8f + new Vector3(0, -1.0f, 0);
            m.Tri(top, mid + side, mid - side, frond);
            m.Tri(top, mid - side, mid + side, frond.Darkened(0.1f));
            m.Tri(mid + side, tip, mid - side, frond.Darkened(0.05f));
            m.Tri(mid - side, tip, mid + side, frond.Darkened(0.12f));
        }
        return m.ToMesh(Materials.VertexColor);
    }

    private static ArrayMesh ConiferMesh()
    {
        var m = new LowPolyMesh();
        m.Prism(Vector3.Zero, 0.25f, 0.2f, 1.6f, 5, Trunk);
        var green = new Color(0.22f, 0.45f, 0.25f);
        m.Prism(new Vector3(0, 1.4f, 0), 2.4f, 0.9f, 3.0f, 7, green);
        m.Prism(new Vector3(0, 3.6f, 0), 1.9f, 0.5f, 2.8f, 7, green.Lightened(0.04f));
        m.Prism(new Vector3(0, 5.8f, 0), 1.3f, 0.0f, 3.0f, 7, green.Lightened(0.08f));
        return m.ToMesh(Materials.VertexColor);
    }
}
