# Formula_1
Ngiwwwwwwwwwng
wa pa nahuman

## Running

Double-click `run.bat` (or run it in a terminal). It builds the C# and starts the game with the Godot 4.7 .NET in `.tools\`.
`run.bat editor` opens the project in the Godot editor instead.

If `.tools\` is missing, download Godot 4.7.2 .NET (mono, win64) from https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable and unzip it into `.tools\`.

The game opens in Time Trial at Spa-Francorchamps. **Circuits** switches between Spa and Albert Park. **Garage** selects a 2024 or classic car. Pick the driver, tyres and personal-best ghost on the **Time Trial** page, then select **Start Time Trial**. Records and ghosts are stored separately for each circuit.

Use Q/E or the controller shoulder buttons to change page. In Circuits, Left/Right changes the track. Up/Down selects a car, Tab changes collection, and Left/Right selects the driver on the other pages. Keys 1–3 select tyres, G toggles the ghost, and Enter starts. On a controller use the D-pad, accept button and Start.

Spa uses one metre per world unit. It is rebuilt by `tools/SpaSurvey` from the 2023 Walloon orthophoto (road edges, kerbs, grid, start and finish lines) and the 2021-2022 Walloon LiDAR terrain model (elevation, bank, terrain, tree heights). The midline of the painted track limits measures 6,987.4 m on plan and 6,994.6 m over the road surface, with 102.2 m of relief; the published Grand Prix length is 7,004 m and the gap is reported, not rescaled. Sources, licences, the acceptance tests and what is measured versus assumed are in [`tracks/spa/`](tracks/spa/uncertainty_report.md) (see also [the circuit notes](game/data/tracks/spa/SOURCES.md)).

To rebuild Spa: `python tools/SpaSurvey/fetch.py`, then `edges.py`, `build.py`, `glb.py`, `checks.py --lap-log <autotest log>` and `report.py` in the same folder.

Extra options go after `--` on the Godot command line, e.g. `Godot --path game -- --scene=cars`:

- `--scene=tt --track=spa --team=red_bull` starts a Spa time trial. Use `--track=albert_park` for Melbourne.
- `--scene=cars` car showroom (drag to orbit, mouse wheel to zoom, Tab to change team, D for DRS)
- `--scene=tt --team=red_bull` straight into a Time Trial with that team
- `--autotest=carshots --team=red_bull --out=C:\shots` renders the model-sheet views of a car to PNG files
- `--autotest=trackbench --track=spa --points=14` reports render time and draw calls at points around the lap
- Time-trial autotest helpers: `--perf` (frame timings), `--trace=850-1250` (car state between two track distances), `--linemargin=0.6` (AI rides the kerbs), `--flatout=700-1500` (AI holds full throttle through a stretch)

The Red Bull RB20 mesh, livery and animation pivots are in `game/art/cars/rb20/RB20.scn`. The 2024 Cars category uses the season-opening driver lineup in `game/data/teams_2024.json`. Classic Cars includes Lewis Hamilton's 2020 Mercedes-AMG W11 (#44). Select it in the garage or use `--team=mercedes_w11` in the showroom or time trial. Its model and crank-synchronised audio (cut from an Assetto Corsa W11 hotlap of Albert Park by `tools/AudioPreview/w11/make_banks.py`) are in `game/art/cars/w11/`.

Choose **Classic Cars → Ferrari F2004** to drive Michael Schumacher's 2004 #1 car. The open cockpit, grooved Bridgestones and fixed rear wing follow the supplied model sheet. The seven-speed gearbox drives the pitch of the Ferrari 053 V10, with acceleration, lift and shift samples taken from the supplied Jeddah lap. Use `--team=ferrari_f2004` for direct showroom or time-trial entry.
