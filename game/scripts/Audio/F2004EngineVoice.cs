using F1.Game.Vehicle;
using Godot;

namespace F1.Game.Audio;

/// <summary>Stereo Tipo 053 engine loops and shift cuts extracted from the supplied Jeddah lap.</summary>
internal sealed class F2004EngineVoice : ISampledEngineVoice
{
    private const float UnityRpm = 18000;
    public const int Voices = 8;
    public float SynthGain => 0;

    private readonly AudioStreamPlaybackPolyphonic _playback;
    private readonly long _pickup, _pull, _straight, _overrun, _lift;
    private readonly AudioStreamWav _up = Load("Upshift"), _down = Load("Downshift");
    private int _gear;
    private long _shift = AudioStreamPlaybackPolyphonic.InvalidId, _prev = AudioStreamPlaybackPolyphonic.InvalidId;
    private float _rpm, _load, _shiftTime, _shiftLevel;
    // Each shot fades in over 14 ms (make_banks.py); the loops give way over the same time, so a shift has no hole.
    private float _shiftAge, _shiftFade = ShotFade, _duckFrom, _prevAge, _prevLevel;
    private const float ShotFade = 0.014f;

    public F2004EngineVoice(AudioStreamPlaybackPolyphonic playback, Car car)
    {
        _playback = playback;
        _gear = car.Gear;
        _rpm = car.Rpm;
        var loops = new[] { "Pickup", "Pull", "Straight", "Overrun", "Lift" }.Select(Load).ToArray();
        // Start the order-tracked loops on the same audio frame so RPM changes keep them in phase.
        AudioServer.Lock();
        try
        {
            long Start(int index) => _playback.PlayStream(loops[index], 0, -100, _rpm / UnityRpm);
            _pickup = Start(0);
            _pull = Start(1);
            _straight = Start(2);
            _overrun = Start(3);
            _lift = Start(4);
        }
        finally { AudioServer.Unlock(); }
    }

    private static AudioStreamWav Load(string name) => GD.Load<AudioStreamWav>($"res://art/cars/f2004/{name}.res");

    public void Update(Car car, float dt, float onboard, float doppler, float air, float gain)
    {
        _rpm = Mathf.Lerp(_rpm, car.Rpm, 1 - MathF.Exp(-dt / 0.012f));
        _load = Mathf.Lerp(_load, car.ThrottleApplied, 1 - MathF.Exp(-dt / 0.025f));
        float level = gain * Mathf.Lerp(0.78f, 1f, onboard) * Mathf.Clamp(air / 10000, 0.25f, 1);
        _shiftAge += dt;
        _prevAge += dt;
        if (_gear != car.Gear && _gear > 0 && car.Gear > 0 && car.Box.Shifting)
        {
            bool up = car.Gear > _gear;
            var shot = up ? _up : _down;
            // A shift on top of a shot still sounding (a run of downshifts) fades the old shot out under the new
            // one instead of cutting it, and the loops stay ducked, so the run doesn't gap between shots.
            if (_prev != AudioStreamPlaybackPolyphonic.InvalidId) _playback.StopStream(_prev);
            _prev = _shift != AudioStreamPlaybackPolyphonic.InvalidId && _playback.IsStreamPlaying(_shift) ? _shift : AudioStreamPlaybackPolyphonic.InvalidId;
            _prevLevel = _shiftLevel;
            _prevAge = 0;
            _duckFrom = Ducked();
            _shiftAge = 0;
            _shiftLevel = Mathf.SmoothStep(2, 14, car.SpeedMs) * (up ? _load : 0.8f);
            float pitch = Mathf.Clamp(_rpm / (up ? 17800 : 14800), 0.62f, 1.17f) * doppler;
            _shiftFade = ShotFade / pitch;
            _shift = _playback.PlayStream(shot, 0, Db(level * _shiftLevel), pitch);
            _shiftTime = (float)shot.GetLength() / pitch;
        }
        _gear = car.Gear;
        _shiftTime = Math.Max(0, _shiftTime - dt);
        if (_shift != AudioStreamPlaybackPolyphonic.InvalidId)
        {
            if (_playback.IsStreamPlaying(_shift)) _playback.SetStreamVolume(_shift, Db(level * _shiftLevel));
            else _shift = AudioStreamPlaybackPolyphonic.InvalidId;
        }
        if (_prev != AudioStreamPlaybackPolyphonic.InvalidId)
        {
            float gone = _prevAge / _shiftFade;
            if (gone < 1 && _playback.IsStreamPlaying(_prev)) _playback.SetStreamVolume(_prev, Db(level * _prevLevel * MathF.Sqrt(1 - gone * gone)));
            else
            {
                _playback.StopStream(_prev);
                _prev = AudioStreamPlaybackPolyphonic.InvalidId;
            }
        }

        float duck = 1 - 0.80f * _shiftLevel * Ducked();
        float drive = MathF.Sqrt(_load) * level * duck;
        float coast = MathF.Sqrt(1 - _load) * level * duck;
        float mid = Mathf.SmoothStep(10500, 14800, _rpm);
        float high = Mathf.SmoothStep(14800, 18100, _rpm);
        float fastLift = Mathf.SmoothStep(11500, 15700, _rpm);
        float pitchScale = _rpm / UnityRpm * doppler;
        Set(_pickup, pitchScale, drive * MathF.Sqrt(1 - mid));
        Set(_pull, pitchScale, drive * MathF.Sqrt(mid * (1 - high)));
        Set(_straight, pitchScale, drive * MathF.Sqrt(high));
        Set(_overrun, pitchScale, coast * MathF.Sqrt(1 - fastLift));
        Set(_lift, pitchScale, coast * MathF.Sqrt(fastLift));
    }

    /// <summary>How far the loops are ducked for the recorded shot, 0-1: they give way as the shot fades in (equal
    /// power) and come back over its last 35 ms.</summary>
    private float Ducked()
    {
        float arrived = Mathf.Clamp(_shiftAge / _shiftFade, 0, 1);
        return Mathf.Lerp(_duckFrom, 1, 1 - MathF.Sqrt(1 - arrived * arrived)) * Mathf.SmoothStep(0, 0.035f, _shiftTime);
    }

    private void Set(long voice, float pitch, float gain)
    {
        _playback.SetStreamPitchScale(voice, pitch);
        _playback.SetStreamVolume(voice, Db(gain));
    }

    private static float Db(float gain) => gain > 0.00001f ? Mathf.LinearToDb(gain) : -100;
    public void Release() => _playback.Dispose();
}
