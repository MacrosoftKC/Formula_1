using F1.Core.Data;
using F1.Game.Camera;
using F1.Game.Core;
using Godot;

namespace F1.Game.Vehicle;

/// <summary>
/// Livery preview / car-select look: one car in a light-grey studio. Drag (or arrow keys) to orbit, wheel to zoom,
/// Tab / Shift+Tab to change team, D to toggle DRS, Esc to quit.
/// With --autotest=carshots --out=DIR it renders every team (or just --team=ID) from fixed angles to PNG and quits.
/// </summary>
public partial class CarShowroom : Node3D
{
    private Camera3D _cam = null!;
    private MeshInstance3D _floor = null!;
    private CarModel? _model;
    private List<TeamData> _teams = null!;
    private int _team;
    private float _yaw = -2.4f, _pitch = -0.25f, _dist = 7.5f;
    private bool _drag;

    private static readonly (string Name, Vector3 Eye, Vector3 Look, float Fov)[] Angles =
    {
        // fitted to the reference car-select renders: close, low, fairly wide lens
        ("front34", new(-2.38f, 1.27f, -3.98f), new(0.3f, 0.32f, -0.55f), 43),
        ("rear34", new(3.4f, 1.9f, 4.2f), new(0, 0.45f, 0.4f), 40),
        // model-sheet views (long lenses, nearly orthographic)
        ("front", new(0, 1.1f, -14f), new(0, 0.5f, 0), 11),
        ("back", new(0, 1.3f, 14f), new(0, 0.55f, 0), 11),
        ("left", new(-16f, 0.7f, 0), new(0, 0.5f, 0), 13),
        ("right", new(16f, 0.7f, 0), new(0, 0.5f, 0), 13),
        ("top", new(0, 20f, 0.001f), new(0, 0, 0), 18),
        ("bottom", new(0, -20f, 0.001f), new(0, 0, 0), 18),
        // the in-game cockpit camera (FOV 0 = the player's cockpit FOV setting)
        ("onboard", CameraRig.CockpitEye, CameraRig.CockpitEye + new Vector3(0, Mathf.Sin(Mathf.DegToRad(CameraRig.CockpitPitchDeg)), -Mathf.Cos(Mathf.DegToRad(CameraRig.CockpitPitchDeg))), 0),
    };
    private (string Name, Vector3 Eye, Vector3 Look, float Fov)[] _angles = Angles;
    private int _shotTeam = -1, _shotAngle, _wait;
    private string? _out;

    public override void _Ready()
    {
        _floor = Studio.Build(this);

        _cam = new Camera3D { Name = "Camera", Fov = 50, Near = 0.05f, Far = 500, Current = true };
        AddChild(_cam);

        _teams = App.Instance.Teams.Teams;
        if (App.Instance.Arg("team") is { } id)
        {
            int i = _teams.FindIndex(t => t.Id == id);
            if (i >= 0) _teams = new List<TeamData> { _teams[i] };
        }
        _out = App.Instance.Arg("out");
        if (App.Instance.Arg("angle") is { } only && Angles.Any(a => a.Name == only))
            _angles = Angles.Where(a => a.Name == only).ToArray();
        if (App.Instance.Arg("autotest") == "carshots")
        {
            _shotTeam = 0; _shotAngle = 0;
            ShowTeam(0);
            Aim(_angles[0]);
        }
        else
        {
            ShowTeam(0);
            Orbit();
        }
    }

    private void ShowTeam(int index)
    {
        _team = (index + _teams.Count) % _teams.Count;
        _model?.QueueFree();
        var team = _teams[_team];
        _model = new CarModel { Name = "Car" };
        AddChild(_model);
        _model.Build(team, team.Drivers[0]);
        GetWindow().Title = $"Car showroom — {team.Name} #{team.Drivers[0].Number}";
    }

    private void Aim((string Name, Vector3 Eye, Vector3 Look, float Fov) a)
    {
        bool onboard = a.Name == "onboard";
        _cam.Fov = a.Fov > 0 ? a.Fov : App.Instance.Settings.FovFor(CameraMode.Cockpit);
        _cam.Near = onboard ? 0.03f : 0.05f;
        if (_model != null) _model.DriverHead.Visible = !onboard; // as in the real cockpit camera
        _floor.Visible = a.Name != "bottom";
        _cam.GlobalPosition = a.Eye;
        // straight down/up views put the nose on the left, like a model sheet
        _cam.LookAt(a.Look, Mathf.Abs(a.Eye.X) + Mathf.Abs(a.Eye.Z) < 0.01f ? (a.Eye.Y > 0 ? Vector3.Right : Vector3.Left) : Vector3.Up);
    }

    private void Orbit()
    {
        var look = new Vector3(0, 0.45f, 0);
        var dir = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch) * Vector3.Back;
        _cam.Fov = 50;
        _cam.Near = 0.05f;
        if (_model != null) _model.DriverHead.Visible = true;
        _floor.Visible = true;
        _cam.GlobalPosition = look + dir * _dist;
        _cam.LookAt(look, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        if (_shotTeam >= 0)
        {
            // a few frames for shadows and label glyphs to settle before grabbing
            if (++_wait < 10) return;
            var img = GetViewport().GetTexture().GetImage();
            string dir = _out ?? ProjectSettings.GlobalizePath("user://carshots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"{_teams[_shotTeam].Id}_{_angles[_shotAngle].Name}.png");
            img.SavePng(path);
            GD.Print($"saved {path}");
            _wait = 0;
            if (++_shotAngle >= _angles.Length)
            {
                _shotAngle = 0;
                if (++_shotTeam >= _teams.Count) { App.Instance.Quit(0); return; }
                ShowTeam(_shotTeam);
            }
            Aim(_angles[_shotAngle]);
            return;
        }

        float dt = (float)delta;
        if (Input.IsKeyPressed(Key.Left)) _yaw += dt * 1.5f;
        if (Input.IsKeyPressed(Key.Right)) _yaw -= dt * 1.5f;
        if (Input.IsKeyPressed(Key.Up)) _pitch = Mathf.Clamp(_pitch - dt, -1.45f, 0.05f);
        if (Input.IsKeyPressed(Key.Down)) _pitch = Mathf.Clamp(_pitch + dt, -1.45f, 0.05f);
        Orbit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _drag = mb.Pressed;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _dist = Mathf.Max(2.5f, _dist * 0.9f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _dist = Mathf.Min(30f, _dist * 1.1f);
                break;
            case InputEventMouseMotion mm when _drag:
                _yaw -= mm.Relative.X * 0.006f;
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.006f, -1.45f, 0.05f);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Tab } k:
                ShowTeam(_team + (k.ShiftPressed ? -1 : 1));
                break;
            case InputEventKey { Pressed: true, Keycode: Key.D }:
                _model?.SetDrsOpen(!_model.DrsOpenPreview);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                App.Instance.Quit();
                break;
        }
    }
}
