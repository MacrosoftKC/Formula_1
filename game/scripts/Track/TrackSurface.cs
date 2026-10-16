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
    public float Height;
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
/// Runtime queries against the circuit description. Albert Park is flat, so the ground is the y = 0 plane plus kerb
/// height; surface type comes from the lateral position relative to the track edges, kerbs and run-off.
/// </summary>
public sealed class TrackSurface
{
    public TrackData Data { get; }
    public TrackGeometry Center { get; }
    public TrackGeometry Pit { get; }
    public double Length => Center.Length;

    public const float KerbWidth = 1.2f;
    public const float KerbHeight = 0.035f;
    public const float VergeWidth = 1.0f;

    public TrackSurface(TrackData data)
    {
        Data = data;
        Center = new TrackGeometry(data.Centerline, closed: true);
        Pit = new TrackGeometry(data.PitLane.Path, closed: false);
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

        var info = new SurfaceInfo { S = proj.S, D = proj.D, Grip = 1f, Rolling = 0.012f };
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
                info.Grip = 0.97f;
                return info;
            }
        }

        string kerbs = left ? Data.KerbLeft : Data.KerbRight;
        if (kerbs[idx] == 'K' && edgeDist <= KerbWidth)
        {
            info.Type = SurfaceType.Kerb;
            info.Height = KerbHeight * (float)(1.0 - edgeDist / KerbWidth * 0.5);
            info.Grip = 0.9f;
            info.Bump = 0.012f;
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
        var pos = Units.ToWorld(Center.ToWorld(slot.S, slot.D), height);
        return FacingTransform(slot.S, pos);
    }

    /// <summary>Transform at track distance s / offset d facing the race direction (car forward is -Z in Godot).</summary>
    public Transform3D TrackTransform(double s, double d, float height = 0f)
    {
        var pos = Units.ToWorld(Center.ToWorld(s, d), height);
        return FacingTransform(s, pos);
    }

    private Transform3D FacingTransform(double s, Vector3 pos)
    {
        var fwd = Units.DirToWorld(Center.TangentAt(s)).Normalized();
        var basis = Basis.LookingAt(fwd, Vector3.Up);
        return new Transform3D(basis, pos);
    }
}
