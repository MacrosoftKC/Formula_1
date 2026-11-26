"""Stage 1: download and cache every input, then record what was retrieved (cache/fetch_log.json).

  python tools/SpaSurvey/fetch.py

Inputs (see tracks/spa/manifest.json after build.py for the full manifest):
  D1 TUMFTM racetrack-database Spa centreline          (LGPL-3.0, OSM-derived)
  D2 SPW Relief Wallonie MNT 2021-2022 0.5 m (sampled on demand by build.py through the map service)
  D3 SPW ORTHO 2023 summer 25 cm (six 900 m tiles exported from the map service)
  D4 OpenStreetMap raceway ways (Overpass)              (ODbL)
  D5 Circuit operator full-track PDF (altitude markers)
  D6 bacinger f1-circuits GeoJSON                       (MIT)
"""
import datetime
import json
import urllib.parse

from spa_common import *

SOURCES = {
    "D1": ("tum/Spa.csv", "https://raw.githubusercontent.com/TUMFTM/racetrack-database/master/tracks/Spa.csv"),
    "D5": ("d5/fulltrack.pdf", "https://ser2026.racspa.be/docs/CircuitdeSpa-Francorchamps-Fulltrack.pdf"),
    "D6": ("bacinger/f1-circuits.geojson", "https://raw.githubusercontent.com/bacinger/f1-circuits/master/f1-circuits.geojson"),
}
OVERPASS = "https://overpass-api.de/api/interpreter"
OVERPASS_QUERY = '[out:json][timeout:60];(way["highway"="raceway"](50.40,5.90,50.47,5.98););out tags geom;'
TILE_INDEX = f"{SPW}/RELIEF/LIDAR_MAILLES/MapServer/0/query"


def main():
    log_file = CACHE / "fetch_log.json"
    log = json.loads(log_file.read_text()) if log_file.exists() else {}
    now = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")

    def note(key, url, path, **extra):
        if key not in log or not (CACHE / path).exists():
            log[key] = dict(url=url, file=path, retrieved=now, **extra)

    for key, (path, url) in SOURCES.items():
        target = CACHE / path
        if not target.exists():
            print("download", url)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(http(url))
        note(key, url, path)

    osm = CACHE / "osm" / "raceway.json"
    if not osm.exists():
        print("download OSM raceway ways")
        osm.parent.mkdir(parents=True, exist_ok=True)
        osm.write_bytes(http(OVERPASS, {"data": OVERPASS_QUERY}, timeout=120, tries=6))
    note("D4", OVERPASS + "?data=" + urllib.parse.quote(OVERPASS_QUERY), "osm/raceway.json")

    for x, y in ORTHO_TILES:
        target = ortho_tile_path(x, y)
        url = f"{ORTHO_SERVICE}/export?" + urllib.parse.urlencode(dict(
            bbox=f"{x},{y},{x + ORTHO_TILE:.0f},{y + ORTHO_TILE:.0f}", bboxSR=3812, imageSR=3812,
            size=f"{int(ORTHO_TILE / ORTHO_RES)},{int(ORTHO_TILE / ORTHO_RES)}", format="jpg", f="image"))
        if not target.exists():
            print("download", target.name)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(http(url, timeout=180))
        note(f"D3_{x}_{y}", url, f"ortho/{target.name}")

    tiles = CACHE / "dtm_tiles.json"
    if not tiles.exists():
        print("download LiDAR tile index (flight dates)")
        params = dict(geometry="762240,624700,765120,628320", geometryType="esriGeometryEnvelope", inSR=3812,
                      spatialRel="esriSpatialRelIntersects", returnGeometry="false", f="json",
                      outFields="LAS_NAME,DATE_FIRST,DATE_LAST,DATE_PROD,LOW_LEFT_X,LOW_LEFT_Y,VERT_CS,VERT_CS_EPSG")
        tiles.write_bytes(http(TILE_INDEX + "?" + urllib.parse.urlencode(params)))
    note("D2_tiles", TILE_INDEX, "dtm_tiles.json")
    write_json(log_file, log)
    print("cached inputs:", ", ".join(sorted(log)))


if __name__ == "__main__":
    main()
