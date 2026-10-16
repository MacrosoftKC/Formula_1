using F1.Core.Geometry;
using Godot;

namespace F1.Game.Core;

/// <summary>
/// Conversions between track space (X east, Y north, metres, doubles) and Godot world space
/// (X east, Y up, -Z north, floats).
/// </summary>
public static class Units
{
    public static Vector3 ToWorld(Vec2 p, float height = 0f) => new((float)p.X, height, (float)-p.Y);
    public static Vector3 DirToWorld(Vec2 d) => new((float)d.X, 0f, (float)-d.Y);
    public static Vec2 ToTrack(Vector3 p) => new(p.X, -p.Z);
    public static Vec2 DirToTrack(Vector3 d) => new(d.X, -d.Z);

    public const float MsToKmh = 3.6f;
    public const float KmhToMs = 1f / 3.6f;

    public static Color Hex(string hex) => Color.FromHtml(hex);
}
