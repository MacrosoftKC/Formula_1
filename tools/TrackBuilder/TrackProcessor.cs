using F1.Core.Geometry;
using F1.Core.Track;

namespace TrackBuilder;

/// <summary>Turns raw OSM geometry into the game's <see cref="TrackData"/>.</summary>
public sealed class TrackProcessor
{
    private const double Spacing = 2.0;
    /// <summary>Distance after the pit entry / before the pit exit where the concrete pit wall starts / ends.</summary>
    private const double PitWallStart = 100, PitWallEnd = 130;

    private readonly List<OsmWay> _roads;
    private readonly List<OsmWay> _lakeWays;
    private readonly List<string> _log = new();
    public IReadOnlyList<string> Log => _log;

    public TrackProcessor(List<OsmWay> roads, List<OsmWay> lakeWays)
    {
        _roads = roads;
        _lakeWays = lakeWays;
    }

    private Vec2 P(double lat, double lon) => Osm.Project(lat, lon, AlbertPark.OriginLat, AlbertPark.OriginLon);

    public TrackData Build()
    {
        // 1. Chain the lap ways, orienting each to continue from the previous end.
        var byId = _roads.ToDictionary(w => w.Id);
        var raw = new List<Vec2>();
        foreach (long id in AlbertPark.LapWays)
        {
            if (!byId.TryGetValue(id, out var way)) throw new InvalidOperationException($"OSM way {id} missing from cache");
            var g = way.Geometry.Select(p => P(p.Lat, p.Lon)).ToList();
            if (raw.Count > 0 && Vec2.Distance(g[^1], raw[^1]) < Vec2.Distance(g[0], raw[^1])) g.Reverse();
            if (raw.Count > 0)
            {
                double gap = Vec2.Distance(raw[^1], g[0]);
                if (gap > 25) throw new InvalidOperationException($"Gap of {gap:0.0} m before way {id}");
                if (gap < 0.5) g.RemoveAt(0);
            }
            raw.AddRange(g);
        }
        double closeGap = Vec2.Distance(raw[0], raw[^1]);
        if (closeGap > 25) throw new InvalidOperationException($"Lap does not close ({closeGap:0.0} m)");
        _log.Add($"raw OSM lap: {raw.Count} nodes, {Polyline.Length(raw, true):0} m");

        // 2. Clean up: uniform resample, smooth away polyline kinks, enforce clockwise order.
        var pts = Polyline.Dedupe(raw, 0.3, closed: true);
        pts = Polyline.Resample(pts, 1.0, closed: true);
        var baseSmooth = Polyline.GaussianSmooth(pts, sigmaSamples: 4.0, closed: true);
        foreach (var (cx, cy, radius, sigma) in AlbertPark.LocalSmoothing)
        {
            var wide = Polyline.GaussianSmooth(pts, sigma, closed: true);
            var c = new Vec2(cx, cy);
            for (int i = 0; i < baseSmooth.Count; i++)
            {
                double w = SmoothStep(1 - Vec2.Distance(pts[i], c) / radius);
                if (w > 0) baseSmooth[i] = Vec2.Lerp(baseSmooth[i], wide[i], w);
            }
        }
        pts = RoundTightCorners(baseSmooth);
        if (Polyline.SignedArea(pts) > 0) pts.Reverse();
        double smoothLen = Polyline.Length(pts, true);

        // 3. Scale uniformly about the origin so the lap matches the official length (the OSM road centreline is ~0.7% short).
        double scale = AlbertPark.OfficialLength / smoothLen;
        pts = pts.Select(p => p * scale).ToList();
        _log.Add($"smoothed lap {smoothLen:0.0} m, scale x{scale:0.0000} to {AlbertPark.OfficialLength} m");

        // 4. Re-sample so sample 0 lies exactly on the start/finish line.
        var tmp = new TrackGeometry(pts, closed: true);
        var sf = P(AlbertPark.StartLat, AlbertPark.StartLon) * scale;
        var sfProj = tmp.Project(sf);
        _log.Add($"start line projects {Math.Sqrt(sfProj.DistanceSquared):0.0} m from centreline");
        int n = (int)Math.Round(tmp.Length / Spacing);
        double step = tmp.Length / n;
        var center = new List<Vec2>(n);
        for (int i = 0; i < n; i++) center.Add(tmp.PositionAt(sfProj.S + i * step));
        var geo = new TrackGeometry(center, closed: true);

        if (Polyline.SelfIntersects(center, true)) throw new InvalidOperationException("Centreline self-intersects");

        var data = new TrackData
        {
            Id = AlbertPark.Id,
            Name = AlbertPark.Name,
            Location = AlbertPark.Location,
            Attribution = "Circuit, lake and park geometry © OpenStreetMap contributors (ODbL 1.0).",
            OfficialLength = AlbertPark.OfficialLength,
            RaceLaps = AlbertPark.RaceLaps,
            Clockwise = true,
            OriginLat = AlbertPark.OriginLat,
            OriginLon = AlbertPark.OriginLon,
            Scale = scale,
            SampleSpacing = step,
            Centerline = center,
        };

        // 5. Corners: match curvature peaks to the official numbering, keep the rest as bends.
        var detected = CornerDetector.Detect(geo);
        foreach (var c in detected) c.Number = 0;
        foreach (var (num, ax, ay) in AlbertPark.OfficialApexes)
        {
            var target = new Vec2(ax, ay) * scale;
            var best = detected.Where(c => c.Number == 0)
                .OrderBy(c => Vec2.Distance(geo.PositionAt(c.ApexS), target)).FirstOrDefault();
            if (best == null || Vec2.Distance(geo.PositionAt(best.ApexS), target) > 60)
                throw new InvalidOperationException($"No curvature peak found near official Turn {num}");
            best.Number = num;
        }
        data.Corners = detected.Where(c => c.Number > 0).OrderBy(c => c.Number).ToList();
        data.Bends = detected.Where(c => c.Number == 0).ToList();
        for (int i = 1; i < data.Corners.Count; i++)
            if (data.Corners[i].ApexS < data.Corners[i - 1].ApexS)
                throw new InvalidOperationException($"Turn {data.Corners[i].Number} is out of order");
        _log.Add($"{data.Corners.Count} corners, {data.Bends.Count} bends");
        foreach (var c in detected.OrderBy(c => c.ApexS))
            _log.Add($"  {(c.Number > 0 ? "T" + c.Number : "bend"),-4} {(c.Direction > 0 ? "L" : "R")} apex s={c.ApexS,6:0} entry={c.EntryS,6:0} exit={c.ExitS,6:0} r={c.MinRadius,6:0} angle={c.Angle,5:0}");

        BuildWidths(data, geo);
        BuildPitLane(data, geo);
        BuildRaceControl(data, geo);
        BuildLake(data, scale);
        BuildRunoff(data, geo);
        return data;
    }

    /// <summary>
    /// Iteratively relaxes points whose curvature exceeds the local limit (Laplacian smoothing restricted to those
    /// points), rounding junction corners to a minimum radius while leaving everything else untouched.
    /// </summary>
    private List<Vec2> RoundTightCorners(List<Vec2> input)
    {
        var p = input.ToArray();
        int n = p.Length;
        var limit = new double[n];
        for (int i = 0; i < n; i++)
        {
            limit[i] = double.MaxValue;
            foreach (var (cx, cy, radius, minR) in AlbertPark.MinimumRadius)
                if (Vec2.Distance(p[i], new Vec2(cx, cy)) < radius) limit[i] = Math.Min(limit[i], 1.0 / minR);
        }
        int changed = 0;
        for (int it = 0; it < 4000; it++)
        {
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (limit[i] == double.MaxValue) continue;
                var a = p[(i - 3 + n) % n]; var b = p[i]; var c = p[(i + 3) % n];
                double k = Math.Abs(F1.Core.Track.TrackGeometry.ThreePointCurvature(a, b, c));
                if (k <= limit[i]) continue;
                any = true; changed++;
                for (int j = -2; j <= 2; j++)
                {
                    int q = (i + j + n) % n;
                    var mid = (p[(q - 1 + n) % n] + p[(q + 1) % n]) * 0.5;
                    p[q] = Vec2.Lerp(p[q], mid, 0.35);
                }
            }
            if (!any) break;
        }
        _log.Add($"rounded tight corners ({changed} relaxations)");
        return Polyline.Resample(p.ToList(), 1.0, closed: true);
    }

    private Corner? CornerNo(TrackData d, int number) => d.Corners.FirstOrDefault(c => c.Number == number);

    private void BuildWidths(TrackData data, TrackGeometry geo)
    {
        int n = geo.Count;
        var half = new double[n];
        var first = data.Corners[0];
        var last = data.Corners[^1];
        for (int i = 0; i < n; i++)
        {
            double s = geo.SAt(i);
            bool mainStraight = s > last.ExitS || s < first.EntryS;
            half[i] = (mainStraight ? AlbertPark.MainStraightWidth : AlbertPark.BaseWidth) / 2;
        }
        var left = half.ToArray();
        var right = half.ToArray();
        foreach (var (num, extra) in AlbertPark.CornerWidening)
        {
            var c = CornerNo(data, num);
            if (c == null) continue;
            // widen on the outside of the corner from entry to exit
            for (int i = 0; i < n; i++)
            {
                double s = geo.SAt(i);
                double inside = geo.Forward(s, c.EntryS - 20);
                double span = geo.Forward(c.ExitS + 20, c.EntryS - 20);
                if (inside > span) continue;
                double w = Math.Sin(Math.PI * inside / span);
                if (c.Direction > 0) right[i] += extra * w; else left[i] += extra * w;
            }
        }
        data.WidthLeft = Polyline.GaussianSmooth(left, 10, true).Select(v => Math.Round(v, 2)).ToList();
        data.WidthRight = Polyline.GaussianSmooth(right, 10, true).Select(v => Math.Round(v, 2)).ToList();
    }

    private void BuildPitLane(TrackData data, TrackGeometry geo)
    {
        // The pit lane runs along the inside (right) of the main straight, from the exit of the last corner
        // to the exit of Turn 2 (OSM: private service road beside Aughtie Drive).
        var last = data.Corners[^1];
        var t2 = CornerNo(data, 2)!;
        double entryS = geo.Wrap(last.ExitS + 30);
        double exitS = geo.Wrap(t2.ExitS + 40);
        double lengthAlong = geo.Forward(exitS, entryS);
        const double blendIn = 140, blendOut = 160;

        var path = new List<Vec2>();
        var trackS = new List<double>();
        for (double u = 0; u <= lengthAlong; u += 1.0)
        {
            double s = geo.Wrap(entryS + u);
            double k;
            if (u < blendIn) k = SmoothStep(u / blendIn);
            else if (u > lengthAlong - blendOut) k = SmoothStep((lengthAlong - u) / blendOut);
            else k = 1;
            double dEdge = -geo.Sample(data.WidthRight, s);
            // start/finish at the track edge, then out to the pit lane offset
            double d = dEdge * (1 - k) + AlbertPark.PitLaneOffset * k;
            if (k < 1e-3) d = dEdge * 0.4;
            path.Add(geo.ToWorld(s, d));
            trackS.Add(entryS + u);
        }
        var resampled = Polyline.Resample(path, Spacing, closed: false);
        // map each resampled pit point back to a track distance
        var pitS = new List<double>(resampled.Count);
        double hint = entryS;
        foreach (var p in resampled)
        {
            var proj = geo.Project(p, hint, 60);
            hint = proj.S;
            double unwrapped = entryS + geo.Forward(proj.S, entryS);
            pitS.Add(Math.Round(unwrapped, 2));
        }
        double pitLen = Polyline.Length(resampled, false);

        // garages: ten boxes centred on the start/finish line, 18 m apart (team order 1..10 from the pit entry)
        double sfAlong = 0;
        for (int i = 1; i < pitS.Count; i++)
            if (pitS[i] >= geo.Length) { sfAlong = i * (pitLen / (resampled.Count - 1)); break; }
        var boxes = Enumerable.Range(0, 10).Select(i => Math.Round(sfAlong - 4.5 * 18 + i * 18 - 40, 1)).ToList();

        data.PitLane = new PitLaneData
        {
            SpeedLimitKmh = AlbertPark.PitSpeedLimitKmh,
            EntryS = Math.Round(entryS, 2),
            ExitS = Math.Round(exitS, 2),
            Path = resampled,
            TrackS = pitS,
            Width = AlbertPark.PitLaneWidth,
            LimitStart = Math.Round(blendIn + 20, 1),
            LimitEnd = Math.Round(pitLen - blendOut + 10, 1),
            Boxes = boxes,
            BoxOffset = AlbertPark.PitBoxOffset,
            EntryLineEnd = 90,
            ExitLineStart = Math.Round(pitLen - 110, 1),
        };
        _log.Add($"pit lane: entry s={entryS:0} exit s={exitS:0} length {pitLen:0} m, limiter {data.PitLane.LimitStart:0}-{data.PitLane.LimitEnd:0}");
    }

    private void BuildRaceControl(TrackData data, TrackGeometry geo)
    {
        double L = geo.Length;
        Corner C(int i) => CornerNo(data, Math.Min(i, data.Corners.Count))!;
        int count = data.Corners.Count;

        // Sectors: S2 starts between T5 and T6, S3 between T10 and T11 (2022+ numbering).
        double Mid(Corner a, Corner b) => geo.Wrap(a.ExitS + geo.Forward(b.EntryS, a.ExitS) / 2);
        data.SectorStarts = new List<double> { 0, Math.Round(Mid(C(5), C(6)), 1), Math.Round(Mid(C(10), C(11)), 1) };

        // DRS (2022+ layout, 4 zones, 2 detection points).
        double detA = geo.Wrap(C(6).ExitS + 30);
        double detB = geo.Wrap(C(count - 1).EntryS - 40);
        data.DrsZones = new List<DrsZone>
        {
            new() { Number = 1, DetectionS = detB, ActivationS = geo.Wrap(C(count).ExitS + 60), EndS = geo.Wrap(C(1).EntryS - 60) },
            new() { Number = 2, DetectionS = detB, ActivationS = geo.Wrap(C(2).ExitS + 50), EndS = geo.Wrap(C(3).EntryS - 70) },
            new() { Number = 3, DetectionS = detA, ActivationS = geo.Wrap(C(8).ExitS + 40), EndS = geo.Wrap(C(9).EntryS - 70) },
            new() { Number = 4, DetectionS = detA, ActivationS = geo.Wrap(C(10).ExitS + 40), EndS = geo.Wrap(C(11).EntryS - 70) },
        };
        foreach (var z in data.DrsZones)
        {
            z.DetectionS = Math.Round(z.DetectionS, 1); z.ActivationS = Math.Round(z.ActivationS, 1); z.EndS = Math.Round(z.EndS, 1);
            _log.Add($"DRS {z.Number}: detect {z.DetectionS:0} activate {z.ActivationS:0} end {z.EndS:0} (length {geo.Forward(z.EndS, z.ActivationS):0} m)");
        }

        // Grid: pole on the right (inside for the right-handed Turn 1), 8 m stagger, slots alternate sides.
        for (int p = 1; p <= 20; p++)
        {
            double s = geo.Wrap(-6 - (p - 1) * 8.0);
            double d = (p % 2 == 1) ? -3.2 : 3.2;
            data.Grid.Add(new GridSlot { Position = p, S = Math.Round(s, 2), D = d });
        }

        data.SafetyCarLine1 = Math.Round(geo.Wrap(data.PitLane.EntryS - 40), 1);
        data.SafetyCarLine2 = Math.Round(geo.Wrap(data.PitLane.ExitS + 40), 1);

        // Marshal posts roughly every 220 m, alternating sides; each post owns one marshalling sector.
        int posts = (int)Math.Round(L / 220);
        for (int i = 0; i < posts; i++)
            data.MarshalPosts.Add(new MarshalPost { Number = i + 1, S = Math.Round(i * L / posts + 30, 1), Side = i % 2 == 0 ? 1 : -1, Sector = i });
    }

    private void BuildLake(TrackData data, double scale)
    {
        var parts = _lakeWays.Select(w => w.Geometry.Select(p => P(p.Lat, p.Lon) * scale).ToList());
        var rings = Osm.AssembleRings(parts)
            .Select(r => Polyline.Resample(r, 4.0, closed: true))
            .OrderByDescending(r => Math.Abs(Polyline.SignedArea(r)))
            .ToList();
        data.Lake = rings;
        if (rings.Count > 0)
            _log.Add($"lake: {rings.Count} rings, shoreline {Polyline.Length(rings[0], true):0} m, area {Math.Abs(Polyline.SignedArea(rings[0])) / 1e4:0.0} ha");
    }

    private void BuildRunoff(TrackData data, TrackGeometry geo)
    {
        int n = geo.Count;
        var barrierL = new double[n];
        var barrierR = new double[n];
        var runL = new char[n];
        var runR = new char[n];
        var kerbL = Enumerable.Repeat('-', n).ToArray();
        var kerbR = Enumerable.Repeat('-', n).ToArray();

        // defaults: grass verge then barrier
        for (int i = 0; i < n; i++)
        {
            barrierL[i] = data.WidthLeft[i] + 7;
            barrierR[i] = data.WidthRight[i] + 7;
            runL[i] = 'G'; runR[i] = 'G';
        }

        foreach (var c in data.Corners.Concat(data.Bends.Where(b => b.Angle > 25)))
        {
            // outside run-off depth from corner speed: slower corners get deep gravel traps
            double depth, insideDepth = 6;
            char type;
            if (c.MinRadius < 70) { depth = 30; type = 'R'; }
            else if (c.MinRadius < 160) { depth = 36; type = 'R'; }
            else if (c.MinRadius < 320) { depth = 24; type = 'G'; }
            else { depth = 12; type = 'G'; }

            double from = c.EntryS - 60, to = c.ExitS + 70;
            double span = geo.Forward(to, from);
            for (int i = 0; i < n; i++)
            {
                double u = geo.Forward(geo.SAt(i), from);
                if (u > span) continue;
                double w = Math.Sin(Math.PI * Math.Clamp(u / span, 0, 1));
                bool outsideIsLeft = c.Direction < 0; // right-hander: outside is the left
                double r = Math.Max(c.MinRadius, 20);
                if (outsideIsLeft)
                {
                    barrierL[i] = Math.Max(barrierL[i], data.WidthLeft[i] + depth * w);
                    if (w > 0.25) runL[i] = type;
                    barrierR[i] = Math.Min(barrierR[i], data.WidthRight[i] + Math.Min(insideDepth, r * 0.5));
                }
                else
                {
                    barrierR[i] = Math.Max(barrierR[i], data.WidthRight[i] + depth * w);
                    if (w > 0.25) runR[i] = type;
                    barrierL[i] = Math.Min(barrierL[i], data.WidthLeft[i] + Math.Min(insideDepth, r * 0.5));
                }
            }

            // kerbs: inside around the apex, outside on the exit
            void Mark(char[] arr, double a, double b)
            {
                double len = geo.Forward(b, a);
                for (double u = 0; u <= len; u += geo.Length / n)
                    arr[geo.NearestIndex(a + u)] = 'K';
            }
            var inside = c.Direction > 0 ? kerbL : kerbR;
            var outside = c.Direction > 0 ? kerbR : kerbL;
            Mark(inside, c.ApexS - 30, c.ApexS + 25);
            Mark(outside, c.ApexS + 10, c.ExitS + 30);
        }

        // Pit wall along the inside of the main straight; where the pit lane branches off and rejoins there is no
        // barrier on the right ('P'): the pit lane's own walls are built from the pit lane path.
        var pit = data.PitLane;
        double zone = geo.Forward(pit.ExitS + 10, pit.EntryS - 10);
        double wallFrom = geo.Forward(pit.EntryS + PitWallStart, pit.EntryS - 10);
        double wallTo = geo.Forward(pit.ExitS - PitWallEnd, pit.EntryS - 10);
        for (int i = 0; i < n; i++)
        {
            double u = geo.Forward(geo.SAt(i), pit.EntryS - 10);
            if (u > zone) continue;
            if (u >= wallFrom && u <= wallTo)
            {
                barrierR[i] = data.WidthRight[i] + 2.5;
                runR[i] = 'A';
            }
            else
            {
                barrierR[i] = data.WidthRight[i] + 2.5;
                runR[i] = 'P';
            }
        }

        // keep barriers out of the lake
        if (data.Lake.Count > 0)
        {
            var shore = data.Lake[0];
            for (int i = 0; i < n; i++)
            {
                var p = geo.Point(i); var nrm = geo.Normal(i);
                barrierL[i] = ClampToWater(p, nrm, barrierL[i], data.WidthLeft[i], shore);
                barrierR[i] = ClampToWater(p, -nrm, barrierR[i], data.WidthRight[i], shore);
            }
        }

        // smooth barrier lines so walls are continuous; never let smoothing pull them onto the track
        var bl = Polyline.GaussianSmooth(barrierL, 6, true);
        var br = Polyline.GaussianSmooth(barrierR, 6, true);
        for (int i = 0; i < n; i++)
            if (runR[i] == 'A' || runR[i] == 'P') br[i] = barrierR[i];
        for (int i = 0; i < n; i++)
        {
            bl[i] = Math.Max(bl[i], data.WidthLeft[i] + 1.5);
            br[i] = Math.Max(br[i], data.WidthRight[i] + 1.5);
        }
        data.BarrierLeft = bl.Select(v => Math.Round(v, 2)).ToList();
        data.BarrierRight = br.Select(v => Math.Round(v, 2)).ToList();
        data.RunoffLeft = new string(runL);
        data.RunoffRight = new string(runR);
        data.KerbLeft = new string(kerbL);
        data.KerbRight = new string(kerbR);
    }

    private static double ClampToWater(Vec2 p, Vec2 dir, double barrier, double edge, List<Vec2> shore)
    {
        for (double d = edge; d <= barrier; d += 1.0)
            if (Polyline.PointInPolygon(p + dir * d, shore))
                return Math.Max(edge + 1.5, d - 3);
        return barrier;
    }

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
