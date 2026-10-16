using F1.Core.Geometry;
using F1.Core.Track;
using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Garages, pit building, control tower, team pit-wall gantries, paddock and pit lane walls.</summary>
public static class PitBuilder
{
    private static readonly Color Wall = new(0.93f, 0.93f, 0.94f);
    private static readonly Color Dark = new(0.12f, 0.12f, 0.14f);
    private static readonly Color Glass = new(0.36f, 0.5f, 0.62f);
    private const float GarageHeight = 6.5f, BuildingHeight = 13.5f, Depth = 17f;

    /// <summary>Footprint of the pit complex beside the pit lane (lateral depth on the garage side) and the control tower.</summary>
    public sealed record Footprint(double Depth, Vec2 Tower);

    public static Footprint Build(Node3D parent, TrackSurface ts)
    {
        var root = new Node3D { Name = "PitComplex" };
        parent.AddChild(root);
        var pitData = ts.Data.PitLane;
        var pit = ts.Pit;
        float half = (float)pitData.Width * 0.5f;
        float front = half + 4f; // garage frontage, after the apron
        var teams = App.Instance.Teams.Teams;
        var m = new LowPolyMesh();
        var glass = new LowPolyMesh();
        var col = new LowPolyMesh();

        double uA = pitData.Boxes[0] - 30, uB = pitData.Boxes[^1] + 30;
        Vector3 PW(double u, float d, float h) => Units.ToWorld(pit.ToWorld(u, d), h);

        for (double u = 0; u < pit.Length - 2; u += 2)
        {
            double u1 = Math.Min(u + 2, pit.Length);
            var toward = Units.DirToWorld(pit.NormalAt(u)); // towards the pit lane / track
            if (u >= uA && u1 <= uB)
            {
                // which team's garage is this?
                int team = -1;
                for (int b = 0; b < pitData.Boxes.Count; b++)
                    if (u >= pitData.Boxes[b] - 11 && u < pitData.Boxes[b] + 7) team = b;
                var frontColor = team >= 0 ? Units.Hex(teams[team % teams.Count].Primary) : Wall;
                // garage front: coloured frame with a dark opening
                m.QuadFacing(PW(u, -front, 0), PW(u1, -front, 0), PW(u1, -front, 0.5f), PW(u, -front, 0.5f), toward, frontColor);
                m.QuadFacing(PW(u, -front - 0.2f, 0.5f), PW(u1, -front - 0.2f, 0.5f), PW(u1, -front - 0.2f, 5.0f), PW(u, -front - 0.2f, 5.0f), toward, team >= 0 ? Dark : Wall.Darkened(0.1f));
                m.QuadFacing(PW(u, -front, 5.0f), PW(u1, -front, 5.0f), PW(u1, -front, GarageHeight), PW(u, -front, GarageHeight), toward, frontColor);
                // upper floors: glass band then white parapet
                glass.QuadFacing(PW(u, -front + 1.5f, GarageHeight), PW(u1, -front + 1.5f, GarageHeight), PW(u1, -front + 1.5f, BuildingHeight - 2), PW(u, -front + 1.5f, BuildingHeight - 2), toward, Glass);
                m.QuadFacing(PW(u, -front + 1.5f, BuildingHeight - 2), PW(u1, -front + 1.5f, BuildingHeight - 2), PW(u1, -front + 1.5f, BuildingHeight), PW(u, -front + 1.5f, BuildingHeight), toward, Wall);
                // balcony slab, roof and back wall
                m.QuadUp(PW(u, -front + 1.5f, GarageHeight), PW(u1, -front + 1.5f, GarageHeight), PW(u1, -front, GarageHeight), PW(u, -front, GarageHeight), Wall.Darkened(0.05f));
                m.QuadUp(PW(u, -front + 2.5f, BuildingHeight), PW(u1, -front + 2.5f, BuildingHeight), PW(u1, -front - Depth, BuildingHeight), PW(u, -front - Depth, BuildingHeight), Wall.Darkened(0.08f));
                m.QuadFacing(PW(u, -front - Depth, 0), PW(u1, -front - Depth, 0), PW(u1, -front - Depth, BuildingHeight), PW(u, -front - Depth, BuildingHeight), -toward, Wall.Darkened(0.15f));
                col.QuadFacing(PW(u, -front, -0.5f), PW(u1, -front, -0.5f), PW(u1, -front, 3f), PW(u, -front, 3f), toward, Wall);
            }
            else
            {
                // simple wall along the outside of the pit lane beyond the garages
                var a = PW(u, -half - 0.6f, 0); var b = PW(u1, -half - 0.6f, 0);
                var up = new Vector3(0, 1.05f, 0);
                m.QuadFacing(a, b, b + up, a + up, toward, Wall.Darkened(0.12f));
                m.QuadUp(a + up, b + up, PW(u1, -half - 1.1f, 1.05f), PW(u, -half - 1.1f, 1.05f), Wall);
                col.QuadFacing(a - up * 0.5f, b - up * 0.5f, b + up * 2, a + up * 2, toward, Wall);
            }
        }
        // end walls of the pit building
        foreach (double u in new[] { uA, uB })
        {
            var t = Units.DirToWorld(pit.TangentAt(u)) * (u == uA ? -1 : 1);
            m.QuadFacing(PW(u, -front, 0), PW(u, -front - Depth, 0), PW(u, -front - Depth, BuildingHeight), PW(u, -front, BuildingHeight), t, Wall.Darkened(0.1f));
        }

        root.AddChild(m.ToInstance("PitBuilding"));
        root.AddChild(glass.ToInstance("PitGlass", Materials.Glass));
        var body = new StaticBody3D { Name = "PitCollision", CollisionLayer = BarrierBuilder.CollisionLayerWalls, CollisionMask = 0 };
        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(col.Faces());
        body.AddChild(new CollisionShape3D { Shape = shape });
        root.AddChild(body);

        BuildPitWallGantries(root, ts);
        var towerPos = BuildControlTower(root, ts, front);
        BuildPaddock(root, ts, front, uA, uB);

        return new Footprint(front + Depth + 45, towerPos);
    }

    private static void BuildPitWallGantries(Node3D root, TrackSurface ts)
    {
        var pitData = ts.Data.PitLane;
        var teams = App.Instance.Teams.Teams;
        var m = new LowPolyMesh();
        for (int b = 0; b < pitData.Boxes.Count; b++)
        {
            int idx = ts.Pit.NearestIndex(pitData.Boxes[b]);
            double s = pitData.TrackS[Math.Min(idx, pitData.TrackS.Count - 1)];
            double d = -(ts.HalfWidthRight(s) + 2.5 + 0.5 + 1.6);
            var t = ts.TrackTransform(s, d);
            var color = Units.Hex(teams[b % teams.Count].Primary);
            var basis = t.Basis;
            m.Box(t.Origin + basis * new Vector3(0, 1.2f, 0), new Vector3(1.4f, 1.2f, 3.0f), basis, new Color(0.25f, 0.25f, 0.28f));
            m.Box(t.Origin + basis * new Vector3(0, 2.7f, 0), new Vector3(1.8f, 0.15f, 3.4f), basis, color);
        }
        root.AddChild(m.ToInstance("PitWallGantries"));
    }

    private static Vec2 BuildControlTower(Node3D root, TrackSurface ts, float front)
    {
        var pit = ts.Pit;
        var pitData = ts.Data.PitLane;
        // just past the start/finish line along the pit lane, behind the pit building
        double u = pitData.Boxes[^1] + 45;
        var p2 = pit.ToWorld(u, -(front + 8));
        var pos = Units.ToWorld(p2);
        var m = new LowPolyMesh();
        var basis = Basis.LookingAt(Units.DirToWorld(pit.TangentAt(u)), Vector3.Up);
        m.Box(pos + new Vector3(0, 11, 0), new Vector3(4, 11, 4), basis, Wall);
        m.Box(pos + new Vector3(0, 25, 0), new Vector3(6.5f, 3, 6.5f), basis, Glass);
        m.Box(pos + new Vector3(0, 28.4f, 0), new Vector3(7.2f, 0.4f, 7.2f), basis, Wall);
        m.Box(pos + new Vector3(0, 31, 0), new Vector3(0.3f, 2.2f, 0.3f), basis, new Color(0.85f, 0.1f, 0.1f));
        root.AddChild(m.ToInstance("ControlTower"));
        return p2;
    }

    private static void BuildPaddock(Node3D root, TrackSurface ts, float front, double uA, double uB)
    {
        var pit = ts.Pit;
        var teams = App.Instance.Teams.Teams;
        var m = new LowPolyMesh();
        for (int t = 0; t < teams.Count; t++)
        {
            double u = uA + 15 + (uB - uA - 30) * t / Math.Max(1, teams.Count - 1);
            var pos = Units.ToWorld(pit.ToWorld(u, -(front + Depth + 18)));
            var basis = Basis.LookingAt(Units.DirToWorld(pit.TangentAt(u)), Vector3.Up);
            var c = Units.Hex(teams[t].Primary);
            m.Box(pos + new Vector3(0, 3.5f, 0), new Vector3(5.5f, 3.5f, 8f), basis, c.Lerp(Colors.White, 0.15f));
            m.Box(pos + new Vector3(0, 7.2f, 0), new Vector3(6f, 0.2f, 8.5f), basis, Units.Hex(teams[t].Secondary));
        }
        root.AddChild(m.ToInstance("Paddock"));
    }
}
