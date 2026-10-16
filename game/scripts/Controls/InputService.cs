using System.Text.Json;
using Godot;

namespace F1.Game.Controls;

/// <summary>A driver action that can be bound to keys, gamepad buttons/axes or wheel axes.</summary>
public sealed record ActionDef(string Id, string Label, string Group, bool Analog, InputEvent[] Defaults);

/// <summary>Serialisable description of one binding.</summary>
public sealed class BindingDto
{
    public string Kind { get; set; } = "";   // key | joybutton | joyaxis | mouse
    public long Code { get; set; }           // keycode / button / axis / mouse button
    public float Sign { get; set; }          // axis direction
    public int Device { get; set; } = -1;    // -1 = any device
}

/// <summary>
/// Defines every in-car action, installs default bindings into Godot's InputMap and persists the player's
/// rebinds to user://controls.json.
/// </summary>
public static class InputService
{
    public const string Path = "user://controls.json";

    public const string Throttle = "throttle", Brake = "brake", SteerLeft = "steer_left", SteerRight = "steer_right";
    public const string Clutch = "clutch", ShiftUp = "shift_up", ShiftDown = "shift_down", Drs = "drs", PitLimiter = "pit_limiter";
    public const string Overtake = "overtake", ErsUp = "ers_mode_up", ErsDown = "ers_mode_down";
    public const string BiasFwd = "brake_bias_fwd", BiasRear = "brake_bias_rear", DiffUp = "diff_up", DiffDown = "diff_down";
    public const string MixUp = "fuel_mix_up", MixDown = "fuel_mix_down", EngBrakeUp = "engine_braking_up", EngBrakeDown = "engine_braking_down";
    public const string MfdToggle = "mfd_toggle", MfdUp = "mfd_up", MfdDown = "mfd_down", MfdLeft = "mfd_left", MfdRight = "mfd_right", MfdSelect = "mfd_select";
    public const string RadioBox = "radio_box", LookBack = "look_back", LookLeft = "look_left", LookRight = "look_right";
    public const string CameraNext = "camera_next", CameraPrev = "camera_prev", Pause = "pause", ResetCar = "reset_car", HudToggle = "hud_toggle";

    private static InputEvent K(Key k) => new InputEventKey { PhysicalKeycode = k };
    private static InputEvent J(JoyButton b) => new InputEventJoypadButton { ButtonIndex = b, Device = -1 };
    private static InputEvent A(JoyAxis a, float sign) => new InputEventJoypadMotion { Axis = a, AxisValue = sign, Device = -1 };

    public static readonly ActionDef[] Actions =
    {
        new(Throttle, "Throttle", "Driving", true, new[] { K(Key.W), K(Key.Up), A(JoyAxis.TriggerRight, 1) }),
        new(Brake, "Brake", "Driving", true, new[] { K(Key.S), K(Key.Down), A(JoyAxis.TriggerLeft, 1) }),
        new(SteerLeft, "Steer left", "Driving", true, new[] { K(Key.A), K(Key.Left), A(JoyAxis.LeftX, -1) }),
        new(SteerRight, "Steer right", "Driving", true, new[] { K(Key.D), K(Key.Right), A(JoyAxis.LeftX, 1) }),
        new(Clutch, "Clutch", "Driving", true, new[] { K(Key.K) }),
        new(ShiftUp, "Shift up", "Driving", false, new[] { K(Key.E), J(JoyButton.RightShoulder) }),
        new(ShiftDown, "Shift down", "Driving", false, new[] { K(Key.Q), J(JoyButton.LeftShoulder) }),
        new(Drs, "DRS", "Driving", false, new[] { K(Key.Space), J(JoyButton.Y) }),
        new(PitLimiter, "Pit limiter", "Driving", false, new[] { K(Key.L), J(JoyButton.B) }),
        new(Overtake, "Overtake (ERS boost)", "Power unit", false, new[] { K(Key.Shift), J(JoyButton.X) }),
        new(ErsUp, "ERS mode +", "Power unit", false, new[] { K(Key.R) }),
        new(ErsDown, "ERS mode −", "Power unit", false, new[] { K(Key.F) }),
        new(MixUp, "Fuel mix +", "Power unit", false, new[] { K(Key.Period) }),
        new(MixDown, "Fuel mix −", "Power unit", false, new[] { K(Key.Comma) }),
        new(EngBrakeUp, "Engine braking +", "Power unit", false, new[] { K(Key.Key0) }),
        new(EngBrakeDown, "Engine braking −", "Power unit", false, new[] { K(Key.Key9) }),
        new(BiasFwd, "Brake bias forward", "Car balance", false, new[] { K(Key.Bracketleft) }),
        new(BiasRear, "Brake bias rearward", "Car balance", false, new[] { K(Key.Bracketright) }),
        new(DiffUp, "Differential +", "Car balance", false, new[] { K(Key.Apostrophe) }),
        new(DiffDown, "Differential −", "Car balance", false, new[] { K(Key.Semicolon) }),
        new(MfdToggle, "Multi-function display", "MFD & radio", false, new[] { K(Key.M), J(JoyButton.DpadUp) }),
        new(MfdUp, "MFD up", "MFD & radio", false, new[] { K(Key.I) }),
        new(MfdDown, "MFD down", "MFD & radio", false, new[] { K(Key.J), J(JoyButton.DpadDown) }),
        new(MfdLeft, "MFD left", "MFD & radio", false, new[] { K(Key.U), J(JoyButton.DpadLeft) }),
        new(MfdRight, "MFD right", "MFD & radio", false, new[] { K(Key.O), J(JoyButton.DpadRight) }),
        new(MfdSelect, "MFD select", "MFD & radio", false, new[] { K(Key.Enter), J(JoyButton.A) }),
        new(RadioBox, "Request pit stop (box)", "MFD & radio", false, new[] { K(Key.P) }),
        new(LookBack, "Look back", "Camera", false, new[] { K(Key.V), J(JoyButton.RightStick) }),
        new(LookLeft, "Look left", "Camera", true, new[] { K(Key.Z), A(JoyAxis.RightX, -1) }),
        new(LookRight, "Look right", "Camera", true, new[] { K(Key.X), A(JoyAxis.RightX, 1) }),
        new(CameraNext, "Next camera", "Camera", false, new[] { K(Key.C), J(JoyButton.Back) }),
        new(CameraPrev, "Previous camera", "Camera", false, new[] { K(Key.B) }),
        new(HudToggle, "Toggle HUD", "Camera", false, new[] { K(Key.H) }),
        new(Pause, "Pause", "General", false, new[] { K(Key.Escape), J(JoyButton.Start) }),
        new(ResetCar, "Reset car (Time Trial)", "General", false, new[] { K(Key.Backspace) }),
    };

    /// <summary>Registers all actions with their defaults, then applies saved rebinds.</summary>
    public static void Initialize()
    {
        foreach (var a in Actions)
        {
            if (!InputMap.HasAction(a.Id)) InputMap.AddAction(a.Id, 0.08f);
            InputMap.ActionEraseEvents(a.Id);
            foreach (var e in a.Defaults) InputMap.ActionAddEvent(a.Id, (InputEvent)e.Duplicate());
        }
        LoadOverrides();
    }

    public static void ResetToDefaults()
    {
        foreach (var a in Actions)
        {
            InputMap.ActionEraseEvents(a.Id);
            foreach (var e in a.Defaults) InputMap.ActionAddEvent(a.Id, (InputEvent)e.Duplicate());
        }
        Save();
    }

    public static IReadOnlyList<InputEvent> Bindings(string action) => InputMap.ActionGetEvents(action).ToList();

    /// <summary>Replaces binding slot <paramref name="slot"/> of an action (adds it if the slot doesn't exist yet).</summary>
    public static void Rebind(string action, int slot, InputEvent e)
    {
        var events = InputMap.ActionGetEvents(action).ToList();
        if (slot < events.Count) events[slot] = e; else events.Add(e);
        InputMap.ActionEraseEvents(action);
        foreach (var ev in events) InputMap.ActionAddEvent(action, ev);
        Save();
    }

    public static void Unbind(string action, int slot)
    {
        var events = InputMap.ActionGetEvents(action).ToList();
        if (slot >= events.Count) return;
        events.RemoveAt(slot);
        InputMap.ActionEraseEvents(action);
        foreach (var ev in events) InputMap.ActionAddEvent(action, ev);
        Save();
    }

    /// <summary>Other actions already using this input (for conflict warnings in the rebinding UI).</summary>
    public static List<string> Conflicts(string action, InputEvent e)
    {
        var list = new List<string>();
        foreach (var a in Actions)
        {
            if (a.Id == action) continue;
            foreach (var other in InputMap.ActionGetEvents(a.Id))
                if (Same(other, e)) list.Add(a.Label);
        }
        return list;
    }

    private static bool Same(InputEvent a, InputEvent b) => (a, b) switch
    {
        (InputEventKey ka, InputEventKey kb) => ka.PhysicalKeycode == kb.PhysicalKeycode,
        (InputEventJoypadButton ja, InputEventJoypadButton jb) => ja.ButtonIndex == jb.ButtonIndex,
        (InputEventJoypadMotion ma, InputEventJoypadMotion mb) => ma.Axis == mb.Axis && Math.Sign(ma.AxisValue) == Math.Sign(mb.AxisValue),
        (InputEventMouseButton ba, InputEventMouseButton bb) => ba.ButtonIndex == bb.ButtonIndex,
        _ => false,
    };

    /// <summary>Human-readable label for a binding.</summary>
    public static string Describe(InputEvent e) => e switch
    {
        InputEventKey k => OS.GetKeycodeString(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventJoypadButton j => $"Pad {j.ButtonIndex}",
        InputEventJoypadMotion m => $"Axis {(int)m.Axis}{(m.AxisValue < 0 ? "−" : "+")}",
        InputEventMouseButton b => $"Mouse {b.ButtonIndex}",
        _ => e.AsText(),
    };

    private static BindingDto? ToDto(InputEvent e) => e switch
    {
        InputEventKey k => new BindingDto { Kind = "key", Code = (long)(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode) },
        InputEventJoypadButton j => new BindingDto { Kind = "joybutton", Code = (long)j.ButtonIndex, Device = j.Device },
        InputEventJoypadMotion m => new BindingDto { Kind = "joyaxis", Code = (long)m.Axis, Sign = Math.Sign(m.AxisValue), Device = m.Device },
        InputEventMouseButton b => new BindingDto { Kind = "mouse", Code = (long)b.ButtonIndex },
        _ => null,
    };

    private static InputEvent? FromDto(BindingDto d) => d.Kind switch
    {
        "key" => new InputEventKey { PhysicalKeycode = (Key)d.Code },
        "joybutton" => new InputEventJoypadButton { ButtonIndex = (JoyButton)d.Code, Device = d.Device },
        "joyaxis" => new InputEventJoypadMotion { Axis = (JoyAxis)d.Code, AxisValue = d.Sign, Device = d.Device },
        "mouse" => new InputEventMouseButton { ButtonIndex = (MouseButton)d.Code },
        _ => null,
    };

    public static void Save()
    {
        var map = Actions.ToDictionary(a => a.Id, a => InputMap.ActionGetEvents(a.Id).Select(ToDto).Where(x => x != null).ToList());
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void LoadOverrides()
    {
        if (!FileAccess.FileExists(Path)) return;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, List<BindingDto>>>(FileAccess.GetFileAsString(Path));
            if (map == null) return;
            foreach (var (action, list) in map)
            {
                if (!InputMap.HasAction(action)) continue;
                InputMap.ActionEraseEvents(action);
                foreach (var dto in list)
                    if (FromDto(dto) is { } e) InputMap.ActionAddEvent(action, e);
            }
        }
        catch (Exception ex) { GD.PushWarning($"controls.json unreadable, using defaults: {ex.Message}"); }
    }
}
