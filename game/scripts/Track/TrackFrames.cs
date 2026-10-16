using F1.Game.Core;
using Godot;

namespace F1.Game.Track;

/// <summary>Per-sample world-space frames of the centreline (position, left normal, edges and barriers) for mesh building.</summary>
public sealed class TrackFrames
{
    public readonly int Count;
    public readonly Vector3[] P;
    public readonly Vector3[] N;
    public readonly Vector3[] T;
    public readonly float[] WL, WR, BL, BR;
    public readonly TrackSurface Surface;

    public TrackFrames(TrackSurface ts)
    {
        Surface = ts;
        var d = ts.Data;
        var g = ts.Center;
        Count = g.Count;
        P = new Vector3[Count]; N = new Vector3[Count]; T = new Vector3[Count];
        WL = new float[Count]; WR = new float[Count]; BL = new float[Count]; BR = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            P[i] = Units.ToWorld(g.Point(i));
            T[i] = Units.DirToWorld(g.Tangent(i)).Normalized();
            N[i] = Units.DirToWorld(g.Normal(i)).Normalized();
            WL[i] = (float)d.WidthLeft[i]; WR[i] = (float)d.WidthRight[i];
            BL[i] = (float)d.BarrierLeft[i]; BR[i] = (float)d.BarrierRight[i];
        }
    }

    public int Wrap(int i) => ((i % Count) + Count) % Count;

    /// <summary>World point at sample i, lateral offset d (positive left), height h.</summary>
    public Vector3 At(int i, float d, float h = 0f)
    {
        i = Wrap(i);
        return P[i] + N[i] * d + new Vector3(0, h, 0);
    }

    public char RunoffLeft(int i) => Surface.Data.RunoffLeft[Wrap(i)];
    public char RunoffRight(int i) => Surface.Data.RunoffRight[Wrap(i)];
    public bool KerbLeft(int i) => Surface.Data.KerbLeft[Wrap(i)] == 'K';
    public bool KerbRight(int i) => Surface.Data.KerbRight[Wrap(i)] == 'K';
}
