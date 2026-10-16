using F1.Core.Geometry;
using F1.Core.Track;

namespace F1.Core.Racing;

/// <summary>Point-mass performance envelope used to build a speed profile.</summary>
public sealed record PerformanceModel(
    double MassKg = 860,
    double Mu = 1.9,
    double ClA = 5.1,
    double CdA = 1.2,
    double PowerW = 690_000,
    double DrivelineEfficiency = 0.92,
    double RollingCoeff = 0.012,
    double MaxBrakeG = 5.6,
    double TopSpeed = 96)
{
    public const double G = 9.81;
    public const double Rho = 1.19;
    public double DownforcePerV2 => 0.5 * Rho * ClA;
    public double DragPerV2 => 0.5 * Rho * CdA;

    /// <summary>Maximum cornering speed on curvature <paramref name="k"/> (1/m).</summary>
    public double CornerSpeed(double k)
    {
        double ak = Math.Abs(k);
        double denom = MassKg * ak - Mu * DownforcePerV2;
        if (denom <= 1e-9) return TopSpeed;
        return Math.Min(TopSpeed, Math.Sqrt(Mu * MassKg * G / denom));
    }

    /// <summary>Total grip acceleration (m/s²) available at speed v.</summary>
    public double GripAccel(double v) => Mu * (MassKg * G + DownforcePerV2 * v * v) / MassKg;
}

/// <summary>
/// Minimum-curvature racing line within the track limits plus a forward/backward-pass speed profile.
/// Arrays are indexed like the centreline samples.
/// </summary>
public sealed class RacingLine
{
    public TrackGeometry Center { get; }
    /// <summary>Lateral offset of the line from the centreline at each sample (positive left).</summary>
    public double[] Offset { get; }
    /// <summary>Target speed (m/s) at each sample.</summary>
    public double[] Speed { get; }
    /// <summary>Signed curvature of the racing line at each sample.</summary>
    public double[] Curvature { get; }
    public Vec2[] Points { get; }
    public double LapTime { get; }
    public double LineLength { get; }

    private RacingLine(TrackGeometry center, double[] offset, double[] speed, double[] curvature, Vec2[] points, double lapTime, double lineLength)
    {
        Center = center; Offset = offset; Speed = speed; Curvature = curvature; Points = points; LapTime = lapTime; LineLength = lineLength;
    }

    public double OffsetAt(double s) => Center.Sample(Offset, s);
    public double SpeedAt(double s) => Center.Sample(Speed, s);
    public double CurvatureAt(double s) => Center.Sample(Curvature, s);

    /// <summary>Builds the line. <paramref name="margin"/> keeps the car's centre this far inside the white lines.</summary>
    public static RacingLine Build(TrackData track, PerformanceModel perf, double margin = 1.3, int iterations = 600)
    {
        var center = new TrackGeometry(track.Centerline);
        int n = center.Count;
        var lo = new double[n];
        var hi = new double[n];
        for (int i = 0; i < n; i++)
        {
            lo[i] = -track.WidthRight[i] + margin;
            hi[i] = track.WidthLeft[i] - margin;
        }
        var d = new double[n];
        var c = new Vec2[n];
        var nrm = new Vec2[n];
        for (int i = 0; i < n; i++) { c[i] = center.Point(i); nrm[i] = center.Normal(i); }

        // Minimum curvature: Gauss-Seidel on the biharmonic (sum of squared second differences). Each point moves,
        // along its normal, to where the second difference with its neighbours at ±k and ±2k is smallest:
        // p = (4(p[-k] + p[+k]) − (p[-2k] + p[+2k])) / 6. Coarse-to-fine so long corners converge quickly.
        Vec2 P(int i) => c[i] + nrm[i] * d[i];
        foreach (int k in new[] { 16, 8, 4, 2, 1 })
        {
            for (int it = 0; it < iterations; it++)
            {
                for (int i = 0; i < n; i++)
                {
                    var target = (4 * (P((i - k + n) % n) + P((i + k) % n)) - (P((i - 2 * k + 2 * n) % n) + P((i + 2 * k) % n))) / 6;
                    double t = Vec2.Dot(target - c[i], nrm[i]);
                    d[i] = Math.Clamp(d[i] + (t - d[i]) * 0.5, lo[i], hi[i]);
                }
            }
        }

        var pts = new Vec2[n];
        for (int i = 0; i < n; i++) pts[i] = c[i] + nrm[i] * d[i];
        var rawK = new double[n];
        for (int i = 0; i < n; i++)
            rawK[i] = TrackGeometry.ThreePointCurvature(pts[(i - 2 + n) % n], pts[i], pts[(i + 2) % n]);
        var curvature = Polyline.GaussianSmooth(rawK, 1.5, true);

        var ds = new double[n];
        double lineLength = 0;
        for (int i = 0; i < n; i++) { ds[i] = Vec2.Distance(pts[i], pts[(i + 1) % n]); lineLength += ds[i]; }

        var speed = SpeedProfile(curvature, ds, perf);
        double time = 0;
        for (int i = 0; i < n; i++) time += ds[i] / Math.Max(1, 0.5 * (speed[i] + speed[(i + 1) % n]));
        return new RacingLine(center, d, speed, curvature, pts, time, lineLength);
    }

    /// <summary>Forward (traction/power) and backward (braking) passes around a closed loop.</summary>
    public static double[] SpeedProfile(double[] curvature, double[] ds, PerformanceModel p)
    {
        int n = curvature.Length;
        var vmax = new double[n];
        for (int i = 0; i < n; i++) vmax[i] = p.CornerSpeed(curvature[i]);
        var v = (double[])vmax.Clone();

        // start the passes at the slowest point so the loop closes consistently
        int start = Array.IndexOf(vmax, vmax.Min());
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % n, j = (i + 1) % n;
                double vi = v[i];
                double aLat = vi * vi * Math.Abs(curvature[i]);
                double grip = p.GripAccel(vi);
                double aTraction = Math.Sqrt(Math.Max(0, grip * grip - aLat * aLat)) * 0.85;
                double aPower = p.PowerW * p.DrivelineEfficiency / (p.MassKg * Math.Max(vi, 5));
                double aDrag = (p.DragPerV2 * vi * vi) / p.MassKg + p.RollingCoeff * PerformanceModel.G;
                double a = Math.Min(aTraction, aPower) - aDrag;
                double vj = Math.Sqrt(Math.Max(0, vi * vi + 2 * a * ds[i]));
                v[j] = Math.Min(v[j], Math.Min(vj, vmax[j]));
            }
            for (int k = 0; k < n; k++)
            {
                int j = (start - k + n) % n, i = (j - 1 + n) % n;
                double vj = v[j];
                double aLat = vj * vj * Math.Abs(curvature[j]);
                double grip = Math.Min(p.GripAccel(vj), p.MaxBrakeG * PerformanceModel.G);
                double aBrake = Math.Sqrt(Math.Max(0, grip * grip - aLat * aLat)) + (p.DragPerV2 * vj * vj) / p.MassKg;
                double vi = Math.Sqrt(vj * vj + 2 * aBrake * ds[i]);
                v[i] = Math.Min(v[i], vi);
            }
        }
        return v;
    }
}
