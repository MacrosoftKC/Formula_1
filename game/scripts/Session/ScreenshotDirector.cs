using F1.Game.Camera;
using F1.Game.Core;
using Godot;

namespace F1.Game.Session;

/// <summary>
/// Test helper: with --shots=DIR, switches through the camera modes at fixed session times and saves a PNG of each,
/// then quits. Used to check visuals against the reference images.
/// </summary>
public partial class ScreenshotDirector : Node
{
    public SessionBase Session { get; set; } = null!;
    public string Directory { get; set; } = "";
    public double StartAt { get; set; } = 9;
    private readonly (CameraMode Mode, string Name)[] _plan =
    {
        (CameraMode.Cockpit, "cam_cockpit"), (CameraMode.ChaseNear, "cam_chase_near"), (CameraMode.ChaseFar, "cam_chase_far"),
        (CameraMode.TCam, "cam_tcam"), (CameraMode.Helmet, "cam_helmet"), (CameraMode.Nose, "cam_nose"), (CameraMode.TV, "cam_tv"),
    };
    private int _index = -1;
    private double _next;
    private int _settle;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _next = StartAt;
    }

    public override void _Process(double delta)
    {
        if (Session.Time < _next) return;
        if (_index >= 0 && ++_settle < 8) return; // let the camera settle a few frames
        if (_index >= 0)
        {
            var img = GetViewport().GetTexture().GetImage();
            System.IO.Directory.CreateDirectory(Directory);
            string path = System.IO.Path.Combine(Directory, _plan[_index].Name + ".png");
            img.SavePng(path);
            GD.Print($"saved {path}");
        }
        _index++;
        _settle = 0;
        if (_index >= _plan.Length) { App.Instance.Quit(0); return; }
        Session.CameraRig.SetMode(_plan[_index].Mode);
        _next = Session.Time + 1.5;
    }
}
