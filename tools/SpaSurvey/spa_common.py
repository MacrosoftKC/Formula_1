"""Shared paths, coordinate conversions and cached data access for the Spa-Francorchamps survey pipeline.

Coordinates: Belgian Lambert 2008 (EPSG:3812) metres, heights in metres above the Ostend datum (DNG, EPSG:5710).
The game frame subtracts LOCAL_ORIGIN (the measured finish/control line) and keeps (east, north, up) metres.
"""
from __future__ import annotations

import concurrent.futures as fut
import json
import math
import time
import urllib.parse
import urllib.request
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
TOOL = Path(__file__).resolve().parent
CACHE = TOOL / "cache"
OUT = ROOT / "tracks" / "spa"
GAME = ROOT / "game" / "data" / "tracks" / "spa"
USER_AGENT = "SpaSurvey/1.0 (low-poly racing game track builder)"

SPW = "https://geoservices.wallonie.be/arcgis/rest/services"
ORTHO_SERVICE = f"{SPW}/IMAGERIE/ORTHO_2023_ETE/MapServer"
DTM_SERVICE = f"{SPW}/RELIEF/WALLONIE_MNT_2021_2022/MapServer"
DSM_SERVICE = f"{SPW}/RELIEF/WALLONIE_MNS_2021_2022/MapServer"
PROFILE_SERVICE = f"{SPW}/GEOTRAITEMENT/Profil/GPServer/Profil/execute"

ORTHO_TILES = [(x, y) for x in (762700, 763600) for y in (625100, 626000, 626900)]
ORTHO_TILE = 900.0
ORTHO_RES = 0.25

# Rigid prior registration of the TUM centreline (local metres, rolled by 28 rows) into Lambert 2008, fitted by
# least squares to the dossier's nine corner-apex coordinates. Only used to seed the orthophoto edge search.
TUM_ROLL = 28
TUM_ROT = np.array([[0.9998678822598808, -0.016254784684573226], [0.01625478468457318, 0.9998678822598804]])
TUM_TRANS = np.array([763447.5283822252, 627039.986501816])


# ---------------------------------------------------------------- Lambert 2008 (EPSG:3812) <-> WGS84/ETRS89
_A, _F = 6378137.0, 1 / 298.257222101
_E = math.sqrt(2 * _F - _F * _F)
_LAT1, _LAT2 = math.radians(49 + 50 / 60), math.radians(51 + 10 / 60)
_LAT0, _LON0 = math.radians(50 + 47 / 60 + 52.134 / 3600), math.radians(4 + 21 / 60 + 33.177 / 3600)
_FE, _FN = 649328.0, 665262.0


def _m(lat):
    return math.cos(lat) / math.sqrt(1 - (_E * math.sin(lat)) ** 2)


def _t(lat):
    s = _E * math.sin(lat)
    return math.tan(math.pi / 4 - lat / 2) / ((1 - s) / (1 + s)) ** (_E / 2)


_N = (math.log(_m(_LAT1)) - math.log(_m(_LAT2))) / (math.log(_t(_LAT1)) - math.log(_t(_LAT2)))
_FF = _m(_LAT1) / (_N * _t(_LAT1) ** _N)
_R0 = _A * _FF * _t(_LAT0) ** _N


def lambert_to_latlon(x, y):
    """Inverse Lambert conformal conic 2SP (EPSG 9802) for EPSG:3812. Returns degrees (lat, lon)."""
    x = np.asarray(x, float) - _FE
    y = _R0 - (np.asarray(y, float) - _FN)
    r = np.sign(_N) * np.hypot(x, y)
    t = (r / (_A * _FF)) ** (1 / _N)
    theta = np.arctan2(x, y)
    lon = theta / _N + _LON0
    lat = np.pi / 2 - 2 * np.arctan(t)
    for _ in range(8):
        s = _E * np.sin(lat)
        lat = np.pi / 2 - 2 * np.arctan(t * ((1 - s) / (1 + s)) ** (_E / 2))
    return np.degrees(lat), np.degrees(lon)


def latlon_to_lambert(lat, lon):
    lat = np.radians(np.asarray(lat, float))
    lon = np.radians(np.asarray(lon, float))
    s = _E * np.sin(lat)
    t = np.tan(np.pi / 4 - lat / 2) / ((1 - s) / (1 + s)) ** (_E / 2)
    r = _A * _FF * t ** _N
    theta = _N * (lon - _LON0)
    return _FE + r * np.sin(theta), _FN + _R0 - r * np.cos(theta)


# ---------------------------------------------------------------- HTTP with retry
def http(url: str, data: dict | None = None, timeout: int = 90, tries: int = 4) -> bytes:
    body = urllib.parse.urlencode(data).encode() if data else None
    for attempt in range(tries):
        try:
            req = urllib.request.Request(url, data=body, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.read()
        except Exception:
            if attempt == tries - 1:
                raise
            time.sleep(2 + 4 * attempt)
    raise RuntimeError("unreachable")


# ---------------------------------------------------------------- orthophoto (SPW ORTHO 2023 summer, 25 cm)
_tiles: dict[tuple[int, int], np.ndarray] = {}


def ortho_tile_path(x: int, y: int) -> Path:
    return CACHE / "ortho" / f"ortho{x}_{y}.jpg"


def ortho_rgb(coords: np.ndarray) -> np.ndarray:
    """Nearest-pixel RGB at Lambert coordinates (..., 2). Outside the cached tiles returns zeros."""
    coords = np.asarray(coords, float)
    out = np.zeros((*coords.shape[:-1], 3), np.uint8)
    n = int(ORTHO_TILE / ORTHO_RES)
    for x, y in ORTHO_TILES:
        m = (coords[..., 0] >= x) & (coords[..., 0] < x + ORTHO_TILE) & (coords[..., 1] >= y) & (coords[..., 1] < y + ORTHO_TILE)
        if not m.any():
            continue
        if (x, y) not in _tiles:
            _tiles[(x, y)] = np.asarray(Image.open(ortho_tile_path(x, y)).convert("RGB"))
        im = _tiles[(x, y)]
        ij = (coords[m] - [x, y]) / ORTHO_RES
        out[m] = im[np.clip((n - ij[:, 1]).astype(int), 0, n - 1), np.clip(ij[:, 0].astype(int), 0, n - 1)]
    return out


# ---------------------------------------------------------------- raster sampling via ArcGIS identify (cached)
class RasterSampler:
    """Samples the native pixel value of an SPW raster MapServer layer at Lambert points (identify, multipoint).

    The map extent sent with each request is scaled to 0.25 m per display pixel so the server answers from the
    full-resolution level rather than a pyramid. Results are cached on disk keyed by centimetre coordinates."""

    def __init__(self, service: str, cache_name: str):
        self.service = service
        self.file = CACHE / f"{cache_name}.json"
        self.values: dict[str, float] = json.loads(self.file.read_text()) if self.file.exists() else {}

    @staticmethod
    def key(p) -> str:
        return f"{p[0]:.2f},{p[1]:.2f}"

    def _request(self, pts: np.ndarray) -> list[float]:
        lo, hi = pts.min(0) - 5, pts.max(0) + 5
        size = np.clip(np.ceil((hi - lo) / 0.25), 50, 4000).astype(int)
        hi = lo + size * 0.25
        geom = json.dumps({"points": np.round(pts, 2).tolist()}, separators=(",", ":"))
        params = dict(geometry=geom, geometryType="esriGeometryMultipoint", sr=3812, layers="all:0", tolerance=0,
                      mapExtent=f"{lo[0]:.2f},{lo[1]:.2f},{hi[0]:.2f},{hi[1]:.2f}", imageDisplay=f"{size[0]},{size[1]},96",
                      returnGeometry="false", f="json")
        res = json.loads(http(f"{self.service}/identify", params))["results"]
        if len(res) != len(pts):
            raise IOError(f"identify returned {len(res)} values for {len(pts)} points")
        vals = []
        for r in res:
            v = next(iter(r["attributes"].values()))
            vals.append(float(v) if v not in ("NoData", "", None) else float("nan"))
        return vals

    def sample(self, pts: np.ndarray, batch: int = 250, workers: int = 3) -> np.ndarray:
        pts = np.asarray(pts, float).reshape(-1, 2)
        keys = [self.key(p) for p in pts]
        missing = sorted({k for k in keys if k not in self.values})
        if missing:
            arr = np.array([[float(v) for v in k.split(",")] for k in missing])
            # keep batches spatially compact so the request extent stays small
            order = np.lexsort((arr[:, 1], np.floor(arr[:, 0] / 200)))
            arr = arr[order]
            batches = [arr[i:i + batch] for i in range(0, len(arr), batch)]
            print(f"  sampling {len(arr)} points from {self.service.split('/')[-2]} in {len(batches)} requests", flush=True)
            done = 0
            with fut.ThreadPoolExecutor(workers) as pool:
                for b, vals in zip(batches, pool.map(self._request, batches)):
                    for p, v in zip(b, vals):
                        self.values[self.key(p)] = v
                    done += 1
                    if done % 20 == 0:
                        print(f"    {done}/{len(batches)}", flush=True)
                        self.save()
            self.save()
        return np.array([self.values[k] for k in keys])

    def save(self):
        self.file.parent.mkdir(parents=True, exist_ok=True)
        self.file.write_text(json.dumps(self.values, separators=(",", ":")))


# ---------------------------------------------------------------- polyline helpers (closed loops)
def closed(p):
    return np.vstack([p, p[:1]])


def stations(p):
    """Cumulative arc length of a closed loop including the closing segment (len = n + 1)."""
    return np.r_[0, np.linalg.norm(np.diff(closed(p), axis=0), axis=1).cumsum()]


def normals(p):
    """Left-pointing unit normals of a closed loop from central differences."""
    t = np.roll(p, -1, 0) - np.roll(p, 1, 0)
    t /= np.linalg.norm(t, axis=1)[:, None]
    return np.c_[-t[:, 1], t[:, 0]]


def resample_closed(p, spacing=None, count=None, start=0.0):
    """Linear resampling of a closed polyline at uniform arc length beginning at arc length `start`."""
    s = stations(p)
    total = s[-1]
    if count is None:
        count = int(round(total / spacing))
    u = (start + np.arange(count) * total / count) % total
    q = closed(p)
    return np.c_[np.interp(u, s, q[:, 0]), np.interp(u, s, q[:, 1])], total


def heading_change(p):
    """Signed heading change at each vertex of a closed loop (radians, left turn positive)."""
    t = np.roll(p, -1, 0) - p
    a = np.arctan2(t[:, 1], t[:, 0])
    return np.angle(np.exp(1j * (a - np.roll(a, 1))))


def signed_area(p):
    return 0.5 * float(np.sum(p[:, 0] * np.roll(p[:, 1], -1) - np.roll(p[:, 0], -1) * p[:, 1]))


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def write_json(path, obj, compact=False):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(obj, separators=(",", ":")) if compact else json.dumps(obj, indent=1, ensure_ascii=False)
    path.write_text(text + ("" if compact else "\n"), encoding="utf-8")


def rnd(x, n=3):
    return np.round(np.asarray(x, float), n).tolist()
