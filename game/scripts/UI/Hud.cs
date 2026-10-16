using F1.Core.Race;
using F1.Core.Sim;
using F1.Game.Core;
using F1.Game.Controls;
using F1.Game.Track;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.UI;

/// <summary>Shared HUD colours and helpers.</summary>
public static class HudStyle
{
    public static readonly Color Panel = new(0.06f, 0.06f, 0.09f, 0.78f);
    public static readonly Color PanelLight = new(0.14f, 0.14f, 0.18f, 0.85f);
    public static readonly Color Red = new(0.88f, 0.02f, 0.0f);
    public static readonly Color Purple = new(0.67f, 0.25f, 0.95f);
    public static readonly Color Green = new(0.2f, 0.85f, 0.3f);
    public static readonly Color Yellow = new(1f, 0.82f, 0.1f);
    public static readonly Color Text = new(0.97f, 0.97f, 0.98f);
    public static readonly Color Dim = new(0.65f, 0.66f, 0.7f);

    public static Label Label(Control parent, string text, int size, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Text);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 1);
        parent.AddChild(l);
        return l;
    }

    public static Panel Box(Control parent, Vector2 pos, Vector2 size, Color? color = null)
    {
        var p = new Panel { Position = pos, Size = size };
        var sb = new StyleBoxFlat { BgColor = color ?? Panel, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        p.AddThemeStyleboxOverride("panel", sb);
        parent.AddChild(p);
        return p;
    }

    public static Color CompoundColor(string id) => id switch
    {
        "soft" => new Color(0.91f, 0.15f, 0.17f),
        "medium" => new Color(0.95f, 0.76f, 0f),
        "hard" => new Color(0.95f, 0.95f, 0.95f),
        "intermediate" => new Color(0.22f, 0.71f, 0.29f),
        "wet" => new Color(0f, 0.4f, 0.68f),
        _ => Colors.White,
    };

    public static Color TempColor(double t, TyreCompound c)
    {
        if (t < c.OptimalMinC - 25) return new Color(0.2f, 0.45f, 1f);
        if (t < c.OptimalMinC) return new Color(0.3f, 0.85f, 0.95f);
        if (t <= c.OptimalMaxC) return Green;
        if (t <= c.OptimalMaxC + 15) return Yellow;
        return new Color(1f, 0.25f, 0.15f);
    }
}

/// <summary>Driving HUD for the player car.</summary>
public partial class Hud : CanvasLayer
{
    public Car? Car { get; set; }
    public TrackSurface? Track { get; set; }
    public LapTimer? Timer { get; set; }
    /// <summary>Returns the live delta to the reference lap (null if none).</summary>
    public Func<double?>? Delta { get; set; }
    public Func<double>? Clock { get; set; }
    public IEnumerable<Car> OtherCars { get; set; } = Array.Empty<Car>();
    /// <summary>Session best sectors (for purple sectors); defaults to the player's own.</summary>
    public double[]? SessionBestSectors { get; set; }

    private Control _root = null!;
    private Label _speed = null!, _gear = null!, _unit = null!, _lapTime = null!, _last = null!, _best = null!, _delta = null!;
    private Label _ers = null!, _fuel = null!, _drs = null!, _limiter = null!, _message = null!, _lapLabel = null!, _compound = null!;
    private readonly Label[] _sectors = new Label[3];
    private readonly Panel[] _sectorBoxes = new Panel[3];
    private readonly ColorRect[] _tyres = new ColorRect[4];
    private readonly Label[] _tyreText = new Label[4];
    private ColorRect _rpm = null!, _throttle = null!, _brake = null!, _ersBar = null!;
    private Minimap _minimap = null!;
    private double _messageUntil;
    private bool _visible = true;

    public Control Root => _root;

    public override void _Ready()
    {
        Layer = 10;
        _root = new Control { Name = "HudRoot" };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_root);
        BuildCluster();
        BuildTiming();
        BuildTyres();
        _message = HudStyle.Label(_root, "", 30, HudStyle.Text, HorizontalAlignment.Center);
        _message.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _message.Position = new Vector2(-400, 150);
        _message.Size = new Vector2(800, 50);
        _minimap = new Minimap { Track = Track, Position = new Vector2(24, 0), Size = new Vector2(250, 250) };
        _minimap.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _minimap.Position = new Vector2(24, -274);
        _root.AddChild(_minimap);
        _visible = App.Instance.Settings.ShowHud;
        _root.Visible = _visible;
    }

    private void BuildCluster()
    {
        var c = new Control { Name = "Cluster" };
        c.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        c.Position = new Vector2(-380, -200);
        c.Size = new Vector2(356, 176);
        _root.AddChild(c);
        HudStyle.Box(c, Vector2.Zero, new Vector2(356, 176));
        var rpmBg = new ColorRect { Color = new Color(1, 1, 1, 0.08f), Position = new Vector2(12, 10), Size = new Vector2(332, 10) };
        c.AddChild(rpmBg);
        _rpm = new ColorRect { Color = HudStyle.Green, Position = new Vector2(12, 10), Size = new Vector2(0, 10) };
        c.AddChild(_rpm);
        _speed = HudStyle.Label(c, "0", 64, null, HorizontalAlignment.Right);
        _speed.Position = new Vector2(12, 24); _speed.Size = new Vector2(170, 70);
        _unit = HudStyle.Label(c, "KM/H", 16, HudStyle.Dim);
        _unit.Position = new Vector2(186, 62); _unit.Size = new Vector2(60, 24);
        _gear = HudStyle.Label(c, "N", 72, HudStyle.Text, HorizontalAlignment.Center);
        _gear.Position = new Vector2(250, 22); _gear.Size = new Vector2(90, 80);
        var thrBg = new ColorRect { Color = new Color(1, 1, 1, 0.08f), Position = new Vector2(12, 104), Size = new Vector2(160, 8) };
        c.AddChild(thrBg);
        _throttle = new ColorRect { Color = HudStyle.Green, Position = new Vector2(12, 104), Size = new Vector2(0, 8) };
        c.AddChild(_throttle);
        var brBg = new ColorRect { Color = new Color(1, 1, 1, 0.08f), Position = new Vector2(184, 104), Size = new Vector2(160, 8) };
        c.AddChild(brBg);
        _brake = new ColorRect { Color = HudStyle.Red, Position = new Vector2(184, 104), Size = new Vector2(0, 8) };
        c.AddChild(_brake);
        var ersBg = new ColorRect { Color = new Color(1, 1, 1, 0.08f), Position = new Vector2(12, 122), Size = new Vector2(332, 8) };
        c.AddChild(ersBg);
        _ersBar = new ColorRect { Color = HudStyle.Yellow, Position = new Vector2(12, 122), Size = new Vector2(0, 8) };
        c.AddChild(_ersBar);
        _ers = HudStyle.Label(c, "ERS", 16, HudStyle.Dim);
        _ers.Position = new Vector2(12, 134); _ers.Size = new Vector2(150, 30);
        _fuel = HudStyle.Label(c, "FUEL", 16, HudStyle.Dim);
        _fuel.Position = new Vector2(150, 134); _fuel.Size = new Vector2(110, 30);
        _drs = HudStyle.Label(c, "DRS", 18, HudStyle.Dim, HorizontalAlignment.Center);
        _drs.Position = new Vector2(270, 134); _drs.Size = new Vector2(74, 30);
        _limiter = HudStyle.Label(c, "PIT LIMITER", 20, HudStyle.Yellow, HorizontalAlignment.Center);
        _limiter.Position = new Vector2(0, -34); _limiter.Size = new Vector2(356, 30);
    }

    private void BuildTiming()
    {
        var t = new Control { Name = "Timing", Position = new Vector2(24, 24), Size = new Vector2(330, 190) };
        _root.AddChild(t);
        HudStyle.Box(t, Vector2.Zero, new Vector2(330, 190));
        _lapLabel = HudStyle.Label(t, "LAP", 18, HudStyle.Dim);
        _lapLabel.Position = new Vector2(14, 8); _lapLabel.Size = new Vector2(300, 24);
        _lapTime = HudStyle.Label(t, "0:00.000", 40);
        _lapTime.Position = new Vector2(14, 30); _lapTime.Size = new Vector2(220, 48);
        _delta = HudStyle.Label(t, "", 24, HudStyle.Green, HorizontalAlignment.Right);
        _delta.Position = new Vector2(200, 38); _delta.Size = new Vector2(116, 36);
        for (int i = 0; i < 3; i++)
        {
            _sectorBoxes[i] = HudStyle.Box(t, new Vector2(14 + i * 102, 84), new Vector2(96, 34), HudStyle.PanelLight);
            _sectors[i] = HudStyle.Label(_sectorBoxes[i], $"S{i + 1}", 18, null, HorizontalAlignment.Center);
            _sectors[i].Size = new Vector2(96, 34);
        }
        _last = HudStyle.Label(t, "LAST  --:--.---", 18, HudStyle.Dim);
        _last.Position = new Vector2(14, 124); _last.Size = new Vector2(300, 28);
        _best = HudStyle.Label(t, "BEST  --:--.---", 18, HudStyle.Dim);
        _best.Position = new Vector2(14, 152); _best.Size = new Vector2(300, 28);
    }

    private void BuildTyres()
    {
        var t = new Control { Name = "Tyres" };
        t.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        t.Position = new Vector2(-190, 24);
        t.Size = new Vector2(166, 170);
        _root.AddChild(t);
        HudStyle.Box(t, Vector2.Zero, new Vector2(166, 170));
        _compound = HudStyle.Label(t, "S", 22, null, HorizontalAlignment.Center);
        _compound.Position = new Vector2(63, 66); _compound.Size = new Vector2(40, 36);
        Vector2[] pos = { new(14, 14), new(112, 14), new(14, 94), new(112, 94) };
        for (int i = 0; i < 4; i++)
        {
            _tyres[i] = new ColorRect { Position = pos[i], Size = new Vector2(40, 62), Color = HudStyle.Green };
            t.AddChild(_tyres[i]);
            _tyreText[i] = HudStyle.Label(t, "", 13, HudStyle.Text, HorizontalAlignment.Center);
            _tyreText[i].Position = pos[i] + new Vector2(-10, 20); _tyreText[i].Size = new Vector2(60, 22);
        }
    }

    public void ShowMessage(string text, double seconds = 3, Color? color = null)
    {
        _message.Text = text;
        _message.AddThemeColorOverride("font_color", color ?? HudStyle.Text);
        _messageUntil = (Clock?.Invoke() ?? 0) + seconds;
    }

    public override void _Process(double delta)
    {
        if (Godot.Input.IsActionJustPressed(InputService.HudToggle)) { _visible = !_visible; _root.Visible = _visible; }
        if (Car == null || !IsInstanceValid(Car)) return;
        double now = Clock?.Invoke() ?? 0;
        bool metric = App.Instance.Settings.MetricUnits;

        _speed.Text = metric ? $"{Car.SpeedKmh:0}" : $"{Car.SpeedKmh * 0.621371f:0}";
        _unit.Text = metric ? "KM/H" : "MPH";
        _gear.Text = Car.Gear switch { 0 => "N", < 0 => "R", _ => Car.Gear.ToString() };
        float rpm01 = Math.Clamp((Car.Rpm - 4000) / (12500f - 4000f), 0, 1);
        _rpm.Size = new Vector2(332 * rpm01, 10);
        _rpm.Color = Car.Rpm > 11700 ? HudStyle.Purple : Car.Rpm > 10800 ? HudStyle.Red : HudStyle.Green;
        _throttle.Size = new Vector2(160 * Car.ThrottleApplied, 8);
        _brake.Size = new Vector2(160 * Car.BrakeApplied, 8);
        _ersBar.Size = new Vector2(332 * (float)Car.PU.StoreFraction, 8);
        _ersBar.Color = Car.PU.OvertakeActive ? HudStyle.Purple : Car.PU.MguKPowerW > 0 ? HudStyle.Yellow : new Color(0.3f, 0.75f, 1f);
        _ers.Text = $"ERS {Car.PU.StoreFraction * 100:0}% {(Car.PU.OvertakeActive ? "OVERTAKE" : Car.PU.Mode.ToString().ToUpperInvariant())}";
        _fuel.Text = $"FUEL {Car.PU.FuelKg:0.0}kg {Car.PU.Mix.ToString().ToUpperInvariant()[..3]}";
        _drs.Text = "DRS";
        _drs.AddThemeColorOverride("font_color", Car.DrsOpen ? HudStyle.Green : Car.DrsAllowed ? HudStyle.Text : new Color(1, 1, 1, 0.25f));
        _limiter.Visible = Car.PitLimiter;

        if (Timer != null)
        {
            _lapLabel.Text = Timer.Started ? $"LAP {Timer.LapsCompleted + 1}{(Timer.CurrentLapValid ? "" : "   INVALID")}" : "OUT LAP";
            _lapLabel.AddThemeColorOverride("font_color", Timer.CurrentLapValid ? HudStyle.Dim : HudStyle.Red);
            _lapTime.Text = LapTimer.Format(Timer.CurrentLapTime(now));
            var d = Delta?.Invoke();
            _delta.Text = d.HasValue ? LapTimer.Format(d.Value, sign: true) : "";
            _delta.AddThemeColorOverride("font_color", d < 0 ? HudStyle.Green : HudStyle.Red);
            _last.Text = $"LAST  {LapTimer.Format(Timer.LastLapTime)}";
            _best.Text = $"BEST  {LapTimer.Format(Timer.BestLap?.Time)}";
            var sessionBest = SessionBestSectors ?? Timer.BestSectors;
            for (int i = 0; i < 3; i++)
            {
                double? time = i < Timer.CurrentSector ? Timer.CurrentSectors[i] : (Timer.Laps.Count > 0 && Timer.CurrentSector == 0 ? Timer.Laps[^1].Sectors[i] : null);
                _sectors[i].Text = time.HasValue ? LapTimer.Format(time) : $"S{i + 1}";
                var sb = (StyleBoxFlat)_sectorBoxes[i].GetThemeStylebox("panel");
                Color col = HudStyle.PanelLight;
                if (time.HasValue)
                {
                    if (time <= sessionBest[i] + 1e-6) col = HudStyle.Purple;
                    else if (time <= Timer.BestSectors[i] + 1e-6) col = HudStyle.Green.Darkened(0.2f);
                    else col = HudStyle.Yellow.Darkened(0.35f);
                }
                sb.BgColor = col;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            var tyre = Car.Tyres[i];
            _tyres[i].Color = HudStyle.TempColor(tyre.SurfaceTemp, tyre.Compound).Lerp(new Color(0.25f, 0.25f, 0.25f), (float)Math.Clamp(tyre.Wear, 0, 1) * 0.6f);
            _tyreText[i].Text = $"{tyre.SurfaceTemp:0}°\n{(1 - tyre.Wear) * 100:0}%";
        }
        _compound.Text = Car.Tyres[0].Compound.Name[..1];
        _compound.AddThemeColorOverride("font_color", HudStyle.CompoundColor(Car.Tyres[0].Compound.Id));

        if (now > _messageUntil) _message.Text = "";
        _minimap.Track ??= Track;
        _minimap.Player = Car;
        _minimap.Others = OtherCars;
        _minimap.Visible = App.Instance.Settings.ShowMinimap;
        _minimap.QueueRedraw();
    }
}

/// <summary>Track outline with car positions.</summary>
public partial class Minimap : Control
{
    public TrackSurface? Track { get; set; }
    public Car? Player { get; set; }
    public IEnumerable<Car> Others { get; set; } = Array.Empty<Car>();
    private Vector2[]? _outline;
    private Vector2 _min, _scale;

    private Vector2 Map(Vector2 p) => (p - _min) * _scale + new Vector2(10, 10);

    public override void _Draw()
    {
        if (Track == null) return;
        if (_outline == null)
        {
            var pts = Track.Data.Centerline;
            float minX = (float)pts.Min(p => p.X), maxX = (float)pts.Max(p => p.X);
            float minY = (float)pts.Min(p => -p.Y), maxY = (float)pts.Max(p => -p.Y);
            float span = Math.Max(maxX - minX, maxY - minY);
            _min = new Vector2(minX, minY);
            _scale = Vector2.One * (Size.X - 20) / span;
            _outline = pts.Where((_, i) => i % 4 == 0).Select(p => Map(new Vector2((float)p.X, (float)-p.Y))).ToArray();
        }
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.35f));
        DrawPolyline(_outline.Append(_outline[0]).ToArray(), new Color(1, 1, 1, 0.85f), 3f, true);
        foreach (var c in Others)
        {
            if (!IsInstanceValid(c) || c == Player) continue;
            var p = Map(new Vector2(c.GlobalPosition.X, c.GlobalPosition.Z));
            DrawCircle(p, 4.5f, Units.Hex(c.Team.Primary));
        }
        if (Player != null && IsInstanceValid(Player))
        {
            var p = Map(new Vector2(Player.GlobalPosition.X, Player.GlobalPosition.Z));
            DrawCircle(p, 7f, Colors.White);
            DrawCircle(p, 5f, HudStyle.Red);
        }
    }
}
