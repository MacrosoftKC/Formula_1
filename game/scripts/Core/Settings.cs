using System.Text.Json;
using Godot;

namespace F1.Game.Core;

public enum CameraMode { Cockpit, Helmet, TCam, Nose, ChaseNear, ChaseFar, TV }

/// <summary>All user preferences. Persisted as JSON in user://settings.json.</summary>
public sealed class GameSettings
{
    // ---- controls ----
    public bool ManualTransmission { get; set; }
    public bool ManualClutch { get; set; }
    public float SteeringSensitivity { get; set; } = 1.0f;
    public float SteeringLinearity { get; set; } = 1.2f;
    public float SteeringDeadzone { get; set; } = 0.04f;
    public float SteeringSaturation { get; set; } = 1.0f;
    /// <summary>Wheel controllers: physical rotation (degrees) mapped to full lock.</summary>
    public float WheelRotation { get; set; } = 360f;
    public float ThrottleDeadzone { get; set; } = 0.02f;
    public float BrakeDeadzone { get; set; } = 0.02f;
    public bool InvertThrottleAxis { get; set; }
    public bool InvertBrakeAxis { get; set; }
    public float KeyboardSteerSpeed { get; set; } = 2.6f;
    public float KeyboardSteerReturn { get; set; } = 4.5f;
    public bool Vibration { get; set; } = true;

    // ---- assists ----
    public string TractionControl { get; set; } = "Medium"; // Off | Medium | Full
    public bool Abs { get; set; } = true;
    public bool BrakingAssist { get; set; }
    public bool SteeringAssist { get; set; } = true;
    public string RacingLine { get; set; } = "Corners";     // Off | Corners | Full
    public bool PitAssist { get; set; } = true;
    public bool ErsAssist { get; set; } = true;
    public bool DrsAssist { get; set; }

    // ---- camera ----
    public CameraMode DefaultCamera { get; set; } = CameraMode.Cockpit;
    public Dictionary<string, float> Fov { get; set; } = new()
    {
        [nameof(CameraMode.Cockpit)] = 78, [nameof(CameraMode.Helmet)] = 74, [nameof(CameraMode.TCam)] = 70,
        [nameof(CameraMode.Nose)] = 72, [nameof(CameraMode.ChaseNear)] = 70, [nameof(CameraMode.ChaseFar)] = 68,
        [nameof(CameraMode.TV)] = 40,
    };
    public float ChaseDistance { get; set; } = 6.2f;
    public float ChaseHeight { get; set; } = 1.8f;
    public float ChasePitch { get; set; } = -7f;
    public float SeatHeight { get; set; }
    public float SeatForward { get; set; }
    public float HeadMovement { get; set; } = 0.6f;
    public float CameraShake { get; set; } = 0.5f;
    public bool Mirrors { get; set; } = true;
    public bool ShowSteeringWheel { get; set; } = true;

    // ---- gameplay / HUD ----
    public bool MetricUnits { get; set; } = true;
    public bool ShowHud { get; set; } = true;
    public bool ShowMinimap { get; set; } = true;
    public string DriverName { get; set; } = "Player";

    // ---- graphics ----
    public string WindowMode { get; set; } = "Windowed"; // Windowed | Fullscreen | Borderless
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public bool VSync { get; set; } = true;
    public string Antialiasing { get; set; } = "MSAA 2x"; // Off | FXAA | MSAA 2x | MSAA 4x | TAA
    public string Shadows { get; set; } = "High";           // Off | Low | Medium | High
    public float RenderScale { get; set; } = 1.0f;
    public string CrowdDensity { get; set; } = "High";
    public int FpsCap { get; set; }
    public string MirrorQuality { get; set; } = "Medium";

    // ---- audio ----
    public float MasterVolume { get; set; } = 0.8f;
    public float EngineVolume { get; set; } = 0.9f;
    public float EffectsVolume { get; set; } = 0.8f;
    public float AmbientVolume { get; set; } = 0.6f;
    public float RadioVolume { get; set; } = 0.8f;

    public float FovFor(CameraMode mode) => Fov.TryGetValue(mode.ToString(), out var f) ? f : 70f;
}

/// <summary>Loads, saves and applies <see cref="GameSettings"/>.</summary>
public static class SettingsStore
{
    public const string Path = "user://settings.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static GameSettings Load()
    {
        try
        {
            if (FileAccess.FileExists(Path))
            {
                var s = JsonSerializer.Deserialize<GameSettings>(FileAccess.GetFileAsString(Path), Options);
                if (s != null) return s;
            }
        }
        catch (Exception e) { GD.PushWarning($"Settings unreadable, using defaults: {e.Message}"); }
        return new GameSettings();
    }

    public static void Save(GameSettings s)
    {
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(s, Options));
    }

    /// <summary>Applies display/graphics/audio settings to the engine.</summary>
    public static void Apply(GameSettings s, Viewport root)
    {
        if (DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetMode(s.WindowMode switch
        {
            "Fullscreen" => DisplayServer.WindowMode.ExclusiveFullscreen,
            "Borderless" => DisplayServer.WindowMode.Fullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        });
        DisplayServer.WindowSetVsyncMode(s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = s.FpsCap;
        root.Scaling3DScale = Math.Clamp(s.RenderScale, 0.5f, 1.0f);
        root.Msaa3D = s.Antialiasing switch { "MSAA 2x" => Viewport.Msaa.Msaa2X, "MSAA 4x" => Viewport.Msaa.Msaa4X, _ => Viewport.Msaa.Disabled };
        root.ScreenSpaceAA = s.Antialiasing == "FXAA" ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        root.UseTaa = s.Antialiasing == "TAA";
        RenderingServer.DirectionalShadowAtlasSetSize(s.Shadows switch { "Low" => 1024, "Medium" => 2048, "Off" => 512, _ => 4096 }, true);

        int master = AudioServer.GetBusIndex("Master");
        AudioServer.SetBusVolumeDb(master, Mathf.LinearToDb(Math.Max(0.0001f, s.MasterVolume)));
    }
}
