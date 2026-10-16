using F1.Core.Geometry;

namespace F1.Game.Track;

/// <summary>
/// Raster of the area around the circuit (track space, fixed cell size) used to place scenery quickly:
/// water/island flags, distance to the shoreline, distance to the centreline, and blocked/reserved cells
/// (track corridor, grandstands, pit complex). Built once; lookups are O(1).
/// </summary>
public sealed class SceneryMask
{
    public const byte Land = 0, Water = 1, Island = 2;

    public readonly double Cell;
    public readonly double MinX, MinY;
    public readonly int W, H;
    private readonly byte[] _water;
    private readonly bool[] _blocked;
    private readonly float[] _shore;
    private readonly float[] _track;

    public SceneryMask(TrackSurface ts, double margin = 1600, double cell = 5)
    {
        Cell = cell;
        var pts = ts.Data.Centerline;
        MinX = pts.Min(p => p.X) - margin; MinY = pts.Min(p => p.Y) - margin;
        double maxX = pts.Max(p => p.X) + margin, maxY = pts.Max(p => p.Y) + margin;
        W = (int)Math.Ceiling((maxX - MinX) / cell); H = (int)Math.Ceiling((maxY - MinY) / cell);
        _water = new byte[W * H];
        _blocked = new bool[W * H];
        _shore = new float[W * H];
        _track = new float[W * H];

        var lake = ts.Data.Lake;
        if (lake.Count > 0)
        {
            Fill(lake[0], Water);
            for (int r = 1; r < lake.Count; r++) Fill(lake[r], Island);
        }
        ShoreDistance();
        TrackDistance(ts);
    }

    public bool TryIndex(Vec2 p, out int idx)
    {
        int x = (int)((p.X - MinX) / Cell), y = (int)((p.Y - MinY) / Cell);
        idx = y * W + x;
        return x >= 0 && y >= 0 && x < W && y < H;
    }

    public byte WaterAt(Vec2 p) => TryIndex(p, out int i) ? _water[i] : Land;
    public bool Blocked(Vec2 p) => !TryIndex(p, out int i) || _blocked[i];
    public float ShoreDistance(Vec2 p) => TryIndex(p, out int i) ? _shore[i] : 9999f;
    public float TrackDistance(Vec2 p) => TryIndex(p, out int i) ? _track[i] : 9999f;

    public void Block(Vec2 p)
    {
        if (TryIndex(p, out int i)) _blocked[i] = true;
    }

    public void BlockDisk(Vec2 c, double radius)
    {
        int r = (int)Math.Ceiling(radius / Cell);
        int cx = (int)((c.X - MinX) / Cell), cy = (int)((c.Y - MinY) / Cell);
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                double dx = (x - cx) * Cell, dy = (y - cy) * Cell;
                if (dx * dx + dy * dy <= radius * radius) _blocked[y * W + x] = true;
            }
    }

    /// <summary>Blocks the strip between lateral offsets d0..d1 (signed, left positive) along a centreline range.</summary>
    public void BlockStrip(F1.Core.Track.TrackGeometry g, double fromS, double toS, double d0, double d1)
    {
        double len = g.Forward(toS, fromS);
        if (!g.Closed) len = toS - fromS;
        double step = Cell * 0.5;
        for (double u = 0; u <= len; u += step)
        {
            double s = fromS + u;
            var p = g.PositionAt(s); var n = g.NormalAt(s);
            double lo = Math.Min(d0, d1), hi = Math.Max(d0, d1);
            for (double d = lo; d <= hi; d += step) Block(p + n * d);
        }
    }

    private void Fill(List<Vec2> ring, byte value)
    {
        var xs = new List<double>();
        for (int y = 0; y < H; y++)
        {
            double py = MinY + (y + 0.5) * Cell;
            xs.Clear();
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i]; var b = ring[j];
                if ((a.Y > py) != (b.Y > py)) xs.Add(a.X + (py - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int x0 = Math.Max(0, (int)Math.Ceiling((xs[k] - MinX) / Cell - 0.5));
                int x1 = Math.Min(W - 1, (int)Math.Floor((xs[k + 1] - MinX) / Cell - 0.5));
                for (int x = x0; x <= x1; x++) _water[y * W + x] = value;
            }
        }
    }

    private void ShoreDistance()
    {
        // seed: cells whose water state differs from a neighbour
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                bool wet = _water[i] == Water;
                bool edge = false;
                if (x > 0 && (_water[i - 1] == Water) != wet) edge = true;
                if (x < W - 1 && (_water[i + 1] == Water) != wet) edge = true;
                if (y > 0 && (_water[i - W] == Water) != wet) edge = true;
                if (y < H - 1 && (_water[i + W] == Water) != wet) edge = true;
                _shore[i] = edge ? 0f : float.MaxValue;
            }
        Chamfer(_shore);
    }

    private void TrackDistance(TrackSurface ts)
    {
        Array.Fill(_track, float.MaxValue);
        var g = ts.Center;
        for (double s = 0; s < g.Length; s += Cell * 0.5)
        {
            var p = g.PositionAt(s);
            if (TryIndex(p, out int i)) _track[i] = 0f;
            // corridor: track, run-off and barrier plus clearance is blocked for scenery
            double bl = ts.BarrierLeft(s) + 6, br = ts.BarrierRight(s) + 6;
            var n = g.NormalAt(s);
            for (double d = -br; d <= bl; d += Cell * 0.5) Block(p + n * d);
        }
        Chamfer(_track);
    }

    /// <summary>Two-pass 3x3 chamfer distance transform (metres).</summary>
    private void Chamfer(float[] f)
    {
        float a = (float)Cell, b = (float)(Cell * Math.Sqrt(2));
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x; float v = f[i];
                if (x > 0) v = Math.Min(v, f[i - 1] + a);
                if (y > 0)
                {
                    v = Math.Min(v, f[i - W] + a);
                    if (x > 0) v = Math.Min(v, f[i - W - 1] + b);
                    if (x < W - 1) v = Math.Min(v, f[i - W + 1] + b);
                }
                f[i] = v;
            }
        for (int y = H - 1; y >= 0; y--)
            for (int x = W - 1; x >= 0; x--)
            {
                int i = y * W + x; float v = f[i];
                if (x < W - 1) v = Math.Min(v, f[i + 1] + a);
                if (y < H - 1)
                {
                    v = Math.Min(v, f[i + W] + a);
                    if (x < W - 1) v = Math.Min(v, f[i + W + 1] + b);
                    if (x > 0) v = Math.Min(v, f[i + W - 1] + b);
                }
                f[i] = v;
            }
    }
}
