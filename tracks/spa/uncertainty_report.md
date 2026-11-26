# Spa-Francorchamps replica: uncertainty report

Generated 2026-11-25 by `tools/SpaSurvey/report.py`. Every feature is MEASURED (read from a named survey file), DERIVED (computed from measured data), SOURCED (published), ASSUMED (a visible placeholder, never a claim about Spa) or UNKNOWN.

## Features

| Feature | Status | How / why |
|---|---|---|
| Centreline (plan) | MEASURED | Midline of both painted track-limit lines traced in SPW ORTHO 2023 (25 cm), periodic cubic smoothing spline, 0.15 m RMS budget. The TUM/OSM centreline is only the search prior. |
| Lap length | MEASURED | 6,987-6,988 m plan along the painted midline (both edges: 7,028 m left, 6,948 m right). Conflicts with the published 7,004 m; not rescaled. |
| Control (finish) line | MEASURED | Chequered stripe found in D3, 1.25 m from the research estimate (TUM s = 140 m). Sample 0 of track.json. |
| Start line | MEASURED | Separate solid white line ahead of the first grid box (D3), as the operator map shows. |
| Grid boxes | MEASURED | 26 painted box front lines in D3 (16 m ladder per side, pole on the right); the game uses the first 20. |
| Track width | MEASURED | Distance from the centreline to each traced painted line (D3). No published Spa width exists. |
| Elevation profile | MEASURED | SPW Relief Wallonie MNT 2021-2022, 0.5 m, sampled at every metre (identify on the map service); 5 m median then a periodic smoothing spline with a 4 cm RMS budget. Tiles at Spa flown 2022-02-28 to 2022-03-03, during the 2021-22 works. |
| Bank / crossfall | DERIVED | DTM heights 1 m inside each painted line every 3 m; 9-sample median + 6 m gaussian. Noise about ±0.7 percentage points. |
| Kerb positions | MEASURED | Red paint detected 0.3-2.0 m outside the painted line in D3 (per 3 m sample). |
| Kerb colours | MEASURED | Colour of the light stripes of each kerb run averaged from D3 (see build_log.json kerb_runs). |
| Kerb profile and height | ASSUMED | 30 mm flat kerb with 0.35 m chamfers (game physics and mesh). No Spa kerb profile is published; not a claim about the real kerbs. |
| Run-off surface type | DERIVED | Gravel and asphalt intervals read by eye from unwrapped D3 strips (first build), consistent with the 2021-22 works list; grass elsewhere. An automatic colour classifier was rejected (dry grass, shadow and grey gravel read as asphalt). |
| Run-off width and barrier line | DERIVED | Read by eye from unwrapped D3 strips at 64 lap positions (first build), interpolated linearly; about ±3 m; never closer than 1.8 m to the edge. |
| Barrier type (wall, guardrail, tyres, TecPro) | UNKNOWN | Not measurable from the orthophoto. The game draws an ASSUMED generic wall with red/yellow impact blocks beside asphalt and gravel run-off. |
| Debris fence height | UNKNOWN | Game uses an ASSUMED 3 m fence on every wall. |
| Pit lane path | DERIVED | Hand trace of D3 (36 points, spline, 240 samples) from the first build. |
| Pit lane elevation | MEASURED | 2022 DTM along the traced path, blended into the road over 25 m at the entry and exit. |
| Pit lane speed limit | UNKNOWN | Not found for Spa. The game uses an ASSUMED 80 km/h. |
| Pit boxes and garage count | UNKNOWN | Game places 10 ASSUMED boxes evenly along the pit lane. |
| Marshal posts | UNKNOWN | The operator map shows numbered posts but they are not digitised; the game has 20 ASSUMED posts. |
| Sector lines | DERIVED | ENVELOPE from the user map (S1 end ~2,216 m, S2 end ~4,935 m); not published. |
| 2025 DRS zones (game) | DERIVED | FIA distances (240 m before T2, 305 m after T4, 160 m before T18, 30 m after T19) from the measured apexes; ENVELOPE. |
| 2026 Straight Mode / Overtake zones | DERIVED | ENVELOPE between named corners (no lengths published); zones.json only, the game runs 2025 rules. |
| Terrain | MEASURED | 2022 DTM on a 32 m grid plus two rings 7 m and 16 m beyond the barriers, Delaunay-triangulated with the road edges; faceted surface dressing. |
| Tree positions | DERIVED | Dark-green canopy in D3 on a jittered 13 m grid, kept where the 2022 surface model stands 4 m or more above the terrain model. |
| Tree heights | MEASURED | DSM minus DTM (2021-22) at each tree, clamped 4-40 m; the low-poly tree shape is stylised. |
| Grandstands | ASSUMED | Generic stands at the named zones (La Source, Eau Rouge/Raidillon, Les Combes, Bus Stop, start/finish); positions by lap metre, not measured. |
| Pit building and paddock buildings | ASSUMED | The game's generic pit complex; real footprints not used. |
| Uhoda Tower | UNKNOWN | Inauguration status for July 2026 unverified; not modelled. |
| Eau Rouge stream crossing | UNKNOWN | Not modelled. |
| Asphalt, line and kerb paint palette | ASSUMED | Low-poly palette; kerb hues follow the measured light-stripe class. |
| Advertising boards | ASSUMED | Neutral panels; no real sponsor or team marks. |

UNKNOWN: 7. ASSUMED: 5.

## Acceptance tests

| Test | Result | Measured | Tolerance |
|---|---|---|---|
| V1 | FAIL | engine-space centreline 6987.14 m plan (6994.62 m along the road surface); deviation from 7004 / 7001.2 / 7000.05 m = -16.86 / -14.06 / -12.91 m | within 7 m of 7004 m |
| V2 | FAIL | 19 corners, directions RLRLRLRRLLLRLRRLLRL; apex minus registry lap metres [(1, -8.0), (2, -6.0), (3, -17.9), (4, -5.9), (5, -4.9), (6, -30.9), (7, -18.9), (8, -9.8), (9, -0.8), (10, 31.2), (11, 45.2), (12, -40.8), (13, 20.2), (14, 10.3), (15, -8.7), (16, -35.7), (17, -16.7), (18, -15.6), (19, -12.6)]; apex minus registry apex lat/lon projected on the build [(1, -2.0), (2, 9.0), (3, -3.0), (4, 10.0), (5, 5.0), (6, -22.0), (7, -10.0), (8, 2.0), (9, 11.0), (10, 41.0), (11, 56.0), (12, -28.0), (13, 34.0), (14, 24.0), (15, 9.0), (16, -21.0), (17, -4.0), (18, -1.0), (19, -1.0)] | directions RLRLRLRRLLLRLRRLLRL; apex ±15 m (±35 m T10/T11) |
| V3 | PASS | total signed heading -360.00 deg; enclosed area 922,662 m2 (-0.10%); per-corner heading deltas [(1, 1.9), (2, -2.6), (3, -1.8), (4, -0.2), (5, 1.6), (6, 0.6), (7, 1.5), (8, 4.8), (9, -2.1), (10, -9.8), (11, -0.7), (12, 2.5), (13, -0.7), (14, -0.6), (15, -0.1), (16, -2.1), (17, -0.3), (18, 3.8), (19, 4.2)] | |total| = 360±2 deg; area 923,549 m2 ±3%; corner heading ±10 deg (±20 T1/T8/T15/T18/T19) |
| V4 | PASS | signed area in Godot (X,Z) +922,662 (E,N -922,662); La Source/Eau Rouge/Blanchimont orientation registry(E,N) -1 vs engine(X,Z) +1; apex distances to registry lat/lon {1: 12.0, 2: 8.7, 16: 20.2} m | positive in Godot (negative in E,N); marker orientation flips with the N->-Z mapping (no mirror) |
| V5 | FAIL | global offset +2.53 m; residuals max 14.06 m, RMS 3.80 m; worst [(1490, -14.06), (310, 4.02), (6000, -3.58), (3600, 3.57)] | every marker within 3 m after one offset |
| V6 | PASS | relief 102.17 m; max 468.78 m at lap 2476; min 366.60 m at lap 4994 | 95-106 m; max in 2378-2568; min in 4812-5491 |
| V7 | FAIL | Raidillon peak 15.41% at lap 1023 (10 m window); stretches (measured, brief) [('La Source exit to Eau Rouge bottom', np.float64(-5.26), -4.8), ('Eau Rouge bottom to Raidillon crest', np.float64(9.39), 10.0), ('Early Kemmel', np.float64(4.69), 8.6), ('Kemmel overall', np.float64(4.6), 4.1), ('T7 entry to T10 entry', np.float64(-5.82), -6.0), ('Fagnes-Campus valley floor', np.float64(-0.61), -0.5), ('Paul Frère to the line', np.float64(2.58), 2.4)] | peak 11-18%; stretch averages within 1.5 pp (2 pp Eau Rouge to crest) |
| V8 | FAIL | vs OpenStreetMap raceway ways after rigid fit (rotation +0.001 deg, shift 0.43 m): mean 0.47 m, 95th pct 0.99 m, Hausdorff 7.26 m | GPS/imagery-derived reference: mean <= 1 m, Hausdorff <= 3-5 m (no independent LiDAR/orthophoto edge set exists; the build's own edges are not reused) |
| V9 | PASS | total width min/median/max 8.73/10.02/18.50 m; 6039 of 6987 stations under 12 m (flag); independent local re-detection agrees within 0.5 m at 95.0% of 560 probes | within 0.5 m of the orthophoto measurement at each station; <12 m flagged, not failed |
| V10 | FAIL | max curvature change between 1 m stations 0.01586 1/m^2; smoothed minimum radius T1/T18/T19 {1: 10.2, 18: 13.6, 19: 15.4} m | continuous curvature (cubic smoothing spline); Rmin about 15-25 m at T1, T18, T19 |
| V11 | FAIL | terrain 30,962 triangles, 0 degenerate; road-centre stations where terrain is within 5 cm of the road: 0; in-game per view: draw calls 66-239 (including instanced forest patches and text labels), primitives 42k-171k; spa_neutral.glb 3521 KB | budgets: desktop 150-300k triangles, <30 merged draw calls plus instances; no degenerate triangles; no road/terrain clipping |
| V12 | PASS | AI hot lap 1:53.125 vs record 1:44.701 (+8.0%) and 2025 pole 1:40.562 (+12.5%) | within ±10% of 1:44.701 |
| V13 | PASS | pole box front line 23.5 m before the control line, 275.5 m before the T1 apex; separate start line at lap -21.0 m; 8 zones stored as envelopes | pole slot about 271±15 m before T1 apex (soft); zones inside envelopes |
| V14 | PASS | 6987 station rows, 0 without provenance; corners/zones/sectors tagged: True; 32 features in the uncertainty register, 7 UNKNOWN, 5 ASSUMED | every emitted feature tagged; UNKNOWN count printed |
| V15 | PASS | user map vs build after similarity fit: mean 0.50 m, max 4.10 m, scale 0.9988, rotation -96.5 deg (map frame -> north-up E,N) | mean <= 3 m, max <= 15 m (expect scale ~1.0, rotation ~ -97.5 deg) |

Tolerances are the brief's proposals and were not changed. Evidence for each line is in `checks.json`.

## Conflicts between sources

- Lap length: measured painted midline 6,987-6,988 m (plan) vs published 7,004 m, 7,001.2 m (race distance / laps) and 7,000.05 m (TUM closed polygon). The TUM polygon is longer because its OSM-derived vertices wander; smoothing the TUM prior gives 6,989.7 m, in agreement with the measurement.
- Altitude marker at 1,490 m: the operator PDF says 446 m (checked visually); the 2022 DTM gives about 431 m there, on an otherwise uniform 4.5 % Kemmel climb. Every other marker agrees within a few metres.
- Relief: DTM gives about 102 m (between the operator/Williams 97 m and formula1.com 102.2 m).
- Raidillon peak gradient: DTM gives about 15.4 % over a 10 m window (published values range 11-18 %).
- DTM flight date (28 Feb-3 Mar 2022) falls inside the 2021-22 works; the orthophoto is summer 2023. Run-off shapes come from the 2023 image; heights may show mid-works ground at Raidillon and the run-off areas.
- Kerb colours from the 2023 orthophoto differ from the Assetto Corsa reference video, which shows its own palette; the photo wins.

## Values estimated rather than measured

- Barrier line: 64 rows read by eye from the orthophoto (first build), linear in between.
- Pit lane path: 36 hand-placed points on the orthophoto, splined.
- Sector lines and all 2026 zones: envelopes, not published positions.
- Kerb profile (30 mm, chamfered), pit speed limit (80 km/h), pit boxes, marshal posts, grandstands, buildings: game placeholders.

## Checks that could not be fully run

- V8 uses OpenStreetMap as the only independent outline; there is no second LiDAR/orthophoto edge set, and the build's own edges are never used as their own reference.
- V9 re-detects the painted line with a simpler local method on the same orthophoto; a truly independent width survey does not exist.
- V12 uses the game's AI driver, not a reference hot lap.

## Build log highlights

```
{
 "length_m": 6987.37959642738,
 "datum_m": 416.894709160077,
 "control_line": {
  "candidates": [
   {
    "offset_m": -20.0,
    "bright": 0.9958336376618017,
    "dark": 0.0,
    "s": 6967.37959642738
   },
   {
    "offset_m": 1.25,
    "bright": 0.793396315520569,
    "dark": 0.04563492063492063,
    "s": 1.25
   },
   {
    "offset_m": 43.75,
    "bright": 0.9190587010837922,
    "dark": 0.0,
    "s": 43.75
   }
  ],
  "chosen_offset_from_research_estimate_m": 1.25
 },
 "start_line_lap_m": -21.25,
 "grid": {
  "pole_side": "right",
  "boxes": 30,
  "first_fronts_lap_m": [
   -23.5,
   -31.38,
   -39.5,
   -47.38
  ],
  "lateral_centres_m": {
   "left": 3.0,
   "right": -3.625
  },
  "start_line_note": "solid transverse line ahead of the first box; see start_line_lap_m"
 },
 "elevation": {
  "samples": 6987,
  "smoothing_budget_rms_m": 0.04,
  "raw_minus_smoothed_rms_m": 0.04027343293812133,
  "max_abs_m": 0.18246630109820217,
  "vertical_g_at_300kmh": {
   "min": -1.6179972536422693,
   "max": 1.7228396588066757
  }
 },
 "bank": {
  "samples": 2329,
  "noise_estimate_pct": 0.714553490727646,
  "range_pct": [
   -6.944851147399407,
   8.09926181481417
  ]
 },
 "dtm_2022_vs_1m_profile": {
  "mean_m": -0.009958331978772385,
  "median_abs_m": 0.04025581249586474,
  "p95_abs_m": 0.11754634833058636,
  "worst": [
   {
    "lap_m": 260.0141255289994,
    "diff_m": 0.29713252669993273
   },
   {
    "lap_m": 262.0142341869148,
    "diff_m": 0.2764180943475265
   },
   {
    "lap_m": 264.01434284483014,
    "diff_m": 0.2605185315019083
   },
   {
    "lap_m": 258.01401687108404,
    "diff_m": 0.25072663307887644
   },
   {
    "lap_m": 256.01390821316863,
    "diff_m": 0.2129652179618018
   },
   {
    "lap_m": 255.01385388421096,
    "diff_m": 0.20947769124800408
   },
   {
    "lap_m": 266.01445150274554,
    "diff_m": 0.20236903366592407
   },
   {
    "lap_m": 1040.0565021159975,
    "diff_m": -0.19680756737650307
   }
  ]
 }
}
```
