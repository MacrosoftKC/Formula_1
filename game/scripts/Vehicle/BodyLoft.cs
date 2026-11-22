using F1.Game.Track;
using Godot;

namespace F1.Game.Vehicle;

/// <summary>Which part of a cross-section a point or stripe lies on.</summary>
public enum BodyRegion
{
    /// <summary>The curve from the widest point (t = 0) up to the flat top (t = 1).</summary>
    Upper,
    /// <summary>The curve from the widest point (t = 0) down to the flat bottom (t = 1).</summary>
    Lower,
    /// <summary>The flat top, from the left edge (t = 0) to the right edge (t = 1).</summary>
    Top,
}

/// <summary>
/// One cross-section of a lofted body panel at <see cref="Z"/>: a flat bottom <see cref="BotW"/> wide at
/// <see cref="Yb"/>, a lower curve out to the widest point (<see cref="W"/> at <see cref="Mid"/> of the height),
/// an upper curve in to a flat top <see cref="TopW"/> wide at <see cref="Yt"/>. The curves are superellipse quarters
/// (exponent 2 = elliptical, higher = squarer): <see cref="Pl"/> below the widest point, <see cref="Pu"/> above it.
/// </summary>
public readonly record struct Section(float Z, float Cx, float W, float Yb, float Yt, float TopW, float BotW,
    float Mid = 0.5f, float Pl = 2.2f, float Pu = 2.6f)
{
    public float MidY => Yb + (Yt - Yb) * Mid;

    public Section Mirrored => this with { Cx = -Cx };

    public static Section Lerp(Section a, Section b, float t) => new(
        Mathf.Lerp(a.Z, b.Z, t), Mathf.Lerp(a.Cx, b.Cx, t), Mathf.Lerp(a.W, b.W, t), Mathf.Lerp(a.Yb, b.Yb, t),
        Mathf.Lerp(a.Yt, b.Yt, t), Mathf.Lerp(a.TopW, b.TopW, t), Mathf.Lerp(a.BotW, b.BotW, t),
        Mathf.Lerp(a.Mid, b.Mid, t), Mathf.Lerp(a.Pl, b.Pl, t), Mathf.Lerp(a.Pu, b.Pu, t));
}

/// <summary>Colours of a lofted panel by the Z of each segment: top (flat top and the upper half of the upper curve),
/// shoulder (lower half of the upper curve), side (lower curve), and the end caps. The flat bottom is carbon.</summary>
public sealed record BodyPaint(Func<float, Color> Top, Func<float, Color> Shoulder, Func<float, Color> Side, Color? Front = null, Color? Back = null)
{
    public static BodyPaint Solid(Color c, Color? front = null, Color? back = null) => new(_ => c, _ => c, _ => c, front, back);
}

/// <summary>
/// Faceted (flat-shaded) lofts through <see cref="Section"/>s: smooth along the car via Catmull-Rom between key
/// sections, <see cref="K"/> facets per quarter round each cross-section. Also places stripes and lettering exactly on
/// the facets.
/// </summary>
public static class BodyLoft
{
    /// <summary>Facets per curved quarter of a cross-section (4K + 2 facets round the whole section).</summary>
    public const int K = 4;

    private static readonly Color Carbon = new(0.11f, 0.11f, 0.12f);

    // ---- cross-sections -------------------------------------------------------------------------------------------

    /// <summary>Point on the upper (<paramref name="lower"/> false) or lower curve; phi 0 = widest point, pi/2 = top/bottom.</summary>
    private static Vector3 Curve(Section s, int side, bool lower, float phi)
    {
        float p = lower ? s.Pl : s.Pu;
        float c = Mathf.Pow(Mathf.Max(Mathf.Cos(phi), 0), 2f / p), sn = Mathf.Pow(Mathf.Max(Mathf.Sin(phi), 0), 2f / p);
        float hw = s.W * 0.5f, he = (lower ? s.BotW : s.TopW) * 0.5f, mid = s.MidY;
        float x = he + (hw - he) * c;
        float y = lower ? mid - (mid - s.Yb) * sn : mid + (s.Yt - mid) * sn;
        return new Vector3(s.Cx + side * x, y, s.Z);
    }

    /// <summary>
    /// Ring of 4K + 2 points. Facet e joins point e to e + 1: 0 is the flat bottom, 1..K the right lower curve
    /// (bottom → widest), K+1..2K the right upper curve (widest → top), 2K+1 the flat top, 2K+2..3K+1 the left upper
    /// curve (top → widest), 3K+2..4K+1 the left lower curve (widest → bottom).
    /// </summary>
    public static Vector3[] Ring(Section s)
    {
        var pts = new Vector3[4 * K + 2];
        int n = 0;
        pts[n++] = new Vector3(s.Cx - s.BotW * 0.5f, s.Yb, s.Z);
        for (int i = 0; i <= K; i++) pts[n++] = Curve(s, 1, true, Mathf.Pi / 2 * (1 - (float)i / K));
        for (int i = 1; i <= K; i++) pts[n++] = Curve(s, 1, false, Mathf.Pi / 2 * i / K);
        for (int i = 0; i <= K; i++) pts[n++] = Curve(s, -1, false, Mathf.Pi / 2 * (1 - (float)i / K));
        for (int i = 1; i < K; i++) pts[n++] = Curve(s, -1, true, Mathf.Pi / 2 * i / K);
        return pts;
    }

    private static Vector3 Centroid(Vector3[] r)
    {
        var c = Vector3.Zero;
        foreach (var p in r) c += p;
        return c / r.Length;
    }

    /// <summary>Point on a region of the section, on the facets themselves (not the ideal curve).</summary>
    public static Vector3 FacetPoint(Section s, int side, BodyRegion region, float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        if (region == BodyRegion.Top)
            return new Vector3(s.Cx + (t * 2 - 1) * s.TopW * 0.5f, s.Yt, s.Z);
        float f = t * K;
        int i = Math.Min((int)f, K - 1);
        bool lower = region == BodyRegion.Lower;
        var a = Curve(s, side, lower, Mathf.Pi / 2 * i / K);
        var b = Curve(s, side, lower, Mathf.Pi / 2 * (i + 1) / K);
        return a.Lerp(b, f - i);
    }

    // ---- along the car ------------------------------------------------------------------------------------------

    /// <summary>Catmull-Rom through the key sections (Z linear) with <paramref name="sub"/> steps per span.</summary>
    public static Section[] Smooth(Section[] keys, int sub)
    {
        if (keys.Length < 3 || sub <= 1) return keys;
        static float Cr(float a, float b, float c, float d, float t) =>
            0.5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
        var o = new List<Section>();
        for (int i = 0; i < keys.Length - 1; i++)
        {
            var a = keys[Math.Max(i - 1, 0)]; var b = keys[i]; var c = keys[i + 1]; var d = keys[Math.Min(i + 2, keys.Length - 1)];
            for (int j = 0; j < sub; j++)
            {
                float t = (float)j / sub;
                float F(Func<Section, float> g) => Cr(g(a), g(b), g(c), g(d), t);
                float yb = F(s => s.Yb), yt = Mathf.Max(F(s => s.Yt), yb + 0.01f);
                float w = Mathf.Max(F(s => s.W), 0.01f);
                o.Add(new Section(Mathf.Lerp(b.Z, c.Z, t), F(s => s.Cx), w, yb, yt,
                    Mathf.Clamp(F(s => s.TopW), 0, w), Mathf.Clamp(F(s => s.BotW), 0, w),
                    Mathf.Clamp(F(s => s.Mid), 0.15f, 0.85f), Mathf.Max(F(s => s.Pl), 1.2f), Mathf.Max(F(s => s.Pu), 1.2f)));
            }
        }
        o.Add(keys[^1]);
        return o.ToArray();
    }

    /// <summary>Section at <paramref name="z"/> by linear interpolation (keeps the faceted shape unchanged).</summary>
    public static Section At(Section[] st, float z)
    {
        if (z <= st[0].Z) return st[0];
        for (int k = 0; k < st.Length - 1; k++)
            if (z <= st[k + 1].Z) return Section.Lerp(st[k], st[k + 1], (z - st[k].Z) / Mathf.Max(st[k + 1].Z - st[k].Z, 1e-5f));
        return st[^1];
    }

    /// <summary>Adds a section at <paramref name="z"/> (e.g. where a livery colour changes) without changing the shape.</summary>
    public static Section[] SplitAt(Section[] st, float z) =>
        z <= st[0].Z + 0.01f || z >= st[^1].Z - 0.01f ? st
            : st.Where(s => Mathf.Abs(s.Z - z) > 0.01f).Append(At(st, z)).OrderBy(s => s.Z).ToArray();

    // ---- meshing ------------------------------------------------------------------------------------------------

    public static void Loft(LowPolyMesh m, Section[] st, BodyPaint p)
    {
        int n = 4 * K + 2;
        for (int k = 0; k < st.Length - 1; k++)
        {
            var a = Ring(st[k]); var b = Ring(st[k + 1]);
            var center = (Centroid(a) + Centroid(b)) * 0.5f;
            float z = (st[k].Z + st[k + 1].Z) * 0.5f;
            for (int e = 0; e < n; e++)
            {
                int f = (e + 1) % n;
                var outward = (a[e] + a[f] + b[e] + b[f]) * 0.25f - center;
                outward.Z = 0;
                m.QuadFacing(a[e], a[f], b[f], b[e], outward.LengthSquared() > 1e-8f ? outward : Vector3.Up, FacetColor(e, z, p));
            }
        }
        Cap(m, Ring(st[0]), Vector3.Forward, p.Front ?? p.Top(st[0].Z).Darkened(0.1f));
        Cap(m, Ring(st[^1]), Vector3.Back, p.Back ?? p.Side(st[^1].Z).Darkened(0.15f));
    }

    private static Color FacetColor(int e, float z, BodyPaint p)
    {
        if (e == 0) return Carbon;
        if (e <= K) return p.Side(z);                                      // right lower
        if (e <= 2 * K) return e - (K + 1) >= K / 2 ? p.Top(z) : p.Shoulder(z); // right upper (0 = by the widest point)
        if (e == 2 * K + 1) return p.Top(z);                               // flat top
        if (e <= 3 * K + 1) return (3 * K + 1) - e >= K / 2 ? p.Top(z) : p.Shoulder(z); // left upper
        return p.Side(z);                                                  // left lower
    }

    /// <summary>Closes a ring with a fan from its centroid, facing along <paramref name="facing"/>.</summary>
    public static void Cap(LowPolyMesh m, Vector3[] ring, Vector3 facing, Color c)
    {
        var center = Centroid(ring);
        for (int i = 0; i < ring.Length; i++)
            m.QuadFacing(center, ring[i], ring[(i + 1) % ring.Length], center, facing, c);
    }

    /// <summary>
    /// A stripe across <paramref name="t0"/>..<paramref name="t1"/> of a region, along the loft between
    /// <paramref name="zFrom"/> and <paramref name="zTo"/>, laid on the facets just proud of them.
    /// </summary>
    public static void Band(LowPolyMesh m, Section[] st, int side, BodyRegion region, float t0, float t1, Color color,
        float zFrom = float.MinValue, float zTo = float.MaxValue)
    {
        // split the band at the facet edges so each piece lies flat on one facet
        var ts = new List<float> { t0 };
        if (region != BodyRegion.Top)
            for (int i = 1; i < K; i++) { float g = (float)i / K; if (g > t0 && g < t1) ts.Add(g); }
        ts.Add(t1);
        for (int k = 0; k < st.Length - 1; k++)
        {
            float zm = (st[k].Z + st[k + 1].Z) * 0.5f;
            if (zm < zFrom || zm > zTo) continue;
            var center = (Centroid(Ring(st[k])) + Centroid(Ring(st[k + 1]))) * 0.5f;
            for (int j = 0; j < ts.Count - 1; j++)
            {
                var p0 = FacetPoint(st[k], side, region, ts[j]); var p1 = FacetPoint(st[k], side, region, ts[j + 1]);
                var q0 = FacetPoint(st[k + 1], side, region, ts[j]); var q1 = FacetPoint(st[k + 1], side, region, ts[j + 1]);
                var outward = (p0 + p1 + q0 + q1) * 0.25f - center;
                outward.Z = 0;
                var nrm = (q0 - p0).Cross(p1 - p0);
                if (nrm.LengthSquared() < 1e-12f) continue;
                if (nrm.Dot(outward) < 0) nrm = -nrm;
                var off = nrm.Normalized() * 0.004f;
                m.QuadFacing(p0 + off, p1 + off, q1 + off, q0 + off, nrm, color);
            }
        }
    }

    /// <summary>Point on the body at <paramref name="z"/> with its outward facet normal and the direction of increasing t.</summary>
    public static (Vector3 Pos, Vector3 Normal, Vector3 Across) Surface(Section[] st, float z, int side, BodyRegion region, float t)
    {
        var s = At(st, z);
        var pos = FacetPoint(s, side, region, t);
        float dt = 0.02f;
        var across = FacetPoint(s, side, region, Mathf.Min(t + dt, 1)) - FacetPoint(s, side, region, Mathf.Max(t - dt, 0));
        var along = FacetPoint(At(st, z + 0.04f), side, region, t) - FacetPoint(At(st, z - 0.04f), side, region, t);
        var n = along.Cross(across);
        var outward = pos - Centroid(Ring(s));
        if (region == BodyRegion.Top) outward = Vector3.Up;
        if (n.Dot(outward) < 0) n = -n;
        return (pos, n.Normalized(), across.Normalized());
    }

    /// <summary>Dark opening on the front face of a section (sidepod inlets, airbox): the section shrunk towards a
    /// point high on its face.</summary>
    public static void Mouth(LowPolyMesh m, Section s, float scaleX, float yLow, float yHigh, Color c)
    {
        var inner = s with
        {
            Z = s.Z - 0.003f, W = s.W * scaleX, TopW = s.TopW * scaleX, BotW = s.BotW * scaleX,
            Yb = yLow, Yt = yHigh, Mid = 0.45f,
        };
        Cap(m, Ring(inner), Vector3.Forward, c);
    }
}
