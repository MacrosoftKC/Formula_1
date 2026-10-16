using F1.Core.Track;
using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Start gantry with working lights, marshal posts with LED flag panels, DRS boards and braking markers.</summary>
public static class FurnitureBuilder
{
    private static readonly Color Steel = new(0.3f, 0.32f, 0.36f);
    private static readonly Color BoardWhite = new(0.96f, 0.96f, 0.96f);

    public static StartLights BuildStartGantry(Node3D parent, TrackSurface ts)
    {
        var node = new Node3D { Name = "StartGantry" };
        parent.AddChild(node);
        // the gantry stands just past the line, lights face the grid (backwards along the track)
        double s = ts.Center.Wrap(8);
        float wl = (float)ts.HalfWidthLeft(s), wr = (float)ts.HalfWidthRight(s);
        var t = ts.TrackTransform(s, 0);
        var b = t.Basis;
        var m = new LowPolyMesh();
        const float H = 7.0f;
        foreach (float d in new[] { wl + 2.0f, -(wr + 2.0f) })
        {
            var foot = t.Origin + b * new Vector3(-d, 0, 0); // basis X points right of travel; left offset is -X
            m.Box(foot + new Vector3(0, H * 0.5f, 0), new Vector3(0.35f, H * 0.5f, 0.35f), b, Steel);
        }
        m.Box(t.Origin + new Vector3(0, H, 0), new Vector3((wl + wr) * 0.5f + 2.4f, 0.4f, 0.5f), b, Steel);
        node.AddChild(m.ToInstance("Frame"));

        var lights = new StartLights { Name = "Lights" };
        node.AddChild(lights);
        lights.Build(t, H - 1.6f);
        return lights;
    }

    public static List<MarshalPanel> BuildMarshalPosts(Node3D parent, TrackSurface ts)
    {
        var node = new Node3D { Name = "MarshalPosts" };
        parent.AddChild(node);
        var huts = new LowPolyMesh();
        var panels = new List<MarshalPanel>();
        foreach (var post in ts.Data.MarshalPosts)
        {
            double s = post.S;
            double barrier = post.Side > 0 ? ts.BarrierLeft(s) : ts.BarrierRight(s);
            var hutT = ts.TrackTransform(s, post.Side * (barrier + 3.0));
            huts.Box(hutT.Origin + new Vector3(0, 1.3f, 0), new Vector3(1.2f, 1.3f, 1.2f), hutT.Basis, new Color(0.95f, 0.55f, 0.1f));
            huts.Box(hutT.Origin + new Vector3(0, 2.7f, 0), new Vector3(1.5f, 0.12f, 1.5f), hutT.Basis, BoardWhite);

            // LED panel on a pole beside the wall, facing oncoming traffic
            var poleT = ts.TrackTransform(s, post.Side * (barrier + 0.8));
            huts.Box(poleT.Origin + new Vector3(0, 1.6f, 0), new Vector3(0.08f, 1.6f, 0.08f), poleT.Basis, Steel);
            var panel = new MarshalPanel { Name = $"Panel{post.Number}", Post = post };
            node.AddChild(panel);
            panel.Build(poleT, 3.3f);
            panels.Add(panel);
        }
        node.AddChild(huts.ToInstance("Huts"));
        return panels;
    }

    public static void BuildBoards(Node3D parent, TrackSurface ts)
    {
        var node = new Node3D { Name = "Boards" };
        parent.AddChild(node);
        var m = new LowPolyMesh();

        void Board(double s, int side, string text, Color bg, Color fg, float width = 1.6f, float height = 1.0f, float y = 1.6f)
        {
            double barrier = side > 0 ? ts.BarrierLeft(s) : ts.BarrierRight(s);
            double edge = side > 0 ? ts.HalfWidthLeft(s) : ts.HalfWidthRight(s);
            double d = side * Math.Min(barrier - 0.6, edge + 4.0);
            var t = ts.TrackTransform(s, d);
            var b = t.Basis;
            m.Box(t.Origin + new Vector3(0, y * 0.5f, 0), new Vector3(0.06f, y * 0.5f, 0.06f), b, Steel);
            m.Box(t.Origin + new Vector3(0, y + height * 0.5f, 0), new Vector3(width * 0.5f, height * 0.5f, 0.05f), b, bg);
            var label = new Label3D
            {
                Text = text, FontSize = 128, PixelSize = 0.006f, Modulate = fg, OutlineSize = 0, Shaded = false,
                VisibilityRangeEnd = 350,
            };
            // label faces approaching cars: +Z of the label towards -forward
            label.Transform = new Transform3D(b, t.Origin + new Vector3(0, y + height * 0.5f, 0) + b.Z * 0.07f);
            node.AddChild(label);
        }

        // 300/200/100 m braking boards before the heavy braking zones, on the outside of the corner
        var corners = ts.Data.Corners;
        for (int ci = 0; ci < corners.Count; ci++)
        {
            var c = corners[ci];
            var prev = corners[(ci + corners.Count - 1) % corners.Count];
            // only heavy braking zones at the end of a real straight
            if (c.MinRadius >= 70 || ts.Center.Forward(c.EntryS, prev.ExitS) < 320) continue;
            int side = c.Direction > 0 ? -1 : +1;
            foreach (int dist in new[] { 300, 200, 100 })
            {
                double s = ts.Center.Wrap(c.EntryS - dist + 40);
                Board(s, side, dist.ToString(), BoardWhite, new Color(0.1f, 0.1f, 0.12f), 1.4f, 0.9f, 1.2f);
            }
        }
        foreach (var z in ts.Data.DrsZones)
        {
            Board(z.DetectionS, +1, "DRS\nDETECTION", new Color(0.1f, 0.1f, 0.12f), BoardWhite, 2.2f, 1.2f, 1.8f);
            Board(z.ActivationS, +1, "DRS", new Color(0.1f, 0.55f, 0.2f), BoardWhite, 1.8f, 1.0f, 1.8f);
        }
        node.AddChild(m.ToInstance("BoardFrames"));
    }
}

/// <summary>The five red light columns on the start gantry.</summary>
public partial class StartLights : Node3D
{
    private readonly List<StandardMaterial3D> _columns = new();
    private StandardMaterial3D _off = null!;

    public void Build(Transform3D gantry, float height)
    {
        _off = Materials.Solid(new Color(0.18f, 0.03f, 0.03f), 0.4f);
        var b = gantry.Basis;
        var housing = new LowPolyMesh();
        for (int c = 0; c < 5; c++)
        {
            float x = (c - 2) * 1.05f;
            var center = gantry.Origin + b * new Vector3(x, height, 0);
            housing.Box(center, new Vector3(0.42f, 0.95f, 0.25f), b, new Color(0.08f, 0.08f, 0.09f));
            var mat = Materials.Emissive(new Color(1f, 0.08f, 0.05f), 4f);
            _columns.Add(mat);
            for (int r = 0; r < 2; r++)
            {
                var lamp = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.06f, RadialSegments = 10 },
                    MaterialOverride = _off,
                };
                // lamps face backwards along the track (towards the grid)
                lamp.Transform = new Transform3D(b * new Basis(Vector3.Right, Mathf.Pi / 2), center + b * new Vector3(0, 0.3f - r * 0.6f, 0.27f));
                AddChild(lamp);
            }
        }
        AddChild(housing.ToInstance("Housing"));
        SetLit(0);
    }

    /// <summary>Lights the first <paramref name="count"/> columns (0 = all off).</summary>
    public void SetLit(int count)
    {
        int lampIndex = 0;
        foreach (var child in GetChildren())
        {
            if (child is not MeshInstance3D mi || mi.Name == "Housing") continue;
            int column = lampIndex / 2;
            mi.MaterialOverride = column < count ? _columns[column] : _off;
            lampIndex++;
        }
    }
}

/// <summary>A trackside LED panel showing flag colours for one marshalling sector.</summary>
public partial class MarshalPanel : Node3D
{
    public MarshalPost Post { get; set; } = null!;
    private MeshInstance3D _screen = null!;
    private readonly Dictionary<Color, StandardMaterial3D> _cache = new();
    private StandardMaterial3D _offMat = null!;

    public void Build(Transform3D pole, float height)
    {
        _offMat = Materials.Solid(new Color(0.06f, 0.06f, 0.07f), 0.5f);
        _screen = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.3f, 0.85f, 0.12f) }, MaterialOverride = _offMat };
        // face oncoming traffic: panel normal (+Z) points backwards along the track
        _screen.Transform = new Transform3D(pole.Basis, pole.Origin + new Vector3(0, height, 0));
        AddChild(_screen);
    }

    public void Show(Color? color)
    {
        if (color == null) { _screen.MaterialOverride = _offMat; return; }
        if (!_cache.TryGetValue(color.Value, out var mat)) _cache[color.Value] = mat = Materials.Emissive(color.Value, 3f);
        _screen.MaterialOverride = mat;
    }
}
