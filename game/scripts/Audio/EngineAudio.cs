using F1.Game.Core;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.Audio;

/// <summary>
/// Procedurally synthesised car sound (no audio assets): V6 firing harmonics driven by rpm and throttle, turbo and
/// MGU-K whine, overrun crackle, shift cuts, tyre squeal, wind and kerb rumble. Use <see cref="Positional"/> for AI cars.
/// </summary>
public partial class EngineAudio : Node3D
{
    public Car? Target { get; set; }
    /// <summary>3D sound for other cars (simpler mix, distance attenuation); 2D full mix for the player car.</summary>
    public bool Positional { get; set; }

    private AudioStreamGeneratorPlayback? _playback;
    private Node? _player;
    private const float MixRate = 22050f;
    private double _p1, _turbo, _whine, _squealPhase, _rumblePhase;
    private float _noiseLp, _windLp, _squealEnv, _amp, _rpmSmooth = 4000, _crackle;
    private int _lastGear;
    private float _shiftDip;
    private readonly Random _rng = new(1);

    public override void _Ready()
    {
        var gen = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.12f };
        if (Positional)
        {
            var p3 = new AudioStreamPlayer3D { Stream = gen, UnitSize = 18, MaxDistance = 450, Bus = "Master", AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance };
            AddChild(p3);
            p3.Play();
            _playback = (AudioStreamGeneratorPlayback)p3.GetStreamPlayback();
            _player = p3;
        }
        else
        {
            var p = new AudioStreamPlayer { Stream = gen, Bus = "Master" };
            AddChild(p);
            p.Play();
            _playback = (AudioStreamGeneratorPlayback)p.GetStreamPlayback();
            _player = p;
        }
    }

    public override void _Process(double delta)
    {
        if (_playback == null || Target == null || !IsInstanceValid(Target)) return;
        if (Positional) GlobalPosition = Target.GlobalPosition;
        var s = App.Instance.Settings;
        int frames = _playback.GetFramesAvailable();
        if (frames <= 0) return;

        float rpmTarget = Target.Rpm;
        float throttle = Target.ThrottleApplied;
        float speed = Target.SpeedMs;
        bool deploying = Target.PU.MguKPowerW > 1000;
        if (Target.Gear != _lastGear) { _shiftDip = 1f; _lastGear = Target.Gear; }

        float slip = 0, rumble = 0;
        foreach (var w in Target.Wheels)
        {
            if (!w.Grounded) continue;
            slip = Math.Max(slip, (float)Math.Max(Math.Abs(w.SlipAngle) / 0.16 - 0.6, Math.Abs(w.SlipRatio) / 0.18 - 0.6));
            if (w.Surface.Type == Track.SurfaceType.Kerb) rumble = 1;
            else if (w.Surface.Type is Track.SurfaceType.Gravel or Track.SurfaceType.Grass) rumble = Math.Max(rumble, 0.6f);
        }
        slip = Math.Clamp(slip, 0, 1) * Math.Clamp(speed / 8f, 0, 1);

        float engineVol = s.EngineVolume * (Positional ? 1.0f : 0.55f);
        float fxVol = s.EffectsVolume * 0.5f;
        var buffer = new Vector2[frames];
        for (int i = 0; i < frames; i++)
        {
            _rpmSmooth += (rpmTarget - _rpmSmooth) * 0.0025f;
            float f0 = _rpmSmooth / 60f * 3f; // V6: three firing pulses per revolution
            _p1 = (_p1 + f0 / MixRate) % 1.0;
            double ph = _p1 * Math.Tau;
            float saw = (float)(2 * ((_p1 * 0.5) % 1.0) - 1);
            float targetAmp = 0.32f + throttle * 0.68f;
            _amp += (targetAmp - _amp) * 0.0015f;
            _shiftDip = Math.Max(0, _shiftDip - 1f / (MixRate * 0.06f));

            float engine = (float)(0.55 * Math.Sin(ph) + 0.28 * Math.Sin(2 * ph + 0.3) + 0.14 * Math.Sin(3 * ph + 1.1) + 0.07 * Math.Sin(6 * ph));
            engine += saw * (0.12f + throttle * 0.16f);
            float rough = (float)(_rng.NextDouble() * 2 - 1);
            _noiseLp += (rough - _noiseLp) * 0.35f;
            engine += _noiseLp * 0.08f * throttle;

            // overrun crackle off-throttle at high rpm
            if (throttle < 0.05f && _rpmSmooth > 8000 && _rng.NextDouble() < 0.0009) _crackle = 1f;
            _crackle *= 0.992f;
            engine += _crackle * rough * 0.6f;
            engine *= _amp * (1 - _shiftDip * 0.6f);

            // turbo whistle and MGU-K whine
            _turbo = (_turbo + (2600 + _rpmSmooth * 0.32) / MixRate) % 1.0;
            float turbo = (float)Math.Sin(_turbo * Math.Tau) * 0.035f * throttle;
            _whine = (_whine + (speed * 55 + 400) / MixRate) % 1.0;
            float whine = deploying ? (float)Math.Sin(_whine * Math.Tau) * 0.025f : 0;

            float sample = (engine + turbo + whine) * engineVol;

            if (!Positional)
            {
                // wind
                _windLp += (rough - _windLp) * 0.05f;
                sample += _windLp * Math.Min(1, speed * speed / 9000f) * 0.25f * fxVol;
                // tyre squeal: narrow-band noise around 900 Hz
                _squealEnv += (slip - _squealEnv) * 0.002f;
                _squealPhase = (_squealPhase + (850 + rough * 120) / MixRate) % 1.0;
                sample += (float)Math.Sin(_squealPhase * Math.Tau) * _squealEnv * 0.18f * fxVol;
                // kerb rumble
                _rumblePhase = (_rumblePhase + (22 + speed * 0.4) / MixRate) % 1.0;
                sample += (_rumblePhase < 0.5 ? 1 : -1) * rumble * 0.08f * fxVol * Math.Min(1, speed / 15f);
            }
            float v = MathF.Tanh(sample * 1.6f) * 0.7f;
            buffer[i] = new Vector2(v, v);
        }
        _playback.PushBuffer(buffer);
    }
}
