using Godot;

namespace F1.Game.Track.Builders;

/// <summary>Bright, slightly hazy afternoon sky and sun, matching the low-poly reference look.</summary>
public static class EnvironmentBuilder
{
    public static (WorldEnvironment Env, DirectionalLight3D Sun) Build(Node3D parent)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.66f, 0.79f, 0.93f),
            SkyHorizonColor = new Color(0.93f, 0.95f, 0.97f),
            GroundBottomColor = new Color(0.55f, 0.62f, 0.5f),
            GroundHorizonColor = new Color(0.86f, 0.89f, 0.92f),
            SunAngleMax = 30,
            SkyCurve = 0.12f,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.75f,
            AmbientLightEnergy = 1.0f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            TonemapWhite = 6.0f,
            FogEnabled = true,
            FogLightColor = new Color(0.84f, 0.88f, 0.93f),
            FogDensity = 0.00011f,
            FogAerialPerspective = 0.25f,
            FogSkyAffect = 0.15f,
            GlowEnabled = true,
            GlowIntensity = 0.35f,
            GlowBloom = 0.02f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.25f,
            AdjustmentContrast = 1.08f,
        };
        var worldEnv = new WorldEnvironment { Name = "WorldEnvironment", Environment = env };
        parent.AddChild(worldEnv);

        // Southern-hemisphere afternoon: the sun is high in the north-west.
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.15f,
            LightColor = new Color(1.0f, 0.97f, 0.92f),
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 260,
            ShadowBlur = 1.2f,
        };
        parent.AddChild(sun);
        // pointing down (pitch) and towards the south-east (yaw), i.e. coming from the north-west
        sun.RotationDegrees = new Vector3(-52, 150, 0);
        return (worldEnv, sun);
    }
}
