using Godot;

namespace F1.Game.Vehicle;

/// <summary>
/// Seamless light-grey studio, the car-select look used by the showroom and the main menu: the lit floor is calibrated
/// to the backdrop grey (217, 217, 219), key light from the front-left above, soft contact shadows.
/// </summary>
public static class Studio
{
    public static readonly Color Backdrop = new(0.851f, 0.851f, 0.859f);

    /// <summary>Adds the environment, key light and floor to <paramref name="parent"/>; returns the floor.</summary>
    public static MeshInstance3D Build(Node3D parent)
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Backdrop,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.93f, 0.94f, 0.97f),
            AmbientLightEnergy = 0.42f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 0.95f,
            AdjustmentContrast = 1.0f,
        };
        parent.AddChild(new WorldEnvironment { Name = "Studio", Environment = env });
        var key = new DirectionalLight3D
        {
            Name = "Key", LightEnergy = 0.32f, LightColor = new Color(1f, 0.99f, 0.97f), ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal, DirectionalShadowMaxDistance = 30, ShadowBlur = 3.5f,
        };
        parent.AddChild(key);
        key.GlobalBasis = Basis.LookingAt(new Vector3(0.45f, -1.0f, 0.55f), Vector3.Up);
        var floor = new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(4000, 4000) },
            // Lambert (no grazing-angle brightening), albedo calibrated so the lit floor lands on the backdrop grey
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.73f, 0.73f, 0.737f), Roughness = 1.0f, MetallicSpecular = 0.0f,
                DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
            },
        };
        parent.AddChild(floor);
        return floor;
    }
}
