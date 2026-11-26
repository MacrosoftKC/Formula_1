using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Low-resolution survey terrain and spatially batched Ardennes forest. Source coordinates are east, north, height.</summary>
public static class SpaLandscape
{
    private sealed class LandscapeData
    {
        public float[][] Vertices { get; set; } = Array.Empty<float[]>();
        public int[] Triangles { get; set; } = Array.Empty<int>();
        public float[][] Trees { get; set; } = Array.Empty<float[]>();
    }

    private const float GroundClearance = 0.08f;

    private static Vector3 World(float[] p) => new(p[0], p[2], -p[1]);
    private static Vector2I Cell(Vector3 p) => new(Mathf.FloorToInt(p.X / 320), Mathf.FloorToInt(p.Z / 320));

    public static void Build(Node3D parent, TrackFrames frames, bool forest)
    {
        var data = DataFiles.Load<LandscapeData>("res://data/tracks/spa/terrain.json");
        var root = new Node3D { Name = "Ardennes" };
        parent.AddChild(root);
        var verts = data.Vertices.Select(World).ToArray();
        var chunks = new Dictionary<Vector2I, LowPolyMesh>();
        var rng = new RandomNumberGenerator { Seed = 1979 };
        var moss = Color.FromHtml("#607e49");
        for (int i = 0; i < data.Triangles.Length; i += 3)
        {
            var a = verts[data.Triangles[i]];
            var b = verts[data.Triangles[i + 1]];
            var c = verts[data.Triangles[i + 2]];
            var cell = Cell((a + b + c) / 3f);
            if (!chunks.TryGetValue(cell, out var chunk)) chunks[cell] = chunk = new LowPolyMesh();
            chunk.TriUp(a, b, c, moss.Darkened(rng.RandfRange(0, .075f)));
        }
        foreach (var chunk in chunks)
            root.AddChild(chunk.Value.ToInstance($"Slope_{chunk.Key.X}_{chunk.Key.Y}", castShadows: false));

        // Wheels ride the analytic road surface; this collision floor only catches the chassis in a crash or a
        // spin-off, so it sits a little under the road and can never prop a car off its wheels.
        var ground = new StaticBody3D { Name = "Roadbed", CollisionLayer = NatureBuilder.CollisionLayerGround, CollisionMask = 0,
            Position = Vector3.Down * GroundClearance };
        root.AddChild(ground);
        for (int start = 0; start < frames.Count; start += 80)
        {
            var roadbed = new LowPolyMesh();
            for (int i = start; i < Math.Min(frames.Count, start + 80); i++)
            {
                int j = frames.Wrap(i + 1);
                float rightI = frames.RunoffRight(i) == 'P' ? frames.WR[i] + 1 : frames.BR[i];
                float rightJ = frames.RunoffRight(j) == 'P' ? frames.WR[j] + 1 : frames.BR[j];
                roadbed.QuadUp(frames.At(i, -rightI), frames.At(j, -rightJ),
                    frames.At(j, frames.BL[j]), frames.At(i, frames.BL[i]), moss);
            }
            AddCollision(ground, roadbed);
        }
        var pitbed = new LowPolyMesh();
        var ts = frames.Surface;
        float half = (float)ts.Data.PitLane.Width / 2;
        for (int i = 0; i < ts.Pit.Count - 1; i++)
        {
            double u = ts.Pit.SAt(i), v = ts.Pit.SAt(i + 1);
            pitbed.QuadUp(ts.PitPoint(u, -half - 4), ts.PitPoint(v, -half - 4),
                ts.PitPoint(v, half), ts.PitPoint(u, half), moss);
        }
        AddCollision(ground, pitbed);
        if (forest) BuildForest(root, data.Trees, rng);
    }

    private static void AddCollision(StaticBody3D body, LowPolyMesh mesh)
    {
        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(mesh.Faces());
        body.AddChild(new CollisionShape3D { Shape = shape });
    }

    private static void BuildForest(Node3D root, float[][] positions, RandomNumberGenerator rng)
    {
        var pine = new LowPolyMesh();
        pine.Prism(Vector3.Zero, .40f, .27f, 3.7f, 4, Color.FromHtml("#594d3a"));
        pine.Prism(Vector3.Up * 1.8f, 3.4f, .15f, 7.6f, 5, Color.FromHtml("#294d3b"));
        pine.Prism(Vector3.Up * 5.8f, 2.5f, 0, 6.9f, 5, Color.FromHtml("#38654b"));
        var crown = new LowPolyMesh();
        crown.Prism(Vector3.Zero, .38f, .25f, 4.5f, 4, Color.FromHtml("#69533f"));
        crown.Blob(Vector3.Up * 7.2f, new Vector3(3.8f, 4.8f, 3.5f), Color.FromHtml("#577644"), 3, 6, 9);
        var distant = new LowPolyMesh();
        distant.Prism(Vector3.Up * 1.2f, 3.7f, 0, 11.8f, 4, Color.FromHtml("#365842"));
        var pineMesh = pine.ToMesh(Materials.VertexColor);
        var broadleaf = crown.ToMesh(Materials.VertexColor);
        var low = distant.ToMesh(Materials.VertexColor);

        // A fourth value, when present, is the canopy height measured as surface model minus terrain model (metres).
        foreach (var group in positions.Select(p => (Pos: World(p), Height: p.Length > 3 ? p[3] : 0f)).GroupBy(t => Cell(t.Pos)))
        {
            var conifers = new List<Transform3D>();
            var deciduous = new List<Transform3D>();
            var far = new List<Transform3D>();
            foreach (var (point, height) in group)
            {
                float scale = rng.RandfRange(.8f, 1.55f);
                // the pine is ~12.7 m tall at scale 1; keep the measured height but within a stylised range
                float tall = height > 0 ? Mathf.Clamp(height / 12.7f, .5f, 2.4f) : scale * rng.RandfRange(.88f, 1.30f);
                var basis = new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(scale, tall, scale));
                var placement = new Transform3D(basis, point);
                (rng.Randf() < .28f ? deciduous : conifers).Add(placement);
                if (rng.Randf() < .58f) far.Add(placement);
            }
            Batch(pineMesh, conifers, 0, 420);
            Batch(broadleaf, deciduous, 0, 420);
            Batch(low, far, 420, 1500);

            void Batch(Mesh mesh, List<Transform3D> trees, float begin, float end)
            {
                if (trees.Count == 0) return;
                var instances = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = mesh,
                    InstanceCount = trees.Count
                };
                for (int i = 0; i < trees.Count; i++)
                {
                    instances.SetInstanceTransform(i, trees[i]);
                    instances.SetInstanceColor(i, Colors.White.Darkened(rng.RandfRange(0, .19f)));
                }
                // Match the bounds used to switch both levels, irrespective of the trees sampled for the far mesh.
                var key = group.Key;
                var patchBounds = new Aabb(new Vector3(key.X * 320, -110, key.Y * 320), new Vector3(320, 280, 320));
                root.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = instances,
                    CustomAabb = patchBounds,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeBegin = begin,
                    VisibilityRangeEnd = end
                });
            }
        }
    }
    public static readonly StandsBuilder.Stand[] Stands = {
        new("Start finish", 6780, 6980, 1, 12), new("La Source", 285, 395, 1, 7),
        new("Eau Rouge", 710, 855, 1, 9), new("Raidillon", 1030, 1170, 1, 13),
        new("Les Combes", 2180, 2250, 1, 7), new("Chicane", 6500, 6580, 1, 7),
    };
}
