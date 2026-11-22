# Formula_1
Ngiwwwwwwwwwng
wa pa nahuman

## Running

Double-click `run.bat` (or run it in a terminal). It builds the C# and starts the game with the Godot 4.7 .NET in `.tools\`.
`run.bat editor` opens the project in the Godot editor instead.

If `.tools\` is missing, download Godot 4.7.2 .NET (mono, win64) from https://github.com/godotengine/godot-builds/releases/tag/4.7.2-stable and unzip it into `.tools\`.

The game opens on the main menu: pick your car (team and driver), tyres and ghost, then **Start Time Trial** at Albert Park
(keys: Up/Down team, Left/Right driver, 1-3 tyres, G ghost, Enter start).

Extra options go after `--` on the Godot command line, e.g. `Godot --path game -- --scene=cars`:

- `--scene=cars` car showroom (drag to orbit, mouse wheel to zoom, Tab to change team, D for DRS)
- `--scene=tt --team=red_bull` straight into a Time Trial with that team
- `--autotest=carshots --team=red_bull --out=C:\shots` renders the model-sheet views of a car to PNG files

Liveries live in `game/data/teams_2025.json`; logo images can be added per team with `"decals"` (see the comment at the top of that file).
