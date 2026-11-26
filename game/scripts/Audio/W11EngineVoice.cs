using F1.Game.Vehicle;
using Godot;

namespace F1.Game.Audio;

/// <summary>
/// The W11's onboard sound, cut from the supplied Assetto Corsa W11 hotlap of Albert Park by
/// tools/AudioPreview/w11/make_banks.py. Stereo loops share one crank-angle clock (unity pitch at 12,000 RPM) and
/// Godot mixes them continuously; physics ticks set their pitch and level from the car's RPM, road speed, throttle
/// pedal and gearbox.
/// <list type="bullet">
/// <item>Full-throttle loops come in two sets recorded in different gears: slow-corner exits (engine and airbox,
/// little wind) and 8th gear on the straights (road and wind noise), blended by road speed.</item>
/// <item>Off-throttle loops (overrun between the downshift blips) run from idle to the top of the rev range.</item>
/// <item>Loops keep their recorded relative level, so the mix follows the reference's loudness in each state.</item>
/// <item>Gear changes hand over to the recorded upshift cut or downshift blip for their duration.</item>
/// </list>
/// </summary>
internal sealed class W11EngineVoice : ISampledEngineVoice
{
    private readonly record struct Bank(string Name, float Rpm);

    // Recorded centre RPM of each loop, as make_banks.py prints them.
    private static readonly Bank[] LowGear =
    {
        new("LowZ", 5500), new("LowA", 7076), new("LowB", 8200), new("LowC", 9001),
        new("LowD", 9868), new("LowE", 11032), new("LowF", 11868), new("LowTop", 12729),
    };
    private static readonly Bank[] HighGear =
        { new("HighZ", 10070), new("HighA", 11053), new("HighB", 11393), new("HighC", 11892), new("HighTop", 13600) };
    private static readonly Bank[] Coast = { new("Idle", 4600), new("CoastLow", 8147), new("CoastHigh", 10718) };
    private const int CoastLow = 1, CoastHigh = 2;
    /// <summary>Streams the polyphonic player needs: every loop, the limiter and one gear-change shot, with headroom.</summary>
    public static readonly int Voices = LowGear.Length + HighGear.Length + Coast.Length + 4;

    private const float UnityRpm = 12000;
    /// <summary>Keeps the full-throttle straight at the loudness the rest of the game's mix was balanced to.</summary>
    private const float Calibration = 1.18f;
    /// <summary>Road speed (m/s) over which the 8th-gear loops take over from the corner-exit loops.</summary>
    private const float HighGearFrom = 42, HighGearTo = 72;
    /// <summary>RPM below which the straights have no recording, so the corner-exit loops cover it.</summary>
    private const float HighGearRpmFrom = 8800, HighGearRpmTo = 9900;
    /// <summary>How far the recorded shift replaces the loops it overlaps, and the downshift blip's level.</summary>
    private const float UpshiftDuck = 0.7f, DownshiftDuck = 0.85f, BlipGain = 1.15f;
    /// <summary>After an upshift the pull comes back over a few tenths: depth and time constant (s).</summary>
    private const float UpshiftSag = 0.3f, UpshiftRecovery = 0.15f;
    /// <summary>Overrun level against the full-throttle loops (Assetto Corsa's lift is a ~4 dB step).</summary>
    private const float CoastGain = 1.08f;

    // Never call AudioStreamPlaybackPolyphonic.Stop() here: it unbalances the audio mixer lock, so after a scene
    // reload (Restart / Quit to menu) the mixer thread deadlocks (no sound, and the game hangs on exit). The
    // AudioStreamPlayer that owns the playback stops every voice when it leaves the tree.
    private readonly AudioStreamPlaybackPolyphonic _playback;
    private readonly long[] _low, _high, _coast;
    private readonly long _limiterVoice;
    private readonly float[] _lowAmp = new float[LowGear.Length], _highAmp = new float[HighGear.Length], _coastAmp = new float[Coast.Length];
    private readonly AudioStreamWav _upLow = Load("UpshiftLow"), _upHigh = Load("UpshiftHigh");
    private readonly AudioStreamWav _downLow = Load("DownshiftLow"), _downHigh = Load("DownshiftHigh");
    private readonly AudioStreamWav[] _loaded;
    private long _shift = AudioStreamPlaybackPolyphonic.InvalidId;
    private bool _shiftUp;
    private float _shiftLeft, _shiftGain;
    private int _gear;
    private float _rpm, _load, _sag, _limiter;

    public float SynthGain { get; private set; }

    public W11EngineVoice(AudioStreamPlaybackPolyphonic playback, Car car)
    {
        _playback = playback;
        _gear = car.Gear;
        _rpm = car.Rpm;
        AudioStreamWav[] low = LowGear.Select(b => Load(b.Name)).ToArray(), high = HighGear.Select(b => Load(b.Name)).ToArray();
        AudioStreamWav[] coast = Coast.Select(b => Load(b.Name)).ToArray();
        var limiter = Load("Limiter");
        _loaded = [.. low, .. high, .. coast, limiter, _upLow, _upHigh, _downLow, _downHigh];
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

    /// <summary>Drops this voice's references to the playback and its streams when the car's audio leaves the scene,
    /// so they are freed with their player instead of lingering until exit. Only releases references: it doesn't
    /// stop the playback (see the note above).</summary>
    public void Release()
    {
        foreach (var stream in _loaded) stream.Dispose();
        _playback.Dispose();
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
        _load = Follow(_load, pedal, dt, 0.015f);
        _sag = Follow(_sag, 0, dt, UpshiftRecovery);
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
        float duck = (_shiftUp ? UpshiftDuck : DownshiftDuck) * Math.Min(_shiftGain, 1) * Mathf.Clamp(_shiftLeft / 0.012f, 0, 1);
        float load = Mathf.SmoothStep(0, 1, _load);
        // Pickups and lifts are one quick step, as in the reference.
        float drive = MathF.Sqrt(load) * (1 - UpshiftSag * _sag) * (_shiftUp ? 1 - duck : 1);
        float coast = MathF.Sqrt(1 - load) * CoastGain * (_shiftUp ? 1 : 1 - duck);

        Spread(LowGear, _rpm, _lowAmp, drive * MathF.Sqrt((1 - highGear) * (1 - _limiter)));
        Spread(HighGear, _rpm, _highAmp, drive * MathF.Sqrt(highGear * (1 - _limiter)));
        Spread(Coast, _rpm, _coastAmp, coast);
        // The fast overrun loop was recorded at 250-300 km/h; at low speed its wind belongs to the slower one.
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
        // Upshifts were recorded flat out, downshifts on the brakes (the blip flares and settles inside the shot);
        // each plays at the recorded speed, pitched to the new gear's RPM, over the loops it replaces.
        bool high = highGear > 0.5f;
        var clip = up ? high ? _upHigh : _upLow : high ? _downHigh : _downLow;
        float rpmAfter = up ? high ? 10760 : 11000 : high ? 10900 : 10800;
        _shiftUp = up;
        // Torque rebuilds over a few tenths after the cut, so the pull is a little quieter just after an upshift.
        if (up) _sag = 1;
        _shiftGain = (up ? Mathf.SmoothStep(0, 1, _load) : BlipGain) * Mathf.SmoothStep(2, 12, car.SpeedMs);
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
