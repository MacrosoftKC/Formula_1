using F1.Game.Vehicle;
using Godot;

namespace F1.Game.Audio;

/// <summary>
/// The W11's onboard sound, cut from the supplied Monza pole lap by tools/AudioPreview/w11/make_banks.py.
/// Stereo loops share one crank-angle clock (unity pitch at 12,000 RPM) and Godot mixes them continuously;
/// physics ticks set their pitch and level from the car's RPM, road speed, throttle pedal and gearbox.
/// <list type="bullet">
/// <item>Full-throttle loops come in two sets recorded in different gears: 2nd-5th gear chicane exits (engine and
/// airbox, little wind) and 6th-8th gear straights (road and wind noise), blended by road speed.</item>
/// <item>Off-throttle loops (overrun and MGU-K harvest) run from idle to the top of the rev range.</item>
/// <item>Loops keep their recorded relative level, so the mix follows the real loudness of each state. Throttle
/// response has the recording's fast step and slower build-up as boost arrives.</item>
/// <item>Gear changes hand over to the recorded torque cut (upshift) or rev-matching blip (downshift).</item>
/// </list>
/// </summary>
internal sealed class W11EngineVoice
{
    private readonly record struct Bank(string Name, float Rpm);

    // Recorded centre RPM of each loop, as make_banks.py prints them.
    private static readonly Bank[] LowGear =
    {
        new("LowZ", 5500), new("LowA", 7104), new("LowB", 7924), new("LowC", 8889),
        new("LowD", 9498), new("LowE", 11229), new("LowF", 11943), new("LowTop", 13600),
    };
    private static readonly Bank[] HighGear = { new("HighA", 11125), new("HighB", 11802), new("HighC", 12150), new("HighTop", 13600) };
    private static readonly Bank[] Coast = { new("Idle", 4600), new("CoastLow", 8773), new("CoastHigh", 10577) };
    private const int CoastLow = 1, CoastHigh = 2;
    /// <summary>Streams the polyphonic player needs: every loop, the limiter and one gear-change shot, with headroom.</summary>
    public static readonly int Voices = LowGear.Length + HighGear.Length + Coast.Length + 4;

    private const float UnityRpm = 12000;
    /// <summary>Keeps the full-throttle straight at the loudness the rest of the game's mix was balanced to.</summary>
    private const float Calibration = 1.08f;
    /// <summary>Road speed (m/s) over which the 6th-8th gear loops take over from the chicane-exit loops.</summary>
    private const float HighGearFrom = 42, HighGearTo = 72;
    /// <summary>RPM below which the straights have no recording, so the chicane-exit loops cover it.</summary>
    private const float HighGearRpmFrom = 9000, HighGearRpmTo = 10400;

    // Never call AudioStreamPlaybackPolyphonic.Stop() here: it unbalances the audio mixer lock, so after a scene
    // reload (Restart / Quit to menu) the mixer thread deadlocks (no sound, and the game hangs on exit). The
    // AudioStreamPlayer that owns the playback stops every voice when it leaves the tree.
    private readonly AudioStreamPlaybackPolyphonic _playback;
    private readonly long[] _low, _high, _coast;
    private readonly long _limiterVoice;
    private readonly float[] _lowAmp = new float[LowGear.Length], _highAmp = new float[HighGear.Length], _coastAmp = new float[Coast.Length];
    private readonly AudioStreamWav _upLow = Load("UpshiftLow"), _upHigh = Load("UpshiftHigh");
    private readonly AudioStreamWav _downLow = Load("DownshiftLow"), _downHigh = Load("DownshiftHigh");
    private long _shift = AudioStreamPlaybackPolyphonic.InvalidId;
    private bool _shiftUp;
    private float _shiftLeft, _shiftGain;
    private int _gear;
    private float _rpm, _load, _boost, _limiter;

    public float SynthGain { get; private set; }

    public W11EngineVoice(AudioStreamPlaybackPolyphonic playback, Car car)
    {
        _playback = playback;
        _gear = car.Gear;
        _rpm = car.Rpm;
        AudioStreamWav[] low = LowGear.Select(b => Load(b.Name)).ToArray(), high = HighGear.Select(b => Load(b.Name)).ToArray();
        AudioStreamWav[] coast = Coast.Select(b => Load(b.Name)).ToArray();
        var limiter = Load("Limiter");
        // Stage every loop on the same mixer boundary so crossfades keep their crank phase.
        AudioServer.Lock();
        try
        {
            _low = low.Select(Start).ToArray();
            _high = high.Select(Start).ToArray();
            _coast = coast.Select(Start).ToArray();
            _limiterVoice = Start(limiter);
        }
        finally { AudioServer.Unlock(); }
    }

    private long Start(AudioStreamWav loop) => _playback.PlayStream(loop, 0, -100, _rpm / UnityRpm);

    private static AudioStreamWav Load(string name) =>
        GD.Load<AudioStreamWav>($"res://art/cars/w11/{name}.res");

    public void Update(Car car, float dt, float onboard, float doppler, float air, float gain)
    {
        // Pitch follows shaft speed, including wheelspin, clutch slip and the new gear ratio.
        _rpm = Follow(_rpm, car.Rpm, dt, car.Box.Shifting ? 0.004f : 0.015f);
        float highGear = Mathf.SmoothStep(HighGearFrom, HighGearTo, car.SpeedMs) * Mathf.SmoothStep(HighGearRpmFrom, HighGearRpmTo, _rpm);

        // The driver's foot sets the sound; traction control trims it a little, as it trims torque.
        // The pit limiter and an empty tank cut the engine itself, so they follow the applied throttle.
        float applied = Mathf.Clamp(car.ThrottleApplied, 0, 1);
        float pedal = car.PitLimiter || car.PU.FuelKg <= 0 ? applied : Mathf.Lerp(applied, Mathf.Clamp(car.Inputs.Throttle, 0, 1), 0.7f);
        _load = Follow(_load, pedal, dt, pedal > _load ? 0.020f : 0.025f);
        _boost = Follow(_boost, pedal, dt, pedal > _boost ? 0.30f : 0.35f);
        float limit = Mathf.SmoothStep((float)car.PU.RevLimit - 165, (float)car.PU.RevLimit, _rpm) * pedal;
        _limiter = Follow(_limiter, limit, dt, 0.030f);

        // The trackside camera also hears the external exhaust synth and distance filtering.
        float sampled = (0.27f + 0.73f * onboard) * Mathf.Clamp(air / 9500f, 0, 1);
        SynthGain = 1 - sampled;
        float level = gain * sampled * Calibration;

        int gear = car.Gear;
        if (gear != _gear)
        {
            if (Math.Abs(gear - _gear) == 1 && _gear > 0 && gear > 0 && car.Box.Shifting)
                PlayShift(gear > _gear, car, highGear, doppler, level);
            _gear = gear;
        }
        _shiftLeft = Math.Max(0, _shiftLeft - dt);
        // The recorded shift stands in for most of the loops it overlaps, then hands back over its last 12 ms.
        float duck = 0.6f * _shiftGain * Mathf.Clamp(_shiftLeft / 0.012f, 0, 1);
        float load = Mathf.SmoothStep(0, 1, _load);
        // Boost builds for a few tenths after a pickup and lingers after a lift (the turbo and MGU-H keep
        // spinning), so the pull swells in and the overrun falls away in two stages, as in the recording.
        float drive = MathF.Sqrt(load) * (0.78f + 0.22f * _boost) * (_shiftUp ? 1 - duck : 1);
        float coast = MathF.Sqrt(1 - load) * (1 + 0.55f * _boost) * (_shiftUp ? 1 : 1 - duck);

        Spread(LowGear, _rpm, _lowAmp, drive * MathF.Sqrt((1 - highGear) * (1 - _limiter)));
        Spread(HighGear, _rpm, _highAmp, drive * MathF.Sqrt(highGear * (1 - _limiter)));
        Spread(Coast, _rpm, _coastAmp, coast);
        // The fast overrun loop was recorded at 300 km/h; at low speed its wind belongs to the slower one.
        float wind = Mathf.SmoothStep(22, 50, car.SpeedMs);
        float fast = _coastAmp[CoastHigh] * _coastAmp[CoastHigh];
        _coastAmp[CoastHigh] = MathF.Sqrt(fast * wind);
        _coastAmp[CoastLow] = MathF.Sqrt(_coastAmp[CoastLow] * _coastAmp[CoastLow] + fast * (1 - wind));

        float pitch = _rpm * doppler / UnityRpm;
        for (int i = 0; i < _low.Length; i++) Set(_low[i], pitch, level * _lowAmp[i]);
        for (int i = 0; i < _high.Length; i++) Set(_high[i], pitch, level * _highAmp[i]);
        for (int i = 0; i < _coast.Length; i++) Set(_coast[i], pitch, level * _coastAmp[i]);
        Set(_limiterVoice, pitch, level * drive * MathF.Sqrt(_limiter));
        if (_shift != AudioStreamPlaybackPolyphonic.InvalidId)
        {
            if (_playback.IsStreamPlaying(_shift)) _playback.SetStreamVolume(_shift, Db(level * _shiftGain));
            else _shift = AudioStreamPlaybackPolyphonic.InvalidId;
        }
    }

    /// <summary>Equal-power crossfade between the two loops recorded either side of <paramref name="rpm"/>.</summary>
    private static void Spread(Bank[] banks, float rpm, float[] amp, float scale)
    {
        Array.Clear(amp);
        int i = 1;
        while (i < banks.Length && rpm > banks[i].Rpm) i++;
        if (rpm <= banks[0].Rpm) amp[0] = scale;
        else if (i == banks.Length) amp[^1] = scale;
        else
        {
            float x = Mathf.SmoothStep(banks[i - 1].Rpm, banks[i].Rpm, rpm);
            amp[i - 1] = scale * MathF.Sqrt(1 - x);
            amp[i] = scale * MathF.Sqrt(x);
        }
    }

    private void PlayShift(bool up, Car car, float highGear, float doppler, float level)
    {
        if (_shift != AudioStreamPlaybackPolyphonic.InvalidId) _playback.StopStream(_shift);
        // Upshifts were recorded flat out, downshifts on the brakes; each plays at the recorded speed,
        // pitched to the new gear's RPM, with the loops it replaces ducked underneath.
        bool high = highGear > 0.5f;
        var clip = up ? high ? _upHigh : _upLow : high ? _downHigh : _downLow;
        float rpmAfter = up ? high ? 11080 : 10950 : high ? 11380 : 10250;
        _shiftUp = up;
        // Torque rebuilds over a few tenths after the cut, so the pull is a little quieter just after an upshift.
        if (up) _boost *= 0.65f;
        _shiftGain = (up ? Mathf.SmoothStep(0, 1, _load) : 0.8f) * Mathf.SmoothStep(2, 12, car.SpeedMs);
        float pitch = Mathf.Clamp(car.Rpm / rpmAfter, 0.6f, 1.3f) * doppler;
        _shift = _playback.PlayStream(clip, 0, Db(level * _shiftGain), pitch);
        bool playing = _shift != AudioStreamPlaybackPolyphonic.InvalidId;
        if (!playing) _shiftGain = 0;
        _shiftLeft = playing ? (float)(clip.GetLength() / pitch) : 0;
    }

    private void Set(long id, float pitch, float level)
    {
        _playback.SetStreamPitchScale(id, pitch);
        _playback.SetStreamVolume(id, Db(level));
    }

    private static float Db(float gain) => gain > 0.00001f ? Mathf.LinearToDb(gain) : -100f;
    private static float Follow(float value, float target, float dt, float seconds) =>
        float.Lerp(value, target, 1 - MathF.Exp(-dt / seconds));
}
