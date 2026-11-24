using F1.Core.Data;
using F1.Core.Race;
using F1.Core.Sim;
using F1.Game.Core;
using F1.Game.Session;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.UI;

/// <summary>
/// Main menu: pick a car (team and driver), tyres and ghost for a Time Trial at Albert Park, with the chosen car on a
/// turntable in the car-select studio. Keys: Up/Down change team, Left/Right change driver, 1-3 tyres, G ghost,
/// Enter start, Esc quit. With --autotest=menushot --out=DIR it saves a screenshot and quits.
/// </summary>
public partial class MainMenu : Node3D
{
    private const string TrackId = "albert_park";
    private static readonly string[] Compounds = { "soft", "medium", "hard" };

    private List<TeamData> _teams = null!;
    private int _team, _seat, _compound;
    private bool _classic;
    private readonly Button[] _categoryButtons = new Button[2];
    private readonly int[] _lastTeam = new int[2];
    private Panel _carList = null!, _options = null!;
    private bool _ghost = true;
    private CarModel? _car;
    private DriverData? _displayedDriver;
    private Node3D _turntable = null!;
    private Studio _studio = null!;
    private Dictionary<string, TrackRecord> _records = new();

    private readonly List<Button> _teamButtons = new();
    private readonly Button[] _driverButtons = new Button[2];
    private readonly List<Button> _tyreButtons = new();
    private CheckButton _ghostToggle = null!;
    private Label _teamName = null!, _teamInfo = null!, _best = null!;
    private readonly ColorRect[] _statBars = new ColorRect[4];
    private int _shotFrames = -1;
    /// <summary>Turntable angle that points the nose to the lower right, away from the menu.</summary>
    private const float ShowAngle = 2.75f;

    public override void _Ready()
    {
        _studio = new Studio(this);
        _turntable = new Node3D { Name = "Turntable", Rotation = new Vector3(0, ShowAngle, 0) };
        AddChild(_turntable);
        var cam = new Camera3D { Name = "Camera", Fov = 30, Near = 0.05f, Far = 500, Current = true };
        AddChild(cam);
        cam.GlobalPosition = new Vector3(-6.61f, 4.53f, -10.53f);
        cam.LookAt(new Vector3(1.23f, -0.25f, -1.0f), Vector3.Up); // car sits right of centre, clear of the menu

        var cfg = App.Instance.Session;
        string id = App.Instance.Arg("team") ?? cfg.TeamId;
        _classic = App.Instance.ClassicCars.Any(t => t.Id == id);
        _teams = _classic ? App.Instance.ClassicCars : App.Instance.Teams.Teams;
        _team = Math.Max(0, _teams.FindIndex(t => t.Id == id));
        _seat = Math.Clamp(cfg.Seat, 0, 1);
        _compound = Math.Max(0, Array.IndexOf(Compounds, cfg.Compound));
        _ghost = cfg.ShowGhost;
        _records = Records.Load();

        BuildUi();
        PopulateCars();
        Select(_team);
        if (App.Instance.Arg("autotest") == "menushot") _shotFrames = 0;
    }

    // ---- UI --------------------------------------------------------------------------------------------------------

    private static readonly Color Ink = new(0.07f, 0.08f, 0.12f);
    private static readonly Color Muted = new(0.38f, 0.4f, 0.46f);
    private static readonly Color Card = new(1f, 1f, 1f, 0.82f);
    private static readonly Color Accent = new(0.88f, 0.02f, 0.0f);

    private static StyleBoxFlat Box(Color bg, Color? border = null, int borderW = 0, int radius = 6) => new()
    {
        BgColor = bg, BorderColor = border ?? bg, BorderWidthLeft = borderW, BorderWidthRight = borderW, BorderWidthTop = borderW,
        BorderWidthBottom = borderW, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius, ContentMarginLeft = 14, ContentMarginRight = 14,
    };

    private static Label Text(Control parent, string text, int size, Color color, Vector2 pos, Vector2 sizePx, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, Position = pos, Size = sizePx, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        parent.AddChild(l);
        return l;
    }

    private static Button MakeButton(Control parent, string text, Vector2 pos, Vector2 size, int fontSize = 22)
    {
        var b = new Button { Text = text, Position = pos, Size = size, FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
        b.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(b);
        return b;
    }

    private static void Style(Button b, Color bg, Color fg, Color? border = null, int borderW = 0)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
            b.AddThemeStyleboxOverride(state, Box(state == "hover" ? bg.Lightened(0.06f) : bg, border, borderW));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(state, fg);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "Menu" };
        AddChild(layer);
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(root);

        // title
        Text(root, "LOW-POLY F1", 22, Accent, new Vector2(64, 40), new Vector2(600, 30));
        Text(root, "TIME TRIAL", 64, Ink, new Vector2(60, 66), new Vector2(800, 76));
        var teamsFile = App.Instance.Teams;
        Text(root, $"{App.Instance.Track.Name.ToUpperInvariant()}  ·  MELBOURNE  ·  {teamsFile.Event.ToUpperInvariant()}", 20, Muted, new Vector2(64, 140), new Vector2(1100, 30));

        // team list
        _carList = new Panel { Position = new Vector2(60, 196) };
        _carList.AddThemeStyleboxOverride("panel", Box(Card, radius: 10));
        root.AddChild(_carList);
        string[] categories = { "2024 Cars", "Classic Cars" };
        for (int i = 0; i < categories.Length; i++)
        {
            bool classic = i == 1;
            var tab = MakeButton(_carList, categories[i], new Vector2(14 + i * 258, 12), new Vector2(252, 44), 20);
            tab.Alignment = HorizontalAlignment.Center;
            tab.TooltipText = "Switch car category (Tab)";
            tab.Pressed += () => ChangeCategory(classic);
            _categoryButtons[i] = tab;
        }

        // driver, tyres, ghost
        var opts = _options = new Panel { Position = new Vector2(60, 892), Size = new Vector2(540, 150) };
        opts.AddThemeStyleboxOverride("panel", Box(Card, radius: 10));
        root.AddChild(opts);
        for (int s = 0; s < 2; s++)
        {
            int seat = s;
            _driverButtons[s] = MakeButton(opts, "", new Vector2(14 + s * 260, 14), new Vector2(252, 52), 20);
            _driverButtons[s].Pressed += () => { _seat = seat; Refresh(); };
        }
        for (int c = 0; c < Compounds.Length; c++)
        {
            int ci = c;
            var b = MakeButton(opts, TyreCompound.ById(Compounds[c]).Name.ToUpperInvariant(), new Vector2(14 + c * 128, 82), new Vector2(120, 52), 18);
            b.Alignment = HorizontalAlignment.Center;
            b.Pressed += () => { _compound = ci; Refresh(); };
            _tyreButtons.Add(b);
        }
        _ghostToggle = new CheckButton { Text = "GHOST", Position = new Vector2(402, 82), Size = new Vector2(124, 52), FocusMode = Control.FocusModeEnum.None, ButtonPressed = _ghost };
        _ghostToggle.AddThemeFontSizeOverride("font_size", 18);
        _ghostToggle.AddThemeColorOverride("font_color", Ink);
        _ghostToggle.AddThemeColorOverride("font_pressed_color", Ink);
        _ghostToggle.AddThemeColorOverride("font_hover_color", Ink);
        _ghostToggle.Toggled += on => { _ghost = on; };
        opts.AddChild(_ghostToggle);

        // selected car card, start and quit
        var info = new Panel { Position = new Vector2(1320, 650), Size = new Vector2(540, 270) };
        info.AddThemeStyleboxOverride("panel", Box(Card, radius: 10));
        root.AddChild(info);
        _teamName = Text(info, "", 36, Ink, new Vector2(24, 12), new Vector2(500, 46));
        _teamInfo = Text(info, "", 18, Muted, new Vector2(24, 56), new Vector2(500, 28));
        string[] stats = { "POWER", "DOWNFORCE", "TOP SPEED", "TYRE LIFE" };
        for (int i = 0; i < stats.Length; i++)
        {
            Text(info, stats[i], 15, Muted, new Vector2(24, 96 + i * 30), new Vector2(150, 24));
            info.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.08f), Position = new Vector2(176, 104 + i * 30), Size = new Vector2(330, 10) });
            _statBars[i] = new ColorRect { Position = new Vector2(176, 104 + i * 30), Size = new Vector2(0, 10) };
            info.AddChild(_statBars[i]);
        }
        _best = Text(info, "", 18, Ink, new Vector2(24, 222), new Vector2(500, 32));

        var start = MakeButton(root, "START TIME TRIAL", new Vector2(1320, 936), new Vector2(540, 84), 32);
        start.Alignment = HorizontalAlignment.Center;
        Style(start, Accent, Colors.White);
        start.Pressed += StartSession;
        var quit = MakeButton(root, "QUIT", new Vector2(1740, 40), new Vector2(120, 46), 18);
        quit.Alignment = HorizontalAlignment.Center;
        Style(quit, new Color(1, 1, 1, 0.7f), Ink);
        quit.Pressed += () => App.Instance.Quit();
    }

    // ---- selection -------------------------------------------------------------------------------------------------

    private void ChangeCategory(bool classic)
    {
        if (_classic == classic) return;
        _lastTeam[_classic ? 1 : 0] = _team;
        _classic = classic;
        _teams = classic ? App.Instance.ClassicCars : App.Instance.Teams.Teams;
        _seat = 0;
        PopulateCars();
        Select(_lastTeam[classic ? 1 : 0]);
    }

    private void PopulateCars()
    {
        foreach (var button in _teamButtons)
        {
            _carList.RemoveChild(button);
            button.QueueFree();
        }
        _teamButtons.Clear();
        for (int i = 0; i < _categoryButtons.Length; i++)
        {
            bool active = (i == 1) == _classic;
            Style(_categoryButtons[i], active ? Ink : Colors.Transparent, active ? Colors.White : Ink);
        }
        _carList.Size = new Vector2(540, 78 + 59 * _teams.Count);
        _options.Position = _carList.Position + new Vector2(0, _carList.Size.Y + 16);
        for (int i = 0; i < _teams.Count; i++)
        {
            int index = i;
            var t = _teams[i];
            var b = MakeButton(_carList, "", new Vector2(14, 68 + i * 59), new Vector2(512, 54));
            var chip = new ColorRect { Color = Units.Hex(t.Primary), Position = new Vector2(12, 10), Size = new Vector2(14, 36), MouseFilter = Control.MouseFilterEnum.Ignore };
            var chip2 = new ColorRect { Color = Units.Hex(t.Livery.Sidepod ?? t.Secondary), Position = new Vector2(28, 10), Size = new Vector2(8, 36), MouseFilter = Control.MouseFilterEnum.Ignore };
            b.AddChild(chip); b.AddChild(chip2);
            var name = Text(b, t.Name, 24, Ink, new Vector2(52, 4), new Vector2(330, 48));
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            var pu = Text(b, t.ModelYear?.ToString() ?? t.PowerUnit.ToUpperInvariant(), 14, Muted, new Vector2(330, 4), new Vector2(170, 48), HorizontalAlignment.Right);
            pu.MouseFilter = Control.MouseFilterEnum.Ignore;
            b.Pressed += () => Select(index);
            _teamButtons.Add(b);
        }
    }

    private void Select(int index)
    {
        _team = (index + _teams.Count) % _teams.Count;
        _studio.LightForTeam(_teams[_team].Id);
        _seat = Math.Clamp(_seat, 0, _teams[_team].Drivers.Count - 1);
        Refresh();
    }

    private void Refresh()
    {
        var team = _teams[_team];
        var driver = team.Drivers[_seat];
        for (int i = 0; i < _teamButtons.Count; i++)
            Style(_teamButtons[i], i == _team ? Colors.White : new Color(1, 1, 1, 0), Ink, i == _team ? Accent : null, i == _team ? 3 : 0);
        for (int s = 0; s < 2; s++)
        {
            var d = s < team.Drivers.Count ? team.Drivers[s] : null;
            _driverButtons[s].Visible = d != null;
            if (d == null) continue;
            _driverButtons[s].Text = $"#{d.Number}  {d.LastName.ToUpperInvariant()}";
            Style(_driverButtons[s], s == _seat ? Ink : new Color(0, 0, 0, 0.06f), s == _seat ? Colors.White : Ink);
        }
        for (int c = 0; c < _tyreButtons.Count; c++)
        {
            var col = HudStyle.CompoundColor(Compounds[c]);
            Style(_tyreButtons[c], c == _compound ? col : new Color(0, 0, 0, 0.06f), c == _compound && col.Luminance < 0.7f ? Colors.White : Ink, col, 3);
        }

        _teamName.Text = team.Name;
        _teamInfo.Text = $"#{driver.Number} {driver.FullName}  ·  {team.PowerUnit}";
        var p = team.Car;
        float[] values =
        {
            Stat(p.Power, 0.985, 1.01), Stat(p.Downforce, 0.95, 1.005), Stat(2 - p.Drag, 0.995, 1.03), Stat(2 - p.TyreWear, 0.96, 1.09),
        };
        var bar = Units.Hex(team.Primary).Luminance > 0.85f ? Units.Hex(team.Secondary) : Units.Hex(team.Primary);
        for (int i = 0; i < 4; i++) { _statBars[i].Size = new Vector2(330 * values[i], 10); _statBars[i].Color = bar; }
        _best.Text = _records.TryGetValue(TrackId, out var rec) && rec.BestLap > 0
            ? $"PERSONAL BEST  {LapTimer.Format(rec.BestLap)}  ({App.Instance.AllCars.FirstOrDefault(t => t.Id == rec.TeamId)?.Name ?? rec.TeamId})"
            : "PERSONAL BEST  —  no lap yet";

        if (_displayedDriver == driver) return;
        _displayedDriver = driver;
        _turntable.Rotation = new Vector3(0, _classic ? -0.20f : ShowAngle, 0);
        _car?.QueueFree();
        _car = new CarModel { Name = "Car" };
        _turntable.AddChild(_car);
        _car.Build(team, driver);
        _car.SetShowroomView(false);
    }

    private static float Stat(double v, double lo, double hi) => (float)Math.Clamp(0.15 + 0.85 * (v - lo) / (hi - lo), 0.08, 1);

    private void StartSession()
    {
        var cfg = App.Instance.Session;
        App.Instance.Session = new SessionConfig
        {
            Mode = SessionMode.TimeTrial, TeamId = _teams[_team].Id, Seat = _seat, Compound = Compounds[_compound],
            ShowGhost = _ghost, Setup = cfg.Setup,
        };
        App.Instance.ChangeScene("res://scenes/TimeTrial.tscn");
    }

    // ---- per frame -------------------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_shotFrames < 0)
        {
            _turntable.RotateY((float)delta * 0.25f);
            return;
        }
        if (++_shotFrames >= 24)
        {
            string dir = App.Instance.Arg("out") ?? ProjectSettings.GlobalizePath("user://shots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "main_menu.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"saved {path}");
            App.Instance.Quit(0);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.Tab: ChangeCategory(!_classic); break;
            case Key.Up: Select(_team - 1); break;
            case Key.Down: Select(_team + 1); break;
            case Key.Left: _seat = 0; Refresh(); break;
            case Key.Right: _seat = Math.Min(1, _teams[_team].Drivers.Count - 1); Refresh(); break;
            case Key.Key1: case Key.Key2: case Key.Key3: _compound = (int)(k.Keycode - Key.Key1); Refresh(); break;
            case Key.G: _ghostToggle.ButtonPressed = !_ghostToggle.ButtonPressed; break;
            case Key.Enter: case Key.KpEnter: StartSession(); break;
            case Key.Escape: App.Instance.Quit(); break;
        }
    }
}
