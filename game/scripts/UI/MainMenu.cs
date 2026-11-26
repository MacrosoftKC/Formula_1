using F1.Core.Data;
using F1.Core.Race;
using F1.Core.Track;
using F1.Game.Core;
using F1.Game.Session;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.UI;

/// <summary>Time trial, circuit selection and garage share the live session configuration.</summary>
public partial class MainMenu : Node3D
{
    private enum Page { TimeTrial, Circuits, Garage }
    private static readonly string[] Compounds = { "soft", "medium", "hard" };
    private static readonly Color White = Color.FromHtml("#f9f6f7"), Muted = Color.FromHtml("#c7b9c5"),
        Panel = Color.FromHtml("#28212f"), Edge = Color.FromHtml("#514150"), Red = Color.FromHtml("#e9394c"),
        Deep = Color.FromHtml("#15121c");
    private readonly SystemFont _display = new() { FontNames = new[] { "Barlow Condensed", "Bahnschrift", "Arial" }, FontWeight = 740 };
    private readonly SystemFont _body = new() { FontNames = new[] { "Barlow", "Bahnschrift", "Segoe UI" }, FontWeight = 470 };
    private readonly int[] _lastTeam = new int[2];
    private Dictionary<string, TrackRecord> _records = null!;
    private SessionConfig Config => App.Instance.Session;
    private List<TeamData> Teams => _classic ? App.Instance.ClassicCars : App.Instance.Teams.Teams;
    private TeamData Team => Teams[_team];
    private TrackData Circuit => App.Instance.Track;
    private bool Spa => Config.TrackId == DataFiles.SpaId;
    private Page _page;
    private bool _classic;
    private int _team, _shotFrames = -1;
    private Control _frame = null!, _ui = null!;
    private SubViewport _stage = null!;
    private Node3D _studioRoot = null!;
    private Studio _studio = null!;
    private Camera3D _camera = null!;
    private CarModel? _car;
    private DriverData? _shown;
    private Button? _initialFocus;
    private bool _starting;

    public override void _Ready()
    {
        _records = Records.Load();
        string id = App.Instance.Arg("team") ?? Config.TeamId;
        _classic = App.Instance.ClassicCars.Any(t => t.Id == id);
        _team = Math.Max(0, Teams.FindIndex(t => t.Id == id));
        Config.TeamId = Team.Id;
        Config.Seat = Math.Clamp(Config.Seat, 0, Team.Drivers.Count - 1);
        var layer = new CanvasLayer();
        AddChild(layer);
        var canvas = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(canvas);
        canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var back = new ColorRect { Color = Deep };
        canvas.AddChild(back);
        back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _frame = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        canvas.AddChild(_frame);
        _frame.SetAnchorsPreset(Control.LayoutPreset.Center);
        _frame.OffsetLeft = -960; _frame.OffsetRight = 960; _frame.OffsetTop = -540; _frame.OffsetBottom = 540;
        _frame.AddChild(new MenuBackdrop { Size = new Vector2(1920, 1080), MouseFilter = Control.MouseFilterEnum.Ignore });
        BuildStage();
        _page = App.Instance.Arg("page") switch { "garage" => Page.Garage, "circuits" => Page.Circuits, _ => Page.TimeTrial };
        Refresh();
        if (App.Instance.Arg("autotest") == "menushot") _shotFrames = 0;
    }

    private void BuildStage()
    {
        // Render the existing car model only when the selection changes. The menu then composites a single texture.
        _stage = new SubViewport
        {
            Size = new Vector2I(1240, 550),
            TransparentBg = true,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa2X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once
        };
        AddChild(_stage);
        _studioRoot = new Node3D();
        _stage.AddChild(_studioRoot);
        _studio = new Studio(_studioRoot);
        _studio.Floor.Visible = false;
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 3.13f, Current = true };
        _studioRoot.AddChild(_camera);
        _camera.Position = new Vector3(-8.4f, 2.4f, -4.0f);
        _camera.LookAt(new Vector3(0, .6f, 0), Vector3.Up);
        _frame.AddChild(new TextureRect
        {
            Position = new Vector2(675, 192),
            Size = new Vector2(1210, 537),
            Texture = _stage.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
    }

    private void UpdateCar()
    {
        var driver = Team.Drivers[Config.Seat];
        if (_shown == driver) return;
        _shown = driver;
        if (_car != null) { _studioRoot.RemoveChild(_car); _car.QueueFree(); }
        _studio.LightForTeam(Team.Id);
        _car = new CarModel();
        _studioRoot.AddChild(_car);
        _car.Build(Team, driver);
        _car.SetShowroomView(false);
        _stage.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    private static StyleBoxFlat Box(Color color, Color border, int width = 1) => new()
    {
        BgColor = color,
        BorderColor = border,
        BorderWidthLeft = width,
        BorderWidthRight = width,
        BorderWidthTop = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 14,
        ContentMarginRight = 14
    };

    private void Rect(Control parent, int x, int y, int w, int h, Color color)
    {
        parent.AddChild(new ColorRect { Position = new Vector2(x, y), Size = new Vector2(w, h), Color = color, MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    private Label Text(Control parent, string value, int x, int y, int w, int h, int size, Color color, bool bold = false, bool right = false)
    {
        var label = new Label
        {
            Text = value,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", bold ? _display : _body);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    private Button Tile(Control parent, string caption, int x, int y, int w, int h, Action action, bool selected = false, bool primary = false, int size = 22)
    {
        var button = new Button { Text = caption, Position = new Vector2(x, y), Size = new Vector2(w, h), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        var fill = primary ? Red : selected ? Color.FromHtml("#483448") : Panel;
        button.AddThemeStyleboxOverride("normal", Box(fill, selected ? White : primary ? Red : Edge, selected ? 2 : 1));
        button.AddThemeStyleboxOverride("hover", Box(fill.Lightened(.12f), White, 2));
        button.AddThemeStyleboxOverride("pressed", Box(fill.Lightened(.17f), White, 2));
        var focus = Box(Colors.Transparent, White, 3);
        focus.DrawCenter = false;
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeFontOverride("font", _display);
        button.AddThemeFontSizeOverride("font_size", size);
        button.AddThemeColorOverride("font_color", White);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void Refresh()
    {
        UpdateCar();
        bool regainFocus = _ui != null && GetViewport().GuiGetFocusOwner() != null;
        if (_ui != null) { _frame.RemoveChild(_ui); _ui.QueueFree(); }
        _ui = new Control { Size = new Vector2(1920, 1080), MouseFilter = Control.MouseFilterEnum.Ignore };
        _frame.AddChild(_ui);
        for (int i = 0; i < 3; i++)
        {
            float x = 64 + i * 13;
            var slash = new Polygon2D { Color = i == 2 ? White : Red, Polygon = new[] { new Vector2(x + 15, 48), new Vector2(x + 24, 48), new Vector2(x + 7, 75), new Vector2(x - 2, 75) } };
            _ui.AddChild(slash);
        }
        Text(_ui, "LOW-POLY F1", 133, 39, 300, 47, 31, White, true);
        string[] tabs = { "TIME TRIAL", "CIRCUITS", "GARAGE" };
        for (int i = 0; i < 3; i++)
        {
            Page tab = (Page)i;
            var b = Tile(_ui, tabs[i], 550 + i * 190, 38, 172, 48, () => Show(tab), _page == tab);
            if (_page == tab) Rect(_ui, 562 + i * 190, 91, 148, 4, Red);
        }
        Tile(_ui, "QUIT", 1755, 39, 100, 45, () => App.Instance.Quit(), size: 18);
        Rect(_ui, 64, 108, 1792, 2, Edge);
        Hero();
        Rect(_ui, 0, 677, 1920, 403, Deep);
        Rect(_ui, 64, 677, 1792, 2, Edge);
        switch (_page)
        {
            case Page.TimeTrial: SessionOptions(); break;
            case Page.Circuits: CircuitCards(); break;
            case Page.Garage: GarageCards(); break;
        }
        Rect(_ui, 64, 954, 1792, 1, Edge);
        Text(_ui, "PERSONAL BEST", 64, 972, 175, 28, 17, Muted, true);
        string pb = _records.TryGetValue(Circuit.Id, out var rec) && rec.BestLap > 0 ? LapTimer.Format(rec.BestLap) : "No timed lap";
        Text(_ui, pb, 64, 999, 300, 35, 25, White, true);
        Text(_ui, "Q / E   Change page", 425, 986, 290, 40, 19, Muted);
        Text(_ui, _page == Page.Garage ? "↑ / ↓   Car     Tab   Collection" : _page == Page.Circuits ? "← / →   Select circuit" : "1–3   Tyres     G   Ghost", 780, 986, 480, 40, 19, Muted);
        var start = Tile(_ui, "START TIME TRIAL     ›", 1412, 971, 444, 74, StartSession, primary: true, size: 29);
        _initialFocus ??= start;
        if (regainFocus) _initialFocus.GrabFocus();
    }

    private void Hero()
    {
        bool garage = _page == Page.Garage;
        string location = Spa ? "STAVELOT   /   BELGIUM" : "MELBOURNE   /   AUSTRALIA";
        Rect(_ui, 65, 170, 51, 4, Red);
        Text(_ui, garage ? (_classic ? "CLASSIC CARS" : "2024 CONSTRUCTORS") : location, 65, 186, 650, 36, 23, Muted, true);
        if (garage)
        {
            Text(_ui, Team.Name.ToUpperInvariant(), 61, 239, 885, 98, Team.Name.Length > 17 ? 59 : 74, White, true);
            var driver = Team.Drivers[Config.Seat];
            Text(_ui, $"#{driver.Number}   {driver.FullName}", 65, 347, 665, 50, 33, White);
            Text(_ui, Team.PowerUnit, 65, 410, 620, 40, 25, Muted);
            var performance = Team.Car;
            Rating("POWER", performance.Power, .985, 1.01, 490);
            Rating("DOWNFORCE", performance.Downforce, .95, 1.005, 524);
            Rating("TOP SPEED", 2 - performance.Drag, .995, 1.03, 558);
            Rating("TYRE LIFE", 2 - performance.TyreWear, .96, 1.09, 592);
        }
        else
        {
            Text(_ui, Spa ? "SPA-" : "ALBERT", 60, 222, 800, 109, 89, White, true);
            Text(_ui, Spa ? "FRANCORCHAMPS" : "PARK", 60, 313, 830, 100, 78, White, true);
            AddMap(_ui, Circuit, 65, 439, 300, 157, true);
            Rect(_ui, 395, 453, 2, 125, Edge);
            Text(_ui, "LAP LENGTH", 425, 443, 240, 32, 17, Muted, true);
            Text(_ui, $"{Circuit.OfficialLength / 1000:0.000} km", 425, 473, 290, 50, 38, White, true);
            Text(_ui, $"{Circuit.Corners.Count} turns   /   {Circuit.RaceLaps} race laps", 425, 541, 360, 38, 23, Muted);
        }
        Text(_ui, $"{Team.Name.ToUpperInvariant()}   /   {Team.Drivers[Config.Seat].Code}", 924, 605, 924, 42, 23, White, true, true);
    }

    private void Rating(string label, double rating, double min, double max, int y)
    {
        Text(_ui, label, 65, y, 165, 25, 17, Muted, true);
        Rect(_ui, 238, y + 10, 292, 7, Edge);
        int width = (int)(292 * Math.Clamp(.15 + .85 * (rating - min) / (max - min), .08, 1));
        Rect(_ui, 238, y + 10, width, 7, Units.Hex(Team.Secondary));
    }

    private static void AddMap(Control parent, TrackData track, int x, int y, int w, int h, bool sectors)
    {
        parent.AddChild(new CircuitDiagram { Circuit = track, Sectors = sectors, Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore });
    }

    private void SessionOptions()
    {
        Text(_ui, "SESSION SETUP", 64, 702, 600, 51, 35, White, true);
        Text(_ui, "DRIVER", 66, 775, 380, 31, 18, Muted, true);
        for (int i = 0; i < Team.Drivers.Count; i++)
        {
            int seat = i; var d = Team.Drivers[i];
            Tile(_ui, $"#{d.Number}  {d.LastName.ToUpperInvariant()}", 64 + i * 265, 823, 254, 72, () => SelectSeat(seat), Config.Seat == seat, size: 21);
        }
        Text(_ui, "TYRE COMPOUND", 676, 775, 440, 31, 18, Muted, true);
        for (int i = 0; i < Compounds.Length; i++)
        {
            string compound = Compounds[i];
            var b = Tile(_ui, compound.ToUpperInvariant(), 674 + i * 198, 823, 185, 72, () => SetCompound(compound), Config.Compound == compound);
            Rect(b, 12, 18, 4, 36, HudStyle.CompoundColor(compound));
        }
        Text(_ui, "PERSONAL BEST GHOST", 1406, 775, 450, 31, 18, Muted, true);
        Tile(_ui, Config.ShowGhost ? "GHOST   ON" : "GHOST   OFF", 1404, 823, 452, 72, ToggleGhost, Config.ShowGhost);
    }

    private void CircuitCards()
    {
        Text(_ui, "SELECT CIRCUIT", 64, 702, 570, 50, 35, White, true);
        for (int i = 0; i < DataFiles.CircuitIds.Length; i++)
        {
            string id = DataFiles.CircuitIds[i]; var c = App.Instance.Circuit(id);
            var b = Tile(_ui, "", 64 + i * 905, 780, 888, 140, () => SelectCircuit(id), Config.TrackId == id);
            AddMap(b, c, 28, 19, 196, 101, false);
            Text(b, id == DataFiles.SpaId ? "BELGIUM" : "AUSTRALIA", 251, 20, 600, 32, 18, Muted, true);
            Text(b, c.Name.ToUpperInvariant(), 250, 53, 608, 56, 38, White, true);
            if (Config.TrackId == id) _initialFocus = b;
        }
    }

    private void GarageCards()
    {
        Text(_ui, "SELECT CAR", 64, 694, 610, 54, 35, White, true);
        Tile(_ui, "2024 CARS", 1378, 698, 220, 45, () => Category(false), !_classic, size: 19);
        Tile(_ui, "CLASSIC CARS", 1608, 698, 248, 45, () => Category(true), _classic, size: 19);
        for (int i = 0; i < Teams.Count; i++)
        {
            int pick = i;
            var t = Teams[i];
            var b = Tile(_ui, "", 64 + (i % 5) * 360, 761 + (i / 5) * 83, 350, 72, () => SelectTeam(pick), i == _team);
            Rect(b, 15, 16, 7, 39, Units.Hex(t.Secondary));
            Text(b, t.ShortName.ToUpperInvariant(), 36, 8, 286, 54, 26, White, true);
            if (i == _team) _initialFocus = b;
        }
    }

    private void Show(Page page) { _page = page; _initialFocus = null; Refresh(); }
    private void SelectCircuit(string id) { Config.TrackId = id; Show(_page); }
    private void SelectSeat(int seat) { Config.Seat = Math.Clamp(seat, 0, Team.Drivers.Count - 1); Show(_page); }
    private void SetCompound(string id) { Config.Compound = id; Show(_page); }
    private void ToggleGhost() { Config.ShowGhost = !Config.ShowGhost; Show(_page); }
    private void SelectTeam(int index)
    {
        _team = (index + Teams.Count) % Teams.Count;
        Config.TeamId = Team.Id; Config.Seat = Math.Clamp(Config.Seat, 0, Team.Drivers.Count - 1); Show(_page);
    }
    private void Category(bool classics)
    {
        if (_classic == classics) return;
        _lastTeam[_classic ? 1 : 0] = _team; _classic = classics; Config.Seat = 0; SelectTeam(_lastTeam[_classic ? 1 : 0]);
    }
    private void StartSession()
    {
        if (_starting) return;
        _starting = true; Config.Mode = SessionMode.TimeTrial;
        App.Instance.ChangeScene("res://scenes/TimeTrial.tscn");
    }
    private void NextPage(int step) => Show((Page)(((int)_page + step + 3) % 3));

    public override void _Input(InputEvent e)
    {
        if (_ui == null || _starting) return;
        if (e is InputEventJoypadButton { Pressed: true } joy)
        {
            if (joy.ButtonIndex == JoyButton.LeftShoulder) NextPage(-1);
            else if (joy.ButtonIndex == JoyButton.RightShoulder) NextPage(1);
            else if (joy.ButtonIndex == JoyButton.Start) StartSession();
            else if (GetViewport().GuiGetFocusOwner() == null) _initialFocus?.GrabFocus();
            else return;
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Q: NextPage(-1); break;
                case Key.E: NextPage(1); break;
                case Key.Tab: _page = Page.Garage; Category(!_classic); break;
                case Key.Up: _page = Page.Garage; SelectTeam(_team - 1); break;
                case Key.Down: _page = Page.Garage; SelectTeam(_team + 1); break;
                case Key.Left:
                case Key.Right:
                    if (_page == Page.Circuits) SelectCircuit(DataFiles.CircuitIds[(Array.IndexOf(DataFiles.CircuitIds, Config.TrackId) + 1) % DataFiles.CircuitIds.Length]);
                    else SelectSeat(key.Keycode == Key.Left ? 0 : 1);
                    break;
                case Key.Key1: case Key.Key2: case Key.Key3: SetCompound(Compounds[(int)(key.Keycode - Key.Key1)]); break;
                case Key.G: ToggleGhost(); break;
                case Key.Enter: case Key.KpEnter: StartSession(); break;
                case Key.Escape: if (_page == Page.TimeTrial) App.Instance.Quit(); else Show(Page.TimeTrial); break;
                default: return;
            }
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_shotFrames < 0 || ++_shotFrames < 24) return;
        var dir = App.Instance.Arg("out") ?? ProjectSettings.GlobalizePath("user://shots");
        System.IO.Directory.CreateDirectory(dir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "main_menu.png"));
        App.Instance.Quit();
    }
}
