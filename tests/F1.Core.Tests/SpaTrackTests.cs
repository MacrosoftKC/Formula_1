using F1.Core.Data;
using F1.Core.Geometry;
using F1.Core.Track;

namespace F1.Core.Tests;

public class SpaTrackTests
{
    private static readonly TrackData Spa = Json.Deserialize<TrackData>(File.ReadAllText(TestData.GameData("tracks", "spa", "track.json")));

    [Fact]
    public void ClosedDrivingSurfaceHasMetricLengthAndRelief()
    {
        var geometry = new TrackGeometry(Spa.Centerline);
        int n = geometry.Count;
        Assert.Equal(n, Spa.Elevation.Count);
        Assert.Equal(n, Spa.Crossfall.Count);
        Assert.Equal(1, Spa.Scale);
        Assert.True(Polyline.SignedArea(Spa.Centerline) < 0);
        double length = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double horizontal = Vec2.Distance(geometry.Point(i), geometry.Point(j));
            double rise = Spa.Elevation[j] - Spa.Elevation[i];
            Assert.InRange(horizontal, 2.8, 3.2);
            Assert.InRange(Math.Abs(rise / horizontal), 0, .19);
            Assert.InRange(Math.Abs(Spa.Crossfall[i]), 0, .12);
            length += Math.Sqrt(horizontal * horizontal + rise * rise);
        }
        Assert.InRange(length, 6997, 7011);
        Assert.InRange(Spa.Elevation.Max() - Spa.Elevation.Min(), 95, 106);
        Assert.InRange(geometry.SAt(Spa.Elevation.IndexOf(Spa.Elevation.Max())), 2378, 2568);
        Assert.InRange(geometry.SAt(Spa.Elevation.IndexOf(Spa.Elevation.Min())), 4812, 5491);
        Assert.Equal("RLRLRLRRLLLRLRRLLRL", string.Concat(Spa.Corners.Select(c => c.Direction < 0 ? "R" : "L")));
        Assert.Equal(3, Spa.SectorStarts.Count);
        Assert.Equal(Spa.PitLane.Path.Count, Spa.PitLane.Elevation.Count);
        Assert.Equal(Spa.PitLane.Path.Count, Spa.PitLane.TrackS.Count);
        Assert.True(Spa.PitLane.EntryS > Spa.Corners[17].ApexS);
        Assert.True(Spa.PitLane.ExitS > Spa.Corners[0].ExitS && Spa.PitLane.ExitS < Spa.Corners[1].EntryS);
    }
}
