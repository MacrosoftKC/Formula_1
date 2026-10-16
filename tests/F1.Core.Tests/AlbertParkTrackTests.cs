using F1.Core.Geometry;
using F1.Core.Track;

namespace F1.Core.Tests;

public class AlbertParkTrackTests
{
    private readonly TrackData _t = TestData.AlbertPark;
    private TrackGeometry Geo => new(_t.Centerline);

    [Fact]
    public void LengthMatchesOfficial()
    {
        Assert.Equal(5278, _t.OfficialLength);
        Assert.InRange(Geo.Length, 5278 * 0.997, 5278 * 1.003);
        Assert.Equal(58, _t.RaceLaps);
    }

    [Fact]
    public void LoopIsClockwiseAndSimple()
    {
        Assert.True(Polyline.SignedArea(_t.Centerline) < 0, "race direction should be clockwise");
        Assert.False(Polyline.SelfIntersects(_t.Centerline, true));
        Assert.InRange(Vec2.Distance(_t.Centerline[0], _t.Centerline[^1]), 0.5, 3.0);
    }

    [Fact]
    public void FourteenNumberedCornersInOrder()
    {
        Assert.Equal(14, _t.Corners.Count);
        Assert.Equal(Enumerable.Range(1, 14), _t.Corners.Select(c => c.Number));
        for (int i = 1; i < _t.Corners.Count; i++) Assert.True(_t.Corners[i].ApexS > _t.Corners[i - 1].ApexS);
        // T1 right-hander, T2 left-hander, T13 left, T14 right onto the main straight
        Assert.Equal(-1, _t.Corners[0].Direction);
        Assert.Equal(1, _t.Corners[1].Direction);
        Assert.Equal(1, _t.Corners[12].Direction);
        Assert.Equal(-1, _t.Corners[13].Direction);
    }

    [Fact]
    public void PerSampleArraysMatch()
    {
        int n = _t.Centerline.Count;
        Assert.Equal(n, _t.WidthLeft.Count);
        Assert.Equal(n, _t.WidthRight.Count);
        Assert.Equal(n, _t.BarrierLeft.Count);
        Assert.Equal(n, _t.BarrierRight.Count);
        Assert.Equal(n, _t.RunoffLeft.Length);
        Assert.Equal(n, _t.KerbRight.Length);
        for (int i = 0; i < n; i++)
        {
            Assert.InRange(_t.WidthLeft[i] + _t.WidthRight[i], 12, 26);
            Assert.True(_t.BarrierLeft[i] > _t.WidthLeft[i]);
            Assert.True(_t.BarrierRight[i] > _t.WidthRight[i]);
        }
    }

    [Fact]
    public void PitLaneJoinsTheTrackAtBothEnds()
    {
        var g = Geo;
        var pit = _t.PitLane;
        Assert.Equal(80, pit.SpeedLimitKmh);
        Assert.True(Vec2.Distance(pit.Path[0], g.PositionAt(pit.EntryS)) < 10);
        Assert.True(Vec2.Distance(pit.Path[^1], g.PositionAt(pit.ExitS)) < 10);
        Assert.Equal(10, pit.Boxes.Count);
        double pitLen = Polyline.Length(pit.Path, false);
        Assert.All(pit.Boxes, b => Assert.InRange(b, pit.LimitStart, pit.LimitEnd));
        Assert.InRange(pitLen, 600, 1200);
    }

    [Fact]
    public void RaceControlData()
    {
        Assert.Equal(3, _t.SectorStarts.Count);
        Assert.Equal(4, _t.DrsZones.Count);
        var g = Geo;
        Assert.All(_t.DrsZones, z => Assert.InRange(g.Forward(z.EndS, z.ActivationS), 300, 1000));
        Assert.Equal(20, _t.Grid.Count);
        Assert.True(_t.Lake.Count >= 1);
        Assert.InRange(Math.Abs(Polyline.SignedArea(_t.Lake[0])) / 1e4, 40, 60); // Albert Park Lake is ~45-50 ha
    }

    [Fact]
    public void GridSlotsAreOnTrackBehindTheLine()
    {
        var g = Geo;
        foreach (var slot in _t.Grid)
        {
            Assert.True(g.Delta(slot.S, 0) < 0, "grid slots are behind the line");
            Assert.InRange(Math.Abs(slot.D), 2, g.Sample(_t.WidthLeft, slot.S) - 1);
        }
    }
}
