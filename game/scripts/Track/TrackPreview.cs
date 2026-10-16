using F1.Game.Core;
using Godot;

namespace F1.Game.Track;

/// <summary>
/// Free-fly preview of the circuit (WASD + mouse, Shift = fast). With --autotest=trackshots --out=DIR it renders a
/// fixed set of viewpoints to PNG files and quits.
/// </summary>
public partial class TrackPreview : Node3D
{
    private TrackWorld _world = null!;
    private Camera3D _cam = null!;
    private float _yaw, _pitch;
    private readonly List<(string Name, Transform3D T)> _shots = new();
    private int _shot = -1, _wait;
    private string? _out;

    public override void _Ready()
    {
        _world = new TrackWorld { Name = "TrackWorld" };
        AddChild(_world);
        _cam = new Camera3D { Name = "Camera", Fov = 70, Far = 9000, Near = 0.05f, Current = true };
        AddChild(_cam);

        var ts = _world.Surface;
        Transform3D Track(double s, double d, float h, float pitchDeg = 0, float yawDeg = 0)
        {
            var t = ts.TrackTransform(s, d, h);
            t.Basis = t.Basis * new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)) * new Basis(Vector3.Right, Mathf.DegToRad(pitchDeg));
            return t;
        }
        var corners = ts.Data.Corners;
        _shots.Add(("01_grid_cockpit", Track(ts.Length - 40, -3.2, 1.05f, -2)));
        _shots.Add(("02_t1_approach", Track(corners[0].EntryS - 120, 0, 1.05f, -2)));
        _shots.Add(("03_t3_from_above", Track(corners[2].EntryS - 90, 0, 14, -14)));
        _shots.Add(("04_lakeside", Track(2800, 0, 1.05f, -2)));
        _shots.Add(("05_pitlane", Track(ts.Length - 120, -14, 4, -10, 20)));
        _shots.Add(("06_gantry", Track(ts.Length - 60, 2, 2.5f, 4)));
        _shots.Add(("07_north_tip_skyline", Track(corners[5].ExitS + 30, 0, 1.2f, 2, 30)));
        _shots.Add(("08_t13_t14", Track(corners[12].EntryS - 80, 0, 12, -12)));
        var over = new Transform3D(Basis.Identity, new Vector3(220, 2300, 900));
        over = over.LookingAt(new Vector3(220, 0, -380), Vector3.Forward);
        _shots.Add(("09_overview", over));

        _out = App.Instance.Arg("out");
        if (App.Instance.Arg("autotest") == "trackshots") { _shot = 0; _wait = 0; _cam.GlobalTransform = _shots[0].T; }
        else
        {
            _cam.GlobalTransform = _shots[1].T;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Process(double delta)
    {
        if (_shot >= 0)
        {
            // give the renderer a few frames (shadows, MultiMesh uploads) before grabbing
            if (++_wait < 12) return;
            var img = GetViewport().GetTexture().GetImage();
            string dir = _out ?? ProjectSettings.GlobalizePath("user://shots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, _shots[_shot].Name + ".png");
            img.SavePng(path);
            GD.Print($"saved {path}");
            _shot++;
            _wait = 0;
            if (_shot >= _shots.Count) { App.Instance.Quit(0); return; }
            _cam.GlobalTransform = _shots[_shot].T;
            return;
        }

        float speed = Input.IsKeyPressed(Key.Shift) ? 120f : 25f;
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) move -= _cam.GlobalBasis.Z;
        if (Input.IsKeyPressed(Key.S)) move += _cam.GlobalBasis.Z;
        if (Input.IsKeyPressed(Key.A)) move -= _cam.GlobalBasis.X;
        if (Input.IsKeyPressed(Key.D)) move += _cam.GlobalBasis.X;
        if (Input.IsKeyPressed(Key.E)) move += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) move -= Vector3.Up;
        _cam.GlobalPosition += move * speed * (float)delta;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= mm.Relative.X * 0.003f;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.003f, -1.5f, 1.5f);
            _cam.GlobalBasis = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);
        }
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) App.Instance.Quit();
    }
}
