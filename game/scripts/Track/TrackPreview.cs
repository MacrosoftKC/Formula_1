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
        bool spa = ts.Data.Id == DataFiles.SpaId;
        _shots.Add(spa ? ("04_pouhon", Track(corners[9].EntryS - 75, 0, 2, -1)) : ("04_lakeside", Track(2800, 0, 1.05f, -2)));
        var pitCamera = Track(ts.Length - 120, -14, 4, -10, 20);
        if (spa)
        {
            double u = ts.Data.PitLane.Boxes[1];
            pitCamera = new Transform3D(Basis.Identity, ts.PitPoint(u, 1.5, 1.6f));
            pitCamera = pitCamera.LookingAt(ts.PitPoint(u + 40, 0, 1.2f), Vector3.Up);
        }
        _shots.Add(("05_pitlane", pitCamera));
        _shots.Add(("06_gantry", Track(ts.Length - 60, 2, 2.5f, 4)));
        _shots.Add(spa ? ("07_raidillon", Track(corners[1].EntryS - 80, 0, 1.3f)) :
            ("07_north_tip_skyline", Track(corners[5].ExitS + 30, 0, 1.2f, 2, 30)));
        _shots.Add(("08_t13_t14", Track(corners[12].EntryS - 80, 0, 12, -12)));
        if (spa)
        {
            _shots.Add(("10_raidillon_onboard", Track(corners[2].ApexS - 20, 0, 1.05f, -1)));
            _shots.Add(("11_bus_stop_kerbs", Track(corners[17].EntryS - 40, -1.5, 3.5f, -9)));
        }
        var over = new Transform3D(Basis.Identity, new Vector3(220, 2300, 900));
        over = over.LookingAt(new Vector3(220, 0, -380), Vector3.Forward);
        if (spa)
        {
            var (lo, hi) = Builders.NatureBuilder.Bounds(ts, 0);
            var middle = (lo + hi) * .5f;
            var aim = new Vector3(middle.X, 0, -middle.Y);
            over = new Transform3D(Basis.Identity, aim + new Vector3(0, 1950, 250)).LookingAt(aim, Vector3.Forward);
        }
        _shots.Add(("09_overview", over));

        _out = App.Instance.Arg("out");
        if (App.Instance.Arg("hide") is { } hidden)
            foreach (var child in _world.GetChildren().OfType<Node3D>())
                if (hidden.Split(',').Contains(child.Name.ToString())) child.Visible = false;
        if (App.Instance.Arg("autotest") == "trackbench") StartBench();
        else if (App.Instance.Arg("autotest") == "trackshots") { _shot = 0; _wait = 0; _cam.GlobalTransform = _shots[0].T; }
        else
        {
            _cam.GlobalTransform = _shots[1].T;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    // --autotest=trackbench: holds an onboard camera at evenly spaced points around the lap and reports the CPU and GPU
    // render time at each, so a slow stretch of scenery can be found. --hide=Name,Name hides top-level world nodes.
    private int _benchPoint = -1, _benchFrame;
    private double _benchClock;
    private readonly List<double> _benchCpu = new(), _benchGpu = new();
    private static int BenchPoints => int.TryParse(App.Instance.Arg("points"), out int n) ? n : 28;
    private const int BenchFrames = 10;

    private void StartBench()
    {
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        GD.Print("BENCH nodes: " + string.Join(", ", _world.GetChildren().Select(c => c.Name.ToString())));
        _benchPoint = 0;
        _benchFrame = 0;
        PlaceBenchCamera();
    }

    private void PlaceBenchCamera()
    {
        var ts = _world.Surface;
        _cam.GlobalTransform = ts.TrackTransform(ts.Length * _benchPoint / BenchPoints, 0, 1.1f);
    }

    private bool StepBench()
    {
        if (_benchPoint < 0) return false;
        var vp = GetViewport().GetViewportRid();
        if (++_benchFrame > 4)
        {
            _benchCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu());
            _benchGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
        }
        if (_benchFrame < BenchFrames) return true;
        var info = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        var prims = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        var ts = _world.Surface;
        double now = Time.GetTicksMsec();
        GD.Print($"BENCH s={ts.Length * _benchPoint / BenchPoints:0} cpu={_benchCpu.Average():0.0}ms gpu={_benchGpu.Average():0.0}ms " +
                 $"draws={info} prims={prims / 1000}k frame={(now - _benchClock) / BenchFrames:0}ms " +
                 $"process={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.0}ms physics={Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.0}ms");
        _benchClock = now;
        _benchCpu.Clear();
        _benchGpu.Clear();
        _benchFrame = 0;
        if (++_benchPoint >= BenchPoints) { App.Instance.Quit(0); return true; }
        PlaceBenchCamera();
        return true;
    }

    public override void _Process(double delta)
    {
        if (StepBench()) return;
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
