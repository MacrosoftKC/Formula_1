using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Concrete walls with catch fencing, tyre walls and advertising boards, plus their collision.</summary>
public static class BarrierBuilder
{
    public const uint CollisionLayerWalls = 1 << 1;

    private static readonly Color WallColor = new(0.8f, 0.8f, 0.82f);
    private static readonly Color PostColor = new(0.42f, 0.44f, 0.47f);
    private static readonly Color RailColor = new(0.55f, 0.57f, 0.6f);
    private const float WallHeight = 1.05f, WallThickness = 0.5f, FenceHeight = 3.0f;
    private const int ChunkSize = 64;

    private sealed class Board { public string Text = ""; public string Background = "#ffffff"; public string Foreground = "#000000"; }
    private sealed class BoardsFile { public List<Board> Boards { get; set; } = new(); }

    public static void Build(Node3D parent, TrackFrames f)
    {
        var root = new Node3D { Name = "Barriers" };
        parent.AddChild(root);
        var body = new StaticBody3D { Name = "BarrierCollision", CollisionLayer = CollisionLayerWalls, CollisionMask = 0 };
        body.PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.35f, Bounce = 0.15f };
        root.AddChild(body);

        foreach (int sign in new[] { +1, -1 })
            for (int start = 0; start < f.Count; start += ChunkSize)
            {
                var wall = new LowPolyMesh();
                var fence = new LowPolyMesh();
                var collision = new LowPolyMesh();
                int end = Math.Min(start + ChunkSize, f.Count);
                for (int i = start; i < end; i++)
                    Segment(f, i, sign, wall, fence, collision);
                if (wall.IsEmpty) continue;
                string side = sign > 0 ? "L" : "R";
                root.AddChild(wall.ToInstance($"Wall{side}_{start / ChunkSize}"));
                var fenceInst = fence.ToInstance($"Fence{side}_{start / ChunkSize}", Materials.VertexColorDoubleSided, castShadows: false);
                fenceInst.VisibilityRangeEnd = 450;
                root.AddChild(fenceInst);
                var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
                shape.SetFaces(collision.Faces());
                body.AddChild(new CollisionShape3D { Name = $"Col{side}_{start / ChunkSize}", Shape = shape });
            }

        BuildTyreWalls(root, f);
        BuildHoardings(root, f);
    }

    private static bool Skip(TrackFrames f, int i, int sign) =>
        sign < 0 && (f.RunoffRight(i) == 'P' || f.RunoffRight(i + 1) == 'P');

    private static float Barrier(TrackFrames f, int i, int sign) => sign > 0 ? f.BL[f.Wrap(i)] : f.BR[f.Wrap(i)];

    private static void Segment(TrackFrames f, int i, int sign, LowPolyMesh wall, LowPolyMesh fence, LowPolyMesh col)
    {
        if (Skip(f, i, sign)) return;
        int j = i + 1;
        float b0 = Barrier(f, i, sign), b1 = Barrier(f, j, sign);
        var i0 = f.At(i, sign * b0); var i1 = f.At(j, sign * b1);
        var o0 = f.At(i, sign * (b0 + WallThickness)); var o1 = f.At(j, sign * (b1 + WallThickness));
        var up = new Vector3(0, WallHeight, 0);
        var towardTrack = -f.N[f.Wrap(i)] * sign;

        var shade = WallColor.Darkened((i / 6) % 2 == 0 ? 0f : 0.04f);
        wall.QuadFacing(i0, i1, i1 + up, i0 + up, towardTrack, shade);
        wall.QuadFacing(i0 + up, i1 + up, o1 + up, o0 + up, Vector3.Up, WallColor.Lightened(0.06f));
        wall.QuadFacing(o0, o1, o1 + up, o0 + up, -towardTrack, WallColor.Darkened(0.12f));

        var tall = new Vector3(0, WallHeight + 1.2f, 0);
        col.QuadFacing(i0 + new Vector3(0, -0.5f, 0), i1 + new Vector3(0, -0.5f, 0), i1 + tall, i0 + tall, towardTrack, shade);

        // catch fence: posts every 4 samples (~8 m) leaning slightly over the track at the top, three rails
        var top0 = i0 + up + new Vector3(0, FenceHeight, 0) + towardTrack * 0.5f;
        var top1 = i1 + up + new Vector3(0, FenceHeight, 0) + f.N[f.Wrap(j)] * -sign * 0.5f;
        if (i % 4 == 0)
        {
            var baseP = i0 + up;
            var knee = baseP + new Vector3(0, FenceHeight * 0.75f, 0);
            fence.Box(baseP + new Vector3(0, FenceHeight * 0.375f, 0), new Vector3(0.07f, FenceHeight * 0.375f, 0.07f), PostColor);
            fence.Box((knee + top0) * 0.5f, new Vector3(0.07f, (top0 - knee).Length() * 0.5f, 0.07f),
                new Basis(Vector3.Up.Cross(top0 - knee).Normalized(), Vector3.Up.AngleTo(top0 - knee)), PostColor);
        }
        foreach (float h in new[] { 0.9f, 1.9f, 2.7f })
        {
            var a = i0 + up + new Vector3(0, h, 0); var b = i1 + up + new Vector3(0, h, 0);
            fence.Quad(a, b, b + new Vector3(0, 0.06f, 0), a + new Vector3(0, 0.06f, 0), RailColor);
        }
        fence.Quad(top0, top1, top1 + new Vector3(0, 0.06f, 0), top0 + new Vector3(0, 0.06f, 0), RailColor);
    }

    private static void BuildTyreWalls(Node3D root, TrackFrames f)
    {
        var transforms = new List<Transform3D>();
        var colors = new List<Color>();
        Color[] palette = { new(0.85f, 0.12f, 0.12f), new(0.95f, 0.95f, 0.95f), new(0.12f, 0.12f, 0.14f) };
        foreach (int sign in new[] { +1, -1 })
            for (int i = 0; i < f.Count; i++)
            {
                char r = sign > 0 ? f.RunoffLeft(i) : f.RunoffRight(i);
                if (r != 'R') continue;
                float b = Barrier(f, i, sign);
                for (int k = 0; k < 2; k++)
                {
                    var pos = f.P[i] + f.T[i] * (k * 1.0f) + f.N[i] * sign * (b - 0.45f);
                    transforms.Add(new Transform3D(Basis.Identity, pos));
                    colors.Add(palette[(i * 2 + k) / 3 % palette.Length]);
                }
            }
        if (transforms.Count == 0) return;
        var tyre = new LowPolyMesh();
        for (int layer = 0; layer < 3; layer++)
            tyre.Prism(new Vector3(0, layer * 0.28f, 0), 0.4f, 0.4f, 0.26f, 8, Colors.White);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = tyre.ToMesh(Materials.VertexColor) };
        mm.InstanceCount = transforms.Count;
        for (int i = 0; i < transforms.Count; i++) { mm.SetInstanceTransform(i, transforms[i]); mm.SetInstanceColor(i, colors[i]); }
        root.AddChild(new MultiMeshInstance3D { Name = "TyreWalls", Multimesh = mm, VisibilityRangeEnd = 600 });
    }

    private static void BuildHoardings(Node3D root, TrackFrames f)
    {
        var file = DataFiles.Load<BoardsFile>("res://data/hoardings.json");
        if (file.Boards.Count == 0) return;
        var node = new Node3D { Name = "Hoardings" };
        root.AddChild(node);
        var panels = new LowPolyMesh();
        int boardIndex = 0;
        const int Every = 18, LengthSamples = 5; // a 10 m board roughly every 36 m
        foreach (int sign in new[] { +1, -1 })
            for (int i = 0; i + LengthSamples < f.Count; i += Every)
            {
                bool ok = true;
                for (int k = 0; k <= LengthSamples; k++)
                {
                    float b = Barrier(f, i + k, sign);
                    float w = sign > 0 ? f.WL[f.Wrap(i + k)] : f.WR[f.Wrap(i + k)];
                    if (Skip(f, i + k, sign) || b - w > 14) { ok = false; break; } // only where the wall is close to the track
                }
                if (!ok) continue;
                var board = file.Boards[boardIndex++ % file.Boards.Count];
                var bg = Units.Hex(board.Background);
                var towardTrack = -f.N[f.Wrap(i)] * sign;
                for (int k = 0; k < LengthSamples; k++)
                {
                    float b0 = Barrier(f, i + k, sign) - 0.04f, b1 = Barrier(f, i + k + 1, sign) - 0.04f;
                    var a = f.At(i + k, sign * b0, 0.1f); var c = f.At(i + k + 1, sign * b1, 0.1f);
                    panels.QuadFacing(a, c, c + new Vector3(0, 0.9f, 0), a + new Vector3(0, 0.9f, 0), towardTrack, bg);
                }
                int mid = i + LengthSamples / 2;
                float bm = Barrier(f, mid, sign) - 0.07f;
                var pos = f.At(mid, sign * bm, 0.55f);
                var label = new Label3D
                {
                    Text = board.Text,
                    FontSize = 96,
                    PixelSize = 0.0075f,
                    Modulate = Units.Hex(board.Foreground),
                    OutlineSize = 0,
                    Shaded = false,
                    DoubleSided = false,
                    VisibilityRangeEnd = 300,
                };
                // face the track
                label.Transform = new Transform3D(Basis.LookingAt(-towardTrack, Vector3.Up), pos);
                node.AddChild(label);
            }
        node.AddChild(panels.ToInstance("BoardPanels", castShadows: false));
    }
}
