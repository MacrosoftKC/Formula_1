using F1.Core.Geometry;
using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Builds the drivable surfaces: asphalt, lines, kerbs, verges, gravel traps, pit lane and grid markings.</summary>
public static class RoadBuilder
{
    public static readonly Color Asphalt = new(0.15f, 0.15f, 0.17f);
    public static readonly Color AsphaltLight = new(0.23f, 0.23f, 0.25f);
    public static readonly Color Line = new(0.95f, 0.95f, 0.95f);
    public static readonly Color KerbRed = new(0.86f, 0.13f, 0.13f);
    public static readonly Color KerbWhite = new(0.96f, 0.96f, 0.96f);
    public static readonly Color Gravel = new(0.85f, 0.76f, 0.55f);
    public static readonly Color Concrete = new(0.62f, 0.62f, 0.64f);
    public static readonly Color PitAsphalt = new(0.19f, 0.19f, 0.21f);

    private const float RoadH = 0.02f, LineH = 0.024f, GravelH = 0.016f, KerbTop = 0.055f;
    private const int ChunkSize = 64;
    private const float LineWidth = 0.25f;

    public static void Build(Node3D parent, TrackFrames f)
    {
        var root = new Node3D { Name = "Road" };
        parent.AddChild(root);
        for (int start = 0; start < f.Count; start += ChunkSize)
        {
            var m = new LowPolyMesh();
            int end = Math.Min(start + ChunkSize, f.Count);
            for (int i = start; i < end; i++) Segment(m, f, i);
            root.AddChild(m.ToInstance($"Road_{start / ChunkSize}", castShadows: false));
        }
        BuildMarkings(root, f);
        BuildPitLane(root, f);
    }

    private static void Segment(LowPolyMesh m, TrackFrames f, int i)
    {
        int j = i + 1;
        float wl0 = f.WL[f.Wrap(i)], wl1 = f.WL[f.Wrap(j)], wr0 = f.WR[f.Wrap(i)], wr1 = f.WR[f.Wrap(j)];
        var shade = (i / 3) % 2 == 0 ? Asphalt : Asphalt.Lightened(0.015f);

        // asphalt between the inner edges of the white lines
        m.QuadUp(f.At(i, -wr0 + LineWidth, RoadH), f.At(j, -wr1 + LineWidth, RoadH), f.At(j, wl1 - LineWidth, RoadH), f.At(i, wl0 - LineWidth, RoadH), shade);
        // white edge lines
        m.QuadUp(f.At(i, wl0 - LineWidth, LineH), f.At(j, wl1 - LineWidth, LineH), f.At(j, wl1, LineH), f.At(i, wl0, LineH), Line);
        m.QuadUp(f.At(i, -wr0, LineH), f.At(j, -wr1, LineH), f.At(j, -wr1 + LineWidth, LineH), f.At(i, -wr0 + LineWidth, LineH), Line);

        Side(m, f, i, +1, wl0, wl1, f.KerbLeft(i), f.RunoffLeft(i), f.BL[f.Wrap(i)], f.BL[f.Wrap(j)]);
        Side(m, f, i, -1, wr0, wr1, f.KerbRight(i), f.RunoffRight(i), f.BR[f.Wrap(i)], f.BR[f.Wrap(j)]);
    }

    private static void Side(LowPolyMesh m, TrackFrames f, int i, int sign, float w0, float w1, bool kerb, char runoff, float b0, float b1)
    {
        int j = i + 1;
        float edge0 = w0, edge1 = w1;
        float outer0 = edge0 + TrackSurface.VergeWidth, outer1 = edge1 + TrackSurface.VergeWidth;

        if (kerb)
        {
            var c = (i % 2 == 0) ? KerbRed : KerbWhite;
            float k = TrackSurface.KerbWidth;
            m.QuadUp(f.At(i, sign * edge0, KerbTop), f.At(j, sign * edge1, KerbTop), f.At(j, sign * (edge1 + k), RoadH + 0.01f), f.At(i, sign * (edge0 + k), RoadH + 0.01f), c);
            // vertical lip on the track side
            var a = f.At(i, sign * edge0, RoadH); var b = f.At(j, sign * edge1, RoadH);
            var at = f.At(i, sign * edge0, KerbTop); var bt = f.At(j, sign * edge1, KerbTop);
            if (sign > 0) m.Quad(a, at, bt, b, c.Darkened(0.2f)); else m.Quad(b, bt, at, a, c.Darkened(0.2f));
            outer0 = Math.Max(outer0, edge0 + k); outer1 = Math.Max(outer1, edge1 + k);
        }
        else
        {
            m.QuadUp(f.At(i, sign * edge0, RoadH), f.At(j, sign * edge1, RoadH), f.At(j, sign * outer1, RoadH), f.At(i, sign * outer0, RoadH), AsphaltLight);
        }

        if (runoff is 'R' or 'A' or 'P')
        {
            var color = runoff == 'R' ? ((i / 2) % 2 == 0 ? Gravel : Gravel.Darkened(0.03f)) : AsphaltLight;
            float h = runoff == 'R' ? GravelH : RoadH;
            float e0 = Math.Max(outer0, b0 - 0.3f), e1 = Math.Max(outer1, b1 - 0.3f);
            if (runoff == 'P') { e0 = outer0 + 4; e1 = outer1 + 4; }
            m.QuadUp(f.At(i, sign * outer0, h), f.At(j, sign * outer1, h), f.At(j, sign * e1, h), f.At(i, sign * e0, h), color);
        }
    }

    private static void BuildMarkings(Node3D root, TrackFrames f)
    {
        var ts = f.Surface;
        var m = new LowPolyMesh();
        // start/finish line: chequered band across the track at s = 0
        const float H = 0.03f;
        float wl = f.WL[0], wr = f.WR[0];
        int cells = 16;
        for (int row = 0; row < 2; row++)
            for (int c = 0; c < cells; c++)
            {
                float d0 = -wr + (wl + wr) * c / cells, d1 = -wr + (wl + wr) * (c + 1) / cells;
                var color = ((c + row) % 2 == 0) ? Line : new Color(0.1f, 0.1f, 0.1f);
                double s0 = -0.6 + row * 0.6, s1 = s0 + 0.6;
                m.QuadUp(W(ts, s0, d0, H), W(ts, s1, d0, H), W(ts, s1, d1, H), W(ts, s0, d1, H), color);
            }

        // grid boxes: front bar plus short side lines, numbered from pole
        foreach (var slot in ts.Data.Grid)
        {
            double s = slot.S, d = slot.D;
            m.QuadUp(W(ts, s + 0.1, d - 1.1, H), W(ts, s + 0.35, d - 1.1, H), W(ts, s + 0.35, d + 1.1, H), W(ts, s + 0.1, d + 1.1, H), Line);
            foreach (float side in new[] { -1.1f, 1.1f })
                m.QuadUp(W(ts, s - 1.2, d + side - 0.12, H), W(ts, s + 0.35, d + side - 0.12, H), W(ts, s + 0.35, d + side + 0.12, H), W(ts, s - 1.2, d + side + 0.12, H), Line);
        }
        root.AddChild(m.ToInstance("Markings", castShadows: false));

        foreach (var slot in ts.Data.Grid)
        {
            var label = new Label3D
            {
                Name = $"Grid{slot.Position}",
                Text = slot.Position.ToString(),
                FontSize = 160,
                PixelSize = 0.005f,
                Modulate = Line,
                OutlineSize = 0,
                Shaded = false,
            };
            // lie flat, text 'up' pointing in the race direction so it reads correctly on approach
            var basis = ts.TrackTransform(slot.S, slot.D).Basis * new Basis(Vector3.Right, -Mathf.Pi / 2);
            label.Transform = new Transform3D(basis, W(ts, slot.S - 2.0, slot.D, 0.032f));
            root.AddChild(label);
        }
    }

    private static Vector3 W(TrackSurface ts, double s, double d, float h) => Units.ToWorld(ts.Center.ToWorld(s, d), h);

    private static void BuildPitLane(Node3D root, TrackFrames f)
    {
        var ts = f.Surface;
        var pitData = ts.Data.PitLane;
        var pit = ts.Pit;
        float half = (float)pitData.Width * 0.5f;
        var m = new LowPolyMesh();
        int n = pit.Count;
        Vector3 PW(int i, float d, float h) => Units.ToWorld(pit.Point(i) + pit.Normal(i) * d, h);

        for (int i = 0; i < n - 1; i++)
        {
            double u = pit.SAt(i);
            // fast lane (towards the track, left) and working lane (right), separated by a dashed line
            m.QuadUp(PW(i, 0, RoadH), PW(i + 1, 0, RoadH), PW(i + 1, half, RoadH), PW(i, half, RoadH), PitAsphalt);
            m.QuadUp(PW(i, -half, RoadH), PW(i + 1, -half, RoadH), PW(i + 1, 0, RoadH), PW(i, 0, RoadH), PitAsphalt.Darkened(0.06f));
            if ((i / 2) % 2 == 0)
                m.QuadUp(PW(i, -0.1f, LineH), PW(i + 1, -0.1f, LineH), PW(i + 1, 0.1f, LineH), PW(i, 0.1f, LineH), Line);
            // pit entry / exit lines along the track side
            if (u <= pitData.EntryLineEnd || u >= pitData.ExitLineStart)
                m.QuadUp(PW(i, half - 0.3f, LineH), PW(i + 1, half - 0.3f, LineH), PW(i + 1, half, LineH), PW(i, half, LineH), Line);
            // concrete apron in front of the garages
            m.QuadUp(PW(i, -half - 4f, RoadH), PW(i + 1, -half - 4f, RoadH), PW(i + 1, -half, RoadH), PW(i, -half, RoadH), Concrete);

            // fill between the pit lane and the track edge (pit wall area / painted divergence)
            double s0 = pitData.TrackS[i], s1 = pitData.TrackS[i + 1];
            var e0 = W(ts, s0, -ts.HalfWidthRight(s0) - TrackSurface.VergeWidth, RoadH - 0.004f);
            var e1 = W(ts, s1, -ts.HalfWidthRight(s1) - TrackSurface.VergeWidth, RoadH - 0.004f);
            var p0 = PW(i, half, RoadH - 0.004f); var p1 = PW(i + 1, half, RoadH - 0.004f);
            if (e0.DistanceTo(p0) > 0.3f)
                m.QuadUp(p0, p1, e1, e0, Concrete.Lightened(0.05f));
        }

        // speed-limit lines across the lane, team boxes in the working lane
        foreach (double u in new[] { pitData.LimitStart, pitData.LimitEnd })
        {
            int i = pit.NearestIndex(u);
            m.QuadUp(PW(i, -half, LineH), PW(i + 1, -half, LineH), PW(i + 1, half, LineH), PW(i, half, LineH), Line);
        }
        var teams = App.Instance.Teams.Teams;
        for (int b = 0; b < pitData.Boxes.Count; b++)
        {
            var color = Units.Hex(teams[b % teams.Count].Primary);
            double u0 = pitData.Boxes[b] - 5.5, u1 = pitData.Boxes[b] + 1.0;
            float d0 = (float)pitData.BoxOffset - 2.2f, d1 = (float)pitData.BoxOffset + 2.2f;
            var a = Units.ToWorld(pit.ToWorld(u0, d0), LineH); var bq = Units.ToWorld(pit.ToWorld(u1, d0), LineH);
            var c = Units.ToWorld(pit.ToWorld(u1, d1), LineH); var dq = Units.ToWorld(pit.ToWorld(u0, d1), LineH);
            m.QuadUp(a, bq, c, dq, color.Darkened(0.15f));
        }
        root.AddChild(m.ToInstance("PitLane", castShadows: false));
    }
}
