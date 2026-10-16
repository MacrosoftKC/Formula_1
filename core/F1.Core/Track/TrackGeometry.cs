using F1.Core.Geometry;

namespace F1.Core.Track;

/// <summary>Result of projecting a world point onto a centreline.</summary>
public readonly record struct TrackProjection(double S, double D, int Segment, double DistanceSquared);

/// <summary>
/// Arc-length parameterised polyline (closed or open) with fast nearest-point projection.
/// Used for the circuit centreline, the pit lane and the racing line.
/// </summary>
public sealed class TrackGeometry
{
    private readonly Vec2[] _pts;
    private readonly double[] _s;
    private readonly Vec2[] _tangent;
    private readonly double[] _curvature;
    private readonly Dictionary<long, List<int>> _grid = new();
    private const double CellSize = 25.0;

    public bool Closed { get; }
    public double Length { get; }
    public int Count => _pts.Length;
    private int SegmentCount => Closed ? _pts.Length : _pts.Length - 1;

    public TrackGeometry(IReadOnlyList<Vec2> points, bool closed = true, int curvatureHalfWindow = 3)
    {
        if (points.Count < 3) throw new ArgumentException("Need at least 3 points", nameof(points));
        Closed = closed;
        _pts = points.ToArray();
        int n = _pts.Length;
        _s = new double[n];
        for (int i = 1; i < n; i++) _s[i] = _s[i - 1] + Vec2.Distance(_pts[i - 1], _pts[i]);
        Length = closed ? _s[n - 1] + Vec2.Distance(_pts[n - 1], _pts[0]) : _s[n - 1];

        _tangent = new Vec2[n];
        _curvature = new double[n];
        for (int i = 0; i < n; i++)
        {
            var prev = Point(i - 1);
            var next = Point(i + 1);
            _tangent[i] = (next - prev).Normalized();
            _curvature[i] = ThreePointCurvature(Point(i - curvatureHalfWindow), _pts[i], Point(i + curvatureHalfWindow));
        }

        for (int i = 0; i < SegmentCount; i++)
        {
            var a = _pts[i];
            var b = _pts[(i + 1) % n];
            int x0 = Cell(Math.Min(a.X, b.X)), x1 = Cell(Math.Max(a.X, b.X));
            int y0 = Cell(Math.Min(a.Y, b.Y)), y1 = Cell(Math.Max(a.Y, b.Y));
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    long key = Key(x, y);
                    if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>();
                    list.Add(i);
                }
        }
    }

    private static int Cell(double v) => (int)Math.Floor(v / CellSize);
    private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

    /// <summary>Signed curvature (1/m) through three points; positive = turning left.</summary>
    public static double ThreePointCurvature(Vec2 a, Vec2 b, Vec2 c)
    {
        double ab = Vec2.Distance(a, b), bc = Vec2.Distance(b, c), ca = Vec2.Distance(c, a);
        double denom = ab * bc * ca;
        if (denom < 1e-9) return 0;
        return 2 * Vec2.Cross(b - a, c - b) / denom;
    }

    private int WrapIndex(int i)
    {
        int n = _pts.Length;
        if (Closed) return ((i % n) + n) % n;
        return Math.Clamp(i, 0, n - 1);
    }

    public Vec2 Point(int i) => _pts[WrapIndex(i)];
    public double SAt(int i) => _s[WrapIndex(i)];
    public Vec2 Tangent(int i) => _tangent[WrapIndex(i)];
    public Vec2 Normal(int i) => _tangent[WrapIndex(i)].PerpLeft;
    public double Curvature(int i) => _curvature[WrapIndex(i)];
    public IReadOnlyList<Vec2> Points => _pts;

    public double Wrap(double s)
    {
        if (!Closed) return Math.Clamp(s, 0, Length);
        s %= Length;
        return s < 0 ? s + Length : s;
    }

    /// <summary>Signed shortest distance from <paramref name="from"/> to <paramref name="to"/> along a closed loop.</summary>
    public double Delta(double to, double from)
    {
        double d = to - from;
        if (!Closed) return d;
        d %= Length;
        if (d > Length / 2) d -= Length;
        if (d < -Length / 2) d += Length;
        return d;
    }

    /// <summary>Distance travelled forwards from <paramref name="from"/> to <paramref name="to"/> (0..Length).</summary>
    public double Forward(double to, double from)
    {
        double d = Wrap(to) - Wrap(from);
        return d < 0 ? d + Length : d;
    }

    /// <summary>Index of the segment containing arc length <paramref name="s"/> and the fraction along it.</summary>
    public (int Index, double T) Locate(double s)
    {
        s = Wrap(s);
        int lo = 0, hi = _s.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_s[mid] <= s) lo = mid; else hi = mid - 1;
        }
        double segEnd = lo + 1 < _s.Length ? _s[lo + 1] : Length;
        double segLen = segEnd - _s[lo];
        double t = segLen > 1e-9 ? (s - _s[lo]) / segLen : 0;
        return (lo, Math.Clamp(t, 0, 1));
    }

    public Vec2 PositionAt(double s)
    {
        var (i, t) = Locate(s);
        return Vec2.Lerp(_pts[i], Point(i + 1), t);
    }

    public Vec2 TangentAt(double s)
    {
        var (i, t) = Locate(s);
        return Vec2.Lerp(_tangent[i], Tangent(i + 1), t).Normalized();
    }

    public Vec2 NormalAt(double s) => TangentAt(s).PerpLeft;

    public double CurvatureAt(double s)
    {
        var (i, t) = Locate(s);
        return _curvature[i] + (Curvature(i + 1) - _curvature[i]) * t;
    }

    /// <summary>Interpolates a per-sample scalar profile at arc length s.</summary>
    public double Sample(IReadOnlyList<double> profile, double s)
    {
        var (i, t) = Locate(s);
        double a = profile[i];
        double b = profile[WrapIndex(i + 1)];
        return a + (b - a) * t;
    }

    public int NearestIndex(double s)
    {
        var (i, t) = Locate(s);
        return t < 0.5 ? i : WrapIndex(i + 1);
    }

    public Vec2 ToWorld(double s, double d) => PositionAt(s) + NormalAt(s) * d;

    /// <summary>
    /// Projects a point onto the polyline. With a hint, only segments within ±window metres of the hint are searched
    /// (fast, and avoids snapping to a nearby but different part of the circuit). Without a hint a spatial grid is used.
    /// </summary>
    public TrackProjection Project(Vec2 p, double? hintS = null, double window = 80)
    {
        int bestSeg = -1;
        double bestD2 = double.MaxValue, bestT = 0;

        void Test(int seg)
        {
            var a = _pts[seg];
            var b = _pts[(seg + 1) % _pts.Length];
            var ab = b - a;
            double l2 = ab.LengthSquared;
            double t = l2 > 1e-12 ? Math.Clamp(Vec2.Dot(p - a, ab) / l2, 0, 1) : 0;
            double d2 = (p - (a + ab * t)).LengthSquared;
            if (d2 < bestD2) { bestD2 = d2; bestSeg = seg; bestT = t; }
        }

        if (hintS.HasValue)
        {
            var (i0, _) = Locate(hintS.Value);
            double spacing = Length / Math.Max(1, SegmentCount);
            int span = (int)Math.Ceiling(window / Math.Max(spacing, 0.01)) + 1;
            for (int k = -span; k <= span; k++)
            {
                int seg = i0 + k;
                if (Closed) seg = ((seg % SegmentCount) + SegmentCount) % SegmentCount;
                else if (seg < 0 || seg >= SegmentCount) continue;
                Test(seg);
            }
        }
        else
        {
            int cx = Cell(p.X), cy = Cell(p.Y);
            for (int ring = 0; ring <= 6 && bestSeg < 0; ring++)
            {
                for (int x = cx - ring; x <= cx + ring; x++)
                    for (int y = cy - ring; y <= cy + ring; y++)
                    {
                        if (Math.Abs(x - cx) != ring && Math.Abs(y - cy) != ring) continue;
                        if (_grid.TryGetValue(Key(x, y), out var list))
                            foreach (int seg in list) Test(seg);
                    }
                // one extra ring after the first hit so a closer segment in a neighbouring cell isn't missed
                if (bestSeg >= 0 && ring < 6)
                {
                    int r2 = ring + 1;
                    for (int x = cx - r2; x <= cx + r2; x++)
                        for (int y = cy - r2; y <= cy + r2; y++)
                        {
                            if (Math.Abs(x - cx) != r2 && Math.Abs(y - cy) != r2) continue;
                            if (_grid.TryGetValue(Key(x, y), out var list))
                                foreach (int seg in list) Test(seg);
                        }
                }
            }
            if (bestSeg < 0)
                for (int seg = 0; seg < SegmentCount; seg++) Test(seg);
        }

        var pa = _pts[bestSeg];
        var pb = _pts[(bestSeg + 1) % _pts.Length];
        double segLen = Vec2.Distance(pa, pb);
        double s = _s[bestSeg] + segLen * bestT;
        var tangent = (pb - pa).Normalized();
        var foot = Vec2.Lerp(pa, pb, bestT);
        double d = Vec2.Dot(p - foot, tangent.PerpLeft);
        return new TrackProjection(Wrap(s), d, bestSeg, bestD2);
    }
}
