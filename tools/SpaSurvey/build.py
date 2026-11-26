"""Stage 3: build the Spa-Francorchamps replica from the cached survey inputs.

  python tools/SpaSurvey/build.py

Writes the runtime files game/data/tracks/spa/{track,terrain}.json and the engine-neutral intermediates in
tracks/spa/ (stations.csv, corners.json, zones.json, sectors.json, manifest.json, build_log.json).
Every line-defining quantity is measured from D2 (2022 LiDAR DTM) or D3 (2023 orthophoto); see uncertainty_report.md.
"""
import csv
import datetime
import re

from scipy.interpolate import CubicSpline, LinearNDInterpolator, splev, splprep, splrep
from scipy.ndimage import binary_closing, gaussian_filter1d, median_filter
from scipy.signal import savgol_filter
from scipy.spatial import Delaunay, cKDTree

from spa_common import *
from registry import BENDS, CORNERS, MARKERS

LOG: dict = {}
GAME_SPACING = 3.0
ELEVATION_NOISE = 0.04  # m RMS: road-surface noise of the 2022 DTM after spike rejection
DTM = RasterSampler(DTM_SERVICE, "dtm2022")
DSM = RasterSampler(DSM_SERVICE, "dsm2022")
# Research estimate of the control line (TUM s = 140 m, registered with the prior). Only seeds the search.
LINE_PRIOR = np.array([763375.00281018, 627161.88426032])


# ============================================================== centreline from the digitised road edges
def edge_polylines():
    e = np.load(CACHE / "edges.npz")
    left = e["points"] + e["normal"] * e["left"][:, None]
    right = e["points"] + e["normal"] * e["right"][:, None]
    return left, right


def smooth_centreline(left, right):
    """Midline of the two painted edges, smoothed by a periodic cubic smoothing spline (curvature-continuous).
    The smoothing budget is 0.15 m RMS, the scale of the edge-tracing noise."""
    mid = (left + right) / 2
    tck, _ = splprep([mid[:, 0], mid[:, 1]], s=len(mid) * 0.15 ** 2, per=1, k=3)
    u = np.linspace(0, 1, 40001)
    fine = np.column_stack(splev(u, tck))
    seg = np.linalg.norm(np.diff(fine, axis=0), axis=1)
    arc = np.r_[0, seg.cumsum()]
    resid = cKDTree(fine).query(mid)[0]
    LOG["centreline_smoothing_residual_m"] = dict(rms=float(np.sqrt((resid ** 2).mean())), max=float(resid.max()))
    return tck, u, arc


def evaluate(tck, u_of_s, s):
    u = u_of_s(s)
    x, y = splev(u, tck)
    dx, dy = splev(u, tck, der=1)
    ddx, ddy = splev(u, tck, der=2)
    kappa = (dx * ddy - dy * ddx) / (dx * dx + dy * dy) ** 1.5
    heading = np.arctan2(dy, dx)
    return np.c_[x, y], heading, kappa


def unwrap_strip(points, heading, d):
    n = np.c_[-np.sin(heading), np.cos(heading)]
    return ortho_rgb(points[:, None, :] + n[:, None, :] * d[None, :, None]).astype(float)


def find_finish_line(tck, u_of_s, length):
    """Locates the painted chequered control line near the research estimate and returns its arc length."""
    fine_s = np.linspace(0, length, int(length * 4), endpoint=False)
    pts, _, _ = evaluate(tck, u_of_s, fine_s)
    s_prior = fine_s[cKDTree(pts).query(LINE_PRIOR)[1]]
    s = (s_prior + np.arange(-60, 60, .125)) % length
    p, h, _ = evaluate(tck, u_of_s, s)
    d = np.arange(-4.5, 4.5, .125)
    lum = unwrap_strip(p, h, d).min(2)
    bright = gaussian_filter1d((lum > 170).mean(1), 1)
    peaks = [i for i in range(2, len(s) - 2) if bright[i] >= bright[i - 1] and bright[i] > bright[i + 1] and bright[i] > .6]
    cands = []
    for i in peaks:
        band = lum[max(0, i - 3):i + 4]
        cands.append(dict(offset_m=float(np.arange(-60, 60, .125)[i]), bright=float(bright[i]), dark=float((band < 110).mean()),
                          s=float(s[i])))
    line = max(cands, key=lambda c: c["dark"])
    assert abs(line["offset_m"]) < 10, cands
    LOG["control_line"] = dict(candidates=cands, chosen_offset_from_research_estimate_m=line["offset_m"])
    print(f"control line: chequered stripe {line['offset_m']:+.2f} m from the research estimate (TUM s = 140 m)")
    return line["s"]


# ============================================================== lap stations
class Lap:
    def __init__(self, tck, u_of_s, length, s0, count):
        self.tck, self.u_of_s, self.length, self.s0, self.n = tck, u_of_s, length, s0, count
        self.lap = np.arange(count) * length / count
        self.xy, self.heading, self.kappa = evaluate(tck, u_of_s, (s0 + self.lap) % length)
        self.normal = np.c_[-np.sin(self.heading), np.cos(self.heading)]

    def at(self, lap_m):
        return evaluate(self.tck, self.u_of_s, (self.s0 + np.asarray(lap_m)) % self.length)


def widths(lap, left, right):
    """Distance from each station to the painted edge polylines (densified to 0.1 m)."""
    def dense(p):
        q, _ = resample_closed(p, spacing=0.1)
        return q
    out = []
    for poly, sign in ((dense(left), 1), (dense(right), -1)):
        dist, ix = cKDTree(poly).query(lap.xy)
        side = np.sign(((poly[ix] - lap.xy) * lap.normal).sum(1))
        assert (side == sign).mean() > .995, "edge on the wrong side of the centreline"
        out.append(dist)
    return out


# ============================================================== elevation and bank (D2)
def elevation(master, wl, wr):
    z_raw = DTM.sample(master.xy)
    bad = ~np.isfinite(z_raw)
    assert not bad.any(), f"{bad.sum()} centreline samples without DTM value"
    # Spike rejection, then a periodic smoothing spline whose residual budget equals the DTM's few-centimetre noise on
    # asphalt: it removes sub-accuracy ripples (which a car at 300 km/h feels as ±1 g load swings) but keeps metre-scale
    # shape such as the Eau Rouge compression and the Raidillon crest.
    z_med = median_filter(z_raw, 5, mode="wrap")
    lap_closed = np.r_[master.lap, master.length]
    tck_z = splrep(lap_closed, np.r_[z_med, z_med[0]], s=master.n * ELEVATION_NOISE ** 2, per=True)
    z = splev(master.lap, tck_z)
    kv = splev(master.lap, tck_z, der=2)
    LOG["elevation"] = dict(samples=len(z_raw), smoothing_budget_rms_m=ELEVATION_NOISE,
                            raw_minus_smoothed_rms_m=float(np.sqrt(((z_raw - z) ** 2).mean())),
                            max_abs_m=float(np.abs(z_raw - z).max()),
                            vertical_g_at_300kmh=dict(min=float(83.3 ** 2 * kv.min() / 9.81), max=float(83.3 ** 2 * kv.max() / 9.81)))
    # bank from the road surface 1 m inside each painted line, every 3 m
    idx = np.arange(0, master.n, 3)
    dl, dr = np.maximum(wl[idx] - 1.0, 2.5), np.maximum(wr[idx] - 1.0, 2.5)
    zl = DTM.sample(master.xy[idx] + master.normal[idx] * dl[:, None])
    zr = DTM.sample(master.xy[idx] - master.normal[idx] * dr[:, None])
    zc = z_raw[idx]
    slope_l, slope_r = (zl - zc) / dl, (zc - zr) / dr
    cross = (zl - zr) / (dl + dr)
    cross = np.interp(master.lap, np.r_[master.lap[idx], master.length], np.r_[cross, cross[0]])
    cross = gaussian_filter1d(median_filter(cross, 9, mode="wrap"), 6, mode="wrap")
    LOG["bank"] = dict(samples=len(idx), noise_estimate_pct=float(np.std(slope_l - slope_r) * 100 / np.sqrt(2)),
                       range_pct=[float(cross.min() * 100), float(cross.max() * 100)])
    return z, z_raw, cross


def compare_profil(master, z):
    """Difference against the older 1 m Walloon DTM profile used by the first build (cache/profil1m)."""
    f = CACHE / "profil1m" / "centre.json"
    if not f.exists():
        return
    a = np.array(read_json(f)["results"][0]["value"]["features"][0]["geometry"]["paths"][0])
    near = cKDTree(master.xy).query(a[:, :2])[1]
    diff = a[:, 2] - z[near]
    LOG["dtm_2022_vs_1m_profile"] = dict(mean_m=float(diff.mean()), median_abs_m=float(np.median(np.abs(diff - diff.mean()))),
                                         p95_abs_m=float(np.percentile(np.abs(diff - diff.mean()), 95)),
                                         worst=[dict(lap_m=float(master.lap[near[i]]), diff_m=float(diff[i] - diff.mean()))
                                                for i in np.argsort(-np.abs(diff - diff.mean()))[:8]])


# ============================================================== kerbs and run-off (D3)
def kerbs(game, w, side):
    """Painted kerb runs just outside the track-limit line, and the colour of their light stripes."""
    off = np.arange(.3, 2.0, .2)
    rgb = ortho_rgb(game.xy[:, None, :] + game.normal[:, None, :] * side * (w[:, None] + off)[..., None]).astype(float)
    r, g, b = rgb.transpose(2, 0, 1)
    red = (r - b > 45) & (r > g * 1.25) & (r > 110)
    k = binary_closing(gaussian_filter1d(red.mean(1), 1.4, mode="wrap") > .16, iterations=3)
    light = (rgb.min(2) > 150) & ~red
    runs = []
    for a, bnd in runs_of(k):
        sel = light[a:bnd] if bnd > a else np.r_[light[a:], light[:bnd]]
        pix = (rgb[a:bnd] if bnd > a else np.r_[rgb[a:], rgb[:bnd]])[sel]
        colour = pix.mean(0) if len(pix) else np.array([np.nan] * 3)
        yellow = bool(len(pix) and colour[2] < 0.72 * colour[0])
        runs.append(dict(from_lap_m=round(float(game.lap[a]), 1), to_lap_m=round(float(game.lap[bnd - 1]), 1),
                         light_stripe_rgb=[round(float(c)) for c in colour], light_stripe="yellow" if yellow else "white"))
    return "".join(np.where(k, "K", "-")), runs


def runs_of(mask):
    n = len(mask)
    if mask.all():
        return [(0, n)]
    start = int(np.argmin(mask))  # begin on a gap so wrapped runs stay whole
    out, i = [], 0
    while i < n:
        j = (start + i) % n
        if mask[j]:
            k = i
            while k < n and mask[(start + k) % n]:
                k += 1
            out.append((j, (start + k) % n or n))
            i = k
        else:
            i += 1
    return out


# Run-off surface beyond the verge, read by eye from unwrapped 2023 orthophoto strips (first build) as lap-metre
# intervals along the first-build centreline: R gravel, A asphalt or painted run-off; everything else grass. An automatic
# colour classifier was tried and rejected: dry summer grass, tree shadow and grey gravel all read as asphalt.
RUNOFF_LEFT = [(225, 455, "R"), (930, 1275, "A"), (2210, 2675, "R"), (2825, 3080, "R"), (4310, 4490, "R"), (4760, 5460, "R"), (6540, 6636, "A")]
RUNOFF_RIGHT = [(900, 1330, "A"), (2230, 2510, "R"), (3120, 3250, "R"), (3610, 4240, "R"), (4445, 4640, "R"), (5580, 6270, "R")]


def runoff(game, side):
    p, _, s = first_build_frame()
    old_s = s[cKDTree(p).query(game.xy)[1]]
    cls = np.full(game.n, "G")
    for a, b, v in (RUNOFF_LEFT if side > 0 else RUNOFF_RIGHT):
        cls[(old_s >= a) & (old_s <= b)] = v
    return cls


# ============================================================== barriers (manual digitisation of D3)
# Lap metres along the first-build centreline and offsets (left, right) in metres from that centreline, read by eye
# from unwrapped 2023 orthophoto strips. Interpolated linearly between rows; never closer than 1.8 m to the edge.
BARRIER_CONTROLS = [[0,15,9],[150,14,9],[210,19,12],[265,33,13],[350,31,15],[440,27,15],[600,23,13],[800,20,13],[870,15,12],[925,19,15],[1000,30,23],[1100,45,33],[1180,51,40],[1260,43,36],[1340,18,24],[1400,11,17],[1520,13,15],[1900,14,19],[2140,14,18],[2210,20,25],[2290,51,19],[2370,31,24],[2440,24,30],[2525,38,16],[2600,39,14],[2670,15,13],[2800,12,12],[2870,21,12],[2980,39,13],[3070,31,12],[3160,14,23],[3240,14,19],[3450,13,11],[3590,12,18],[3700,13,47],[3820,14,50],[3940,13,39],[4140,13,31],[4260,14,12],[4380,35,13],[4450,37,13],[4520,19,33],[4620,11,26],[4750,17,17],[4830,25,12],[4920,25,12],[5070,40,11],[5180,35,12],[5400,32,12],[5480,18,11],[5580,12,28],[5800,11,30],[6060,11,31],[6160,12,38],[6290,13,30],[6400,12,15],[6510,14,12],[6590,17,15],[6630,24,12],[6660,12,12],[6720,14,16],[6790,14,13],[6860,14,9],[6998.304,15,9]]


def first_build_frame():
    """The first build's centreline (TUM prior, Savitzky-Golay smoothed) on which the barrier controls were read."""
    raw = np.loadtxt(CACHE / "tum" / "Spa.csv", delimiter=",", comments="#")
    c = savgol_filter(np.roll(raw[:, :2], -TUM_ROLL, axis=0), 9, 3, axis=0, mode="wrap") @ TUM_ROT + TUM_TRANS
    s0 = stations(c)
    s = np.linspace(0, s0[-1], 2334, endpoint=False)
    p = CubicSpline(s0, closed(c), bc_type="periodic")(s)
    return p, normals(p), stations(p)[:-1]


def barriers(game, wl, wr):
    p, n, s = first_build_frame()
    ctrl = np.array(BARRIER_CONTROLS)
    out = []
    for col, sign, w in ((1, 1, wl), (2, -1, wr)):
        poly, _ = resample_closed(p + sign * n * np.interp(s, ctrl[:, 0], ctrl[:, col])[:, None], spacing=0.25)
        dist, ix = cKDTree(poly).query(game.xy)
        out.append(np.maximum(w + 1.8, dist))
    return out


# ============================================================== pit lane (hand trace of D3)
PIT_LIMIT, PIT_BOXES = (119, 592), np.linspace(228, 454, 10)


def pit_lane(game, z, cross, datum):
    path = np.load(CACHE / "pit.npy")
    u = np.r_[0, np.linalg.norm(np.diff(path, axis=0), axis=1).cumsum()]
    pz = DTM.sample(path)
    pz = gaussian_filter1d(median_filter(pz, 5), 1.0) - datum
    near = cKDTree(game.xy).query(path)[1]
    lateral = ((path - game.xy[near]) * game.normal[near]).sum(1)
    road = z[near] + lateral * cross[near]
    blend = np.clip(np.minimum(u, u[-1] - u) / 25, 0, 1)
    pz = blend * pz + (1 - blend) * road
    return path, pz, near, u


# ============================================================== grid boxes (D3)
def grid_boxes(master):
    lap = np.arange(-240, -10, .125)
    p, h, _ = master.at(lap)
    d = np.arange(-7.5, 7.5, .125)
    lum = unwrap_strip(p, h, d).min(2)
    res = {}
    for name, m in (("left", (d > .8) & (d < 6.5)), ("right", (d < -.8) & (d > -6.5))):
        sc = gaussian_filter1d((lum[:, m] > 165).mean(1), 1)
        peaks = [i for i in range(1, len(sc) - 1) if sc[i] >= sc[i - 1] and sc[i] > sc[i + 1] and sc[i] > .25]
        # keep the 16 m ladder of box front lines (drops other transverse paint)
        best = []
        for i in peaks:
            ladder = [j for j in peaks if abs(((lap[j] - lap[i]) / 16) - round((lap[j] - lap[i]) / 16)) < .06]
            if len(ladder) > len(best):
                best = ladder
        fronts = sorted(lap[best], reverse=True)
        lateral = []
        for j in best:
            rows = lum[max(0, j - 2):j + 3][:, m]
            dd = d[m][(rows > 165).any(0)]
            if len(dd):
                lateral.append((dd.min() + dd.max()) / 2)
        res[name] = dict(fronts=[float(f) for f in fronts], centre=float(np.median(lateral)))
    pole = "right" if res["right"]["fronts"][0] > res["left"]["fronts"][0] else "left"
    other = "left" if pole == "right" else "right"
    slots = []
    for k in range(max(len(res[pole]["fronts"]), len(res[other]["fronts"])) * 2):
        side = pole if k % 2 == 0 else other
        if k // 2 < len(res[side]["fronts"]):
            slots.append((res[side]["fronts"][k // 2], res[side]["centre"]))
    LOG["grid"] = dict(pole_side=pole, boxes=len(slots), first_fronts_lap_m=[round(s[0], 2) for s in slots[:4]],
                       lateral_centres_m=dict(left=res["left"]["centre"], right=res["right"]["centre"]),
                       start_line_note="solid transverse line ahead of the first box; see start_line_lap_m")
    return slots


def start_line(master, first_box):
    lap = np.arange(first_box + .5, first_box + 8, .125)
    p, h, _ = master.at(lap)
    d = np.arange(-4.5, 4.5, .125)
    lum = unwrap_strip(p, h, d).min(2)
    sc = gaussian_filter1d((lum > 170).mean(1), 1)
    i = int(sc.argmax())
    assert sc[i] > .7, "start line not found ahead of the first grid box"
    return float(lap[i])


# ============================================================== corners
def corner_table(master, entries, fallback_shift=0.0):
    """Measures each registry corner on the build's centreline. The registry window is placed by projecting the
    registry apex (lat/lon) onto the centreline; heading change is integrated over that window and the geometric apex
    is where half of the turn is done (robust for broad corners whose curvature peak is flat)."""
    k = gaussian_filter1d(master.kappa, 2, mode="wrap")
    k8 = gaussian_filter1d(master.kappa, 8, mode="wrap")
    tree = cKDTree(master.xy)
    ds = master.length / master.n
    out = []
    for c in entries:
        if "lat" in c:
            proj = float(master.lap[tree.query(np.array(latlon_to_lambert(c["lat"], c["lon"])))[1]])
        else:
            proj = c["apex"] + fallback_shift
        shift = proj - c["apex"]
        entry, exit_ = c["entry"] + shift, c["exit"] + shift
        span = np.flatnonzero((master.lap >= entry) & (master.lap <= exit_))
        sign = (1 if c["dir"] == "L" else -1) if c["dir"] else (1 if master.kappa[span].sum() > 0 else -1)
        direction = "L" if sign > 0 else "R"
        dtheta = np.degrees(np.sum(master.kappa[span]) * ds)
        turn = np.cumsum(np.clip(sign * master.kappa[span], 0, None))
        half = span[min(len(span) - 1, int(np.searchsorted(turn, turn[-1] / 2)))]
        a = span[int(np.argmax(sign * k8[span]))]  # tightest point, the registry's apex definition
        rmin = 1 / np.max(np.abs(k[span]))
        lat, lon = lambert_to_latlon(*master.xy[a])
        out.append(dict(number=c["number"], name=c["name"], direction=direction, entryS=round(float(entry), 1),
                        apexS=round(float(master.lap[a]), 1), halfTurnS=round(float(master.lap[half]), 1), exitS=round(float(exit_), 1),
                        headingChangeDeg=round(float(dtheta), 1), minRadiusM=round(float(rmin), 1),
                        apexLatLon=[round(float(lat), 6), round(float(lon), 6)],
                        registry=dict(entry=c["entry"], apex=c["apex"], exit=c["exit"], heading=c["heading"], rmin=c["rmin"],
                                      apexOnBuildS=round(proj, 1)),
                        provenance=dict(apex="DERIVED(curvature maximum, 8 m gaussian, of the D3-edge centreline inside the registry window)",
                                        entryExit="DERIVED(registry window placed by projecting the registry apex lat/lon onto the centreline)",
                                        headingChange="DERIVED(integrated centreline curvature entry→exit)",
                                        minRadius="DERIVED(1/max|curvature| after 2 m smoothing)")))
    return out


# ============================================================== terrain and forest
def terrain(game, z, cross, wl, wr, bl, br, pit, pz, datum, origin):
    xs, ys = np.arange(762240, 765121, 32), np.arange(624704, 628321, 32)
    gx, gy = np.meshgrid(xs, ys)
    grid = np.c_[gx.ravel(), gy.ravel()].astype(float)
    tree = cKDTree(game.xy)
    ptree = cKDTree(pit)
    dist, near = tree.query(grid)
    pdist, _ = ptree.query(grid)
    keep = (dist > np.maximum(bl[near], br[near]) + 22) & (pdist > 23)
    grid = grid[keep]
    gz = DTM.sample(grid) - datum
    # two rings of true ground just beyond the barriers capture cuttings and embankments beside the road
    rings = []
    idx = np.arange(0, game.n, 3)
    for extra in (7.0, 16.0):
        for sign, b in ((1, bl), (-1, br)):
            q = game.xy[idx] + sign * game.normal[idx] * (b[idx] + extra)[:, None]
            d2, n2 = tree.query(q)
            pd2, _ = ptree.query(q)
            ok = (d2 > np.maximum(bl[n2], br[n2]) + extra - 3) & (pd2 > 20)
            rings.append(q[ok])
    rings = np.vstack(rings)
    rz = DTM.sample(rings) - datum
    good = np.isfinite(gz)
    verts = [np.c_[grid[good], gz[good]], np.c_[rings, rz][np.isfinite(rz)]]
    for off in (-br, -wr, np.zeros_like(wl), wl, bl):
        xy = game.xy + game.normal * off[:, None]
        verts.append(np.c_[xy, z + cross * off - .20][::3])
    t = np.gradient(pit, axis=0)
    t /= np.linalg.norm(t, axis=1)[:, None]
    pn = np.c_[-t[:, 1], t[:, 0]]
    for off in (-9, 0, 5):
        verts.append(np.c_[pit + pn * off, pz - .20][::2])
    v = np.vstack(verts)
    v[:, :2] -= origin
    tri = Delaunay(v[:, :2])
    LOG["terrain"] = dict(vertices=len(v), triangles=len(tri.simplices), dtm_grid_points=int(good.sum()), ring_points=len(rings))
    return v, tri


def forest(game, bl, br, pit, tri, v, origin, datum):
    """Tree positions from dark-green canopy in D3, kept only where the 2022 surface model stands at least 4 m
    above the terrain model (D2); the canopy height scales each instanced tree."""
    rng = np.random.default_rng(47)
    gx, gy = np.meshgrid(np.arange(762260, 765110, 13), np.arange(624720, 628305, 13))
    pts = np.c_[gx.ravel(), gy.ravel()] + rng.uniform(-5, 5, (gx.size, 2))
    r, g, b = ortho_rgb(pts).astype(float).T
    tree = cKDTree(game.xy)
    dist, near = tree.query(pts)
    pd, _ = cKDTree(pit).query(pts)
    inside = g > 0
    woods = ((g > r * 1.04) & (g > b * 1.05) & (g < 100) & (g > 24)) | (~inside & (dist > 170) & (rng.random(len(r)) < .65))
    keep = woods & (dist > np.maximum(bl[near], br[near]) + 22) & (pd > 48) & (dist < 790)
    pts = pts[keep]
    covered = (pts[:, 0] > 762700) & (pts[:, 0] < 764500) & (pts[:, 1] > 625100) & (pts[:, 1] < 627800)
    height = np.full(len(pts), np.nan)
    if covered.any():
        height[covered] = DSM.sample(pts[covered]) - DTM.sample(pts[covered])
    is_tree = ~covered | (height > 4)
    pts, height = pts[is_tree], height[is_tree]
    base = LinearNDInterpolator(tri, v[:, 2])(pts - origin)
    ok = np.isfinite(base)
    h = np.where(np.isfinite(height), np.clip(height, 4, 40), 0)
    LOG["forest"] = dict(candidates=int(keep.sum()), kept=int(ok.sum()), with_measured_height=int(np.isfinite(height[ok]).sum()),
                         height_percentiles_m=np.round(np.nanpercentile(height[ok], [10, 50, 90]), 1).tolist())
    return np.c_[pts[ok] - origin, base[ok], h[ok]]


# ============================================================== main
def main():
    started = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")
    left, right = edge_polylines()
    tck, u, arc = smooth_centreline(left, right)
    length = arc[-1]
    u_of_s = lambda s: np.interp(np.asarray(s) % length, arc, u)
    s_line = find_finish_line(tck, u_of_s, length)

    master = Lap(tck, u_of_s, length, s_line, int(round(length)))
    game = Lap(tck, u_of_s, length, s_line, int(round(length / GAME_SPACING)))
    origin = master.xy[0].copy()
    lat0, lon0 = lambert_to_latlon(*origin)
    print(f"centreline {length:.2f} m, origin {origin.round(3)} = {lat0:.6f} N {lon0:.6f} E")

    wl_m, wr_m = widths(master, left, right)
    wl, wr = widths(game, left, right)
    z_abs, z_raw, cross_m = elevation(master, wl_m, wr_m)
    datum = float(z_abs[0])
    compare_profil(master, z_abs)
    z_m = z_abs - datum
    z = np.interp(game.lap, np.r_[master.lap, length], np.r_[z_m, z_m[0]])
    cross = np.interp(game.lap, np.r_[master.lap, length], np.r_[cross_m, cross_m[0]])

    kerb_l, runs_l = kerbs(game, wl, 1)
    kerb_r, runs_r = kerbs(game, wr, -1)
    LOG["kerb_runs"] = dict(left=runs_l, right=runs_r)
    run_l, run_r = runoff(game, 1), runoff(game, -1)
    bl, br = barriers(game, wl, wr)

    pit, pz, pit_near, pit_u = pit_lane(game, z, cross, datum)
    # the pit branch lies alongside the right edge from the entry split to the exit merge
    entry_s, exit_s = game.lap[pit_near[0]], game.lap[pit_near[-1]]
    pdist = cKDTree(pit).query(game.xy - game.normal * wr[:, None])[0]
    in_span = (game.lap >= entry_s) | (game.lap <= exit_s)
    run_r[in_span & (pdist < 14)] = "P"
    run_l, run_r = "".join(run_l), "".join(run_r)

    slots = grid_boxes(master)
    start_s = start_line(master, slots[0][0])
    LOG["start_line_lap_m"] = start_s

    reg_corners = corner_table(master, CORNERS)
    shift = float(np.median([c["registry"]["apexOnBuildS"] - c["registry"]["apex"] for c in reg_corners]))
    reg_bends = corner_table(master, BENDS, shift)
    LOG["registry_apex_shift_median_m"] = shift

    def lapref(x):
        return round(float((x + shift) % length), 1)

    T = {c["number"]: c for c in reg_corners}
    sectors = [dict(sector=1, startS=0.0, status="ENVELOPE", provenance="DEFINITION(control line)"),
               dict(sector=2, startS=lapref(2216), envelope=[lapref(2216), lapref(2335)], status="ENVELOPE",
                    provenance="DERIVED(user map, S1 end ≈ 2,216 m after the 155 m flag correction; inferred envelope 2,216-2,335 m)"),
               dict(sector=3, startS=lapref(4935), envelope=[T[14]["exitS"], T[15]["entryS"]], status="ENVELOPE",
                    provenance="DERIVED(user map, S2 end ≈ 4,935 m; inferred envelope T14 exit-T15 entry)")]
    # 2025 DRS: detection 240 m before T2 and 160 m before T18, activation 305 m after T4 and 30 m after T19 (FIA),
    # measured here from the apexes; zone ends at the next braking point (user map)
    drs = [dict(number=1, detectionS=round(T[2]["apexS"] - 240, 1), activationS=round(T[4]["apexS"] + 305, 1), endS=lapref(2216)),
           dict(number=2, detectionS=round(T[18]["apexS"] - 160, 1), activationS=round((T[19]["apexS"] + 30) % length, 1), endS=lapref(210))]

    track = dict(
        id="spa", name="Spa-Francorchamps", location="Stavelot, Belgium", officialLength=7004, raceLaps=44, clockwise=True,
        scale=1, originLat=round(float(lat0), 7), originLon=round(float(lon0), 7),
        attribution="Road edges, kerbs and run-off measured from SPW ORTHO 2023 (© SPW 2023, CC BY 4.0); elevation and bank "
                    "from SPW Relief Wallonie MNT 2021-2022 (© SPW 2021-2022, CC BY 4.0); search prior TUMFTM racetrack-database "
                    "(LGPL-3.0, © OpenStreetMap contributors). See tracks/spa/CREDITS.md.",
        sampleSpacing=length / game.n, centerline=rnd(game.xy - origin), elevation=rnd(z), crossfall=rnd(cross, 5),
        widthLeft=rnd(wl), widthRight=rnd(wr), barrierLeft=rnd(bl), barrierRight=rnd(br),
        kerbLeft=kerb_l, kerbRight=kerb_r, runoffLeft=run_l, runoffRight=run_r,
        corners=[dict(number=c["number"], name=c["name"], entryS=c["entryS"], apexS=c["apexS"], exitS=c["exitS"],
                      direction=1 if c["direction"] == "L" else -1, minRadius=c["minRadiusM"], angle=round(abs(c["headingChangeDeg"]), 1))
                 for c in reg_corners],
        bends=[dict(number=0, name=c["name"], entryS=c["entryS"], apexS=c["apexS"], exitS=c["exitS"],
                    direction=1 if c["direction"] == "L" else -1, minRadius=c["minRadiusM"], angle=round(abs(c["headingChangeDeg"]), 1))
               for c in reg_bends],
        sectorStarts=[s["startS"] for s in sectors], drsZones=drs,
        startLineS=round(start_s % length, 2),
        grid=[dict(position=j + 1, s=round(front - 0.22, 2) % round(length, 3), d=round(dd, 2)) for j, (front, dd) in enumerate(slots[:20])],
        pitLane=dict(speedLimitKmh=80, entryS=round(float(entry_s), 2), exitS=round(float(exit_s), 2), path=rnd(pit - origin),
                     elevation=rnd(pz), trackS=rnd(game.lap[pit_near]), width=7.6, limitStart=PIT_LIMIT[0], limitEnd=PIT_LIMIT[1],
                     boxes=rnd(PIT_BOXES), boxOffset=-1.9, entryLineEnd=118, exitLineStart=595),
        marshalPosts=[dict(number=j + 1, s=float(st), side=1 if j % 2 == 0 else -1, sector=int(np.searchsorted([sectors[1]["startS"], sectors[2]["startS"]], st)))
                      for j, st in enumerate([95, 450, 800, 1085, 1440, 1850, 2190, 2470, 2850, 3280, 3635, 4000, 4400, 4760, 5060, 5500, 5820, 6130, 6490, 6810])])
    write_json(GAME / "track.json", track, compact=True)

    v, tri = terrain(game, z, cross, wl, wr, bl, br, pit, pz, datum, origin)
    trees = forest(game, bl, br, pit, tri, v, origin, datum)
    write_json(GAME / "terrain.json", dict(vertices=rnd(v, 2), triangles=tri.simplices.ravel().tolist(), trees=rnd(trees, 2)), compact=True)

    # ---------------------------------------------------------- engine-neutral intermediates
    OUT.mkdir(parents=True, exist_ok=True)
    rel = master.xy - origin
    bank_deg = np.degrees(np.arctan(cross_m))
    with open(OUT / "stations.csv", "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["lap_m", "e_m", "n_m", "u_m", "heading_deg", "curvature_1_per_m", "half_width_left_m", "half_width_right_m",
                    "bank_deg", "u_prov", "width_prov", "bank_prov"])
        for i in range(master.n):
            w.writerow([f"{master.lap[i]:.3f}", f"{rel[i, 0]:.3f}", f"{rel[i, 1]:.3f}", f"{z_abs[i]:.3f}",
                        f"{np.degrees(master.heading[i]) % 360:.3f}", f"{master.kappa[i]:.6f}", f"{wl_m[i]:.3f}", f"{wr_m[i]:.3f}",
                        f"{bank_deg[i]:.3f}", "MEASURED(D2 MNT 2021-2022 0.5 m identify; 5 m median + 1.5 m gaussian)",
                        "MEASURED(D3 ORTHO 2023 painted-line trace)", "DERIVED(D2 heights 1 m inside each painted line; 9-sample median + 4 m gaussian)"])
    write_json(OUT / "corners.json", dict(origin="lap metres from the measured control (finish) line", corners=reg_corners, bends=reg_bends))
    write_json(OUT / "sectors.json", dict(note="Sector lines are not published; these are envelopes.", sectors=sectors))
    write_json(OUT / "zones.json", zones(T, lapref, length, drs))
    LOG.update(started=started, length_m=length, stations=master.n, game_samples=game.n, datum_m=datum,
               origin_lambert=origin.tolist(), origin_latlon=[float(lat0), float(lon0)], dtm_samples=len(DTM.values),
               dsm_samples=len(DSM.values))
    write_json(OUT / "build_log.json", LOG)
    print(json.dumps({k: LOG[k] for k in ("centreline_smoothing_residual_m", "elevation", "bank", "grid", "start_line_lap_m", "terrain", "forest")}, indent=1))


def zones(T, lapref, length, drs):
    def z(name, a, b, status, prov, **kw):
        return dict(name=name, fromS=round(float(a), 1), toS=round(float(b), 1), status=status, provenance=prov, **kw)
    return dict(
        note="Lap metres from the measured control line. 2026 endpoints are envelopes between named corners: no zone length is published.",
        season2026=[
            z("Straight Mode: start/finish straight", T[19]["exitS"], T[1]["entryS"], "ENVELOPE", "SOURCED(formula1.com 2026 zones) / DERIVED(T19 exit→T1 entry)"),
            z("Straight Mode: La Source to Eau Rouge", T[1]["exitS"], T[2]["entryS"], "ENVELOPE", "SOURCED(formula1.com, 'between Turns 2 and 3' read as T1-T2) / DERIVED"),
            z("Straight Mode: Kemmel", T[4]["exitS"], T[5]["entryS"], "ENVELOPE", "SOURCED / DERIVED(T4 exit→T5 entry)"),
            z("Straight Mode: T15-T16", T[15]["exitS"], T[16]["entryS"], "ENVELOPE", "SOURCED / DERIVED; alternative start at T14 exit",
              alternativeFromS=T[14]["exitS"]),
            z("Straight Mode: T17-T18", T[17]["exitS"], T[18]["entryS"], "ENVELOPE", "SOURCED; barred under Low Grip Status"),
            dict(name="Overtake Mode", detectionS=T[19]["apexS"], activationS=T[19]["exitS"], status="ENVELOPE",
                 provenance="SOURCED(FIA 2026: detection at T19 / Safety Car Line 1, activation at T19 exit)")],
        legacy2025Drs=[dict(**d, status="ENVELOPE", provenance="DERIVED(FIA 2025: detection 240 m before T2 / 160 m before T18, "
                                                               "activation 305 m after T4 / 30 m after T19; user map ends)") for d in drs],
        speedTrap=dict(s=lapref(1258), status="ENVELOPE", provenance="DERIVED(user map, ≈74 m after T4 exit)"),
        selected="legacy2025Drs (the game runs the 2025 regulations)")


if __name__ == "__main__":
    main()
