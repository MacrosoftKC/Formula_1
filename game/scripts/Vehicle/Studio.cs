using Godot;

namespace F1.Game.Vehicle;

/// <summary>
/// Seamless light-grey studio, the car-select look used by the showroom and the main menu: the lit floor is calibrated
/// to the backdrop grey (217, 217, 219), key light from the front-left above, soft contact shadows.
/// </summary>
public sealed class Studio
{
    public static readonly Color Backdrop = new(0.851f, 0.851f, 0.859f);
    private readonly Godot.Environment _environment;
    private readonly DirectionalLight3D _key, _fill;
    private readonly StandardMaterial3D _floorPaint;
    private Sky? _studioSky;
    public MeshInstance3D Floor { get; }

    public Studio(Node3D parent)
    {
        _environment = new Godot.Environment
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
        parent.AddChild(new WorldEnvironment { Name = "Studio", Environment = _environment });
        _key = new DirectionalLight3D
        {
            Name = "Key", LightEnergy = 0.32f, LightColor = new Color(1f, 0.99f, 0.97f), ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal, DirectionalShadowMaxDistance = 30, ShadowBlur = 3.5f,
        };
        parent.AddChild(_key);
        _key.GlobalBasis = Basis.LookingAt(new Vector3(0.45f, -1.0f, 0.55f), Vector3.Up);
        _fill = new DirectionalLight3D
        {
            Name = "StudioFill", LightEnergy = 0.38f, LightColor = Color.FromHtml("#D4DEFF"), Visible = false,
        };
        parent.AddChild(_fill);
        _fill.GlobalBasis = Basis.LookingAt(new Vector3(0.65f, -0.55f, -0.6f), Vector3.Up);
        _floorPaint = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.73f, 0.73f, 0.737f), Roughness = 1.0f, MetallicSpecular = 0.0f,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
        };
        Floor = new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(4000, 4000) },
            MaterialOverride = _floorPaint,
        };
        parent.AddChild(Floor);
    }

    public void LightForTeam(string teamId)
    {
        bool detailed = teamId is "red_bull" or F1.Core.Data.TeamData.W11 or F1.Core.Data.TeamData.F2004;
        // Keep the white planes and polished rims legible next to the saturated Ferrari paint.
        _environment.TonemapExposure = teamId == F1.Core.Data.TeamData.F2004 ? 0.88f : 1.0f;
        _environment.BackgroundColor = detailed ? Color.FromHtml("#D7D9E5") : Backdrop;
        _environment.AmbientLightEnergy = detailed ? 0.46f : 0.42f;
        _environment.SsaoEnabled = detailed;
        _environment.SsaoRadius = 0.42f;
        _environment.SsaoIntensity = 1.5f;
        if (detailed)
            _environment.Sky = _studioSky ??= new Sky { SkyMaterial = new ProceduralSkyMaterial {
                SkyTopColor = Color.FromHtml("#CDD4EA"), SkyHorizonColor = Color.FromHtml("#667089"),
                GroundHorizonColor = Color.FromHtml("#454B5C"), GroundBottomColor = Color.FromHtml("#21232C"),
            }};
        _environment.ReflectedLightSource = detailed ? Godot.Environment.ReflectionSource.Sky : Godot.Environment.ReflectionSource.Disabled;
        _key.LightEnergy = detailed ? 1.04f : 0.32f;
        _key.GlobalBasis = Basis.LookingAt(detailed ? new Vector3(-0.36f, -1, 0.68f) : new Vector3(0.45f, -1, 0.55f), Vector3.Up);
        _key.ShadowBlur = detailed ? 1.35f : 3.5f;
        _fill.Visible = detailed;
        _floorPaint.AlbedoColor = detailed ? Color.FromHtml("#BCC0CB") : new Color(0.73f, 0.73f, 0.737f);
    }
}
