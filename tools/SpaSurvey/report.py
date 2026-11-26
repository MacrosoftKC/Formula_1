"""Stage 5: write tracks/spa/manifest.json, uncertainty_report.md, CREDITS.md, progress.md and the circuit notes
game/data/tracks/spa/SOURCES.md from the cached inputs, build_log.json and checks.json.

  python tools/SpaSurvey/report.py
"""
import datetime
import hashlib

from spa_common import *
from uncertainty import CONFLICTS, FEATURES

LICENCES = {
    "D1": ("TUMFTM racetrack-database, tracks/Spa.csv", "LGPL-3.0 (data derived from OpenStreetMap, ODbL)", "local metres, origin at first row, no CRS"),
    "D2_tiles": ("SPW LiDAR 2021-2022 tile index (flight dates)", "CC BY 4.0, © SPW 2021-2022", "EPSG:3812"),
    "D4": ("OpenStreetMap highway=raceway ways (Overpass)", "ODbL, © OpenStreetMap contributors", "WGS84 lat/lon"),
    "D5": ("Circuit de Spa-Francorchamps full-track map (altitude markers)", "© Circuit de Spa-Francorchamps, reference only (not redistributed)", "lap metres, altitude m"),
    "D6": ("bacinger f1-circuits GeoJSON", "MIT", "WGS84 lat/lon"),
}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()[:16]


def manifest(log):
    fetched = read_json(CACHE / "fetch_log.json")
    items = []
    for key, entry in sorted(fetched.items()):
        f = CACHE / entry["file"]
        item = dict(id=key, url=entry["url"], file=f"tools/SpaSurvey/cache/{entry['file']}", retrieved=entry["retrieved"],
                    bytes=f.stat().st_size if f.exists() else None, sha256_16=sha(f) if f.exists() else None)
        if key.startswith("D3_"):
            x, y = (int(v) for v in key.split("_")[1:])
            item.update(name="SPW ORTHO 2023 summer, 25 cm (map service export)", crs="EPSG:3812", units="m, RGB",
                        bbox=[x, y, x + 900, y + 900], pixels="3600x3600", licence="CC BY 4.0, © SPW 2023")
        elif key in LICENCES:
            name, lic, crs = LICENCES[key]
            item.update(name=name, licence=lic, crs=crs)
        if key == "D1" and f.exists():
            raw = np.loadtxt(f, delimiter=",", comments="#")
            seg = np.linalg.norm(np.diff(np.vstack([raw[:, :2], raw[:1, :2]]), axis=0), axis=1)
            item.update(units="m", points=len(raw), spacing_m=[round(float(seg[:-1].min()), 2), round(float(seg[:-1].max()), 2)],
                        closed_length_m=round(float(seg.sum()), 2), closing_gap_m=round(float(seg[-1]), 2),
                        signed_area_m2=round(signed_area(raw[:, :2])), bbox=[round(float(v), 1) for v in (*raw[:, :2].min(0), *raw[:, :2].max(0))],
                        use="search prior only")
        if key == "D2_tiles" and f.exists():
            feats = read_json(f)["features"]
            dates = sorted({datetime.datetime.fromtimestamp(t["attributes"][k] / 1000, datetime.timezone.utc).date().isoformat()
                            for t in feats for k in ("DATE_FIRST", "DATE_LAST")})
            item.update(tiles=len(feats), flight_dates=[dates[0], dates[-1]], vertical_datum="Ostend height (DNG, EPSG:5710)")
        items.append(item)
    items.append(dict(id="D2", name="SPW Relief Wallonie MNT 2021-2022, 0.5 m DTM", url=DTM_SERVICE + "/identify",
                      crs="EPSG:3812 / heights EPSG:5710", units="m", licence="CC BY 4.0, © SPW 2021-2022",
                      vertical_accuracy="0.12 m (metadata)", samples=log["dtm_samples"], file="tools/SpaSurvey/cache/dtm2022.json",
                      access="multipoint identify at 0.25 m display resolution (native pixel values)"))
    items.append(dict(id="D2_DSM", name="SPW Relief Wallonie MNS 2021-2022, 0.5 m DSM (tree heights only)", url=DSM_SERVICE + "/identify",
                      crs="EPSG:3812 / heights EPSG:5710", licence="CC BY 4.0, © SPW 2021-2022", samples=log["dsm_samples"]))
    items.append(dict(id="D2_1m", name="SPW Profil geoprocessing service (1 m Walloon DTM) - first build only, kept for comparison",
                      url=PROFILE_SERVICE, crs="EPSG:3812", files="tools/SpaSurvey/cache/profil1m/*.json",
                      comparison=log.get("dtm_2022_vs_1m_profile")))
    items.append(dict(id="D7", name="User map centreline (spa_map_centerline_10m.csv)", file="tools/SpaSurvey/cache/user/spa_map_centerline_10m.csv",
                      use="check V15 only"))
    items.append(dict(id="D8", name="User Assetto Corsa onboard video", use="feel reference only; not used as data"))
    write_json(OUT / "manifest.json", dict(
        generated=datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
        frame=dict(crs="EPSG:3812 Belgian Lambert 2008", local_origin_lambert=log["origin_lambert"], local_origin_latlon=log["origin_latlon"],
                   origin_is="measured chequered control line", vertical="metres above Ostend (DNG); track.json heights are relative to "
                   f"{log['datum_m']:.3f} m at the control line", engine="Godot: (X, Y, Z) = (E, U, -N), 1 unit = 1 m"),
        inputs=items))


def uncertainty(checks, log):
    lines = ["# Spa-Francorchamps replica: uncertainty report", "",
             f"Generated {datetime.date.today().isoformat()} by `tools/SpaSurvey/report.py`. Every feature is MEASURED (read from a named survey file), "
             "DERIVED (computed from measured data), SOURCED (published), ASSUMED (a visible placeholder, never a claim about Spa) or UNKNOWN.", "",
             "## Features", "", "| Feature | Status | How / why |", "|---|---|---|"]
    lines += [f"| {f} | {s} | {r} |" for f, s, r in FEATURES]
    lines += ["", f"UNKNOWN: {sum(s == 'UNKNOWN' for _, s, _ in FEATURES)}. ASSUMED: {sum(s == 'ASSUMED' for _, s, _ in FEATURES)}.", "",
              "## Acceptance tests", "", "| Test | Result | Measured | Tolerance |", "|---|---|---|---|"]
    for c in checks:
        res = "PASS" if c["pass"] is True else "FAIL" if c["pass"] is False else "NOT RUN"
        lines.append(f"| {c['id']} | {res} | {c['measured']} | {c['tolerance']} |")
    lines += ["", "Tolerances are the brief's proposals and were not changed. Evidence for each line is in `checks.json`.", "",
              "## Conflicts between sources", ""] + [f"- {c}" for c in CONFLICTS]
    lines += ["", "## Values estimated rather than measured", "",
              "- Barrier line: 64 rows read by eye from the orthophoto (first build), linear in between.",
              "- Pit lane path: 36 hand-placed points on the orthophoto, splined.",
              "- Sector lines and all 2026 zones: envelopes, not published positions.",
              "- Kerb profile (30 mm, chamfered), pit speed limit (80 km/h), pit boxes, marshal posts, grandstands, buildings: game placeholders.",
              "", "## Checks that could not be fully run", "",
              "- V8 uses OpenStreetMap as the only independent outline; there is no second LiDAR/orthophoto edge set, and the build's own edges are never used as their own reference.",
              "- V9 re-detects the painted line with a simpler local method on the same orthophoto; a truly independent width survey does not exist.",
              "- V12 uses the game's AI driver, not a reference hot lap.",
              "", "## Build log highlights", "", "```", json.dumps({k: log[k] for k in ("length_m", "datum_m", "control_line", "start_line_lap_m", "grid",
                                                                                   "elevation", "bank", "dtm_2022_vs_1m_profile") if k in log}, indent=1), "```"]
    (OUT / "uncertainty_report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


CREDITS = """# Credits and licences: Spa-Francorchamps replica

- **SPW orthophoto 2023 (ORTHO_2023_ETE, 25 cm)** — © SPW 2023, licence CC BY 4.0. Road edges, kerbs, run-off, grid and start/finish lines.
- **SPW Relief de la Wallonie, MNT and MNS 2021-2022 (0.5 m LiDAR)** — © SPW 2021-2022, licence CC BY 4.0. Elevation, bank, terrain and tree heights.
- **SPW 1 m Walloon DTM (Profil geoprocessing service)** — © SPW. First build only, kept as a comparison.
- **OpenStreetMap** — © OpenStreetMap contributors, ODbL. Raceway ways used as an independent outline check (V8).
- **TUMFTM racetrack-database** (github.com/TUMFTM/racetrack-database) — LGPL-3.0, derived from OpenStreetMap (ODbL). Search prior only; no TUM coordinates are shipped.
- **bacinger/f1-circuits** — MIT. Downloaded as a georeferencing prior; not used in the shipped geometry.
- **Circuit de Spa-Francorchamps full-track map** — altitude markers used as a check only; the PDF is not redistributed.

No Formula 1 telemetry, real sponsor marks or team logos are used. Advertising boards are neutral panels.
"""


def progress(checks, log):
    passed = sum(c["pass"] is True for c in checks)
    failed = [c["id"] for c in checks if c["pass"] is False]
    text = f"""# Spa-Francorchamps replica: progress

Updated {datetime.date.today().isoformat()}.

| Stage | State |
|---|---|
| 0 Project inspection | Godot 4.7 .NET, 1 unit = 1 m, (X, Y, Z) = (E, U, -N); tracks load from `game/data/tracks/<id>/track.json` |
| 1 Manifest | `manifest.json` (D1-D8; DTM sampled through the SPW map service) |
| 2 Coordinates | Belgian Lambert 2008, local origin at the measured control line {log['origin_lambert']} |
| 3 Start/finish | Chequered line measured in D3 ({log['control_line']['chosen_offset_from_research_estimate_m']:+.2f} m from the research estimate); start line at {log['start_line_lap_m']:.2f} m |
| 4 Centreline | Midline of both painted lines, smoothing spline, {log['length_m']:.2f} m plan, {log['stations']} 1 m stations |
| 5 Elevation | 2022 DTM, 4 cm smoothing budget |
| 6 Width and bank | Painted-line widths; bank from DTM edge heights |
| 7-8 Ribbon, kerbs, run-off, barriers | Built at runtime by the game from track.json; kerbs measured, run-off and barriers read by eye from the orthophoto |
| 9 Terrain | 2022 DTM grid + trackside rings; forest from orthophoto canopy with DSM-DTM heights |
| 10 Race data | `corners.json`, `zones.json`, `sectors.json` |
| 11 Export | `game/data/tracks/spa/*.json` (engine adapter), `spa_neutral.glb` |
| 12 Validation | {passed} of {len(checks)} tests pass; failing: {', '.join(failed) or 'none'} (see `uncertainty_report.md`) |

Rebuild: `python tools/SpaSurvey/fetch.py`, `edges.py`, `build.py`, `glb.py`, `checks.py --lap-log <log>`, `report.py`.
"""
    (OUT / "progress.md").write_text(text, encoding="utf-8")


def main():
    log = read_json(OUT / "build_log.json")
    checks = read_json(OUT / "checks.json") if (OUT / "checks.json").exists() else []
    manifest(log)
    uncertainty(checks, log)
    (OUT / "CREDITS.md").write_text(CREDITS, encoding="utf-8")
    progress(checks, log)
    (GAME / "SOURCES.md").write_text(
        "# Spa-Francorchamps circuit notes\n\nThis circuit is rebuilt by `tools/SpaSurvey` from the 2023 Walloon orthophoto and the 2021-2022 "
        "Walloon LiDAR terrain model (both © SPW, CC BY 4.0). Sources, licences, measured-versus-assumed status for every feature and the "
        "acceptance-test results are in [`tracks/spa/`](../../../../tracks/spa/): `uncertainty_report.md`, `manifest.json`, `checks.json` "
        "and `CREDITS.md`.\n", encoding="utf-8")
    print("wrote manifest.json, uncertainty_report.md, CREDITS.md, progress.md, game/data/tracks/spa/SOURCES.md")


if __name__ == "__main__":
    main()
