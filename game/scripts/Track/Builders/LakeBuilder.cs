using F1.Core.Geometry;
using F1.Game.Core;
using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Albert Park Lake from the OSM shoreline: water surface, islands and the concrete lake edge.</summary>
public static class LakeBuilder
{
    private const float WaterH = 0.03f, IslandH = 0.06f, EdgeH = 0.28f, EdgeW = 0.6f;

    public static void Build(Node3D parent, TrackSurface ts)
    {
        var rings = ts.Data.Lake;
        if (rings.Count == 0) return;
        var root = new Node3D { Name = "Lake" };
        parent.AddChild(root);

        var water = Triangulate(rings[0], WaterH, new Color(1, 1, 1));
        if (water != null)
        {
            root.AddChild(new MeshInstance3D
            {
                Name = "Water",
                Mesh = water.ToMesh(Materials.Water),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        var islands = new LowPolyMesh();
        for (int r = 1; r < rings.Count; r++)
            AppendTo(islands, rings[r], IslandH, NatureBuilder.Grass.Lightened(0.04f));
        if (!islands.IsEmpty) root.AddChild(islands.ToInstance("Islands", castShadows: false));

        // concrete edge around the shoreline
        var edge = new LowPolyMesh();
        var ring = rings[0];
        var edgeColor = new Color(0.78f, 0.78f, 0.76f);
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            var dir = (b - a).Normalized();
            var outward = dir.PerpLeft; // ring orientation varies; make both faces
            var a0 = Units.ToWorld(a); var b0 = Units.ToWorld(b);
            var a1 = Units.ToWorld(a + outward * EdgeW); var b1 = Units.ToWorld(b + outward * EdgeW);
            var up = new Vector3(0, EdgeH, 0);
            edge.QuadUp(a0 + up, b0 + up, b1 + up, a1 + up, edgeColor.Lightened(0.05f));
            edge.QuadFacing(a0 - up * 0.3f, b0 - up * 0.3f, b0 + up, a0 + up, -Units.DirToWorld(outward), edgeColor.Darkened(0.1f));
            edge.QuadFacing(a1 - up * 0.3f, b1 - up * 0.3f, b1 + up, a1 + up, Units.DirToWorld(outward), edgeColor.Darkened(0.05f));
        }
        root.AddChild(edge.ToInstance("LakeEdge", castShadows: false));
    }

    private static LowPolyMesh? Triangulate(List<Vec2> ring, float h, Color color)
    {
        var m = new LowPolyMesh();
        return AppendTo(m, ring, h, color) ? m : null;
    }

    private static bool AppendTo(LowPolyMesh m, List<Vec2> ring, float h, Color color)
    {
        var poly = ring.Select(p => new Vector2((float)p.X, (float)-p.Y)).ToArray();
        int[] idx = Geometry2D.TriangulatePolygon(poly);
        if (idx.Length < 3) { GD.PushWarning("Lake polygon could not be triangulated"); return false; }
        for (int k = 0; k < idx.Length; k += 3)
        {
            var a = poly[idx[k]]; var b = poly[idx[k + 1]]; var c = poly[idx[k + 2]];
            m.TriUp(new Vector3(a.X, h, a.Y), new Vector3(b.X, h, b.Y), new Vector3(c.X, h, c.Y), color);
        }
        return true;
    }
}
