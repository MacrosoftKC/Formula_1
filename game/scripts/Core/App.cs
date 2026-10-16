using F1.Core.Data;
using F1.Core.Track;
using F1.Game.Controls;
using Godot;

namespace F1.Game.Core;

/// <summary>
/// Autoloaded singleton: owns the loaded data files, user settings, the session about to be started,
/// input-device tracking and scene switching.
/// </summary>
public partial class App : Node
{
    public static App Instance { get; private set; } = null!;

    public TeamsFile Teams { get; private set; } = null!;
    public RegulationsFile Regulations { get; private set; } = null!;
    public GameSettings Settings { get; private set; } = null!;
    public SessionConfig Session { get; set; } = new();

    private TrackData? _track;
    public TrackData Track => _track ??= DataFiles.LoadTrack();

    /// <summary>Command-line arguments after "--", e.g. --autotest=boot.</summary>
    public Dictionary<string, string> Args { get; } = new();

    /// <summary>True when the most recent driving input came from the keyboard (enables steering smoothing).</summary>
    public bool LastInputWasKeyboard { get; private set; } = true;
    /// <summary>True when the active joypad looks like a steering wheel.</summary>
    public bool ActiveJoypadIsWheel { get; private set; }

    private static readonly string[] WheelNames = { "wheel", "g29", "g920", "g923", "g27", "fanatec", "thrustmaster", "t300", "t150", "moza", "simagic", "simucube" };

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string a = arg.TrimStart('-');
            int eq = a.IndexOf('=');
            if (eq > 0) Args[a[..eq]] = a[(eq + 1)..];
            else Args[a] = "true";
        }
        Teams = DataFiles.LoadTeams();
        Regulations = DataFiles.LoadRegulations();
        Settings = SettingsStore.Load();
        InputService.Initialize();
    }

    public override void _Ready()
    {
        SettingsStore.Apply(Settings, GetViewport());
    }

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true }:
                LastInputWasKeyboard = true;
                break;
            case InputEventJoypadMotion m when Math.Abs(m.AxisValue) > 0.3f:
            case InputEventJoypadButton { Pressed: true }:
                LastInputWasKeyboard = false;
                string name = Godot.Input.GetJoyName(e.Device).ToLowerInvariant();
                ActiveJoypadIsWheel = WheelNames.Any(name.Contains);
                break;
        }
    }

    public string? Arg(string name) => Args.TryGetValue(name, out var v) ? v : null;

    public void SaveSettings() => SettingsStore.Save(Settings);

    public void ChangeScene(string path) => CallDeferred(MethodName.DeferredChangeScene, path);

    private void DeferredChangeScene(string path)
    {
        GetTree().Paused = false;
        var err = GetTree().ChangeSceneToFile(path);
        if (err != Error.Ok) GD.PushError($"Could not load scene {path}: {err}");
    }

    public void Quit(int exitCode = 0) => GetTree().Quit(exitCode);
}
