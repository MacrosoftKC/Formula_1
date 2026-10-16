using F1.Core.Race;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.UI;

/// <summary>
/// The live screen on the player's steering wheel (like the reference cockpit shot): RPM LED strip, gear, speed,
/// lap delta, ERS state and lap counter. Rendered into a small SubViewport and mapped onto the wheel.
/// </summary>
public partial class SteeringWheelDisplay : Node3D
{
    private Car _car = null!;
    private SubViewport _vp = null!;
    private readonly List<ColorRect> _leds = new();
    private Label _gear = null!, _speed = null!, _delta = null!, _ers = null!, _lap = null!, _mode = null!;
    public Func<string>? DeltaText { get; set; }
    public Func<string>? LapText { get; set; }

    public static SteeringWheelDisplay Attach(Car car)
    {
        var d = new SteeringWheelDisplay { Name = "WheelScreen", _car = car };
        car.Model!.WheelDisplay.AddChild(d);
        return d;
    }

    public override void _Ready()
    {
        _vp = new SubViewport { Size = new Vector2I(320, 160), TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Disable3D = true };
        AddChild(_vp);
        var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f), Size = new Vector2(320, 160) };
        _vp.AddChild(bg);
        Color[] ledColors = { new(0.1f, 0.9f, 0.2f), new(0.95f, 0.15f, 0.1f), new(0.3f, 0.4f, 1f) };
        for (int i = 0; i < 15; i++)
        {
            var c = i < 5 ? ledColors[0] : i < 10 ? ledColors[1] : ledColors[2];
            var led = new ColorRect { Color = c, Position = new Vector2(10 + i * 20, 6), Size = new Vector2(16, 12) };
            _vp.AddChild(led);
            _leds.Add(led);
        }
        _gear = MakeLabel(new Vector2(130, 24), 84, HorizontalAlignment.Center, 60);
        _speed = MakeLabel(new Vector2(8, 34), 30, HorizontalAlignment.Left, 110);
        _mode = MakeLabel(new Vector2(8, 74), 20, HorizontalAlignment.Left, 120);
        _ers = MakeLabel(new Vector2(8, 104), 20, HorizontalAlignment.Left, 120);
        _delta = MakeLabel(new Vector2(200, 34), 28, HorizontalAlignment.Right, 112);
        _lap = MakeLabel(new Vector2(200, 104), 20, HorizontalAlignment.Right, 112);

        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.15f, 0.075f) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoTexture = _vp.GetTexture() };
        quad.MaterialOverride = mat;
        AddChild(quad);
    }

    private Label MakeLabel(Vector2 pos, int size, HorizontalAlignment align, float width)
    {
        var l = new Label { Position = pos, Size = new Vector2(width, size + 8), HorizontalAlignment = align };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Colors.White);
        _vp.AddChild(l);
        return l;
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_car)) return;
        float rpm = _car.Rpm;
        int lit = (int)Math.Clamp((rpm - 9000) / (11900 - 9000) * 15, 0, 15);
        bool flash = rpm > 11800 && (Time.GetTicksMsec() / 80) % 2 == 0;
        for (int i = 0; i < _leds.Count; i++) _leds[i].Visible = flash || i < lit;
        _gear.Text = _car.Gear switch { 0 => "N", < 0 => "R", _ => _car.Gear.ToString() };
        _speed.Text = $"{_car.SpeedKmh:0}";
        _mode.Text = _car.PitLimiter ? "PIT LIM" : _car.DrsOpen ? "DRS OPEN" : _car.DrsAllowed ? "DRS" : $"BB {_car.Setup.BrakeBias:0}";
        _ers.Text = $"ERS {_car.PU.StoreFraction * 100:0}% {_car.PU.Mode.ToString().ToUpperInvariant()[..3]}";
        _delta.Text = DeltaText?.Invoke() ?? "";
        _lap.Text = LapText?.Invoke() ?? "";
        _delta.AddThemeColorOverride("font_color", _delta.Text.StartsWith("-") ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.35f, 0.3f));
    }
}
