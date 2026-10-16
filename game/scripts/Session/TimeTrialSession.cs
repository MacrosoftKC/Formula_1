using F1.Core.Race;
using F1.Core.Sim;
using F1.Game.Core;
using F1.Game.Controls;
using F1.Game.UI;
using Godot;

namespace F1.Game.Session;

/// <summary>
/// Time Trial: one car, low fuel, Hotlap ERS, flying laps with sector times, track-limit invalidation,
/// a live delta and translucent ghost of the personal best (saved to user://records.json).
/// </summary>
public partial class TimeTrialSession : SessionBase
{
    protected override string SceneFile => "res://scenes/TimeTrial.tscn";

    private LapTimer _timer = null!;
    private GhostLap _recording = new();
    private GhostLap? _pbGhost;
    private GhostCar? _ghostCar;
    private Dictionary<string, TrackRecord> _records = null!;
    private double _sampleTimer;
    private bool _offTrackWarned;
    private bool _offLogged;
    private readonly Dictionary<int, double> _cornerMin = new();
    private double _vmax;

    protected override void SetupSession()
    {
        var cfg = Config;
        var team = App.Instance.Teams.Teams.FirstOrDefault(t => t.Id == cfg.TeamId) ?? App.Instance.Teams.Teams[0];
        var driver = PlayerDriverData(team);
        var compound = TyreCompound.ById(cfg.Compound);

        // start on the run to the line after the final corner, standing still
        double spawnS = Track.Center.Wrap(Track.Data.Corners[^1].ExitS + 30);
        var car = SpawnCar(team, driver, Track.TrackTransform(spawnS, 0, 0.01f), compound, 12, cfg.Setup, player: true);
        car.PU.Mode = ErsMode.Hotlap;
        car.TyreWearMultiplier = 1.0;
        foreach (var t in car.Tyres) { t.SurfaceTemp = 95; t.CoreTemp = 92; }
        AttachPlayer(car);

        _timer = new LapTimer(Track.Length, Track.Data.SectorStarts) { Compound = compound.Id };
        Hud.Timer = _timer;
        Hud.Delta = LiveDelta;

        _records = Records.Load();
        if (_records.TryGetValue(Track.Data.Id, out var rec) && rec.Ghost.Length > 0)
        {
            _pbGhost = GhostLap.FromArray(rec.Ghost);
            Hud.ShowMessage($"PERSONAL BEST {LapTimer.Format(rec.BestLap)}", 4);
        }
        if (cfg.ShowGhost)
        {
            _ghostCar = new GhostCar { Name = "Ghost" };
            AddChild(_ghostCar);
            _ghostCar.Build(team, driver);
            _ghostCar.Lap = _pbGhost;
            _ghostCar.Visible = false;
        }
        if (car.Model?.FindChild("WheelScreen", true, false) is SteeringWheelDisplay screen)
        {
            screen.DeltaText = () => LiveDelta() is { } d ? LapTimer.Format(d, sign: true) : "";
            screen.LapText = () => _timer.Started ? $"LAP {_timer.LapsCompleted + 1}" : "OUT";
        }
        Hud.ShowMessage("TIME TRIAL  —  ALBERT PARK", 3);
    }

    private double? LiveDelta()
    {
        if (_pbGhost == null || !_timer.Started || Player == null) return null;
        double dist = Track.Center.Forward(Player.TrackS, 0);
        if (dist > Track.Length - 5) return null;
        var t = _pbGhost.TimeAtDistance(dist);
        return t.HasValue ? _timer.CurrentLapTime(Time) - t.Value : null;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        var car = Player;
        if (car == null) return;

        // DRS may be used freely in the zones during Time Trial (as in qualifying)
        car.DrsAllowed = Track.DrsZoneAt(car.TrackS) >= 0;

        var evt = _timer.Update(car.TrackS, Time);
        switch (evt)
        {
            case TimingEvent.LapStarted:
                _recording = new GhostLap();
                break;
            case TimingEvent.LapComplete:
                OnLapComplete();
                break;
            case TimingEvent.SectorComplete when _timer.LastSplit is { } split:
                Hud.ShowMessage($"S{split.Sector + 1}  {LapTimer.Format(split.Time)}", 2);
                break;
        }

        // track limits: all four wheels beyond the white lines invalidates the lap
        if (car.WheelsOffTrack >= 4 && _timer.Started)
        {
            if (_timer.CurrentLapValid) Hud.ShowMessage("TRACK LIMITS  —  LAP INVALIDATED", 3, HudStyle.Red);
            _timer.Invalidate();
            _offTrackWarned = true;
        }
        else if (car.WheelsOffTrack == 0) _offTrackWarned = false;

        // ghost recording at 20 Hz
        if (_timer.Started)
        {
            _sampleTimer += delta;
            if (_sampleTimer >= 0.05)
            {
                _sampleTimer = 0;
                var xf = car.GlobalTransform;
                _recording.Samples.Add(new GhostSample((float)_timer.CurrentLapTime(Time), (float)Track.Center.Forward(car.TrackS, 0), xf.Origin, xf.Basis.GetRotationQuaternion()));
            }
        }

        if (Godot.Input.IsActionJustPressed(InputService.ResetCar) && !Autotest) ResetCar();
        if (App.Instance.Arg("autotest") == "ttplayer") PlayerInputTest(car);
        else if (Autotest && App.Instance.Arg("shots") == null) CheckAutotest(car);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_ghostCar != null && _timer.Started) _ghostCar.ShowAt(_timer.CurrentLapTime(Time));
        else if (_ghostCar != null) _ghostCar.Visible = false;
    }

    private void OnLapComplete()
    {
        var lap = _timer.Laps[^1];
        var rec = _records.GetValueOrDefault(Track.Data.Id);
        bool pb = lap.Valid && (rec == null || lap.Time < rec.BestLap);
        if (pb)
        {
            _records[Track.Data.Id] = new TrackRecord
            {
                BestLap = lap.Time, Sectors = lap.Sectors, TeamId = Config.TeamId, Compound = lap.Compound,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Ghost = _recording.ToArray(),
            };
            if (!Autotest) Records.Save(_records);
            _pbGhost = _recording;
            if (_ghostCar != null) _ghostCar.Lap = _pbGhost;
            Hud.ShowMessage($"PERSONAL BEST  {LapTimer.Format(lap.Time)}", 4, HudStyle.Purple);
        }
        else
            Hud.ShowMessage($"LAP {lap.Number}  {LapTimer.Format(lap.Time)}{(lap.Valid ? "" : "  (INVALID)")}", 4, lap.Valid ? HudStyle.Text : HudStyle.Red);
        _recording = new GhostLap();
        GD.Print($"LAP {lap.Number}: {LapTimer.Format(lap.Time)} valid={lap.Valid} sectors={string.Join(" ", lap.Sectors.Select(s => s.ToString("0.000")))}");
        if (Autotest)
        {
            var parts = Track.Data.Corners.Select(c =>
            {
                double lineMin = 999;
                for (double x = c.EntryS - 20; x <= c.ExitS + 20; x += 2) lineMin = Math.Min(lineMin, Line.SpeedAt(x) * 3.6);
                return $"T{c.Number}:{_cornerMin.GetValueOrDefault(c.Number):0}/{lineMin:0}";
            });
            GD.Print($"   corners car/line: {string.Join(" ", parts)}  vmax={_vmax:0}");
            _cornerMin.Clear(); _vmax = 0;
        }
    }

    private float _startYaw;

    /// <summary>Scripted keyboard-action test of the player driving path (PlayerDriver, assists, gearbox).</summary>
    private void PlayerInputTest(Vehicle.Car car)
    {
        if (Time < 0.5) { _startYaw = car.GlobalRotation.Y; return; }
        if (Time < 8) Godot.Input.ActionPress(InputService.Throttle);
        if (Time >= 8 && Time < 8.02)
        {
            GD.Print($"PLAYER after 7.5 s full throttle: {car.SpeedKmh:0} km/h gear {car.Gear}");
            Godot.Input.ActionRelease(InputService.Throttle);
            Godot.Input.ActionPress(InputService.Brake);
        }
        if (Time >= 10 && Time < 10.02)
        {
            GD.Print($"PLAYER after 2 s braking: {car.SpeedKmh:0} km/h gear {car.Gear}");
            Godot.Input.ActionRelease(InputService.Brake);
            Godot.Input.ActionPress(InputService.Throttle, 0.5f);
            Godot.Input.ActionPress(InputService.SteerLeft);
        }
        if (Time >= 12)
        {
            float yaw = Mathf.RadToDeg(Mathf.AngleDifference(_startYaw, car.GlobalRotation.Y));
            GD.Print($"PLAYER after 2 s steering left: yaw change {yaw:0} deg, speed {car.SpeedKmh:0} km/h");
            bool ok = yaw > 10;
            GD.Print(ok ? "PLAYER INPUT TEST PASSED" : "PLAYER INPUT TEST FAILED");
            App.Instance.Quit(ok ? 0 : 4);
        }
    }

    private void ResetCar()
    {
        var car = Player!;
        car.PlaceAt(Track.TrackTransform(car.TrackS, 0, 0.01f));
        _timer.Invalidate();
        _timer.Teleport(car.TrackS, Time);
        Hud.ShowMessage("CAR RESET  —  LAP INVALIDATED", 2, HudStyle.Red);
    }

    private void CheckAutotest(Vehicle.Car car)
    {
        if (!float.IsFinite(car.GlobalPosition.X) || !float.IsFinite(car.SpeedMs))
        {
            GD.PrintErr("AUTOTEST FAIL: non-finite car state");
            App.Instance.Quit(2);
        }
        if (car.WheelsOffTrack >= 3 && !_offLogged)
        {
            _offLogged = true;
            double tg = car.Driver is AI.AIDriver a ? a.LastTarget * 3.6 : 0;
            GD.Print($"  OFF at s={car.TrackS:0} d={car.TrackD:0.0} v={car.SpeedKmh:0} tgt={tg:0} line={Line.SpeedAt(car.TrackS) * 3.6:0} steer={car.SteerApplied:0.00} slipF={car.Wheels[0].SlipAngle:0.000} slipR={car.Wheels[2].SlipAngle:0.000} brk={car.BrakeApplied:0.00}");
        }
        else if (car.WheelsOffTrack == 0) _offLogged = false;
        if (Time > 1 && (Time % 5) < 1.0 / 240)
        {
            double tgt = car.Driver is AI.AIDriver ai ? ai.LastTarget * 3.6 : 0;
            GD.Print($"t={Time:0} s={car.TrackS:0} v={car.SpeedKmh:0} tgt={tgt:0} thr={car.ThrottleApplied:0.00} brk={car.BrakeApplied:0.00} g={car.Gear} rpm={car.Rpm:0} axle={car.DebugAxle:0} fx={car.DebugFx:0} drag={car.DebugDrag:0} kR={car.Wheels[2].SlipRatio:0.000} fzR={car.Wheels[2].Fz:0} off={car.WheelsOffTrack} tyre={car.Tyres[0].SurfaceTemp:0}/{car.Tyres[2].SurfaceTemp:0}");
        }
        foreach (var c in Track.Data.Corners)
        {
            double from = c.EntryS - 20, to = c.ExitS + 20;
            if (Track.Center.Forward(car.TrackS, from) <= Track.Center.Forward(to, from))
                _cornerMin[c.Number] = Math.Min(_cornerMin.GetValueOrDefault(c.Number, 999), car.SpeedKmh);
        }
        _vmax = Math.Max(_vmax, car.SpeedKmh);
        int wanted = int.TryParse(App.Instance.Arg("laps"), out int l) ? l : 2;
        if (_timer.LapsCompleted >= wanted || Time > 60 + 100 * wanted)
        {
            GD.Print($"AUTOTEST DONE laps={_timer.LapsCompleted} best={LapTimer.Format(_timer.BestLap?.Time)} time={Time:0}");
            App.Instance.Quit(_timer.LapsCompleted >= wanted ? 0 : 3);
        }
    }
}
