using F1.Core.Audio;

namespace F1.Core.Tests;

public class AudioSynthTests
{
    private const int Fs = 44100;

    private static float[] Render(HybridV6Synth synth, PowerUnitSoundState state, double seconds, double skip = 0.3)
    {
        synth.State = state;
        var warm = new float[(int)(Fs * skip)];
        synth.Render(warm);
        var buf = new float[(int)(Fs * seconds)];
        for (int off = 0; off < buf.Length; off += 512)
            synth.Render(buf.AsSpan(off, Math.Min(512, buf.Length - off)));
        return buf;
    }

    private static PowerUnitSoundState Flat(float rpm, float throttle) => new()
    {
        Rpm = rpm, Throttle = throttle, SpeedMs = 60, Gear = 6, RevLimit = 15000, Onboard = 1, Doppler = 1, AirCutoffHz = 20000,
    };

    /// <summary>Magnitude of one frequency (Goertzel, Hann-windowed).</summary>
    private static double Mag(float[] x, double freq)
    {
        double w = 2 * Math.PI * freq / Fs, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double win = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (x.Length - 1));
            double s = x[i] * win + c * s1 - s2;
            s2 = s1; s1 = s;
        }
        return Math.Sqrt(s1 * s1 + s2 * s2 - c * s1 * s2);
    }

    private static double Rms(ReadOnlySpan<float> x)
    {
        double sum = 0;
        foreach (var v in x) sum += v * v;
        return Math.Sqrt(sum / Math.Max(1, x.Length));
    }

    [Fact]
    public void FiringFrequencyTracksRpm()
    {
        // 10,800 rpm → 540 Hz firing (3 pulses per revolution); half-orders sit on multiples of 90 Hz.
        var x = Render(new HybridV6Synth(Fs), Flat(10_800, 1), 1.0);
        double firing = Mag(x, 540), halfOrder = Mag(x, 270), offHarmonic = Math.Max(Mag(x, 495), Mag(x, 585));
        Assert.True(firing > 5 * offHarmonic, $"firing {firing:F1} vs off-harmonic {offHarmonic:F1}");
        Assert.True(halfOrder > 3 * offHarmonic, $"half-order {halfOrder:F1} vs off-harmonic {offHarmonic:F1}");
    }

    [Fact]
    public void FullThrottleIsLouderThanOverrunAndIdleIsAudible()
    {
        double full = Rms(Render(new HybridV6Synth(Fs), Flat(11_000, 1), 1.0));
        double overrun = Rms(Render(new HybridV6Synth(Fs), Flat(11_000, 0), 1.0));
        double idle = Rms(Render(new HybridV6Synth(Fs), Flat(4_100, 0) with { SpeedMs = 0, Gear = 0 }, 1.0));
        Assert.True(full > 2 * overrun, $"full {full:F3} overrun {overrun:F3}");
        Assert.True(idle > full * 0.12, $"idle {idle:F3} full {full:F3}");
    }

    [Fact]
    public void PitLimiterProducesTheBrapBrapModulation()
    {
        static double EnvelopeVariation(float[] x)
        {
            const int win = Fs / 200; // 5 ms
            var env = new List<double>();
            for (int i = 0; i + win <= x.Length; i += win) env.Add(Rms(x.AsSpan(i, win)));
            double mean = env.Average();
            return Math.Sqrt(env.Average(e => (e - mean) * (e - mean))) / mean;
        }
        var cruise = Render(new HybridV6Synth(Fs), Flat(6_500, 0.9f) with { SpeedMs = 22 }, 1.0);
        var limiter = Render(new HybridV6Synth(Fs), Flat(6_500, 0.9f) with { SpeedMs = 22, PitLimiter = true }, 1.0);
        Assert.True(EnvelopeVariation(limiter) > 2 * EnvelopeVariation(cruise));
    }

    [Fact]
    public void OutputStaysFiniteAndBoundedThroughEvents()
    {
        var synth = new HybridV6Synth(Fs);
        var fx = new ChassisFxSynth(Fs);
        var buf = new float[512];
        var l = new float[512];
        var r = new float[512];
        var rng = new Random(3);
        int gear = 1;
        for (int block = 0; block < 2000; block++)
        {
            float rpm = 4000 + 11_200 * (float)rng.NextDouble();
            int next = Math.Clamp(gear + rng.Next(-1, 2), 1, 8);
            if (next != gear) synth.GearChanged(gear, next);
            gear = next;
            synth.State = Flat(rpm, (float)rng.NextDouble()) with
            {
                MguKPowerW = (float)(rng.NextDouble() * 240_000 - 120_000), PitLimiter = rng.Next(10) == 0,
                Onboard = (float)rng.NextDouble(), Doppler = 0.8f + 0.4f * (float)rng.NextDouble(), AirCutoffHz = 1500 + 18_000 * (float)rng.NextDouble(),
            };
            fx.State = new ChassisSoundState { SpeedMs = 90 * (float)rng.NextDouble(), Slip = (float)rng.NextDouble(), Lockup = rng.Next(2), Kerb = rng.Next(2), Gravel = rng.Next(2), Grass = rng.Next(2), DrsOpen = rng.Next(2) == 0, Onboard = 1 };
            synth.Render(buf);
            fx.Render(l, r);
            for (int i = 0; i < buf.Length; i++)
            {
                Assert.True(float.IsFinite(buf[i]) && Math.Abs(buf[i]) < 2f, $"engine sample {buf[i]} at block {block}");
                Assert.True(float.IsFinite(l[i]) && Math.Abs(l[i]) < 3f && float.IsFinite(r[i]), $"fx sample {l[i]} at block {block}");
            }
        }
    }
}
