using static F1.Core.Audio.SynthMath;

namespace F1.Core.Audio;

/// <summary>What the power unit is doing right now, as the sound model sees it. Set once per audio block.</summary>
public struct PowerUnitSoundState
{
    public float Rpm;
    /// <summary>Throttle actually applied to the ICE, 0..1.</summary>
    public float Throttle;
    public float SpeedMs;
    /// <summary>MGU-K electrical power, + deploying / − harvesting (W).</summary>
    public float MguKPowerW;
    public int Gear;
    public bool PitLimiter;
    public float RevLimit;
    /// <summary>1 = onboard (airbox, MGU-K and gearbox up close), 0 = outside the car (exhaust dominated).</summary>
    public float Onboard;
    /// <summary>Frequency scale from the listener's relative motion (1 = none).</summary>
    public float Doppler;
    /// <summary>Air-absorption cut-off for a distant listener (Hz); ≥ 16 kHz means none.</summary>
    public float AirCutoffHz;
}

/// <summary>
/// Procedural sound of a 2014+ 1.6 L turbo-hybrid V6 power unit, built from first principles rather than samples:
/// <list type="bullet">
/// <item>Exhaust pulse train: six firings per 720° (firing frequency = 3 × rpm / 60, ≈ 600 Hz at 12k), each a short
/// pressure pulse with a little cylinder-to-cylinder spread (bank-to-bank runner lengths, combustion variance). That
/// spread is what puts the half-orders in and gives the hybrid V6 its rasp instead of a pure buzz.</item>
/// <item>Single central tailpipe resonance (comb) and fixed exhaust/airbox formants, so timbre shifts with rpm the
/// way a real engine does as harmonics sweep through fixed resonances.</item>
/// <item>The turbine sits in the exhaust stream and eats pulse energy: high-frequency content is muffled under load.
/// The MGU-H keeps the turbo spooled off throttle, so the whistle hangs on through braking zones.</item>
/// <item>MGU-K geared to the crank: an electric whine that tracks rpm, loudest while harvesting under braking.</item>
/// <item>Seamless upshifts (≈12 ms ignition cut and a bark), auto-blipped downshifts, cylinder-cut overrun burble with
/// the odd afterfire pop, the pit-limiter "brap-brap-brap", the rev limiter, and straight-cut gear whine.</item>
/// </list>
/// Pure C#, no engine dependencies; render with <see cref="Render"/>.
/// </summary>
public sealed class HybridV6Synth
{
    public float SampleRate { get; }
    public PowerUnitSoundState State;

    private Noise _noise;
    // crank
    private float _rpm, _thr, _load;
    private double _cycle; // 0..1 over 720°
    private int _next;
    // Firing order alternates banks; the two banks' runners differ in length into the single turbine, so their
    // pulses arrive with different strength and a few crank-degrees early/late (fraction of the 120° interval).
    private static readonly float[] CylinderLevel = { 1.00f, 0.72f, 0.94f, 0.78f, 1.04f, 0.70f };
    private static readonly double[] CylinderTiming = { 0.0, 0.07, -0.03, 0.06, -0.02, 0.05 };

    // exhaust pulses
    private struct Pulse { public float Age, Width, Amp; }
    private readonly Pulse[] _pulses = new Pulse[8];
    private float _fireEnv;
    private readonly float[] _comb;
    private int _combPos;
    private OnePole _combLp;
    private Biquad[] _exhaust, _intake;
    private static readonly (float F, float Q, float G)[] ExhaustFormants = { (115, 1.1f, 0.75f), (430, 1.6f, 1.0f), (880, 2.2f, 0.85f), (1650, 2.6f, 0.55f), (3000, 3.0f, 0.28f) };
    private static readonly (float F, float Q, float G)[] IntakeFormants = { (240, 1.3f, 0.9f), (610, 1.6f, 1.0f), (1250, 2.0f, 0.6f), (2300, 2.6f, 0.3f) };
    private OnePole _intakeLp, _muffle, _air;
    private Biquad _rasp;
    private DcBlock _dc;

    // turbo / MGU-H
    private float _spool = 0.3f, _whoosh, _prevThr;
    private double _turboPh;
    private Biquad _turboBand, _whooshBand;

    // MGU-K and gearbox
    private float _kDeploy, _kHarvest;
    private double _kPh, _gearPh;

    // events
    private int _cutSamples, _barkPulses, _blipSamples, _popDelay = -1;
    private float _popEnv, _popAmp, _clack;
    private double _thumpPh;
    private OnePole _popLp;
    private double _limiterPh, _revPh;

    public HybridV6Synth(float sampleRate = 44100, uint seed = 1)
    {
        SampleRate = sampleRate;
        _noise = new Noise(seed);
        _comb = new float[Math.Max(8, (int)(sampleRate * 0.0029f))]; // ≈ 1 m of hot tailpipe
        _exhaust = ExhaustFormants.Select(f => Biquad.BandPass(sampleRate, f.F, f.Q)).ToArray();
        _intake = IntakeFormants.Select(f => Biquad.BandPass(sampleRate, f.F, f.Q)).ToArray();
        _combLp.SetCutoff(sampleRate, 2200);
        _intakeLp.SetCutoff(sampleRate, 1800);
        _popLp.SetCutoff(sampleRate, 1400);
        _rasp = Biquad.BandPass(sampleRate, 2300, 0.9f);
        _turboBand = Biquad.BandPass(sampleRate, 4000, 9);
        _whooshBand = Biquad.BandPass(sampleRate, 1800, 1.2f);
        State = new PowerUnitSoundState { Rpm = 4000, RevLimit = 15000, Doppler = 1, AirCutoffHz = 20000, Onboard = 1 };
        _rpm = 4000;
    }

    /// <summary>Call when the gearbox changes gear (seamless-shift cut on the way up, auto-blip on the way down).</summary>
    public void GearChanged(int from, int to)
    {
        if (to <= 0 || from <= 0) return;
        if (to > from)
        {
            _cutSamples = (int)(SampleRate * 0.012f);
            _barkPulses = 3;
        }
        else
        {
            _blipSamples = (int)(SampleRate * 0.05f);
            if (_noise.Next01() < 0.45f) _popDelay = (int)(SampleRate * (0.06f + 0.05f * _noise.Next01()));
        }
        _clack = 1f;
    }

    /// <summary>Renders <paramref name="output"/>.Length mono samples, added to nothing (overwrites).</summary>
    public void Render(Span<float> output)
    {
        var st = State;
        float fs = SampleRate;
        float kRpm = Coef(fs, 0.008f), kThr = Coef(fs, 0.012f), kLoad = Coef(fs, 0.05f);
        float doppler = Math.Clamp(st.Doppler <= 0 ? 1 : st.Doppler, 0.5f, 2f);
        float onboard = Math.Clamp(st.Onboard, 0, 1);
        float speed = Math.Max(0, st.SpeedMs);
        float revLimit = st.RevLimit > 0 ? st.RevLimit : 15000;

        // per-block filter tuning
        _air.SetCutoff(fs, Math.Min(st.AirCutoffHz <= 0 ? 20000 : st.AirCutoffHz, fs * 0.45f));
        float spoolTarget = Math.Clamp(0.3f + 0.7f * st.Throttle * SmoothStep(5500, 10500, st.Rpm), 0, 1);
        spoolTarget = Math.Max(spoolTarget, 0.5f * SmoothStep(6000, 11000, st.Rpm)); // MGU-H holds speed off throttle
        float turboHz = (2600 + 5400 * _spool) * doppler;
        _turboBand.SetBandPass(fs, turboHz, 9);
        _whooshBand.SetBandPass(fs, (1100 + 1400 * _whoosh) * doppler, 1.3f);
        float kPower = Coef(fs, 0.04f);
        float deployT = Math.Clamp(st.MguKPowerW / 120_000f, 0, 1);
        float harvestT = Math.Clamp(-st.MguKPowerW / 120_000f, 0, 1);
        bool moving = speed > 3;
        bool pitLimiter = st.PitLimiter && speed > 6;

        for (int i = 0; i < output.Length; i++)
        {
            _rpm += (st.Rpm - _rpm) * kRpm;
            float thr = st.Throttle;
            // ignition-cut patterns that override the pedal
            bool cut = false;
            if (_cutSamples > 0) { _cutSamples--; cut = true; }
            if (_blipSamples > 0) { _blipSamples--; thr = Math.Max(thr, 0.8f); }
            if (pitLimiter)
            {
                _limiterPh = (_limiterPh + 11.0 / fs) % 1.0;
                if (_limiterPh > 0.5) cut = true; else thr = Math.Max(thr, 0.6f);
            }
            if (_rpm > revLimit - 120)
            {
                // hard rev-limiter chatter
                _revPh = (_revPh + 26.0 / fs) % 1.0;
                if (thr > 0.5f && _revPh > 0.55) cut = true;
            }

            _thr += (thr - _thr) * kThr;
            _load += (_thr - _load) * kLoad;

            // ---- crank and firing ----
            double cyclesPerSec = _rpm / 120.0 * doppler;
            _cycle += cyclesPerSec / fs;
            if (_cycle >= 1) { _cycle -= 1; _next = 0; }
            while (_next < 6 && _cycle * 6 >= _next + CylinderTiming[_next])
            {
                Fire(_next, cut);
                _next++;
            }

            // excitation: sum of active pulses (raised-cosine pressure pulse then a rarefaction)
            float exc = 0;
            for (int p = 0; p < _pulses.Length; p++)
            {
                ref var pu = ref _pulses[p];
                if (pu.Amp == 0) continue;
                float t = pu.Age / pu.Width;
                if (t < 1) exc += pu.Amp * (0.5f - 0.5f * MathF.Cos(2 * MathF.PI * t));
                else if (t < 3) exc -= pu.Amp * 0.32f * (0.5f - 0.5f * MathF.Cos(MathF.PI * (t - 1)));
                else { pu.Amp = 0; continue; }
                pu.Age++;
            }

            // tailpipe resonance
            float fb = _combLp.Process(_comb[_combPos]);
            float pipe = exc - 0.36f * fb;
            _comb[_combPos] = pipe;
            if (++_combPos >= _comb.Length) _combPos = 0;

            float exhaust = pipe * 0.22f;
            for (int f = 0; f < _exhaust.Length; f++) exhaust += _exhaust[f].Process(pipe) * ExhaustFormants[f].G;
            float intakeIn = _intakeLp.Process(exc);
            float intake = 0;
            for (int f = 0; f < _intake.Length; f++) intake += _intake[f].Process(intakeIn) * IntakeFormants[f].G;

            // combustion rasp: noise gated by each firing
            _fireEnv *= 0.9965f;
            float n = _noise.Next();
            float rasp = _rasp.Process(n) * _fireEnv * (0.25f + 0.75f * _load);

            // turbine muffles the exhaust under load; onboard you mostly hear the airbox above your head
            _muffle.SetCutoff(fs, (2200 + 3800 * (1 - _load * 0.6f)) * (0.8f + 0.4f * onboard));
            float engine = _muffle.Process(exhaust * (1.25f - 0.65f * onboard) + intake * (0.35f + 1.05f * onboard)) + rasp * 0.9f;

            // afterfire pop and its low thump
            if (_popDelay >= 0 && _popDelay-- == 0) TriggerPop(0.7f);
            _popEnv *= 0.9975f;
            _thumpPh += 85.0 * doppler / fs;
            float pop = _popEnv > 1e-4f ? (_popLp.Process(n) * 2.2f + (float)Math.Sin(_thumpPh * Math.Tau) * 0.5f) * _popEnv * _popAmp : 0;

            // asymmetric saturation for exhaust bark
            float drive = 1.6f + 1.2f * _load;
            float ice = MathF.Tanh(drive * (engine + 0.25f * engine * engine) + pop) * (0.5f + 0.5f * _load);

            // ---- turbo / MGU-H ----
            _spool += (spoolTarget - _spool) * (spoolTarget > _spool ? 1f / (fs * 0.15f) : 1f / (fs * 1.1f));
            _turboPh = (_turboPh + turboHz / fs) % 1.0;
            float turbo = ((float)Math.Sin(_turboPh * Math.Tau) * 0.010f + _turboBand.Process(n) * 0.11f) * _spool * _spool * (0.35f + 0.65f * _thr);
            if (_prevThr - st.Throttle > 0.4f && _spool > 0.55f) _whoosh = 1f; // lift: compressor surge / wastegate chuff
            _prevThr += (st.Throttle - _prevThr) * Coef(fs, 0.05f);
            _whoosh *= 0.99985f;
            turbo += _whooshBand.Process(n) * 0.09f * _whoosh * _whoosh;

            // ---- MGU-K (geared to the crank) ----
            _kDeploy += (deployT - _kDeploy) * kPower;
            _kHarvest += (harvestT - _kHarvest) * kPower;
            double kHz = _rpm / 60.0 * 3.3 * 4 * doppler;
            _kPh = (_kPh + kHz / fs) % 1.0;
            double kp = _kPh * Math.Tau;
            float kTone = (float)(Math.Sin(kp) + 0.35 * Math.Sin(2 * kp + 0.4) + 0.12 * Math.Sin(3 * kp));
            float mguK = kTone * (0.010f * _kDeploy + 0.024f * _kHarvest) * (0.35f + 0.65f * onboard);

            // ---- straight-cut gearbox ----
            float gear = 0;
            if (moving && st.Gear > 0)
            {
                _gearPh = (_gearPh + speed * 40.7 * doppler / fs) % 1.0;
                gear = (float)Math.Sin(_gearPh * Math.Tau) * (0.004f + 0.006f * (1 - _thr)) * (0.2f + 0.8f * onboard);
            }
            _clack *= 0.996f;
            float clack = _clack > 1e-3f ? n * _clack * _clack * 0.12f * (0.3f + 0.7f * onboard) : 0;

            float s = ice * 0.62f + turbo + mguK + gear + clack;
            s = _air.Process(_dc.Process(s));
            output[i] = s;
        }
    }

    private void Fire(int cyl, bool cut)
    {
        float rpm = _rpm, thr = _thr;
        float amp;
        bool overrun = thr < 0.08f && rpm > 6500;
        if (cut)
            amp = 0.07f; // valves still pump air: a weak pulse
        else if (overrun)
        {
            // fuel cut on most cylinders for engine braking: an irregular, burbling stutter
            float u = _noise.Next01();
            amp = u < 0.38f ? 0.22f + 0.28f * _noise.Next01() : 0.06f;
            if (u > 0.985f - 0.012f * Math.Clamp((rpm - 6500) / 5000, 0, 1)) TriggerPop(0.35f + 0.4f * _noise.Next01());
        }
        else
            amp = 0.26f + 0.74f * MathF.Pow(thr, 0.8f);

        if (_barkPulses > 0 && !cut) { amp *= 1.45f; _barkPulses--; }

        // combustion variance is largest at idle and light load
        float jitter = 0.05f + 0.18f * (1 - SmoothStep(4500, 8000, rpm)) * (1 - thr * 0.7f);
        amp *= CylinderLevel[cyl] * (1 + jitter * _noise.Next());

        // harder load = faster pressure rise = a sharper, brighter pulse
        float width = SampleRate * (0.00055f - 0.0003f * _load);
        for (int p = 0; p < _pulses.Length; p++)
        {
            if (_pulses[p].Amp != 0) continue;
            _pulses[p] = new Pulse { Age = 0, Width = Math.Max(2, width), Amp = amp };
            break;
        }
        _fireEnv = Math.Max(_fireEnv, amp);
    }

    private void TriggerPop(float amp)
    {
        _popEnv = 1;
        _popAmp = amp;
        _thumpPh = 0;
    }
}
