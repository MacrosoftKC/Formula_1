# Spa-Francorchamps replica: progress

Updated 2026-11-25.

| Stage | State |
|---|---|
| 0 Project inspection | Godot 4.7 .NET, 1 unit = 1 m, (X, Y, Z) = (E, U, -N); tracks load from `game/data/tracks/<id>/track.json` |
| 1 Manifest | `manifest.json` (D1-D8; DTM sampled through the SPW map service) |
| 2 Coordinates | Belgian Lambert 2008, local origin at the measured control line [763376.0784465298, 627163.9777324494] |
| 3 Start/finish | Chequered line measured in D3 (+1.25 m from the research estimate); start line at -21.25 m |
| 4 Centreline | Midline of both painted lines, smoothing spline, 6987.38 m plan, 6987 1 m stations |
| 5 Elevation | 2022 DTM, 4 cm smoothing budget |
| 6 Width and bank | Painted-line widths; bank from DTM edge heights |
| 7-8 Ribbon, kerbs, run-off, barriers | Built at runtime by the game from track.json; kerbs measured, run-off and barriers read by eye from the orthophoto |
| 9 Terrain | 2022 DTM grid + trackside rings; forest from orthophoto canopy with DSM-DTM heights |
| 10 Race data | `corners.json`, `zones.json`, `sectors.json` |
| 11 Export | `game/data/tracks/spa/*.json` (engine adapter), `spa_neutral.glb` |
| 12 Validation | 8 of 15 tests pass; failing: V1, V2, V5, V7, V8, V10, V11 (see `uncertainty_report.md`) |

Rebuild: `python tools/SpaSurvey/fetch.py`, `edges.py`, `build.py`, `glb.py`, `checks.py --lap-log <log>`, `report.py`.
