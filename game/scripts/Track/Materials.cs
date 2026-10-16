using Godot;

namespace F1.Game.Track;

/// <summary>Shared materials. Almost everything is vertex-coloured so the whole world uses a handful of materials.</summary>
public static class Materials
{
    private static StandardMaterial3D? _vertexColor, _vertexColorUnshaded, _water, _glass, _emissive, _doubleSided;

    // Vertex colours are authored in sRGB (Color.FromHtml), so tell Godot to linearise them.
    public static StandardMaterial3D VertexColor => _vertexColor ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 1.0f,
        MetallicSpecular = 0.12f,
    };

    public static StandardMaterial3D VertexColorDoubleSided => _doubleSided ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 1.0f,
        MetallicSpecular = 0.12f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public static StandardMaterial3D VertexColorUnshaded => _vertexColorUnshaded ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    public static StandardMaterial3D Water => _water ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.36f, 0.72f, 0.86f),
        Metallic = 0.15f,
        Roughness = 0.08f,
        MetallicSpecular = 0.7f,
    };

    public static StandardMaterial3D Glass => _glass ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.45f, 0.6f, 0.7f),
        Metallic = 0.5f,
        Roughness = 0.1f,
    };

    public static StandardMaterial3D Emissive(Color color, float energy = 2f) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    public static StandardMaterial3D Solid(Color color, float roughness = 0.8f) => new()
    {
        AlbedoColor = color,
        Roughness = roughness,
    };
}
