using F1.Core.Geometry;
using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Grandstands following the barrier line on the outside of the main straight and key corners, filled with instanced spectators.</summary>
public static class StandsBuilder
{
    /// <summary>A grandstand along track distance [From, To] on one side (+1 left, -1 right).</summary>
    public sealed record Stand(string Name, double From, double To, int Side, int Rows = 9, float Setback = 4f);

    public sealed record Band(double From, double To, int Side, double DMin, double DMax);

    private static readonly Color Seat = new(0.72f, 0.74f, 0.78f);
    private static readonly Color Frame = new(0.35f, 0.38f, 0.44f);
    private static readonly Color Roof = new(0.95f, 0.95f, 0.96f);
    private const float TierRise = 0.5f, TierDepth = 0.85f, BaseHeight = 1.4f;

    public static List<Stand> AlbertParkStands(TrackSurface ts)
    {
        var c = ts.Data.Corners;
        double L = ts.Length;
        double S(int corner, double offset) => ts.Center.Wrap(c[corner - 1].ApexS + offset);
        return new List<Stand>
        {
            new("Main straight A", L - 430, L - 70, +1, 11),
            new("Main straight B", 25, 210, +1, 11),
            new("Turn 1-2", S(1, -70), S(2, 40), +1, 10),
            new("Turn 3", S(3, -80), S(3, 30), +1, 9),
            new("Turn 6", S(6, -70), S(6, 25), +1, 9),
            new("Turn 9", S(9, -60), S(9, 30), -1, 8),
            new("Turn 10", S(10, -50), S(10, 40), +1, 8),
            new("Turn 11", S(11, -70), S(11, 25), +1, 9),
            new("Turn 13", S(13, -50), S(13, 20), -1, 8),
            new("Turn 14", S(14, -60), S(14, 40), +1, 9),
        };
    }

    public static List<Band> Build(Node3D parent, TrackFrames f, IReadOnlyList<Stand> stands, int seed = 7)
    {
        var ts = f.Surface;
        var root = new Node3D { Name = "Grandstands" };
        parent.AddChild(root);
        var crowd = new List<(Transform3D T, Color C)>();
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var bands = new List<Band>();
        var lake = ts.Data.Lake.Count > 0 ? ts.Data.Lake[0] : null;
        var teamColors = App.Instance.Teams.Teams.Select(t => Units.Hex(t.Primary)).ToArray();
        Color[] shirts = { new(0.95f, 0.95f, 0.95f), new(0.9f, 0.2f, 0.15f), new(0.15f, 0.3f, 0.75f), new(0.95f, 0.8f, 0.1f), new(0.1f, 0.1f, 0.12f), new(0.2f, 0.6f, 0.25f) };

        foreach (var st in stands)
        {
            var m = new LowPolyMesh();
            int i0 = ts.Center.NearestIndex(st.From);
            int count = (int)Math.Round(ts.Center.Forward(st.To, st.From) / ts.Data.SampleSpacing);
            float depth = st.Rows * TierDepth;
            float top = BaseHeight + st.Rows * TierRise;

            // smoothed barrier offset so the stand follows the wall without jitter
            float Off(int i)
            {
                float sum = 0; int k = 0;
                for (int j = -6; j <= 6; j++) { sum += st.Side > 0 ? f.BL[f.Wrap(i + j)] : f.BR[f.Wrap(i + j)]; k++; }
                return sum / k + 0.5f + st.Setback;
            }

            double maxD = 0;
            for (int n = 0; n < count; n++)
            {
                int i = f.Wrap(i0 + n), j = f.Wrap(i + 1);
                float o0 = Off(i), o1 = Off(j);
                maxD = Math.Max(maxD, o0 + depth);
                var mid = f.At(i, st.Side * (o0 + depth * 0.5f));
                if (lake != null && Polyline.PointInPolygon(Units.ToTrack(mid), lake)) continue;
                var toward = -f.N[i] * st.Side;
                Vector3 A(int idx, float o, float dd, float h) => f.At(idx, st.Side * (o + dd), h);

                // base block under the first tier
                m.QuadFacing(A(i, o0, 0, 0), A(j, o1, 0, 0), A(j, o1, 0, BaseHeight), A(i, o0, 0, BaseHeight), toward, Frame);
                for (int k = 0; k < st.Rows; k++)
                {
                    float h = BaseHeight + k * TierRise, d0 = k * TierDepth, d1 = (k + 1) * TierDepth;
                    var shade = k % 2 == 0 ? Seat : Seat.Darkened(0.06f);
                    m.QuadUp(A(i, o0, d0, h), A(j, o1, d0, h), A(j, o1, d1, h), A(i, o0, d1, h), shade);
                    m.QuadFacing(A(i, o0, d1, h), A(j, o1, d1, h), A(j, o1, d1, h + TierRise), A(i, o0, d1, h + TierRise), toward, Frame.Lightened(0.15f));

                    for (int q = 0; q < 2; q++)
                    {
                        if (rng.Randf() > 0.82f) continue;
                        float t = 0.25f + q * 0.5f;
                        var pos = A(i, o0, d0 + TierDepth * 0.45f, h).Lerp(A(j, o1, d0 + TierDepth * 0.45f, h), t);
                        var basis = Basis.LookingAt(toward, Vector3.Up);
                        var color = rng.Randf() < 0.3f ? teamColors[rng.RandiRange(0, teamColors.Length - 1)] : shirts[rng.RandiRange(0, shirts.Length - 1)];
                        crowd.Add((new Transform3D(basis, pos), color));
                    }
                }
                // back wall and roof
                m.QuadFacing(A(i, o0, depth, 0), A(j, o1, depth, 0), A(j, o1, depth, top + 3.2f), A(i, o0, depth, top + 3.2f), -toward, Frame.Darkened(0.1f));
                m.QuadUp(A(i, o0, -1.2f, top + 3.2f), A(j, o1, -1.2f, top + 3.2f), A(j, o1, depth + 0.3f, top + 3.4f), A(i, o0, depth + 0.3f, top + 3.4f), Roof);
                if (n % 10 == 0)
                    m.Box(A(i, o0, -0.6f, (top + 3.2f) * 0.5f), new Vector3(0.18f, (top + 3.2f) * 0.5f, 0.18f), Frame);
            }
            root.AddChild(m.ToInstance(st.Name.Replace(' ', '_')));
            double dMin = Off(i0) - 2;
            bands.Add(new Band(st.From - 10, st.To + 10, st.Side, dMin, maxD + 8));
        }

        if (crowd.Count > 0)
        {
            var person = new LowPolyMesh();
            person.Box(new Vector3(0, 0.3f, 0), new Vector3(0.22f, 0.3f, 0.14f), Colors.White);
            person.Box(new Vector3(0, 0.75f, 0), new Vector3(0.11f, 0.13f, 0.11f), new Color(0.92f, 0.76f, 0.62f));
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = person.ToMesh(Materials.VertexColor) };
            mm.InstanceCount = crowd.Count;
            for (int i = 0; i < crowd.Count; i++) { mm.SetInstanceTransform(i, crowd[i].T); mm.SetInstanceColor(i, crowd[i].C); }
            root.AddChild(new MultiMeshInstance3D { Name = "Crowd", Multimesh = mm, VisibilityRangeEnd = 700, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        return bands;
    }
}
