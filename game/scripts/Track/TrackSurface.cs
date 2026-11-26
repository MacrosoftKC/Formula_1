using F1.Core.Geometry;
using F1.Core.Track;
using F1.Game.Core;
using Godot;

namespace F1.Game.Track;

public enum SurfaceType
{
    Asphalt,
    Kerb,
    Verge,
    Grass,
    Gravel,
    RunoffAsphalt,
    PitLane,
}

/// <summary>What a wheel is touching: surface type, height of the surface and grip/drag modifiers.</summary>
public struct SurfaceInfo
{
    public SurfaceType Type;
    /// <summary>Height of the smooth road (or pit lane) surface under the wheel.</summary>
    public float Height;
    /// <summary>Extra height of short features on top of <see cref="Height"/> (kerb profile). The car's tyre and
    /// unsprung mass filter this; it is never applied as a step.</summary>
    public float Lift;
    public Vector3 Normal;
    /// <summary>Peak grip multiplier relative to dry asphalt.</summary>
    public float Grip;
    /// <summary>Rolling-resistance coefficient (gravel and grass slow the car down).</summary>
    public float Rolling;
    /// <summary>Amplitude (m) of surface bumps, used for kerb rumble and gravel judder.</summary>
    public float Bump;
    /// <summary>Inside the white lines (asphalt or the painted line itself).</summary>
    public bool OnTrack;
    public bool InPitLane;
    public double S;
    public double D;
}

/// <summary>
/// Surface classification and contact geometry in the same metric frame as the road and pit meshes.
/// </summary>
public sealed class TrackSurface
{
    public TrackData Data { get; }
    public TrackGeometry Center { get; }
    public TrackGeometry Pit { get; }
    public double Length => Center.Length;

    public const float KerbWidth = 1.2f;
    public const float KerbHeight = 0.03f;
    /// <summary>Width of the chamfer that takes the kerb from road level to its full height (and back at the outside).</summary>
    public const float KerbRamp = 0.35f;
    public const float VergeWidth = 1.0f;

    public TrackSurface(TrackData data)
    {
        Data = data;
        Center = new TrackGeometry(data.Centerline, closed: true);
        Pit = new TrackGeometry(data.PitLane.Path, closed: false);
    }

    public bool HasRelief => Data.Elevation.Count != 0;
    // Heights use slope-continuous interpolation so the suspension never sees a step in vertical velocity.
    public float RoadHeight(double s, double d = 0) => HasRelief
        ? (float)(Center.SampleSmooth(Data.Elevation, s) + d * Center.SampleSmooth(Data.Crossfall, s)) : 0f;

    public Vector3 WorldPoint(double s, double d, float height = 0) =>
        Units.ToWorld(Center.ToWorld(s, d), RoadHeight(s, d) + height);

    public float PitHeight(double u) => Data.PitLane.Elevation.Count == 0 ? 0f : (float)Pit.SampleSmooth(Data.PitLane.Elevation, u);

    /// <summary>
    /// Height of the pit-lane surface at (u, d). Where the exit lane runs over the circuit's own asphalt (it merges over
    /// the last ~90 m at Spa) the flat lane must never stand proud of the cambered road, or it would prop a car up.
    /// </summary>
    public float PitSurfaceHeight(double u, double d)
    {
        float h = PitHeight(u);
        if (!HasRelief) return h;
        var p = Pit.ToWorld(u, d);
        var proj = Center.Project(p, null, 0);
        if (proj.D <= HalfWidthLeft(proj.S) + VergeWidth && proj.D >= -HalfWidthRight(proj.S) - VergeWidth)
            h = Math.Min(h, RoadHeight(proj.S, proj.D));
        return h;
    }

    public Vector3 PitPoint(double u, double d, float height = 0) => Units.ToWorld(Pit.ToWorld(u, d), PitSurfaceHeight(u, d) + height);

    /// <summary>Surface normal of the smooth road at (s, d): grade along the lap and crossfall across it.</summary>
    private Vector3 RoadNormal(double s, double d)
    {
        if (!HasRelief) return Vector3.Up;
        var tangent = Units.DirToWorld(Center.TangentAt(s));
        var left = Units.DirToWorld(Center.NormalAt(s));
        double grade = RoadHeight(s + 0.5, d) - RoadHeight(s - 0.5, d);
        double crossfall = Center.SampleSmooth(Data.Crossfall, s);
        return (Vector3.Up - tangent * (float)grade - left * (float)crossfall).Normalized();
    }

    /// <summary>Kerb height above the road at <paramref name="e"/> metres outside the white line: chamfered up from
    /// road level, falling slightly across the top, and chamfered back down at the outer edge.</summary>
    public static float KerbProfile(double e)
    {
        if (e <= 0 || e >= KerbWidth) return 0f;
        double rise = Math.Clamp(e / KerbRamp, 0, 1), fall = Math.Clamp((KerbWidth - e) / KerbRamp, 0, 1);
        double smooth(double x) => x * x * (3 - 2 * x);
        return KerbHeight * (float)(smooth(rise) * smooth(fall) * (1 - 0.3 * e / KerbWidth));
    }

    public double HalfWidthLeft(double s) => Center.Sample(Data.WidthLeft, s);
    public double HalfWidthRight(double s) => Center.Sample(Data.WidthRight, s);
    public double BarrierLeft(double s) => Center.Sample(Data.BarrierLeft, s);
    public double BarrierRight(double s) => Center.Sample(Data.BarrierRight, s);

    /// <summary>True if track distance s lies in the stretch where the pit lane runs beside the circuit.</summary>
    public bool InPitZone(double s)
    {
        var pit = Data.PitLane;
        return Center.Forward(s, pit.EntryS - 5) <= Center.Forward(pit.ExitS + 5, pit.EntryS - 5);
    }

    /// <summary>Index of the DRS zone containing track distance s, or -1.</summary>
    public int DrsZoneAt(double s)
    {
        var zones = Data.DrsZones;
        for (int i = 0; i < zones.Count; i++)
            if (Center.Forward(s, zones[i].ActivationS) <= Center.Forward(zones[i].EndS, zones[i].ActivationS)) return i;
        return -1;
    }

    /// <summary>Projects a world position onto the pit lane (distance along it and lateral offset).</summary>
    public TrackProjection ProjectPit(Vector3 world, double? hint = null) => Pit.Project(Units.ToTrack(world), hint, 60);

    public SurfaceInfo Query(Vector3 world, ref double hintS)
    {
        var p = Units.ToTrack(world);
        var proj = Center.Project(p, hintS >= 0 ? hintS : null, 60);
        // Guard against snapping to a different part of the circuit after a big jump (e.g. reset).
        if (proj.DistanceSquared > 400 * 400) proj = Center.Project(p);
        hintS = proj.S;

        var info = new SurfaceInfo { S = proj.S, D = proj.D, Grip = 1f, Rolling = 0.012f,
            Height = RoadHeight(proj.S, proj.D), Normal = RoadNormal(proj.S, proj.D) };
        double s = proj.S, d = proj.D;
        double halfL = HalfWidthLeft(s), halfR = HalfWidthRight(s);

        if (d <= halfL && d >= -halfR)
        {
            info.Type = SurfaceType.Asphalt;
            info.OnTrack = true;
            return info;
        }

        bool left = d > 0;
        double edgeDist = left ? d - halfL : -d - halfR;
        int idx = Center.NearestIndex(s);

        if (InPitZone(s) && !left)
        {
            var pp = Pit.Project(p, null, 0);
            if (Math.Abs(pp.D) <= Data.PitLane.Width * 0.5 + 0.5 && pp.DistanceSquared < 30 * 30)
            {
                info.Type = SurfaceType.PitLane;
                info.InPitLane = true;
                info.Height = PitSurfaceHeight(pp.S, pp.D);
                float slope = (PitHeight(pp.S + 1) - PitHeight(pp.S - 1)) * 0.5f;
                info.Normal = (Vector3.Up - Units.DirToWorld(Pit.TangentAt(pp.S)) * slope).Normalized();
                info.Grip = 0.97f;
                return info;
            }
        }

        string kerbs = left ? Data.KerbLeft : Data.KerbRight;
        if (kerbs[idx] == 'K' && edgeDist <= KerbWidth)
        {
            info.Type = SurfaceType.Kerb;
            info.Lift = KerbProfile(edgeDist);
            info.Grip = 0.92f;
            info.Bump = 0.004f;
            return info;
        }

        if (edgeDist <= VergeWidth)
        {
            info.Type = SurfaceType.Verge;
            info.Grip = 0.95f;
            return info;
        }

        char run = (left ? Data.RunoffLeft : Data.RunoffRight)[idx];
        double barrier = left ? BarrierLeft(s) : BarrierRight(s);
        if (Math.Abs(d) <= barrier || run == 'P')
        {
            switch (run)
            {
                case 'R':
                    info.Type = SurfaceType.Gravel;
                    info.Grip = 0.45f;
                    info.Rolling = 0.35f;
                    info.Bump = 0.02f;
                    return info;
                case 'A':
                case 'P':
                    info.Type = SurfaceType.RunoffAsphalt;
                    info.Grip = 0.95f;
                    return info;
            }
        }

        info.Type = SurfaceType.Grass;
        info.Grip = 0.55f;
        info.Rolling = 0.06f;
        info.Bump = 0.008f;
        return info;
    }

    /// <summary>World transform of a grid slot: on the track, facing the race direction.</summary>
    public Transform3D GridTransform(GridSlot slot, float height = 0f)
    {
        return TrackTransform(slot.S, slot.D, height);
    }

    /// <summary>Transform at track distance s / offset d facing the race direction (car forward is -Z in Godot).</summary>
    public Transform3D TrackTransform(double s, double d, float height = 0f)
    {
        var pos = WorldPoint(s, d, height);
        var fwd = Units.DirToWorld(Center.TangentAt(s));
        if (!HasRelief) return new Transform3D(Basis.LookingAt(fwd, Vector3.Up), pos);
        fwd.Y = (RoadHeight(s + 1, d) - RoadHeight(s - 1, d)) * 0.5f;
        return new Transform3D(Basis.LookingAt(fwd.Normalized(), RoadNormal(s, d)), pos);
    }
}
