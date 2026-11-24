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
    private Studio _studio = null!;
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
        _studio = new Studio(this);

        _cam = new Camera3D { Name = "Camera", Fov = 50, Near = 0.05f, Far = 500, Current = true };
        AddChild(_cam);

        _teams = App.Instance.AllCars.ToList();
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
        _studio.LightForTeam(team.Id);
        _model = new CarModel { Name = "Car" };
        AddChild(_model);
        _model.Build(team, team.Drivers[0]);
        string category = App.Instance.ClassicCars.Contains(team) ? "Classic Cars" : "2024 Cars";
        GetWindow().Title = $"{category} — {team.Name} #{team.Drivers[0].Number}";
    }

    private void Aim((string Name, Vector3 Eye, Vector3 Look, float Fov) a)
    {
        bool onboard = a.Name == "onboard";
        bool studioModel = _teams[_team].Id is "red_bull" or TeamData.W11;
        _cam.Projection = Camera3D.ProjectionType.Perspective;
        if (studioModel && a.Name == "front34")
            a = ("front34", new(-5.65f, 2.36f, -6.0f), new(0.20f, 0.38f, -0.60f), 25);
        if (_teams[_team].Id == TeamData.W11 && a.Name == "front34")
            a = ("front34", new(-4.45f, 1.92f, -5.55f), new(0.02f, 0.36f, -0.52f), 27.5f);
        if (onboard && _model != null) a = (a.Name, a.Eye + _model.OnboardOffset, a.Look + _model.OnboardOffset, a.Fov);
        _cam.Fov = a.Fov > 0 ? a.Fov : App.Instance.Settings.FovFor(CameraMode.Cockpit);
        _cam.Near = onboard ? 0.03f : 0.05f;
        _model?.SetShowroomView(onboard);
        _studio.Floor.Visible = a.Name != "bottom";
        if (studioModel && a.Name is "front" or "back" or "left" or "right" or "top" or "bottom")
        {
            var centre = new Vector3(0, 0.63f, -0.345f);
            var axis = a.Name switch
            {
                "left" => Vector3.Left, "right" => Vector3.Right, "front" => Vector3.Forward,
                "back" => Vector3.Back, "top" => Vector3.Up, _ => Vector3.Down,
            };
            var size = GetViewport().GetVisibleRect().Size;
            float width = axis.Z == 0 ? 6.35f : 2.45f;
            _cam.Projection = Camera3D.ProjectionType.Orthogonal;
            _cam.Size = Mathf.Max(a.Name is "top" or "bottom" ? 2.35f : 1.52f, width * size.Y / size.X);
            _cam.GlobalPosition = centre + axis * 12;
            _cam.LookAt(centre, a.Name == "top" ? Vector3.Right : a.Name == "bottom" ? Vector3.Left : Vector3.Up);
            return;
        }
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
        _model?.SetShowroomView(false);
        _studio.Floor.Visible = true;
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
