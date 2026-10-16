using Godot;

namespace F1.Game.Track;

/// <summary>
/// Accumulates flat-shaded, vertex-coloured triangles (every triangle gets its own vertices and face normal),
/// which gives the faceted low-poly look without textures.
/// </summary>
public sealed class LowPolyMesh
{
    private readonly List<Vector3> _verts = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Color> _colors = new();

    public int TriangleCount => _verts.Count / 3;
    public bool IsEmpty => _verts.Count == 0;

    /// <summary>Adds a triangle. Godot treats clockwise winding (as seen by the viewer) as the front face.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        var n = (c - a).Cross(b - a);
        float len = n.Length();
        if (len < 1e-9f) return;
        n /= len;
        _verts.Add(a); _verts.Add(b); _verts.Add(c);
        _normals.Add(n); _normals.Add(n); _normals.Add(n);
        _colors.Add(color); _colors.Add(color); _colors.Add(color);
    }

    /// <summary>Quad a-b-c-d in order around its edge, clockwise as seen from the front.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        Tri(a, b, c, color);
        Tri(a, c, d, color);
    }

    /// <summary>Triangle forced to face upwards (for ground-level surfaces built from arbitrary point order).</summary>
    public void TriUp(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        var n = (c - a).Cross(b - a);
        if (n.Y < 0) Tri(a, c, b, color); else Tri(a, b, c, color);
    }

    /// <summary>Upward-facing quad from four corners in order around the edge.</summary>
    public void QuadUp(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float variation = 0f)
    {
        TriUp(a, b, c, color);
        TriUp(a, c, d, variation > 0 ? color.Darkened(variation) : color);
    }

    /// <summary>Quad whose front face points along <paramref name="facing"/> (order of corners is fixed up automatically).</summary>
    public void QuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color color)
    {
        var n = (c - a).Cross(b - a);
        if (n.Dot(facing) < 0) Quad(d, c, b, a, color); else Quad(a, b, c, d, color);
    }

    /// <summary>Quad split into two triangles with slightly different shades for a faceted look.</summary>
    public void QuadShaded(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float variation)
    {
        Tri(a, b, c, color);
        Tri(a, c, d, color.Darkened(variation));
    }

    /// <summary>Axis-aligned or oriented box from a centre, half extents and basis.</summary>
    public void Box(Vector3 center, Vector3 half, Basis basis, Color color, bool bottom = false)
    {
        Vector3 P(float x, float y, float z) => center + basis * new Vector3(x * half.X, y * half.Y, z * half.Z);
        var c000 = P(-1, -1, -1); var c100 = P(1, -1, -1); var c010 = P(-1, 1, -1); var c110 = P(1, 1, -1);
        var c001 = P(-1, -1, 1); var c101 = P(1, -1, 1); var c011 = P(-1, 1, 1); var c111 = P(1, 1, 1);
        Quad(c010, c110, c111, c011, color);                 // top
        Quad(c011, c111, c101, c001, color.Darkened(0.06f)); // +z
        Quad(c110, c010, c000, c100, color.Darkened(0.10f)); // -z
        Quad(c111, c110, c100, c101, color.Darkened(0.04f)); // +x
        Quad(c010, c011, c001, c000, color.Darkened(0.14f)); // -x
        if (bottom) Quad(c001, c101, c100, c000, color.Darkened(0.3f));
    }

    public void Box(Vector3 center, Vector3 half, Color color) => Box(center, half, Basis.Identity, color);

    /// <summary>Box between two ground points with given width and height (e.g. a wall segment).</summary>
    public void Beam(Vector3 from, Vector3 to, float width, float height, Color color)
    {
        var dir = to - from;
        float len = dir.Length();
        if (len < 1e-4f) return;
        var x = dir / len;
        var y = Vector3.Up;
        var z = x.Cross(y).Normalized();
        y = z.Cross(x);
        var basis = new Basis(x, y, z);
        Box((from + to) * 0.5f + Vector3.Up * (height * 0.5f), new Vector3(len * 0.5f, height * 0.5f, width * 0.5f), basis, color);
    }

    /// <summary>Low-poly cylinder/prism with <paramref name="sides"/> sides, standing on <paramref name="baseCenter"/>.</summary>
    public void Prism(Vector3 baseCenter, float radiusBottom, float radiusTop, float height, int sides, Color color, float rotation = 0)
    {
        for (int i = 0; i < sides; i++)
        {
            float a0 = rotation + Mathf.Tau * i / sides, a1 = rotation + Mathf.Tau * (i + 1) / sides;
            var b0 = baseCenter + new Vector3(Mathf.Cos(a0) * radiusBottom, 0, Mathf.Sin(a0) * radiusBottom);
            var b1 = baseCenter + new Vector3(Mathf.Cos(a1) * radiusBottom, 0, Mathf.Sin(a1) * radiusBottom);
            var t0 = baseCenter + new Vector3(Mathf.Cos(a0) * radiusTop, height, Mathf.Sin(a0) * radiusTop);
            var t1 = baseCenter + new Vector3(Mathf.Cos(a1) * radiusTop, height, Mathf.Sin(a1) * radiusTop);
            Quad(b0, b1, t1, t0, i % 2 == 0 ? color : color.Darkened(0.08f));
            if (radiusTop > 1e-3f) Tri(baseCenter + Vector3.Up * height, t0, t1, color.Lightened(0.05f));
        }
    }

    /// <summary>Faceted blob (low-poly icosphere-like) used for tree crowns.</summary>
    public void Blob(Vector3 center, Vector3 radius, Color color, int rings = 3, int segments = 6, float jitterSeed = 0)
    {
        var pts = new Vector3[rings + 1, segments];
        for (int r = 0; r <= rings; r++)
        {
            float phi = Mathf.Pi * r / rings;
            for (int s = 0; s < segments; s++)
            {
                float th = Mathf.Tau * s / segments + (r % 2) * Mathf.Pi / segments;
                float j = 1f + 0.12f * Mathf.Sin(jitterSeed * 13.1f + r * 7.7f + s * 3.3f);
                if (r == 0 || r == rings) j = 1f;
                pts[r, s] = center + new Vector3(
                    Mathf.Sin(phi) * Mathf.Cos(th) * radius.X * j,
                    Mathf.Cos(phi) * radius.Y * j,
                    Mathf.Sin(phi) * Mathf.Sin(th) * radius.Z * j);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                var c = color.Darkened(0.05f * ((r + s) % 3));
                if (r == 0) Tri(pts[0, 0], pts[1, s], pts[1, s1], c);
                else if (r == rings - 1) Tri(pts[r, s], pts[rings, 0], pts[r, s1], c);
                else
                {
                    Tri(pts[r, s], pts[r + 1, s1], pts[r, s1], c);
                    Tri(pts[r, s], pts[r + 1, s], pts[r + 1, s1], c.Darkened(0.04f));
                }
            }
    }

    public ArrayMesh ToMesh(Material? material = null)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
        var mesh = new ArrayMesh();
        if (_verts.Count == 0) return mesh;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (material != null) mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    /// <summary>Triangles as a flat vertex list, for building concave collision shapes.</summary>
    public Vector3[] Faces() => _verts.ToArray();

    public MeshInstance3D ToInstance(string name, Material? material = null, bool castShadows = true)
    {
        return new MeshInstance3D
        {
            Name = name,
            Mesh = ToMesh(material ?? Materials.VertexColor),
            CastShadow = castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
