namespace F1.Core.Track;

/// <summary>Finds corners from the curvature profile of a uniformly sampled closed centreline.</summary>
public static class CornerDetector
{
    public sealed record Options(
        double SmoothingMetres = 10,
        double MaxCornerRadius = 450,
        double MergeGapMetres = 25,
        double MinAngleDegrees = 16,
        double EdgeRadius = 900);

    public static List<Corner> Detect(TrackGeometry geo, Options? options = null)
    {
        var o = options ?? new Options();
        int n = geo.Count;
        double spacing = geo.Length / n;
        var raw = new double[n];
        for (int i = 0; i < n; i++) raw[i] = geo.Curvature(i);
        var k = F1.Core.Geometry.Polyline.GaussianSmooth(raw, o.SmoothingMetres / spacing, true);

        double on = 1.0 / o.MaxCornerRadius;
        double edge = 1.0 / o.EdgeRadius;

        // Start scanning at a point on a straight so no region wraps the seam.
        int start = 0;
        for (int i = 0; i < n; i++) if (Math.Abs(k[i]) < edge) { start = i; break; }

        var regions = new List<(int A, int B, int Sign)>();
        int idx = 0;
        while (idx < n)
        {
            int i = (start + idx) % n;
            if (Math.Abs(k[i]) > on)
            {
                int sign = Math.Sign(k[i]);
                // grow backwards/forwards to the softer edge threshold
                int a = idx, b = idx;
                while (b + 1 < n && Math.Sign(k[(start + b + 1) % n]) == sign && Math.Abs(k[(start + b + 1) % n]) > edge) b++;
                while (a - 1 >= 0 && Math.Sign(k[(start + a - 1) % n]) == sign && Math.Abs(k[(start + a - 1) % n]) > edge) a--;
                regions.Add((a, b, sign));
                idx = b + 1;
            }
            else idx++;
        }

        // merge same-direction regions separated by a short gap
        int gap = (int)Math.Round(o.MergeGapMetres / spacing);
        var merged = new List<(int A, int B, int Sign)>();
        foreach (var r in regions)
        {
            if (merged.Count > 0 && merged[^1].Sign == r.Sign && r.A - merged[^1].B <= gap)
                merged[^1] = (merged[^1].A, r.B, r.Sign);
            else merged.Add(r);
        }

        var corners = new List<Corner>();
        foreach (var r in merged)
        {
            double angle = 0, maxK = 0;
            int apex = r.A;
            for (int j = r.A; j <= r.B; j++)
            {
                int i = (start + j) % n;
                angle += k[i] * spacing;
                if (Math.Abs(k[i]) > maxK) { maxK = Math.Abs(k[i]); apex = j; }
            }
            double deg = Math.Abs(angle) * 180 / Math.PI;
            if (deg < o.MinAngleDegrees) continue;
            corners.Add(new Corner
            {
                EntryS = geo.SAt((start + r.A) % n),
                ApexS = geo.SAt((start + apex) % n),
                ExitS = geo.SAt((start + r.B) % n),
                Direction = r.Sign,
                MinRadius = Math.Round(1.0 / Math.Max(maxK, 1e-6), 1),
                Angle = Math.Round(deg, 1),
            });
        }

        corners.Sort((x, y) => x.ApexS.CompareTo(y.ApexS));
        for (int i = 0; i < corners.Count; i++) corners[i].Number = i + 1;
        return corners;
    }
}
