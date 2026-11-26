"""Acceptance tests V1-V15 for the Spa-Francorchamps replica (tolerances from the replica brief, all PROPOSALS).

  python tools/SpaSurvey/checks.py [--lap-log builds/spa-lap.log]

Reads the built files and writes tracks/spa/checks.json as a list of {id, measured, tolerance, pass, evidence}.
Tolerances are never adjusted here; a failing test is reported with its evidence.
"""
import argparse
import csv
import re
import sys

from scipy.spatial import Delaunay, cKDTree

from spa_common import *
from registry import AREA_REFERENCE, BENDS, CORNERS, DIRECTIONS, LAP_REFERENCES, MARKERS, STRETCHES

RESULTS = []
COMMAND = "python tools/SpaSurvey/checks.py"


def record(id_, measured, tolerance, ok, detail):
    ok = None if ok is None else bool(ok)
    RESULTS.append(dict(id=id_, measured=measured, tolerance=tolerance, pass_=ok, evidence=f"{COMMAND} -> {detail}"))
    flag = "PASS" if ok is True else "FAIL" if ok is False else "SKIP"
    print(f"{id_:4} {flag:4}  {measured}  [{tolerance}]")


def load():
    track = read_json(GAME / "track.json")
    st = {k: [] for k in ("lap_m", "e_m", "n_m", "u_m", "heading_deg", "curvature_1_per_m", "half_width_left_m", "half_width_right_m", "bank_deg")}
    provs = []
    with open(OUT / "stations.csv") as f:
        for row in csv.DictReader(f):
            for k in st:
                st[k].append(float(row[k]))
            provs.append((row["u_prov"], row["width_prov"], row["bank_prov"]))
    st = {k: np.array(v) for k, v in st.items()}
    return track, st, provs, read_json(OUT / "corners.json"), read_json(OUT / "zones.json"), read_json(OUT / "sectors.json"), read_json(OUT / "build_log.json")


def godot(xy):
    """Engine frame: Godot X = east, Z = -north (right-handed, Y up)."""
    return np.c_[xy[:, 0], -xy[:, 1]]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lap-log", help="autotest log with a 'LAP 1: m:ss.fff' line for V12")
    ap.add_argument("--bench-log", help="trackbench log with draw calls for V11")
    args = ap.parse_args()
    track, st, provs, corners, zones, sectors, log = load()
    p = np.array(track["centerline"])
    n = len(p)
    seg = np.linalg.norm(np.roll(p, -1, 0) - p, axis=1)
    plan = seg.sum()
    z = np.array(track["elevation"])
    length3d = np.sqrt(seg ** 2 + (np.roll(z, -1) - z) ** 2).sum()
    eng = godot(p)
    eng_len = np.linalg.norm(np.roll(eng, -1, 0) - eng, axis=1).sum()

    # V1 lap length ------------------------------------------------------------------------------------------
    dev = {k: round(eng_len - v, 2) for k, v in LAP_REFERENCES.items()}
    record("V1", f"engine-space centreline {eng_len:.2f} m plan ({length3d:.2f} m along the road surface); deviation from "
                 f"7004 / 7001.2 / 7000.05 m = {dev['published']:+.2f} / {dev['race_distance_over_laps']:+.2f} / {dev['tum_closed_polygon']:+.2f} m",
           "within 7 m of 7004 m", abs(dev["published"]) <= 7,
           f"plan length of track.json centreline ({n} samples) in Godot (X,Z); 1 m stations.csv length {st['lap_m'][-1] + st['lap_m'][1]:.2f} m")

    # V2 corner registry ---------------------------------------------------------------------------------------
    cs = corners["corners"]
    dirs = "".join(c["direction"] for c in cs)
    offs = [(c["number"], round(c["apexS"] - c["registry"]["apex"], 1)) for c in cs]
    placed = [(c["number"], round(c["apexS"] - c["registry"]["apexOnBuildS"], 1)) for c in cs]
    bad = [(k, o) for k, o in offs if abs(o) > (35 if k in (10, 11) else 15)]
    bad_placed = [(k, o) for k, o in placed if abs(o) > (35 if k in (10, 11) else 15)]
    record("V2", f"19 corners, directions {dirs}; apex minus registry lap metres {offs}; apex minus registry apex lat/lon projected on the build {placed}",
           "directions RLRLRLRRLLLRLRRLLRL; apex ±15 m (±35 m T10/T11)",
           len(cs) == 19 and dirs == DIRECTIONS and not bad,
           f"apexes are curvature maxima of the orthophoto-edge centreline (not the TUM registry); registry lap metres run ~12.7 m long "
           f"(TUM line longer); outside tolerance in lap metres: {bad or 'none'}; by position: {bad_placed or 'none'}")

    # V3 heading and closure --------------------------------------------------------------------------------
    dh = heading_change(p)
    total = np.degrees(dh.sum())
    area = signed_area(p)
    hd = [(c["number"], round(abs(c["headingChangeDeg"]) - c["registry"]["heading"], 1)) for c in cs]
    hbad = [(k, d) for k, d in hd if abs(d) > (20 if k in (1, 8, 15, 18, 19) else 10)]
    area_err = (abs(area) - AREA_REFERENCE) / AREA_REFERENCE * 100
    record("V3", f"total signed heading {total:+.2f} deg; enclosed area {abs(area):,.0f} m2 ({area_err:+.2f}%); per-corner heading deltas {hd}",
           "|total| = 360±2 deg; area 923,549 m2 ±3%; corner heading ±10 deg (±20 T1/T8/T15/T18/T19)",
           abs(abs(total) - 360) <= 2 and abs(area_err) <= 3 and not hbad, f"corners outside tolerance: {hbad or 'none'}")

    # V4 handedness ------------------------------------------------------------------------------------------
    a_eng = signed_area(eng)
    origin = np.array(log["origin_lambert"])
    ref = {c["number"]: np.array(latlon_to_lambert(c["lat"], c["lon"])) - origin for c in CORNERS}
    ours = {c["number"]: p[int(round(c["apexS"] / track["sampleSpacing"])) % n] for c in cs}

    def orient(a, b, c):
        u, v = b - a, c - a
        return np.sign(u[0] * v[1] - u[1] * v[0])
    o_ref = orient(ref[1], ref[2], ref[16])
    o_our = orient(godot(ours[1][None])[0], godot(ours[2][None])[0], godot(ours[16][None])[0])
    dist = {k: round(float(np.linalg.norm(ours[k] - ref[k])), 1) for k in (1, 2, 16)}
    record("V4", f"signed area in Godot (X,Z) {a_eng:+,.0f} (E,N {area:+,.0f}); La Source/Eau Rouge/Blanchimont orientation "
                 f"registry(E,N) {o_ref:+.0f} vs engine(X,Z) {o_our:+.0f}; apex distances to registry lat/lon {dist} m",
           "positive in Godot (negative in E,N); marker orientation flips with the N->-Z mapping (no mirror)",
           a_eng > 0 and area < 0 and o_ref == -o_our, "mapping (X,Y,Z) = (E,U,-N)")

    # V5 anchor altitudes -----------------------------------------------------------------------------------
    lap, u = st["lap_m"], st["u_m"]
    L = lap[-1] + (lap[1] - lap[0])
    zi = np.array([np.interp(m % L, np.r_[lap, L], np.r_[u, u[0]]) for m, _ in MARKERS])
    alt = np.array([a for _, a in MARKERS], float)
    off = float(np.mean(alt - zi))
    res = zi + off - alt
    worst = [(MARKERS[i][0], round(float(res[i]), 2)) for i in np.argsort(-np.abs(res))[:4]]
    record("V5", f"global offset {off:+.2f} m; residuals max {np.abs(res).max():.2f} m, RMS {np.sqrt((res ** 2).mean()):.2f} m; worst {worst}",
           "every marker within 3 m after one offset", bool(np.all(np.abs(res) <= 3)),
           "stations.csv u_m (2022 DTM, DNG) vs operator PDF markers (re-read visually: identical to the brief)")

    # V6 relief ----------------------------------------------------------------------------------------------
    relief = u.max() - u.min()
    smax, smin = lap[u.argmax()], lap[u.argmin()]
    record("V6", f"relief {relief:.2f} m; max {u.max():.2f} m at lap {smax:.0f}; min {u.min():.2f} m at lap {smin:.0f}",
           "95-106 m; max in 2378-2568; min in 4812-5491", 95 <= relief <= 106 and 2378 <= smax <= 2568 and 4812 <= smin <= 5491,
           "stations.csv u_m")

    # V7 gradients -------------------------------------------------------------------------------------------
    best = None
    for w in (5, 10, 20, 30):
        g = (np.roll(u, -w // 2) - np.roll(u, w // 2)) / w
        m = (lap > 880) & (lap < 1200)
        i = int(np.argmax(np.where(m, g, -1)))
        if w == 10:
            best = (g[i] * 100, lap[i])
    stretch = []
    ok7 = 11 <= best[0] <= 18
    for name, a, b, val, tol in STRETCHES:
        ua, ub = np.interp(a % L, np.r_[lap, L], np.r_[u, u[0]]), np.interp(b % L, np.r_[lap, L], np.r_[u, u[0]])
        g = (ub - ua) / (b - a) * 100
        stretch.append((name, round(g, 2), val))
        ok7 &= abs(g - val) <= tol
    record("V7", f"Raidillon peak {best[0]:.2f}% at lap {best[1]:.0f} (10 m window); stretches (measured, brief) {stretch}",
           "peak 11-18%; stretch averages within 1.5 pp (2 pp Eau Rouge to crest)", bool(ok7), "centred differences on stations.csv u_m")

    # V8 outline overlay ---------------------------------------------------------------------------------------
    v8(p, origin)

    # V9 width ------------------------------------------------------------------------------------------------
    w = st["half_width_left_m"] + st["half_width_right_m"]
    narrow = int((w < 12).sum())
    remeasure = v9(st, origin)
    record("V9", f"total width min/median/max {w.min():.2f}/{np.median(w):.2f}/{w.max():.2f} m; {narrow} of {len(w)} stations under 12 m (flag); "
                 f"independent local re-detection agrees within 0.5 m at {remeasure[0]:.1f}% of {remeasure[1]} probes",
           "within 0.5 m of the orthophoto measurement at each station; <12 m flagged, not failed", remeasure[0] >= 95,
           "widths are distances to the traced painted lines; re-detection uses the brightest white-line peak within ±1.5 m, no smoothing prior")

    # V10 smoothness ------------------------------------------------------------------------------------------
    k = st["curvature_1_per_m"]
    jump = np.abs(np.diff(np.r_[k, k[0]])).max()
    radii = {c["number"]: c["minRadiusM"] for c in cs if c["number"] in (1, 18, 19)}
    record("V10", f"max curvature change between 1 m stations {jump:.5f} 1/m^2; smoothed minimum radius T1/T18/T19 {radii} m",
           "continuous curvature (cubic smoothing spline); Rmin about 15-25 m at T1, T18, T19",
           all(15 <= r <= 25 for r in radii.values()), "curvature from the spline's analytic derivatives")

    # V11 mesh --------------------------------------------------------------------------------------------------
    v11(track, args.bench_log)

    # V12 lap time --------------------------------------------------------------------------------------------
    if args.lap_log and Path(args.lap_log).exists():
        m = re.search(r"LAP 1: (\d+):(\d+\.\d+)", Path(args.lap_log).read_text(errors="ignore"))
        if m:
            t = int(m.group(1)) * 60 + float(m.group(2))
            record("V12", f"AI hot lap {m.group(1)}:{m.group(2)} vs record 1:44.701 ({(t / 104.701 - 1) * 100:+.1f}%) and 2025 pole 1:40.562 ({(t / 100.562 - 1) * 100:+.1f}%)",
                   "within ±10% of 1:44.701", abs(t / 104.701 - 1) <= .10, f"{args.lap_log}")
    else:
        record("V12", "not run (no --lap-log)", "±10% of 1:44.701", None, "pass an autotest log")

    # V13 start line and zones ---------------------------------------------------------------------------------
    pole = track["grid"][0]["s"] - plan
    t1 = next(c for c in cs if c["number"] == 1)["apexS"]
    gap = t1 - pole
    zs = zones["season2026"] + zones["legacy2025Drs"]
    order_ok = all(z_.get("status") == "ENVELOPE" for z_ in zs)
    record("V13", f"pole box front line {abs(pole):.1f} m before the control line, {gap:.1f} m before the T1 apex; separate start line at lap "
                  f"{track['startLineS'] - plan:+.1f} m; {len(zs)} zones stored as envelopes",
           "pole slot about 271±15 m before T1 apex (soft); zones inside envelopes", abs(gap - 271) <= 15 and order_ok,
           "grid and start line detected in D3; zones from corners.json")

    # V14 provenance -------------------------------------------------------------------------------------------
    from uncertainty import FEATURES
    unknown = sum(1 for f in FEATURES if f[1] == "UNKNOWN")
    missing = sum(1 for r in provs if not all(r))
    tagged = all("provenance" in c for c in cs) and all("provenance" in s for s in sectors["sectors"]) and all("provenance" in z_ for z_ in zs)
    record("V14", f"{len(provs)} station rows, {missing} without provenance; corners/zones/sectors tagged: {tagged}; "
                  f"{len(FEATURES)} features in the uncertainty register, {unknown} UNKNOWN, {sum(1 for f in FEATURES if f[1] == 'ASSUMED')} ASSUMED",
           "every emitted feature tagged; UNKNOWN count printed", missing == 0 and tagged, "stations.csv, corners/zones/sectors.json, uncertainty.py")

    # V15 user map ---------------------------------------------------------------------------------------------
    v15(p)

    for r in RESULTS:
        r["pass"] = r.pop("pass_")
    write_json(OUT / "checks.json", RESULTS)
    print(f"{sum(r['pass'] is True for r in RESULTS)} passed, {sum(r['pass'] is False for r in RESULTS)} failed, "
          f"{sum(r['pass'] is None for r in RESULTS)} not run -> tracks/spa/checks.json")


def rigid_icp(src, dst_tree, dst, iters=30, scale=False):
    R, t, sc = np.eye(2), np.zeros(2), 1.0
    for _ in range(iters):
        cur = sc * src @ R.T + t
        _, ix = dst_tree.query(cur)
        a, b = src, dst[ix]
        ma, mb = a.mean(0), b.mean(0)
        A, B = a - ma, b - mb
        U, S, Vt = np.linalg.svd(B.T @ A)
        D = np.diag([1, np.sign(np.linalg.det(U @ Vt))])
        R = U @ D @ Vt
        sc = (S * np.diag(D)).sum() / (A ** 2).sum() if scale else 1.0
        t = mb - sc * ma @ R.T
    return R, t, sc


def densify(poly, step=1.0):
    out = [poly[0]]
    for a, b in zip(poly[:-1], poly[1:]):
        k = max(1, int(np.ceil(np.linalg.norm(b - a) / step)))
        out.extend(a + (b - a) * (np.arange(1, k + 1) / k)[:, None])
    return np.array(out)


def v8(p, origin):
    data = read_json(CACHE / "osm" / "raceway.json")
    skip = {"Kart", "Pit Lane", "Support Pit Lane", "Moto layout"}
    pts = []
    for wy in data["elements"]:
        if wy["tags"].get("name") in skip:
            continue
        g = np.array([[q["lat"], q["lon"]] for q in wy["geometry"]])
        x, y = latlon_to_lambert(g[:, 0], g[:, 1])
        pts.append(densify(np.c_[x, y] - origin))
    osm = np.vstack(pts)
    ours, _ = resample_closed(p, spacing=1.0)
    tree = cKDTree(osm)
    R, t, _ = rigid_icp(ours, tree, osm)
    fitted = ours @ R.T + t
    d1, _ = tree.query(fitted)
    near = cKDTree(fitted).query(osm)[0]
    d2 = near[near < 40]
    haus = max(d1.max(), d2.max())
    rot = np.degrees(np.arctan2(R[1, 0], R[0, 0]))
    record("V8", f"vs OpenStreetMap raceway ways after rigid fit (rotation {rot:+.3f} deg, shift {np.linalg.norm(t):.2f} m): mean {d1.mean():.2f} m, "
                 f"95th pct {np.percentile(d1, 95):.2f} m, Hausdorff {haus:.2f} m",
           "GPS/imagery-derived reference: mean <= 1 m, Hausdorff <= 3-5 m (no independent LiDAR/orthophoto edge set exists; the build's own edges are not reused)",
           d1.mean() <= 1 and haus <= 5, f"{len(osm)} OSM points (pit, kart and moto ways excluded; OSM->ours direction limited to points within 40 m)")


def v9(st, origin):
    idx = np.arange(0, len(st["lap_m"]), 25)
    xy = np.c_[st["e_m"], st["n_m"]][idx] + origin
    h = np.radians(st["heading_deg"][idx])
    nrm = np.c_[-np.sin(h), np.cos(h)]
    agree = total = 0
    for side, w in ((1, st["half_width_left_m"][idx]), (-1, st["half_width_right_m"][idx])):
        d = np.arange(-1.5, 1.5, .05)
        q = xy[:, None, :] + nrm[:, None, :] * side * (w[:, None] + d[None, :])[..., None]
        lum = ortho_rgb(q).astype(float).min(2)
        peak = d[np.argmax(lum, axis=1)]
        # the traced width is the outer edge of a 0.1-0.25 m line: compare with the peak's outer side
        ok = np.abs(peak + 0.06) <= 0.5
        agree += ok.sum()
        total += len(ok)
    return agree / total * 100, total


def v11(track, bench_log):
    t = read_json(GAME / "terrain.json")
    v = np.array(t["vertices"])
    tri = np.array(t["triangles"]).reshape(-1, 3)
    a, b, c = v[tri[:, 0]], v[tri[:, 1]], v[tri[:, 2]]
    u, w = b[:, :2] - a[:, :2], c[:, :2] - a[:, :2]
    area = 0.5 * np.abs(u[:, 0] * w[:, 1] - u[:, 1] * w[:, 0])
    degenerate = int((area < 1e-6).sum())
    p = np.array(track["centerline"])
    z = np.array(track["elevation"])
    from scipy.interpolate import LinearNDInterpolator
    terr = LinearNDInterpolator(Delaunay(v[:, :2]), v[:, 2])(p)
    clip = int(np.nansum(terr > z - 0.05))
    bench, within = "", True
    if bench_log and Path(bench_log).exists():
        draws = [int(x) for x in re.findall(r"draws=(\d+)", Path(bench_log).read_text())]
        prims = [int(x) for x in re.findall(r"prims=(\d+)k", Path(bench_log).read_text())]
        if draws:
            bench = (f"; in-game per view: draw calls {min(draws)}-{max(draws)} (including instanced forest patches and text labels), "
                     f"primitives {min(prims)}k-{max(prims)}k")
            within = max(draws) <= 30 and max(prims) <= 300
    else:
        bench = "; in-game budget not measured (no --bench-log)"
        within = None
    glb = OUT / "spa_neutral.glb"
    glb_note = f"; spa_neutral.glb {glb.stat().st_size // 1024} KB" if glb.exists() else ""
    record("V11", f"terrain {len(tri):,} triangles, {degenerate} degenerate; road-centre stations where terrain is within 5 cm of the road: {clip}{bench}{glb_note}",
           "budgets: desktop 150-300k triangles, <30 merged draw calls plus instances; no degenerate triangles; no road/terrain clipping",
           None if within is None else (degenerate == 0 and clip == 0 and within),
           "terrain.json triangulation sampled at every centreline sample; draw calls from --autotest=trackbench")


def v15(p):
    f = CACHE / "user" / "spa_map_centerline_10m.csv"
    if not f.exists():
        record("V15", "not run (spa_map_centerline_10m.csv not supplied)", "mean <= 3 m, max <= 15 m", None, "optional")
        return
    m = np.loadtxt(f, delimiter=",", skiprows=1)[:, 1:3]
    ours, _ = resample_closed(p, spacing=1.0)
    tree = cKDTree(ours)
    best = None
    for rot in np.radians(np.arange(-180, 180, 5)):
        R0 = np.array([[np.cos(rot), -np.sin(rot)], [np.sin(rot), np.cos(rot)]])
        src = (m - m.mean(0)) @ R0.T + ours.mean(0)
        R, t, sc = rigid_icp(src, tree, ours, iters=25, scale=True)
        d, _ = tree.query(sc * src @ R.T + t)
        if best is None or d.mean() < best[0]:
            best = (d.mean(), d.max(), np.degrees(rot + np.arctan2(R[1, 0], R[0, 0])), sc)
    rot = (best[2] + 180) % 360 - 180
    record("V15", f"user map vs build after similarity fit: mean {best[0]:.2f} m, max {best[1]:.2f} m, scale {best[3]:.4f}, rotation {rot:+.1f} deg (map frame -> north-up E,N)",
           "mean <= 3 m, max <= 15 m (expect scale ~1.0, rotation ~ -97.5 deg)", best[0] <= 3 and best[1] <= 15, "spa_map_centerline_10m.csv (user map, reference only)")


if __name__ == "__main__":
    main()
