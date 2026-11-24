# Formula_1
Ngiwwwwwwwwwng
wa pa nahuman

## Running

Double-click `run.bat` (or run it in a terminal). It builds the C# and starts the game with the Godot 4.7 .NET in `.tools\`.
`run.bat editor` opens the project in the Godot editor instead.

If `.tools\` is missing, download Godot 4.7.2 .NET (mono, win64) from https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable and unzip it into `.tools\`.

The game opens on the main menu: pick your car (team and driver), tyres and ghost, then **Start Time Trial** at Albert Park
(keys: Tab category, Up/Down car, Left/Right driver, 1-3 tyres, G ghost, Enter start).

Extra options go after `--` on the Godot command line, e.g. `Godot --path game -- --scene=cars`:

- `--scene=cars` car showroom (drag to orbit, mouse wheel to zoom, Tab to change team, D for DRS)
- `--scene=tt --team=red_bull` straight into a Time Trial with that team
- `--autotest=carshots --team=red_bull --out=C:\shots` renders the model-sheet views of a car to PNG files

The Red Bull RB20 mesh, livery and animation pivots are in `game/art/cars/rb20/RB20.scn`. The 2024 Cars category uses the season-opening driver lineup in `game/data/teams_2024.json`. Classic Cars includes Lewis Hamilton's 2020 Mercedes-AMG W11 (#44). Select it in the garage or use `--team=mercedes_w11` in the showroom or time trial. Its model and crank-synchronised Monza audio are in `game/art/cars/w11/`.
