using F1.Core.Racing;
using F1.Game.Core;
using F1.Game.Controls;
using Godot;

namespace F1.Game.Vehicle;

/// <summary>Turns the player's keyboard / gamepad / wheel input into car inputs, applying input tuning and assists.</summary>
public sealed class PlayerDriver : IDriver
{
    private readonly GameSettings _settings;
    private float _steer;
    /// <summary>When false the car receives no input (e.g. while paused or before the start).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Racing line used by the braking assist (optional).</summary>
    public RacingLine? Line { get; set; }

    public PlayerDriver(GameSettings settings) { _settings = settings; }

    public static void ApplyAssists(Car car, GameSettings s)
    {
        car.Assists = new DrivingAssists
        {
            TractionControl = s.TractionControl switch
            {
                "Full" => DrivingAssists.TractionLevel.Full,
                "Medium" => DrivingAssists.TractionLevel.Medium,
                _ => DrivingAssists.TractionLevel.Off,
            },
            Abs = s.Abs,
            AutoGearbox = !s.ManualTransmission,
            AutoClutch = !s.ManualClutch,
            PitLimiterAssist = s.PitAssist,
            ErsAssist = s.ErsAssist,
            DrsAssist = s.DrsAssist,
        };
    }

    public void Drive(Car car, double dt, ref CarInputs inputs)
    {
        if (!Enabled) { inputs.Throttle = 0; inputs.Brake = car.SpeedMs > 1 ? 0.3f : 0; inputs.Steer = 0; return; }
        var s = _settings;

        float left = Godot.Input.GetActionStrength(InputService.SteerLeft, true);
        float right = Godot.Input.GetActionStrength(InputService.SteerRight, true);
        float steer;
        if (App.Instance.LastInputWasKeyboard)
        {
            float target = left - right;
            float rate = (target != 0 && (Math.Sign(target) == Math.Sign(_steer) || _steer == 0)) ? s.KeyboardSteerSpeed : s.KeyboardSteerReturn;
            if (target != 0 && Math.Sign(target) != Math.Sign(_steer) && _steer != 0) rate = s.KeyboardSteerReturn + s.KeyboardSteerSpeed;
            _steer = Mathf.MoveToward(_steer, target, rate * (float)dt);
            steer = _steer;
        }
        else
        {
            float raw = left - right;
            if (App.Instance.ActiveJoypadIsWheel) raw *= 900f / Math.Max(90f, s.WheelRotation);
            float mag = Math.Abs(raw);
            mag = mag < s.SteeringDeadzone ? 0 : (mag - s.SteeringDeadzone) / (1 - s.SteeringDeadzone);
            mag = Math.Clamp(mag / Math.Max(0.1f, s.SteeringSaturation), 0, 1);
            mag = Mathf.Pow(mag, s.SteeringLinearity);
            steer = Math.Clamp(Math.Sign(raw) * mag * s.SteeringSensitivity, -1, 1);
            _steer = steer;
        }

        if (s.SteeringAssist)
        {
            // speed-sensitive lock so a keyboard or pad doesn't instantly exceed the tyres' grip at speed
            float v = car.SpeedMs;
            float limit = Math.Clamp(1f - (v - 15f) / 80f * 0.72f, 0.28f, 1f);
            steer *= limit;
        }
        inputs.Steer = steer;

        float throttle = Godot.Input.GetActionStrength(InputService.Throttle, true);
        float brake = Godot.Input.GetActionStrength(InputService.Brake, true);
        if (s.InvertThrottleAxis) throttle = 1 - throttle;
        if (s.InvertBrakeAxis) brake = 1 - brake;
        throttle = throttle < s.ThrottleDeadzone ? 0 : throttle;
        brake = brake < s.BrakeDeadzone ? 0 : brake;

        if (s.BrakingAssist && Line != null)
        {
            double allowed = AllowedSpeed(car.TrackS, car.SpeedMs);
            double over = car.SpeedMs - allowed;
            if (over > 0)
            {
                brake = Math.Max(brake, (float)Math.Clamp(over * 0.22, 0, 1));
                throttle = Math.Min(throttle, (float)Math.Clamp(1 - over * 0.5, 0, 1));
            }
        }
        inputs.Throttle = throttle;
        inputs.Brake = brake;
        inputs.Clutch = Godot.Input.GetActionStrength(InputService.Clutch);

        inputs.ShiftUp = Godot.Input.IsActionJustPressed(InputService.ShiftUp);
        inputs.ShiftDown = Godot.Input.IsActionJustPressed(InputService.ShiftDown);
        inputs.Drs = Godot.Input.IsActionPressed(InputService.Drs);
        inputs.TogglePitLimiter = Godot.Input.IsActionJustPressed(InputService.PitLimiter);
        inputs.Overtake = Godot.Input.IsActionPressed(InputService.Overtake);
        if (Godot.Input.IsActionJustPressed(InputService.ErsUp)) inputs.ErsModeDelta = 1;
        if (Godot.Input.IsActionJustPressed(InputService.ErsDown)) inputs.ErsModeDelta = -1;
        if (Godot.Input.IsActionJustPressed(InputService.BiasFwd)) inputs.BrakeBiasDelta = 1;
        if (Godot.Input.IsActionJustPressed(InputService.BiasRear)) inputs.BrakeBiasDelta = -1;
        if (Godot.Input.IsActionJustPressed(InputService.DiffUp)) inputs.DiffDelta = 1;
        if (Godot.Input.IsActionJustPressed(InputService.DiffDown)) inputs.DiffDelta = -1;
        if (Godot.Input.IsActionJustPressed(InputService.MixUp)) inputs.FuelMixDelta = 1;
        if (Godot.Input.IsActionJustPressed(InputService.MixDown)) inputs.FuelMixDelta = -1;
        if (Godot.Input.IsActionJustPressed(InputService.EngBrakeUp)) inputs.EngineBrakingDelta = 1;
        if (Godot.Input.IsActionJustPressed(InputService.EngBrakeDown)) inputs.EngineBrakingDelta = -1;
    }

    /// <summary>Highest speed from which the car can still slow down for every point of the racing line within 250 m.</summary>
    private double AllowedSpeed(double s, double v)
    {
        if (Line == null) return double.MaxValue;
        const double brakeDecel = 38; // m/s², conservative so the assist brakes a little early
        double best = double.MaxValue;
        for (double x = 0; x <= 250; x += 5)
        {
            double vt = Line.SpeedAt(s + x) * 0.97;
            best = Math.Min(best, Math.Sqrt(vt * vt + 2 * brakeDecel * x));
        }
        return best;
    }
}
