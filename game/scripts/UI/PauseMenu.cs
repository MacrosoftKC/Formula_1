using F1.Game.Core;
using Godot;

namespace F1.Game.UI;

/// <summary>In-session pause overlay: resume, restart, quit to menu.</summary>
public partial class PauseMenu : CanvasLayer
{
    public event Action? RestartRequested;
    public event Action? QuitRequested;
    private Control _root = null!;
    private Button _resume = null!;

    public override void _Ready()
    {
        Layer = 50;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);
        var panel = HudStyle.Box(_root, Vector2.Zero, new Vector2(420, 330), new Color(0.35f, 0.04f, 0.08f, 0.95f));
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Position = new Vector2(-210, -165);
        var title = HudStyle.Label(panel, "PAUSED", 34, null, HorizontalAlignment.Center);
        title.Position = new Vector2(0, 16); title.Size = new Vector2(420, 50);
        var box = new VBoxContainer { Position = new Vector2(60, 90), Size = new Vector2(300, 220) };
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);
        _resume = MenuButton(box, "RESUME", Toggle);
        MenuButton(box, "RESTART", () => { Hide(); RestartRequested?.Invoke(); });
        MenuButton(box, "QUIT TO MENU", () => { Hide(); QuitRequested?.Invoke(); });
        _root.Visible = false;
    }

    public static Button MenuButton(Control parent, string text, Action onPressed)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(300, 52), FocusMode = Control.FocusModeEnum.All };
        b.AddThemeFontSizeOverride("font_size", 22);
        var normal = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.15f, 0.95f), ContentMarginLeft = 16 };
        var hover = new StyleBoxFlat { BgColor = new Color(0.95f, 0.95f, 0.95f), ContentMarginLeft = 16 };
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("focus", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeColorOverride("font_hover_color", new Color(0.1f, 0.1f, 0.12f));
        b.AddThemeColorOverride("font_focus_color", new Color(0.1f, 0.1f, 0.12f));
        b.AddThemeColorOverride("font_pressed_color", new Color(0.1f, 0.1f, 0.12f));
        b.Pressed += onPressed;
        parent.AddChild(b);
        return b;
    }

    public void Toggle()
    {
        bool show = !_root.Visible;
        _root.Visible = show;
        GetTree().Paused = show;
        if (show) _resume.GrabFocus();
        Godot.Input.MouseMode = show ? Godot.Input.MouseModeEnum.Visible : Godot.Input.MouseModeEnum.Visible;
    }

    public new void Hide()
    {
        _root.Visible = false;
        GetTree().Paused = false;
    }
}
