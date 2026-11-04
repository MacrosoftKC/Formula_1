using static F1.Core.Audio.SynthMath;

namespace F1.Core.Audio;

public struct ChassisSoundState
{
    public float SpeedMs;
    /// <summary>Combined tyre slip beyond the peak, 0..1.</summary>
    public float Slip;
    /// <summary>1 while a wheel is locked under braking.</summary>
    public float Lockup;
    public float Kerb, Gravel, Grass;
    public bool DrsOpen;
    /// <summary>1 = onboard (helmet buffeting), 0 = outside.</summary>
    public float Onboard;
}

/// <summary>
/// Non-engine car sounds: wind and helmet buffeting, slick-tyre scrub (a low grind, not a road-car squeal),
/// lock-up groan, sausage-kerb rumble, gravel and grass, and the DRS flap actuator. Stereo output.
/// </summary>
public sealed class ChassisFxSynth
{
    public float SampleRate { get; }
    public ChassisSoundState State;

    private Noise _nl, _nr;
    private OnePole _windL, _windR, _windLow, _kerbLp, _grassLp, _drsLp;
    private Biquad _scrub, _lock, _gravel, _buffet;
    private float _slip, _lockEnv, _kerb, _gravelEnv, _grassEnv, _drsClunk;
    private double _kerbPh, _lockPh, _buffetPh;
    private bool _lastDrs;

    public ChassisFxSynth(float sampleRate = 44100, uint seed = 7)
    {
        SampleRate = sampleRate;
        _nl = new Noise(seed);
        _nr = new Noise(seed * 7919 + 13);
        _windL.SetCutoff(sampleRate, 900);
        _windR.SetCutoff(sampleRate, 900);
        _windLow.SetCutoff(sampleRate, 140);
        _kerbLp.SetCutoff(sampleRate, 260);
        _grassLp.SetCutoff(sampleRate, 500);
        _drsLp.SetCutoff(sampleRate, 160);
        _scrub = Biquad.BandPass(sampleRate, 650, 1.1f);
        _lock = Biquad.BandPass(sampleRate, 1050, 5f);
        _gravel = Biquad.BandPass(sampleRate, 2600, 0.8f);
        _buffet = Biquad.BandPass(sampleRate, 180, 0.9f);
    }

    public void Render(Span<float> left, Span<float> right)
    {
        var st = State;
        float fs = SampleRate;
        float speed = Math.Max(0, st.SpeedMs);
        float onboard = Math.Clamp(st.Onboard, 0, 1);
        float wind = Math.Min(1.2f, speed * speed / 7000f) * (0.45f + 0.55f * onboard) * (st.DrsOpen ? 0.9f : 1f);
        float rollFade = Math.Min(1, speed / 12f);
        _scrub.SetBandPass(fs, 520 + 380 * _slip, 1.1f);
        _windL.SetCutoff(fs, 500 + speed * 9);
        _windR.SetCutoff(fs, 500 + speed * 9);
        if (st.DrsOpen != _lastDrs) { _drsClunk = 1; _lastDrs = st.DrsOpen; }

        float kSlip = Coef(fs, 0.06f), kLock = Coef(fs, 0.03f), kSurf = Coef(fs, 0.02f);
        double kerbHz = Math.Min(70, speed / 1.1); // ~1.1 m between kerb ridges

        for (int i = 0; i < left.Length; i++)
        {
            float a = _nl.Next(), b = _nr.Next();
            float mono = (a + b) * 0.5f;
            _slip += (st.Slip * rollFade - _slip) * kSlip;
            _lockEnv += (st.Lockup * rollFade - _lockEnv) * kLock;
            _kerb += (st.Kerb * rollFade - _kerb) * kSurf;
            _gravelEnv += (st.Gravel * rollFade - _gravelEnv) * kSurf;
            _grassEnv += (st.Grass * rollFade - _grassEnv) * kSurf;

            // wind: decorrelated L/R plus low helmet buffeting
            _buffetPh = (_buffetPh + (6 + speed * 0.08) / fs) % 1.0;
            float buffet = _buffet.Process(mono) * (0.6f + 0.4f * (float)Math.Sin(_buffetPh * Math.Tau)) * onboard;
            float wl = _windL.Process(a) * wind * 0.5f + buffet * wind * 0.9f;
            float wr = _windR.Process(b) * wind * 0.5f + buffet * wind * 0.9f;

            // tyres
            float scrub = _scrub.Process(mono) * _slip * _slip * 0.55f;
            _lockPh = (_lockPh + (180 + speed * 1.5) / fs) % 1.0;
            float lockTone = _lock.Process(mono) * (0.7f + 0.3f * (float)Math.Sin(_lockPh * Math.Tau));
            float lockup = lockTone * _lockEnv * 0.9f;

            // kerbs, gravel, grass
            _kerbPh = (_kerbPh + kerbHz / fs) % 1.0;
            float ridge = (float)(_kerbPh < 0.5 ? 1 : -1);
            float kerb = _kerbLp.Process(ridge + mono * 0.4f) * _kerb * 0.45f;
            float gravel = _gravel.Process(mono) * _gravelEnv * (0.5f + 0.5f * Math.Abs(_nl.Next())) * 0.5f;
            float grass = _grassLp.Process(mono) * _grassEnv * 0.35f + _windLow.Process(mono) * _grassEnv * 0.6f;

            _drsClunk *= 0.9985f;
            float drs = _drsClunk > 1e-3f ? (_drsLp.Process(mono) * 3f + mono * 0.15f) * _drsClunk * _drsClunk * 0.25f * (0.3f + 0.7f * onboard) : 0;

            float common = scrub + lockup + kerb + gravel + grass + drs;
            left[i] = wl + common;
            right[i] = wr + common;
        }
    }
}
