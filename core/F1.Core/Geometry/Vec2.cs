using System.Text.Json;
using System.Text.Json.Serialization;

namespace F1.Core.Geometry;

/// <summary>Double-precision 2D vector. Track space: X = east, Y = north, metres.</summary>
[JsonConverter(typeof(Vec2JsonConverter))]
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        double l = Length;
        return l > 1e-12 ? this / l : Zero;
    }

    /// <summary>Perpendicular pointing to the left of this direction (rotated +90°).</summary>
    public Vec2 PerpLeft => new(-Y, X);

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;

    public Vec2 Rotated(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}

/// <summary>Serialises <see cref="Vec2"/> as a compact [x, y] array rounded to millimetres.</summary>
public sealed class Vec2JsonConverter : JsonConverter<Vec2>
{
    public override Vec2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Vec2 must be [x, y]");
        reader.Read(); double x = reader.GetDouble();
        reader.Read(); double y = reader.GetDouble();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Vec2 must be [x, y]");
        return new Vec2(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Vec2 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Math.Round(value.X, 3));
        writer.WriteNumberValue(Math.Round(value.Y, 3));
        writer.WriteEndArray();
    }
}
