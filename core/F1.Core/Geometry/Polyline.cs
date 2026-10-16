namespace F1.Core.Geometry;

/// <summary>Polyline helpers used by the track builder and runtime geometry.</summary>
public static class Polyline
{
    public static double Length(IReadOnlyList<Vec2> pts, bool closed)
    {
        double len = 0;
        for (int i = 1; i < pts.Count; i++) len += Vec2.Distance(pts[i - 1], pts[i]);
        if (closed && pts.Count > 1) len += Vec2.Distance(pts[^1], pts[0]);
        return len;
    }

    /// <summary>Signed area (shoelace). Negative means clockwise in a Y-up (north-up) frame.</summary>
    public static double SignedArea(IReadOnlyList<Vec2> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a * 0.5;
    }

    /// <summary>Removes consecutive points closer than <paramref name="minDist"/>.</summary>
    public static List<Vec2> Dedupe(IReadOnlyList<Vec2> pts, double minDist, bool closed)
    {
        var res = new List<Vec2>(pts.Count);
        foreach (var p in pts)
            if (res.Count == 0 || Vec2.Distance(res[^1], p) >= minDist) res.Add(p);
        if (closed)
            while (res.Count > 2 && Vec2.Distance(res[^1], res[0]) < minDist) res.RemoveAt(res.Count - 1);
        return res;
    }

    /// <summary>Resamples to uniform arc-length spacing. For closed loops the spacing is adjusted so the loop divides evenly.</summary>
    public static List<Vec2> Resample(IReadOnlyList<Vec2> pts, double spacing, bool closed)
    {
        double total = Length(pts, closed);
        int count = Math.Max(2, (int)Math.Round(total / spacing));
        double step = closed ? total / count : total / (count - 1);
        int n = closed ? count : count;

        var res = new List<Vec2>(n);
        int seg = 0;
        double segStart = 0;
        int segCount = closed ? pts.Count : pts.Count - 1;
        for (int k = 0; k < n; k++)
        {
            double target = k * step;
            if (!closed && k == n - 1) { res.Add(pts[^1]); break; }
            while (true)
            {
                var a = pts[seg];
                var b = pts[(seg + 1) % pts.Count];
                double segLen = Vec2.Distance(a, b);
                if (target <= segStart + segLen || seg == segCount - 1)
                {
                    double t = segLen > 1e-9 ? (target - segStart) / segLen : 0;
                    res.Add(Vec2.Lerp(a, b, Math.Clamp(t, 0, 1)));
                    break;
                }
                segStart += segLen;
                seg++;
            }
        }
        return res;
    }

    /// <summary>Gaussian smoothing over a closed or open polyline sampled at uniform spacing.</summary>
    public static List<Vec2> GaussianSmooth(IReadOnlyList<Vec2> pts, double sigmaSamples, bool closed)
    {
        if (sigmaSamples <= 0) return pts.ToList();
        int radius = (int)Math.Ceiling(sigmaSamples * 3);
        var weights = new double[radius * 2 + 1];
        for (int k = -radius; k <= radius; k++) weights[k + radius] = Math.Exp(-(k * k) / (2 * sigmaSamples * sigmaSamples));

        int n = pts.Count;
        var res = new List<Vec2>(n);
        for (int i = 0; i < n; i++)
        {
            double sx = 0, sy = 0, sw = 0;
            for (int k = -radius; k <= radius; k++)
            {
                int j = i + k;
                if (closed) j = ((j % n) + n) % n;
                else if (j < 0 || j >= n) continue;
                double w = weights[k + radius];
                sx += pts[j].X * w; sy += pts[j].Y * w; sw += w;
            }
            res.Add(new Vec2(sx / sw, sy / sw));
        }
        return res;
    }

    /// <summary>Smooths a scalar profile along a closed or open sequence.</summary>
    public static double[] GaussianSmooth(IReadOnlyList<double> values, double sigmaSamples, bool closed)
    {
        int n = values.Count;
        var res = new double[n];
        if (sigmaSamples <= 0) { for (int i = 0; i < n; i++) res[i] = values[i]; return res; }
        int radius = (int)Math.Ceiling(sigmaSamples * 3);
        for (int i = 0; i < n; i++)
        {
            double s = 0, sw = 0;
            for (int k = -radius; k <= radius; k++)
            {
                int j = i + k;
                if (closed) j = ((j % n) + n) % n;
                else if (j < 0 || j >= n) continue;
                double w = Math.Exp(-(k * k) / (2 * sigmaSamples * sigmaSamples));
                s += values[j] * w; sw += w;
            }
            res[i] = s / sw;
        }
        return res;
    }

    public static bool SegmentsIntersect(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        double d1 = Vec2.Cross(q2 - q1, p1 - q1);
        double d2 = Vec2.Cross(q2 - q1, p2 - q1);
        double d3 = Vec2.Cross(p2 - p1, q1 - p1);
        double d4 = Vec2.Cross(p2 - p1, q2 - p1);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    /// <summary>True if any two non-adjacent segments cross. O(n²) — intended for build-time validation.</summary>
    public static bool SelfIntersects(IReadOnlyList<Vec2> pts, bool closed)
    {
        int n = pts.Count;
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a1 = pts[i]; var a2 = pts[(i + 1) % n];
            for (int j = i + 2; j < segs; j++)
            {
                if (closed && i == 0 && j == segs - 1) continue;
                var b1 = pts[j]; var b2 = pts[(j + 1) % n];
                if (SegmentsIntersect(a1, a2, b1, b2)) return true;
            }
        }
        return false;
    }

    public static bool PointInPolygon(Vec2 p, IReadOnlyList<Vec2> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double l2 = ab.LengthSquared;
        double t = l2 > 1e-12 ? Math.Clamp(Vec2.Dot(p - a, ab) / l2, 0, 1) : 0;
        return Vec2.Distance(p, a + ab * t);
    }

    public static double DistanceToPolyline(Vec2 p, IReadOnlyList<Vec2> pts, bool closed)
    {
        double best = double.MaxValue;
        int segs = closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < segs; i++)
            best = Math.Min(best, DistanceToSegment(p, pts[i], pts[(i + 1) % pts.Count]));
        return best;
    }
}
