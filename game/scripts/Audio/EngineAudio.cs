using F1.Core.Audio;
using F1.Game.Camera;
using F1.Game.Core;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.Audio;

/// <summary>
/// Car sound: feeds the car's live power-unit and chassis state into the procedural turbo-hybrid V6
/// (<see cref="HybridV6Synth"/>) and chassis effects (<see cref="ChassisFxSynth"/>) and streams the result.
/// The player mix follows the camera: onboard cams hear the airbox, MGU-K and gearbox; chase cams hear the exhaust;
/// trackside TV cams hear the car at distance with air absorption and Doppler. Use <see cref="Positional"/> for AI cars.
/// </summary>
public partial class EngineAudio : Node3D
{
    public Car? Target { get; set; }
    /// <summary>Player camera, for the onboard/external mix and the TV-camera distance and Doppler.</summary>
    public CameraRig? Rig { get; set; }
    /// <summary>3D sound for other cars (exhaust mix, engine attenuation handles distance); 2D full mix for the player.</summary>
    public bool Positional { get; set; }

    private const int MixRate = 44100;
    private const float SpeedOfSound = 343f;
    private AudioStreamGeneratorPlayback? _playback;
    private HybridV6Synth _engine = null!;
    private ChassisFxSynth _fx = null!;
    private float[] _eng = Array.Empty<float>(), _l = Array.Empty<float>(), _r = Array.Empty<float>();
    private Vector2[] _buffer = Array.Empty<Vector2>();
    private int _lastGear = 1;
    private float _onboard = 1, _doppler = 1, _gain = 1, _air = 20000;
    private float _prevDistance = -1;

    public override void _Ready()
    {
        _engine = new HybridV6Synth(MixRate, (uint)(GetInstanceId() & 0xFFFF) + 1);
        _fx = new ChassisFxSynth(MixRate);
        var gen = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f };
        if (Positional)
        {
            var p3 = new AudioStreamPlayer3D { Stream = gen, UnitSize = 18, MaxDistance = 500, Bus = "Master", AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance };
            AddChild(p3);
            p3.Play();
            _playback = (AudioStreamGeneratorPlayback)p3.GetStreamPlayback();
        }
        else
        {
            var p = new AudioStreamPlayer { Stream = gen, Bus = "Master" };
            AddChild(p);
            p.Play();
            _playback = (AudioStreamGeneratorPlayback)p.GetStreamPlayback();
        }
        if (Target != null) _lastGear = Target.Gear;
    }

    public override void _Process(double delta)
    {
        if (_playback == null || Target == null || !IsInstanceValid(Target)) return;
        var car = Target;
        if (Positional) GlobalPosition = car.GlobalPosition;
        float dt = (float)Math.Max(delta, 1e-4);
        UpdateListener(car, dt);

        if (car.Gear != _lastGear)
        {
            _engine.GearChanged(_lastGear, car.Gear);
            _lastGear = car.Gear;
        }

        _engine.State = new PowerUnitSoundState
        {
            Rpm = car.Rpm,
            Throttle = car.ThrottleApplied,
            SpeedMs = car.SpeedMs,
            MguKPowerW = (float)car.PU.MguKPowerW,
            Gear = car.Gear,
            PitLimiter = car.PitLimiter,
            RevLimit = (float)car.PU.RevLimit,
            Onboard = _onboard,
            Doppler = _doppler,
            AirCutoffHz = _air,
        };

        int frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;
        if (_eng.Length < frames)
        {
            _eng = new float[frames]; _l = new float[frames]; _r = new float[frames];
            _buffer = new Vector2[frames];
        }
        var eng = _eng.AsSpan(0, frames);
        _engine.Render(eng);

        var s = App.Instance.Settings;
        float engineVol = s.EngineVolume * _gain * (Positional ? 1.1f : 0.6f);
        if (Positional)
        {
            for (int i = 0; i < frames; i++)
            {
                float v = MathF.Tanh(eng[i] * engineVol * 1.6f) * 0.7f;
                _buffer[i] = new Vector2(v, v);
            }
        }
        else
        {
            _fx.State = ChassisState(car);
            _fx.Render(_l.AsSpan(0, frames), _r.AsSpan(0, frames));
            float fxVol = s.EffectsVolume * 0.45f * Math.Max(_gain, _onboard);
            for (int i = 0; i < frames; i++)
            {
                float e = eng[i] * engineVol;
                _buffer[i] = new Vector2(MathF.Tanh((e + _l[i] * fxVol) * 1.6f) * 0.7f, MathF.Tanh((e + _r[i] * fxVol) * 1.6f) * 0.7f);
            }
        }
        _playback.PushBuffer(frames == _buffer.Length ? _buffer : _buffer[..frames]);
    }

    /// <summary>Works out how the listener (the camera) hears the car: onboard blend, distance, Doppler.</summary>
    private void UpdateListener(Car car, float dt)
    {
        float onboard = 0, doppler = 1, gain = 1, air = 20000;
        if (!Positional && Rig != null)
        {
            onboard = Rig.Mode switch
            {
                CameraMode.Cockpit or CameraMode.Helmet => 1f,
                CameraMode.TCam => 0.75f,
                CameraMode.Nose => 0.55f,
                CameraMode.ChaseNear => 0.15f,
                _ => 0f,
            };
            if (Rig.Mode == CameraMode.TV && Rig.Camera != null)
            {
                float d = Rig.Camera.GlobalPosition.DistanceTo(car.GlobalPosition);
                // a TV camera cut jumps the distance; don't read that as a supersonic closing speed
                if (_prevDistance >= 0 && Math.Abs(d - _prevDistance) < 15f)
                {
                    float radial = (d - _prevDistance) / dt;
                    doppler = Math.Clamp(SpeedOfSound / (SpeedOfSound + radial), 0.7f, 1.4f);
                }
                else doppler = _doppler;
                _prevDistance = d;
                gain = Math.Clamp(16f / Math.Max(d, 1f), 0.06f, 1f);
                air = Math.Clamp(16000f * 30f / Math.Max(d, 1f), 1500f, 20000f);
            }
            else _prevDistance = -1;
        }
        else if (Positional) _prevDistance = -1;

        // ease camera switches so the mix doesn't snap
        float k = Math.Min(1, dt * 8);
        _onboard += (onboard - _onboard) * k;
        _doppler += (doppler - _doppler) * Math.Min(1, dt * 12);
        _gain += (gain - _gain) * k;
        _air += (air - _air) * k;
    }

    private ChassisSoundState ChassisState(Car car)
    {
        float slip = 0, kerb = 0, gravel = 0, grass = 0, lockup = 0;
        foreach (var w in car.Wheels)
        {
            if (!w.Grounded) continue;
            slip = Math.Max(slip, (float)Math.Max(Math.Abs(w.SlipAngle) / 0.16 - 0.6, Math.Abs(w.SlipRatio) / 0.18 - 0.6));
            if (w.Locked) lockup = 1;
            switch (w.Surface.Type)
            {
                case Track.SurfaceType.Kerb: kerb = 1; break;
                case Track.SurfaceType.Gravel: gravel = 1; break;
                case Track.SurfaceType.Grass: grass = 1; break;
            }
        }
        return new ChassisSoundState
        {
            SpeedMs = car.SpeedMs,
            Slip = Math.Clamp(slip, 0, 1),
            Lockup = lockup,
            Kerb = kerb,
            Gravel = gravel,
            Grass = grass,
            DrsOpen = car.DrsOpen,
            Onboard = _onboard,
        };
    }
}
