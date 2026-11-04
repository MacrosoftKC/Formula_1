namespace F1.Core.Audio;

/// <summary>RBJ biquad (direct form I). Coefficients can be re-tuned per block without clicks at moderate rates.</summary>
public struct Biquad
{
    private float _b0, _b1, _b2, _a1, _a2;
    private float _x1, _x2, _y1, _y2;

    /// <summary>Band-pass with 0 dB peak gain.</summary>
    public static Biquad BandPass(float sampleRate, float freq, float q)
    {
        var b = new Biquad();
        b.SetBandPass(sampleRate, freq, q);
        return b;
    }

    public void SetBandPass(float sampleRate, float freq, float q)
    {
        freq = Math.Clamp(freq, 20f, sampleRate * 0.45f);
        double w0 = 2 * Math.PI * freq / sampleRate;
        double alpha = Math.Sin(w0) / (2 * q);
        double a0 = 1 + alpha;
        _b0 = (float)(alpha / a0);
        _b1 = 0;
        _b2 = (float)(-alpha / a0);
        _a1 = (float)(-2 * Math.Cos(w0) / a0);
        _a2 = (float)((1 - alpha) / a0);
    }

    public float Process(float x)
    {
        float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x;
        _y2 = _y1; _y1 = y;
        // flush denormals
        if (Math.Abs(_y1) < 1e-20f) _y1 = 0;
        return y;
    }
}

/// <summary>One-pole low-pass.</summary>
public struct OnePole
{
    private float _a, _y;

    public void SetCutoff(float sampleRate, float freq) =>
        _a = 1f - MathF.Exp(-2f * MathF.PI * Math.Clamp(freq, 5f, sampleRate * 0.49f) / sampleRate);

    public float Process(float x) => _y += _a * (x - _y);
}

/// <summary>DC blocker (≈ 20 Hz high-pass).</summary>
public struct DcBlock
{
    private float _x1, _y1;

    public float Process(float x)
    {
        float y = x - _x1 + 0.997f * _y1;
        _x1 = x; _y1 = y;
        return y;
    }
}

/// <summary>Fast deterministic white noise (xorshift32).</summary>
public struct Noise
{
    private uint _s;

    public Noise(uint seed) => _s = seed == 0 ? 0x9E3779B9u : seed;

    /// <summary>Uniform in [-1, 1).</summary>
    public float Next()
    {
        _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
        return (_s >> 8) * (2f / 16777216f) - 1f;
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float Next01() => (Next() + 1f) * 0.5f;
}

internal static class SynthMath
{
    /// <summary>Per-sample smoothing coefficient for a time constant in seconds.</summary>
    public static float Coef(float sampleRate, float tau) => 1f - MathF.Exp(-1f / (sampleRate * Math.Max(tau, 1e-5f)));

    public static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
