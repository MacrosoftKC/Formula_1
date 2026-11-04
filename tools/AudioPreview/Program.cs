using F1.Core.Audio;
using F1.Core.Sim;

// Renders a scripted run of the procedural turbo-hybrid V6 to a WAV so the sound can be auditioned and tuned
// without launching Godot:  dotnet run --project tools/AudioPreview -- [out.wav]
//
// Sequence: idle → grid revs → launch and full-throttle run 1st→8th with ERS deploy → heavy braking (MGU-K harvest,
// downshifts, overrun) → corner exit → pit limiter → trackside TV camera fly-by (Doppler + distance).

const int Fs = 44100;
string outPath = args.Length > 0 ? args[0] : "f1_hybrid_v6_preview.wav";

var engine = new HybridV6Synth(Fs);
var fx = new ChassisFxSynth(Fs);
var box = new Gearbox();
const double WheelR = 0.36;
double RpmAt(double v, int gear) => Math.Max(PowerUnit.IdleRpm, v / (2 * Math.PI * WheelR) * box.OverallRatio(gear) * 60);

var left = new List<float>();
var right = new List<float>();
const int Block = 441; // 10 ms control rate, like a game frame or two
var eng = new float[Block];
var fl = new float[Block];
var fr = new float[Block];

double v = 0; // m/s
int gear = 1;
double t = 0;

void Step(float throttle, float brake, float onboard = 1, float doppler = 1, float gain = 1, float air = 20000, bool pit = false, float? rpmOverride = null, float slip = 0)
{
    // longitudinal model: traction-limited launch, drag-limited top speed, ~5 g braking
    double accel = throttle * Math.Min(11, 16 * (1 - Math.Pow(v / 96, 2))) - brake * (20 + 0.0045 * v * v) - 0.0004 * v * v;
    v = Math.Max(0, v + accel * Block / Fs);
    int before = gear;
    double rpm = RpmAt(v, gear);
    if (throttle > 0.5f && rpm > 11900 && gear < 8) gear++;
    else if (gear > 1 && RpmAt(v, gear - 1) < 11400 && rpm < (throttle > 0.5 ? 9800 : 9000)) gear--;
    if (gear != before) engine.GearChanged(before, gear);
    rpm = rpmOverride ?? RpmAt(v, gear);

    double mguK = brake > 0.1 && v > 17 ? -120_000 * Math.Min(1, brake * 1.6) : throttle > 0.85 && v > 11 ? 120_000 : 0;
    engine.State = new PowerUnitSoundState
    {
        Rpm = (float)rpm, Throttle = throttle, SpeedMs = (float)v, MguKPowerW = (float)mguK, Gear = gear,
        PitLimiter = pit, RevLimit = 15000, Onboard = onboard, Doppler = doppler, AirCutoffHz = air,
    };
    fx.State = new ChassisSoundState { SpeedMs = (float)v, Onboard = onboard, Slip = slip, Lockup = brake > 0.9f && v > 60 && v < 64 ? 1 : 0 };
    engine.Render(eng);
    fx.Render(fl, fr);
    for (int i = 0; i < Block; i++)
    {
        float e = eng[i] * 0.55f * gain;
        left.Add(MathF.Tanh((e + fl[i] * 0.4f * gain) * 1.6f) * 0.7f);
        right.Add(MathF.Tanh((e + fr[i] * 0.4f * gain) * 1.6f) * 0.7f);
    }
    t += (double)Block / Fs;
}

void Run(double seconds, Func<double, (float thr, float brake)> pedal, float onboard = 1, bool pit = false, Func<double, float?>? rpm = null)
{
    double start = t;
    while (t - start < seconds)
    {
        double local = t - start;
        var (thr, brk) = pedal(local);
        Step(thr, brk, onboard, pit: pit, rpmOverride: rpm?.Invoke(local));
    }
}

// 1. idle in neutral, then grid revs
Run(2.0, _ => (0, 0), rpm: l => (float)(4100 + 60 * Math.Sin(l * 17)));
Run(2.4, l => (l % 0.8 < 0.35 ? 1f : 0f, 0), rpm: l => (float)(l % 0.8 < 0.35 ? 4100 + 6500 * Math.Min(1, (l % 0.8) / 0.3) : Math.Max(4100, 10600 - 16000 * ((l % 0.8) - 0.35))));
// 2. launch and run through the gears
Run(9.5, _ => (1, 0));
// 3. heavy braking from top speed, downshifts, then trail-off
Run(2.6, l => (0, l < 1.9 ? 1f : 0.35f));
Run(0.8, l => ((float)Math.Min(1, l * 0.6), 0));
// 4. corner exit and a short squirt
Run(3.0, l => ((float)Math.Min(1, 0.4 + l * 0.5), 0));
Run(1.6, l => (0, l < 1.2 ? 0.8f : 0));
// 5. pit limiter at 80 km/h
v = 80 / 3.6; gear = 3;
Run(3.5, _ => (0.9f, 0), pit: true);

// 6. TV camera fly-by: 300 km/h past a camera 12 m off the racing line
v = 300 / 3.6; gear = 8;
const double C = 343, Offset = 12;
double x = -260;
double prevDist = Math.Sqrt(x * x + Offset * Offset);
while (x < 260)
{
    x += v * Block / Fs;
    double dist = Math.Sqrt(x * x + Offset * Offset);
    double radial = (dist - prevDist) / ((double)Block / Fs);
    prevDist = dist;
    float doppler = (float)(C / (C + radial));
    float gain = (float)Math.Min(1, 14 / dist);
    float air = (float)Math.Clamp(16000 * 30 / dist, 1500, 20000);
    Step(1, 0, onboard: 0, doppler: doppler, gain: gain, air: air, rpmOverride: 11600);
}

WriteWav(outPath, left, right);
Console.WriteLine($"Wrote {outPath}: {left.Count / (double)Fs:F1} s, peak {left.Concat(right).Max(Math.Abs):F3}");

static void WriteWav(string path, List<float> l, List<float> r)
{
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    int n = l.Count, bytes = n * 4;
    w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
    w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Fs); w.Write(Fs * 4); w.Write((short)4); w.Write((short)16);
    w.Write("data"u8); w.Write(bytes);
    for (int i = 0; i < n; i++)
    {
        w.Write((short)Math.Clamp(l[i] * 32767, -32768, 32767));
        w.Write((short)Math.Clamp(r[i] * 32767, -32768, 32767));
    }
}
