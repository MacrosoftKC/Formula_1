using F1.Core.Data;
using F1.Core.Racing;
using F1.Core.Sim;
using F1.Game.AI;
using F1.Game.Audio;
using F1.Game.Camera;
using F1.Game.Core;
using F1.Game.Controls;
using F1.Game.Track;
using F1.Game.UI;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.Session;

/// <summary>Common plumbing for Time Trial and Race: world, racing line, cars, camera, HUD, audio, pause menu, clock.</summary>
public abstract partial class SessionBase : Node3D
{
    public TrackWorld World { get; private set; } = null!;
    public TrackSurface Track => World.Surface;
    public RacingLine Line { get; private set; } = null!;
    public CameraRig CameraRig { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public PauseMenu Pause { get; private set; } = null!;
    public Car? Player { get; protected set; }
    public List<Car> Cars { get; } = new();
    /// <summary>Session time in seconds, advanced on every physics tick (pauses with the game).</summary>
    public double Time { get; private set; }
    public SessionConfig Config => App.Instance.Session;
    public bool Autotest => App.Instance.Arg("autotest") != null;

    private static RacingLine? _cachedLine;

    public override void _Ready()
    {
        World = new TrackWorld { Name = "TrackWorld", BuildScenery = App.Instance.Arg("noscenery") == null };
        AddChild(World);
        Line = _cachedLine ??= RacingLine.Build(Track.Data, new PerformanceModel());

        CameraRig = new CameraRig { Name = "CameraRig" };
        AddChild(CameraRig);
        Hud = new Hud { Name = "Hud", Track = Track, Clock = () => Time };
        AddChild(Hud);
        Pause = new PauseMenu { Name = "PauseMenu" };
        Pause.RestartRequested += () => App.Instance.ChangeScene(SceneFile);
        Pause.QuitRequested += () => App.Instance.ChangeScene("res://scenes/Main.tscn");
        AddChild(Pause);

        // trackside TV camera positions: on the outside of every corner, raised
        foreach (var c in Track.Data.Corners)
        {
            int side = c.Direction > 0 ? -1 : 1;
            double barrier = side > 0 ? Track.BarrierLeft(c.ApexS) : Track.BarrierRight(c.ApexS);
            CameraRig.TvPositions.Add(Units.ToWorld(Track.Center.ToWorld(c.ApexS, side * (barrier + 6)), 7));
        }
        SetupSession();
        if (App.Instance.Arg("shots") is { } dir)
            AddChild(new ScreenshotDirector { Name = "Shots", Session = this, Directory = dir, StartAt = double.TryParse(App.Instance.Arg("shotsat"), out var t) ? t : 9 });
    }

    /// <summary>Scene to reload for "Restart".</summary>
    protected abstract string SceneFile { get; }
    protected abstract void SetupSession();

    public override void _PhysicsProcess(double delta) => Time += delta;

    public override void _Process(double delta)
    {
        if (Godot.Input.IsActionJustPressed(InputService.Pause) && !Autotest) Pause.Toggle();
    }

    protected DriverData PlayerDriverData(TeamData team)
    {
        var seat = team.Drivers[Math.Clamp(Config.Seat, 0, team.Drivers.Count - 1)];
        string name = App.Instance.Settings.DriverName;
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return new DriverData
        {
            Number = seat.Number,
            FirstName = parts.Length > 1 ? parts[0] : "",
            LastName = parts.Length > 1 ? parts[1] : (parts.Length == 1 ? parts[0] : "Player"),
            Code = (parts.Length > 0 ? parts[^1] : "PLY").ToUpperInvariant().PadRight(3, 'X')[..3],
            Nationality = seat.Nationality,
            Helmet = seat.Helmet,
            Ratings = seat.Ratings,
        };
    }

    protected Car SpawnCar(TeamData team, DriverData driver, Transform3D at, TyreCompound compound, double fuelKg, CarSetup setup, bool player)
    {
        var car = new Car { IsPlayer = player, Index = Cars.Count };
        car.Configure(Track, team, driver, setup, compound, fuelKg, App.Instance.Regulations.Tyres.BlanketTempC);
        AddChild(car);
        car.PlaceAt(at);
        Cars.Add(car);
        return car;
    }

    /// <summary>Hooks the player car up to input, camera, HUD, audio and the steering-wheel screen.</summary>
    protected void AttachPlayer(Car car)
    {
        Player = car;
        var settings = App.Instance.Settings;
        if (Autotest && App.Instance.Arg("autotest") != "ttplayer")
        {
            car.Driver = new AIDriver(Line, car.DriverInfo, 100, 1);
            car.Assists = new DrivingAssists { AutoGearbox = true, TractionControl = DrivingAssists.TractionLevel.Medium, Abs = true };
        }
        else
        {
            car.Driver = new PlayerDriver(settings) { Line = Line };
            PlayerDriver.ApplyAssists(car, settings);
        }
        CameraRig.Target = car;
        CameraRig.SetMode(settings.DefaultCamera);
        Hud.Car = car;
        Hud.OtherCars = Cars;
        if (!Autotest || App.Instance.Arg("audio") != null)
            AddChild(new EngineAudio { Name = "PlayerAudio", Target = car });
        if (settings.ShowSteeringWheel) SteeringWheelDisplay.Attach(car);
    }
}
