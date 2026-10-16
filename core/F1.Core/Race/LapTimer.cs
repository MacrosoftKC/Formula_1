namespace F1.Core.Race;

/// <summary>One completed lap.</summary>
public sealed record LapRecord(int Number, double Time, double[] Sectors, bool Valid, string Compound);

/// <summary>What happened when the car's track position advanced.</summary>
public enum TimingEvent { None, SectorComplete, LapComplete, LapStarted }

/// <summary>
/// Timing for one car: detects forward crossings of the line and the sector boundaries (with crossing times
/// interpolated between physics ticks), and keeps lap/sector history and personal bests.
/// </summary>
public sealed class LapTimer
{
    private readonly double _length;
    private readonly double[] _sectorStarts;
    private double _prevS = double.NaN, _prevTime;
    private double _sectorStartTime;

    public bool Started { get; private set; }
    public int LapsCompleted { get; private set; }
    public double LapStartTime { get; private set; }
    public int CurrentSector { get; private set; }
    public double[] CurrentSectors { get; } = new double[3];
    public bool CurrentLapValid { get; private set; } = true;
    public string Compound { get; set; } = "";
    public List<LapRecord> Laps { get; } = new();
    public LapRecord? BestLap { get; private set; }
    public double[] BestSectors { get; } = { double.MaxValue, double.MaxValue, double.MaxValue };
    /// <summary>Total distance covered in race order: laps × length + distance into the lap.</summary>
    public double Distance { get; private set; }
    /// <summary>Last sector index that was completed and its time (for HUD split display).</summary>
    public (int Sector, double Time)? LastSplit { get; private set; }

    public LapTimer(double trackLength, IReadOnlyList<double> sectorStarts)
    {
        _length = trackLength;
        _sectorStarts = sectorStarts.ToArray();
        if (_sectorStarts.Length != 3) throw new ArgumentException("Expected three sector starts");
    }

    public double CurrentLapTime(double now) => Started ? now - LapStartTime : 0;

    public void Invalidate() => CurrentLapValid = false;

    private double Forward(double to, double from)
    {
        double d = (to - from) % _length;
        return d < 0 ? d + _length : d;
    }

    /// <summary>Resets the previous position without registering crossings (after a teleport/reset).</summary>
    public void Teleport(double s, double now)
    {
        _prevS = s;
        _prevTime = now;
    }

    /// <summary>Feeds the car's new track position. Returns the most significant event (lap &gt; start &gt; sector).</summary>
    public TimingEvent Update(double s, double now)
    {
        if (double.IsNaN(_prevS)) { _prevS = s; _prevTime = now; return TimingEvent.None; }
        double moved = Forward(s, _prevS);
        var result = TimingEvent.None;
        if (moved > 0 && moved < 150)
        {
            if (Started) Distance += moved;
            // check boundaries in order along the travelled segment
            for (int k = 0; k < 3; k++)
            {
                double b = _sectorStarts[k];
                double into = Forward(b, _prevS);
                if (into <= 0 || into > moved) continue;
                double t = _prevTime + (now - _prevTime) * into / moved;
                var e = Cross(k, t);
                if (Rank(e) > Rank(result)) result = e;
            }
        }
        _prevS = s;
        _prevTime = now;
        return result;
    }

    private static int Rank(TimingEvent e) => e switch
    {
        TimingEvent.LapComplete => 3,
        TimingEvent.LapStarted => 2,
        TimingEvent.SectorComplete => 1,
        _ => 0,
    };

    private TimingEvent Cross(int boundary, double t)
    {
        if (boundary == 0)
        {
            if (!Started)
            {
                Started = true;
                LapStartTime = t;
                _sectorStartTime = t;
                CurrentSector = 0;
                CurrentLapValid = true;
                return TimingEvent.LapStarted;
            }
            // finish the lap
            CurrentSectors[2] = t - _sectorStartTime;
            LastSplit = (2, CurrentSectors[2]);
            double lap = t - LapStartTime;
            LapsCompleted++;
            var rec = new LapRecord(LapsCompleted, lap, (double[])CurrentSectors.Clone(), CurrentLapValid, Compound);
            Laps.Add(rec);
            if (rec.Valid)
            {
                if (BestLap == null || lap < BestLap.Time) BestLap = rec;
                for (int k = 0; k < 3; k++) BestSectors[k] = Math.Min(BestSectors[k], rec.Sectors[k]);
            }
            LapStartTime = t;
            _sectorStartTime = t;
            CurrentSector = 0;
            CurrentLapValid = true;
            Array.Clear(CurrentSectors);
            return TimingEvent.LapComplete;
        }
        if (!Started) return TimingEvent.None;
        int finished = boundary - 1;
        if (finished != CurrentSector) { CurrentSector = boundary; _sectorStartTime = t; return TimingEvent.None; }
        CurrentSectors[finished] = t - _sectorStartTime;
        LastSplit = (finished, CurrentSectors[finished]);
        _sectorStartTime = t;
        CurrentSector = boundary;
        return TimingEvent.SectorComplete;
    }

    public double? LastLapTime => Laps.Count > 0 ? Laps[^1].Time : null;

    /// <summary>Sum of best sectors (theoretical best lap), or null until all three have been set.</summary>
    public double? TheoreticalBest => BestSectors.All(s => s < double.MaxValue) ? BestSectors.Sum() : null;

    public static string Format(double? seconds, bool sign = false)
    {
        if (seconds == null || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value)) return "--:--.---";
        double v = seconds.Value;
        string prefix = sign ? (v < 0 ? "-" : "+") : (v < 0 ? "-" : "");
        v = Math.Abs(v);
        int min = (int)(v / 60);
        double sec = v - min * 60;
        return min > 0 ? $"{prefix}{min}:{sec:00.000}" : $"{prefix}{sec:0.000}";
    }
}
