using F1.Core.Race;
using F1.Core.Racing;
using F1.Core.Sim;
using F1.Core.Track;

namespace F1.Core.Tests;

public class TyreModelTests
{
    [Fact]
    public void CurvePeaksAtOneAndKeepsMostGripWhenSliding()
    {
        Assert.InRange(TyreModel.Curve(1.0), 0.999, 1.0001);
        Assert.True(TyreModel.Curve(0.8) < 1 && TyreModel.Curve(1.2) < 1);
        Assert.InRange(TyreModel.Curve(20), 0.8, 0.9);
    }

    [Fact]
    public void CombinedSlipStaysWithinFrictionCircle()
    {
        double fz = 4000, mu = 1.8;
        var (fx, fy) = TyreModel.Forces(0.1, 0.105, fz, mu);
        Assert.True(Math.Sqrt(fx * fx + fy * fy) <= mu * fz + 1e-6);
        Assert.True(fx > 0);   // driving slip pushes forwards
        Assert.True(fy < 0);   // sliding right → force to the left
    }

    [Fact]
    public void LoadSensitivityReducesMuAtHighLoad()
    {
        Assert.True(TyreModel.LoadedMu(1.8, 8000) < TyreModel.LoadedMu(1.8, 3000));
    }

    [Fact]
    public void ColdAndWornTyresLoseGrip()
    {
        var t = new TyreState(TyreCompound.Medium, 40);
        double cold = t.Grip(0);
        t.SurfaceTemp = t.CoreTemp = 105;
        double hot = t.Grip(0);
        Assert.True(hot > cold);
        t.Wear = 0.85;
        Assert.True(t.Grip(0) < hot * 0.9);
    }
}

public class PowerUnitTests
{
    [Fact]
    public void PeakPowerIsAroundFiveHundredSixtyKilowatts()
    {
        var pu = new PowerUnit(100);
        double rpm = 11500;
        double kw = pu.IceTorque(rpm, 1) * rpm * Math.Tau / 60 / 1000;
        Assert.InRange(kw, 540, 580);
    }

    [Fact]
    public void DeploymentIsCappedAtFourMegajoulesPerLap()
    {
        var pu = new PowerUnit(100) { Mode = ErsMode.Hotlap };
        pu.StoreJ = pu.StoreMaxJ;
        for (int i = 0; i < 120 * 240; i++)
        {
            pu.UpdateErs(1.0 / 240, 1, 0, 80, 11500, true);
            if (i % 240 == 0) pu.StoreJ = pu.StoreMaxJ; // keep the store topped up to test the lap cap
        }
        // energy drawn from the battery is capped; the MGU-H direct path keeps some K power available afterwards
        Assert.InRange(pu.DeployedThisLapJ, 3.9e6, 4.0e6 + 1);
        double after = pu.UpdateErs(1.0 / 240, 1, 0, 80, 11500, true);
        Assert.InRange(after, 1, pu.MguKMaxW);
    }

    [Fact]
    public void KHarvestIsCappedAtTwoMegajoulesPerLap()
    {
        var pu = new PowerUnit(100) { StoreJ = 0 };
        for (int i = 0; i < 60 * 240; i++) pu.UpdateErs(1.0 / 240, 0, 1, 70, 9000, false);
        Assert.InRange(pu.HarvestedKThisLapJ, 1.99e6, 2.0e6 + 1);
        pu.NewLap();
        Assert.Equal(0, pu.HarvestedKThisLapJ);
    }

    [Fact]
    public void FuelFlowLimitHoldsAtFullThrottle()
    {
        var pu = new PowerUnit(110);
        double used = 0;
        for (int i = 0; i < 3600; i++) used += pu.BurnFuel(1, 1, 12000);
        Assert.InRange(used, 99, 100.5);
    }

    [Fact]
    public void GearboxRatiosGiveSensibleSpeeds()
    {
        var box = new Gearbox();
        double SpeedAt(int gear, double rpm) => rpm * Math.Tau / 60 / box.OverallRatio(gear) * 0.36 * 3.6;
        Assert.InRange(SpeedAt(1, 12000), 80, 100);
        Assert.InRange(SpeedAt(8, 12000), 320, 345);
    }
}

public class LapTimerTests
{
    [Fact]
    public void LapsAndSectorsWithInterpolatedCrossings()
    {
        var t = new LapTimer(1000, new[] { 0.0, 300, 700 });
        double now = 0;
        t.Update(950, now);
        // drive at 50 m/s with 0.1 s ticks for three laps
        double s = 950;
        var events = new List<TimingEvent>();
        for (int i = 0; i < 700; i++)
        {
            now += 0.1; s = (s + 5) % 1000;
            var e = t.Update(s, now);
            if (e != TimingEvent.None) events.Add(e);
        }
        Assert.Equal(TimingEvent.LapStarted, events[0]);
        Assert.Equal(3, t.LapsCompleted);
        Assert.All(t.Laps, l => Assert.InRange(l.Time, 19.999, 20.001));
        Assert.InRange(t.Laps[0].Sectors[0], 5.999, 6.001);
        Assert.InRange(t.Laps[0].Sectors[1], 7.999, 8.001);
        Assert.InRange(t.Laps[0].Sectors[2], 5.999, 6.001);
        Assert.NotNull(t.BestLap);
    }

    [Fact]
    public void InvalidLapsDoNotCountAsBest()
    {
        var t = new LapTimer(100, new[] { 0.0, 30, 60 });
        double now = 0, s = 95;
        t.Update(s, now);
        for (int i = 0; i < 30; i++) { now += 1; s = (s + 10) % 100; t.Update(s, now); if (t.LapsCompleted == 1) t.Invalidate(); }
        Assert.True(t.Laps.Count >= 2);
        Assert.False(t.Laps[1].Valid);
        Assert.Equal(t.Laps[0], t.BestLap);
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("1:15.096", LapTimer.Format(75.096));
        Assert.Equal("+0.250", LapTimer.Format(0.25, sign: true));
        Assert.Equal("--:--.---", LapTimer.Format(null));
    }
}

public class RacingLineTests
{
    private static readonly Lazy<RacingLine> Line = new(() => RacingLine.Build(TestData.AlbertPark, new PerformanceModel()));

    [Fact]
    public void LineStaysInsideTrackLimits()
    {
        var t = TestData.AlbertPark;
        var line = Line.Value;
        for (int i = 0; i < t.Centerline.Count; i++)
            Assert.InRange(line.Offset[i], -t.WidthRight[i] + 1.29, t.WidthLeft[i] - 1.29);
        Assert.True(line.LineLength < t.OfficialLength); // the racing line cuts corners
    }

    [Fact]
    public void IdealLapTimeIsPlausibleForAlbertPark()
    {
        // 2025 pole was 1:15.096; a point-mass ideal should land in the same ballpark
        Assert.InRange(Line.Value.LapTime, 70, 82);
    }

    [Fact]
    public void SlowestCornersAreTheTightOnes()
    {
        var t = TestData.AlbertPark;
        var line = Line.Value;
        double minSpeed = line.Speed.Min();
        Assert.InRange(minSpeed * 3.6, 70, 140);
        Assert.InRange(line.Speed.Max() * 3.6, 290, 346);
    }
}
