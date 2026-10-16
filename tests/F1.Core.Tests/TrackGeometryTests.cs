using F1.Core.Geometry;
using F1.Core.Track;

namespace F1.Core.Tests;

public class TrackGeometryTests
{
    private static TrackGeometry Circle(double radius, int n, bool clockwise)
    {
        var pts = new List<Vec2>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n * (clockwise ? -1 : 1);
            pts.Add(new Vec2(Math.Cos(a) * radius, Math.Sin(a) * radius));
        }
        return new TrackGeometry(pts);
    }

    [Fact]
    public void CircleLengthAndCurvature()
    {
        var g = Circle(100, 720, clockwise: false);
        Assert.InRange(g.Length, 2 * Math.PI * 100 * 0.999, 2 * Math.PI * 100 * 1.0001);
        Assert.InRange(g.CurvatureAt(123), 0.0099, 0.0101);  // left turn, 1/R
        Assert.InRange(Circle(100, 720, true).CurvatureAt(50), -0.0101, -0.0099);
    }

    [Fact]
    public void ProjectRoundTrip()
    {
        var g = Circle(200, 600, clockwise: true);
        foreach (double s in new[] { 0.0, 10.5, 600, 1200.25 })
            foreach (double d in new[] { -7.0, 0.0, 5.5 })
            {
                var p = g.ToWorld(s, d);
                var proj = g.Project(p);
                Assert.InRange(Math.Abs(g.Delta(proj.S, s)), 0, 0.2);
                Assert.InRange(proj.D, d - 0.1, d + 0.1);
                var hinted = g.Project(p, s + 20);
                Assert.InRange(Math.Abs(g.Delta(hinted.S, s)), 0, 0.2);
            }
    }

    [Fact]
    public void WrapDeltaForward()
    {
        var g = Circle(100, 360, false);
        double L = g.Length;
        Assert.Equal(10, g.Wrap(L + 10), 6);
        Assert.Equal(L - 5, g.Wrap(-5), 6);
        Assert.Equal(-20, g.Delta(L - 10, 10), 6);
        Assert.Equal(20, g.Forward(10, L - 10), 6);
    }

    [Fact]
    public void ResampleKeepsLength()
    {
        var sq = new List<Vec2> { new(0, 0), new(100, 0), new(100, 100), new(0, 100) };
        var r = Polyline.Resample(sq, 3, closed: true);
        // sampling cuts each 90° corner by up to ~0.9 m
        Assert.InRange(Polyline.Length(r, true), 396, 400.01);
        Assert.False(Polyline.SelfIntersects(r, true));
        Assert.True(Polyline.SignedArea(sq) > 0);
    }
}
