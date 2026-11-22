using F1.Core.Data;
using F1.Game.Core;
using F1.Game.Track;
using Godot;

namespace F1.Game.Vehicle;

/// <summary>
/// Procedural faceted 2022+ F1 car in team livery, modelled on the reference car-select renders: a broad flat-topped
/// nose running down onto a wide three-flap front wing with tall curved endplates, a rounded chassis, bulky undercut
/// sidepods with inlets and pinstripes, a tall airbox with a T-camera, a tubular halo, a swan-neck rear wing with
/// curved tips, floor with fences, mirrors, suspension, driver and chunky wheels with coloured rim rings and hubs.
/// Team names and the car number are printed on the bodywork. Wheels steer, spin and follow the suspension; the DRS
/// flap opens. Local frame: forward −Z, right +X, up +Y, origin on the ground under the centre of the car.
/// </summary>
public partial class CarModel : Node3D
{
    private readonly Node3D[] _wheelRoots = new Node3D[4];
    private readonly Node3D[] _wheelSteer = new Node3D[4];
    private readonly Node3D[] _wheelSpin = new Node3D[4];
    private Node3D _drsFlap = null!;
    private Node3D _steeringWheel = null!;
    private MeshInstance3D _rainLight = null!;
    private StandardMaterial3D _rainOn = null!, _rainOff = null!;
    private float _drsAngle;
    private double _blink;

    public Node3D FrontWing { get; private set; } = null!;
    /// <summary>Node at the steering wheel's display; the cockpit camera can attach a screen here.</summary>
    public Node3D WheelDisplay { get; private set; } = null!;
    public Node3D DriverHead { get; private set; } = null!;
    /// <summary>Engine cover, airbox and T-cam; the over-the-shoulder camera turns it shadow-only so it can sit
    /// right behind the driver's helmet.</summary>
    public MeshInstance3D EngineCover { get; private set; } = null!;

    private static readonly Color Carbon = new(0.11f, 0.11f, 0.12f);
    private static readonly Color Dark = new(0.06f, 0.06f, 0.07f);
    private static readonly Color Tyre = new(0.13f, 0.13f, 0.14f);
    private static readonly Color WheelCover = new(0.19f, 0.19f, 0.21f);
    private static readonly Color TcamYellow = new(0.85f, 0.95f, 0.06f);
    private static readonly Color MirrorGlass = new(0.55f, 0.6f, 0.65f);

    // ---- bodywork key sections (smoothed along the car by BodyLoft) ------------------------------------------------
    //                 Z       Cx    W      Yb     Yt     TopW   BotW   Mid    Pl    Pu

    /// <summary>Broad, flat-topped nose rising to the chassis; rounded cockpit sides.</summary>
    private static readonly Section[] ChassisKeys =
    {
        new(-2.70f, 0, 0.26f, 0.095f, 0.21f, 0.07f, 0.20f, 0.40f, 2.6f, 2.2f),
        new(-2.40f, 0, 0.30f, 0.11f, 0.29f, 0.08f, 0.22f, 0.42f, 2.5f, 2.2f),
        new(-2.00f, 0, 0.36f, 0.13f, 0.38f, 0.10f, 0.24f, 0.44f, 2.4f, 2.3f),
        new(-1.55f, 0, 0.45f, 0.135f, 0.48f, 0.16f, 0.28f, 0.47f, 2.3f, 2.5f),
        new(-1.10f, 0, 0.56f, 0.12f, 0.58f, 0.30f, 0.32f, 0.50f, 2.3f, 2.8f),
        new(-0.75f, 0, 0.62f, 0.11f, 0.65f, 0.38f, 0.34f, 0.50f, 2.2f, 2.8f),
        new(-0.20f, 0, 0.70f, 0.10f, 0.69f, 0.48f, 0.36f, 0.50f, 2.2f, 2.8f),
        new(0.25f, 0, 0.74f, 0.10f, 0.69f, 0.52f, 0.38f, 0.50f, 2.2f, 2.8f),
        new(0.55f, 0, 0.66f, 0.12f, 0.65f, 0.46f, 0.36f, 0.50f, 2.2f, 2.8f),
    };

    /// <summary>Tall airbox above the driver's head tapering to a ridged spine at the gearbox.</summary>
    private static readonly Section[] EngineKeys =
    {
        new(0.16f, 0, 0.42f, 0.58f, 1.03f, 0.15f, 0.30f, 0.40f, 2.0f, 2.4f),
        new(0.34f, 0, 0.46f, 0.54f, 1.02f, 0.17f, 0.30f, 0.42f, 2.0f, 2.4f),
        new(0.55f, 0, 0.50f, 0.48f, 0.96f, 0.16f, 0.30f, 0.45f, 2.0f, 2.4f),
        new(1.00f, 0, 0.44f, 0.38f, 0.80f, 0.10f, 0.28f, 0.45f, 2.0f, 2.2f),
        new(1.50f, 0, 0.34f, 0.30f, 0.62f, 0.06f, 0.24f, 0.45f, 2.0f, 2.0f),
        new(2.00f, 0, 0.22f, 0.26f, 0.45f, 0.04f, 0.16f, 0.45f, 2.0f, 2.0f),
    };

    /// <summary>Right sidepod (use <see cref="Section.Mirrored"/> for the left): bulky, strong undercut, top sloping
    /// down to the rear.</summary>
    private static readonly Section[] SidepodKeys =
    {
        new(-0.62f, 0.56f, 0.50f, 0.16f, 0.64f, 0.30f, 0.16f, 0.62f, 1.8f, 2.6f),
        new(-0.35f, 0.60f, 0.60f, 0.13f, 0.67f, 0.40f, 0.16f, 0.60f, 1.8f, 2.6f),
        new(0.10f, 0.62f, 0.62f, 0.12f, 0.64f, 0.42f, 0.16f, 0.58f, 1.8f, 2.6f),
        new(0.60f, 0.58f, 0.56f, 0.11f, 0.55f, 0.36f, 0.16f, 0.55f, 1.9f, 2.5f),
        new(1.10f, 0.45f, 0.40f, 0.10f, 0.42f, 0.24f, 0.14f, 0.52f, 2.0f, 2.4f),
        new(1.62f, 0.28f, 0.18f, 0.10f, 0.29f, 0.08f, 0.08f, 0.50f, 2.0f, 2.2f),
    };

    private static readonly Section[] Chassis = BodyLoft.Smooth(ChassisKeys, 3);
    private static readonly Section[] Engine = BodyLoft.Smooth(EngineKeys, 3);
    private static readonly Section[] PodRight = BodyLoft.Smooth(SidepodKeys, 3);
    private static readonly Section[] PodLeft = PodRight.Select(s => s.Mirrored).ToArray();

    private static Color C(string? hex, string fallback) => Units.Hex(string.IsNullOrEmpty(hex) ? fallback : hex);
    private static Color C(string? hex, Color fallback) => string.IsNullOrEmpty(hex) ? fallback : Units.Hex(hex);

    public void Build(TeamData team, DriverData driver)
    {
        var lv = team.Livery;
        var primary = Units.Hex(team.Primary);
        var accent = Units.Hex(team.Accent);
        var noseTip = C(lv.NoseTip, team.Primary);
        var sidepod = C(lv.Sidepod, team.Primary);
        var engine = C(lv.EngineCover, team.Primary);
        var engineTop = C(lv.EngineCoverTop, engine);
        var airbox = C(lv.Airbox, lv.EngineCover ?? team.Primary);
        var frontWing = C(lv.FrontWing, team.Secondary);
        var frontWingMain = C(lv.FrontWingMain, Carbon);
        var endplates = C(lv.Endplates, team.Primary);
        var rearWing = C(lv.RearWing, team.Secondary);
        var rearFlap = C(lv.RearWingFlap, rearWing);
        var rearEndplates = C(lv.RearEndplates, rearWing.Darkened(0.15f));
        var halo = C(lv.Halo, team.Primary);
        var text = C(lv.Text, "#FFFFFF");
        Color? stripe = string.IsNullOrEmpty(lv.Stripe) ? null : Units.Hex(lv.Stripe);
        Color? floorEdge = string.IsNullOrEmpty(lv.FloorEdge) ? null : Units.Hex(lv.FloorEdge);
        Color? floorFlash = string.IsNullOrEmpty(lv.FloorFlash) ? null : Units.Hex(lv.FloorFlash);
        var hub = C(lv.Hub, accent);
        int seat = team.Drivers.FindIndex(d => d.Number == driver.Number);
        var tcam = C(lv.Tcam, seat == 1 ? TcamYellow : Dark);
        string logo = string.IsNullOrWhiteSpace(lv.Logo) ? team.Name : lv.Logo;

        var m = new LowPolyMesh();
        BuildFloor(m, floorEdge);

        // ---- nose and chassis: the nose-tip colour runs back NoseTipLength from the tip -------------------------
        float tipEnd = Chassis[0].Z + Mathf.Clamp((float)lv.NoseTipLength, 0.05f, 1.6f);
        var chassis = BodyLoft.SplitAt(Chassis, tipEnd);
        Color Nose(float z) => z < tipEnd ? noseTip : primary;
        BodyLoft.Loft(m, chassis, new BodyPaint(Nose, Nose, Nose, Front: noseTip));
        // cockpit opening (dark padding), body-coloured rims and headrest pads round the helmet
        m.QuadUp(new Vector3(-0.2f, 0.672f, -0.5f), new Vector3(0.2f, 0.672f, -0.5f), new Vector3(0.23f, 0.694f, 0.2f), new Vector3(-0.23f, 0.694f, 0.2f), new Color(0.2f, 0.2f, 0.23f));
        foreach (int side in new[] { -1, 1 })
        {
            m.Box(new Vector3(side * 0.245f, 0.705f, -0.15f), new Vector3(0.03f, 0.02f, 0.36f), Basis.Identity, primary, bottom: true);
            m.Box(new Vector3(side * 0.2f, 0.73f, 0.1f), new Vector3(0.055f, 0.045f, 0.12f), Basis.Identity, primary.Darkened(0.04f), bottom: true);
        }

        // ---- engine cover, airbox intake and T-camera ---------------------------------------------------------
        // the airbox colour covers the whole airbox, then runs AirboxLength back over the top of the engine cover
        float airboxBack = Engine[0].Z + 0.32f, airboxEnd = Engine[0].Z + Mathf.Max((float)lv.AirboxLength, 0.32f);
        var engineCover = BodyLoft.SplitAt(BodyLoft.SplitAt(Engine, airboxBack), airboxEnd);
        var eng = new LowPolyMesh();
        BodyLoft.Loft(eng, engineCover, new BodyPaint(z => z < airboxEnd ? airbox : engineTop, z => z < airboxEnd ? airbox : engineTop,
            z => z < airboxBack ? airbox : engine, Front: airbox.Darkened(0.08f)));
        BodyLoft.Mouth(eng, Engine[0], 0.58f, Engine[0].MidY + 0.0f, Engine[0].Yt - 0.07f, Dark);
        // T-cam at the back of the airbox: behind the onboard camera positions so it never blocks them
        eng.Box(new Vector3(0, 1.03f, 0.46f), new Vector3(0.014f, 0.045f, 0.018f), tcam);
        eng.Box(new Vector3(0, 1.085f, 0.46f), new Vector3(0.11f, 0.018f, 0.028f), Basis.Identity, tcam, bottom: true);
        EngineCover = eng.ToInstance("EngineCover");
        AddChild(EngineCover);

        // ---- sidepods with inlets, pinstripes, mirrors and suspension ------------------------------------------
        foreach (int side in new[] { -1, 1 })
        {
            var pod = side > 0 ? PodRight : PodLeft;
            BodyLoft.Loft(m, pod, new BodyPaint(_ => primary, _ => sidepod, _ => sidepod, Front: sidepod.Darkened(0.12f)));
            var front = pod[0];
            BodyLoft.Mouth(m, front, 0.84f, front.MidY - 0.06f, front.Yt - 0.035f, Dark);
            if (stripe is { } s)
            {
                // along the top of the sidepod shoulder, continuing forward along the chassis
                BodyLoft.Band(m, pod, side, BodyRegion.Upper, 0.56f, 0.66f, s);
                BodyLoft.Band(m, chassis, side, BodyRegion.Upper, 0.12f, 0.24f, s, -1.95f, -0.8f);
            }
            if (floorEdge is { } fe)
                BodyLoft.Band(m, pod, side, BodyRegion.Lower, 0.72f, 0.9f, fe);
            if (floorFlash is { } ff)
                BodyLoft.Band(m, pod, side, BodyRegion.Lower, 0.45f, 0.95f, ff, -0.5f, 0.05f);
            BuildMirror(m, side, primary);
            BuildSuspension(m, side);
        }

        // ---- front wing, rear wing, halo -----------------------------------------------------------------------
        FrontWing = new Node3D { Name = "FrontWing" };
        AddChild(FrontWing);
        FrontWing.AddChild(FrontWingMesh(frontWingMain, frontWing, endplates).ToInstance("FrontWingMesh", Materials.VertexColorDoubleSided));
        if (lv.FrontWingLogo)
            foreach (int side in new[] { -1, 1 })
                AddLabel(FrontWing, logo, new Vector3(side * 0.5f, 0.078f, -2.69f), LabelBasis(Vector3.Up, Vector3.Back), Readable(text, frontWingMain), 0.74f, 0.2f);

        m.Box(new Vector3(0, 0.36f, 2.13f), new Vector3(0.075f, 0.07f, 0.17f), Basis.Identity, Carbon, bottom: true); // crash structure
        AddChild(RearWingMesh(rearWing, rearEndplates).ToInstance("RearWing", Materials.VertexColorDoubleSided));
        _drsFlap = new Node3D { Name = "DrsFlap", Position = new Vector3(0, 0.99f, 2.44f) };
        AddChild(_drsFlap);
        _drsFlap.AddChild(FlapMesh(rearFlap).ToInstance("Flap", Materials.VertexColorDoubleSided));
        var flapNormal = new Vector3(0, FlapChord.Z, -FlapChord.Y).Normalized();
        AddLabel(_drsFlap, logo, FlapChord * 0.5f + flapNormal * 0.012f, LabelBasis(flapNormal, FlapChord), Readable(text, rearFlap), 0.8f, 0.13f);

        Halo(m, halo);

        // ---- rain light ---------------------------------------------------------------------------------------
        _rainOff = Materials.Solid(new Color(0.25f, 0.02f, 0.02f), 0.4f);
        _rainOn = Materials.Emissive(new Color(1f, 0.05f, 0.05f), 6f);
        _rainLight = new MeshInstance3D { Name = "RainLight", Mesh = new BoxMesh { Size = new Vector3(0.11f, 0.06f, 0.025f) }, MaterialOverride = _rainOff, Position = new Vector3(0, 0.36f, 2.31f) };
        AddChild(_rainLight);

        // ---- driver: helmet and steering wheel ----------------------------------------------------------------
        DriverHead = new Node3D { Name = "DriverHead", Position = new Vector3(0, 0.83f, -0.03f) };
        AddChild(DriverHead);
        var head = new LowPolyMesh();
        HelmetShell(head, Units.Hex(driver.Helmet), primary);
        head.Box(new Vector3(0, 0.0f, -0.13f), new Vector3(0.11f, 0.035f, 0.05f), Dark);
        DriverHead.AddChild(head.ToInstance("Helmet"));

        _steeringWheel = new Node3D { Name = "SteeringWheel", Position = SteeringWheelPos, Scale = Vector3.One * 0.88f };
        _steeringWheel.Rotation = new Vector3(Mathf.DegToRad(SteeringWheelTiltDeg), 0, 0);
        AddChild(_steeringWheel);
        _steeringWheel.AddChild(SteeringWheelMesh(team).ToInstance("Wheel"));
        WheelDisplay = new Node3D { Name = "Display", Position = new Vector3(0, 0.012f, 0.022f) };
        _steeringWheel.AddChild(WheelDisplay);

        AddChild(m.ToInstance("Body"));

        // ---- lettering: logo on both sidepods and in front of the cockpit, car number on the nose --------------
        var decals = lv.Decals ?? new Dictionary<string, string>();
        foreach (int side in new[] { -1, 1 })
        {
            var pod = side > 0 ? PodRight : PodLeft;
            // big lettering on the near-vertical flank, just above the widest point
            var (pos, normal, up) = BodyLoft.Surface(pod, 0.2f, side, BodyRegion.Upper, 0.1f);
            if (decals.TryGetValue("sidepod", out var podDecal)) AddDecal(this, podDecal, pos + normal * 0.006f, LabelBasis(normal, up), 1.0f, 0.32f);
            else AddLabel(this, logo, pos + normal * 0.006f, LabelBasis(normal, up), Readable(text, sidepod), 1.05f, 0.34f);
            if (decals.TryGetValue("engineCover", out var ecDecal))
            {
                var (p2, n2, u2) = BodyLoft.Surface(Engine, 0.62f, side, BodyRegion.Upper, 0.45f);
                AddDecal(this, ecDecal, p2 + n2 * 0.008f, LabelBasis(n2, u2), 0.6f, 0.25f);
            }
            if (decals.TryGetValue("nose", out var noseDecal))
            {
                var (p3, n3, u3) = BodyLoft.Surface(Chassis, -2.2f, side, BodyRegion.Upper, 0.45f);
                AddDecal(this, noseDecal, p3 + n3 * 0.006f, LabelBasis(n3, u3), 0.34f, 0.12f);
            }
        }
        if (decals.TryGetValue("chassisTop", out var topDecal))
        {
            var (p4, n4, _) = BodyLoft.Surface(Chassis, -1.0f, 0, BodyRegion.Top, 0.5f);
            AddDecal(this, topDecal, p4 + n4 * 0.006f, LabelBasis(n4, n4.Cross(Vector3.Right).Normalized()), 0.3f, 0.14f);
        }
        {
            var (pos, normal, _) = BodyLoft.Surface(Chassis, -1.55f, 0, BodyRegion.Top, 0.5f);
            var forward = normal.Cross(Vector3.Right).Normalized(); // along the nose towards the tip
            AddLabel(this, driver.Number.ToString(), pos + normal * 0.006f, LabelBasis(normal, forward), Readable(text, primary), 0.2f, 0.16f);
        }
        {
            // readable from the driver's seat, between the halo pillar and the cockpit
            var (pos, normal, _) = BodyLoft.Surface(Chassis, -0.74f, 0, BodyRegion.Top, 0.5f);
            var forward = normal.Cross(Vector3.Right).Normalized();
            AddLabel(this, logo, pos + normal * 0.006f, LabelBasis(normal, forward), Readable(text, primary), 0.36f, 0.11f);
        }

        // ---- wheels -------------------------------------------------------------------------------------------
        var rim = Units.Hex(team.Rim);
        var wheelMesh = WheelMesh(rim, hub, false);
        var rearWheelMesh = WheelMesh(rim, hub, true);
        Vector3[] pos4 =
        {
            new(-Car.FrontTrackHalf, Car.WheelRadius, Car.FrontAxleZ), new(Car.FrontTrackHalf, Car.WheelRadius, Car.FrontAxleZ),
            new(-Car.RearTrackHalf, Car.WheelRadius, Car.RearAxleZ), new(Car.RearTrackHalf, Car.WheelRadius, Car.RearAxleZ),
        };
        for (int i = 0; i < 4; i++)
        {
            _wheelRoots[i] = new Node3D { Name = $"Wheel{i}", Position = pos4[i] };
            _wheelSteer[i] = new Node3D { Name = "Steer" };
            _wheelSpin[i] = new Node3D { Name = "Spin" };
            AddChild(_wheelRoots[i]);
            _wheelRoots[i].AddChild(_wheelSteer[i]);
            _wheelSteer[i].AddChild(_wheelSpin[i]);
            var mi = new MeshInstance3D { Mesh = i < 2 ? wheelMesh : rearWheelMesh };
            if (i % 2 == 1) mi.Rotation = new Vector3(0, Mathf.Pi, 0); // rim face outwards on the right side
            _wheelSpin[i].AddChild(mi);
        }
    }

    // ================================================================================================================
    // floor, mirrors, suspension

    private static void BuildFloor(LowPolyMesh m, Color? floorEdge)
    {
        var right = new[]
        {
            new Vector2(0.36f, -1.40f), new Vector2(0.50f, -1.10f), new Vector2(0.72f, -0.86f), new Vector2(0.93f, -0.72f),
            new Vector2(0.955f, -0.50f), new Vector2(0.96f, 1.00f), new Vector2(0.90f, 1.12f), new Vector2(0.64f, 1.36f),
            new Vector2(0.56f, 1.60f), new Vector2(0.55f, 2.00f), new Vector2(0.42f, 2.14f),
        };
        var outline = right.Concat(Enumerable.Reverse(right).Select(p => new Vector2(-p.X, p.Y))).ToArray();
        Slab(m, outline, 0.035f, 0.07f, Carbon);
        foreach (int side in new[] { -1, 1 })
        {
            // upturned edge lip (in the floor-edge livery colour when there is one) and fences under the sidepod nose
            m.Beam(new Vector3(side * 0.948f, 0.07f, -0.66f), new Vector3(side * 0.958f, 0.07f, 1.0f), 0.016f, 0.045f, floorEdge ?? Carbon);
            foreach (float x in new[] { 0.42f, 0.58f, 0.74f })
                Plate(m, side * x, 0.012f, new[] { new Vector2(-1.06f, 0.07f), new Vector2(-0.62f, 0.07f), new Vector2(-0.62f, 0.2f), new Vector2(-0.96f, 0.15f) }, Carbon);
            // diffuser side fences
            var f0 = new Vector3(side * 0.52f, 0.07f, 1.75f); var f1 = new Vector3(side * 0.52f, 0.34f, 2.3f); var f2 = new Vector3(side * 0.52f, 0.07f, 2.3f);
            m.QuadFacing(f0, f1, f2, f0, Vector3.Right, Carbon.Darkened(0.1f));
            m.QuadFacing(f0, f1, f2, f0, Vector3.Left, Carbon.Darkened(0.1f));
        }
        m.QuadFacing(new Vector3(-0.52f, 0.07f, 1.75f), new Vector3(0.52f, 0.07f, 1.75f), new Vector3(0.52f, 0.34f, 2.3f), new Vector3(-0.52f, 0.34f, 2.3f), new Vector3(0, -1, 1), Carbon);
    }

    /// <summary>Mirror: a thin stalk from the cockpit side to a rounded body-coloured housing with the glass facing back.</summary>
    private static void BuildMirror(LowPolyMesh m, int side, Color housing)
    {
        Tube(m, new[] { new Vector3(side * 0.25f, 0.66f, -0.53f), new Vector3(side * 0.42f, 0.71f, -0.55f), new Vector3(side * 0.52f, 0.75f, -0.55f) }, 0.012f, 0.012f, 6, Carbon);
        var shell = new[]
        {
            new Section(-0.59f, side * 0.575f, 0.13f, 0.735f, 0.79f, 0.07f, 0.08f, 0.5f, 2.4f, 2.4f),
            new Section(-0.565f, side * 0.575f, 0.15f, 0.728f, 0.80f, 0.09f, 0.10f, 0.5f, 2.4f, 2.4f),
            new Section(-0.515f, side * 0.575f, 0.15f, 0.728f, 0.80f, 0.09f, 0.10f, 0.5f, 2.4f, 2.4f),
        };
        BodyLoft.Loft(m, shell, BodyPaint.Solid(housing, back: MirrorGlass));
    }

    private static void BuildSuspension(LowPolyMesh m, int side)
    {
        // front: double wishbones (aero-section blades), pushrod and track rod
        Strut(m, new Vector3(side * 0.16f, 0.40f, -1.98f), new Vector3(side * 0.62f, 0.48f, -1.80f), 0.045f, 0.016f, Carbon);
        Strut(m, new Vector3(side * 0.18f, 0.42f, -1.55f), new Vector3(side * 0.62f, 0.48f, -1.80f), 0.045f, 0.016f, Carbon);
        Strut(m, new Vector3(side * 0.13f, 0.20f, -2.02f), new Vector3(side * 0.63f, 0.22f, -1.80f), 0.045f, 0.016f, Carbon);
        Strut(m, new Vector3(side * 0.17f, 0.18f, -1.50f), new Vector3(side * 0.63f, 0.22f, -1.80f), 0.045f, 0.016f, Carbon);
        Strut(m, new Vector3(side * 0.60f, 0.24f, -1.76f), new Vector3(side * 0.16f, 0.46f, -1.68f), 0.028f, 0.028f, Carbon);
        Strut(m, new Vector3(side * 0.14f, 0.31f, -1.90f), new Vector3(side * 0.62f, 0.33f, -1.92f), 0.024f, 0.014f, Carbon);
        // upright
        m.Box(new Vector3(side * 0.63f, 0.35f, -1.8f), new Vector3(0.02f, 0.14f, 0.05f), Basis.Identity, Dark, bottom: true);
        // rear
        Strut(m, new Vector3(side * 0.24f, 0.42f, 1.55f), new Vector3(side * 0.55f, 0.50f, 1.80f), 0.045f, 0.016f, Carbon);
        Strut(m, new Vector3(side * 0.20f, 0.42f, 2.02f), new Vector3(side * 0.55f, 0.50f, 1.80f), 0.045f, 0.016f, Carbon);
        Strut(m, new Vector3(side * 0.26f, 0.20f, 1.50f), new Vector3(side * 0.56f, 0.22f, 1.80f), 0.045f, 0.016f, Carbon);
        Strut(m, new Vector3(side * 0.20f, 0.20f, 2.06f), new Vector3(side * 0.56f, 0.22f, 1.80f), 0.045f, 0.016f, Carbon);
        m.Box(new Vector3(side * 0.56f, 0.36f, 1.8f), new Vector3(0.02f, 0.14f, 0.06f), Basis.Identity, Dark, bottom: true);
    }

    // ================================================================================================================
    // wings

    /// <summary>Main plane flat under the nose, three flaps sweeping up outboard, tall endplates with curved tops.</summary>
    private static LowPolyMesh FrontWingMesh(Color main, Color flaps, Color endplates)
    {
        var fw = new LowPolyMesh();
        static float Up(float x) => Mathf.Pow(Mathf.Clamp((Mathf.Abs(x) - 0.12f) / 0.78f, 0, 1), 1.6f);
        float[] full = { -0.9f, -0.75f, -0.6f, -0.45f, -0.3f, -0.15f, 0, 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f };
        WingElement(fw, full, -2.80f, -2.58f, 0.05f, 0.07f, 0.0f, 0.02f, main, Up);
        foreach (int side in new[] { -1, 1 })
        {
            var xs = new[] { 0.12f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f }.Select(x => x * side).ToArray();
            WingElement(fw, xs, -2.62f, -2.47f, 0.08f, 0.105f, 0.02f, 0.035f, main.Darkened(0.05f), Up);
            WingElement(fw, xs, -2.50f, -2.37f, 0.108f, 0.135f, 0.03f, 0.05f, flaps, Up);
            WingElement(fw, xs.Skip(1).ToArray(), -2.40f, -2.30f, 0.132f, 0.16f, 0.04f, 0.07f, flaps.Darkened(0.1f), Up);
            Plate(fw, side * 0.915f, 0.02f, new[]
            {
                new Vector2(-2.82f, 0.035f), new Vector2(-2.29f, 0.035f), new Vector2(-2.29f, 0.30f), new Vector2(-2.40f, 0.31f),
                new Vector2(-2.52f, 0.28f), new Vector2(-2.64f, 0.22f), new Vector2(-2.74f, 0.15f), new Vector2(-2.80f, 0.09f),
            }, endplates);
        }
        return fw;
    }

    private static readonly float[] RearSpan = { -0.52f, -0.47f, -0.41f, -0.3f, -0.15f, 0, 0.15f, 0.3f, 0.41f, 0.47f, 0.52f };
    /// <summary>Rear wing tips curl down into the endplates.</summary>
    private static float Droop(float x) => Mathf.Pow(Mathf.Clamp((Mathf.Abs(x) - 0.36f) / 0.16f, 0, 1), 2f);
    /// <summary>DRS flap chord from its pivot (leading edge) in the flap's frame.</summary>
    private static readonly Vector3 FlapChord = new(0, 0.105f, 0.17f);

    private static LowPolyMesh RearWingMesh(Color wing, Color endplates)
    {
        var rw = new LowPolyMesh();
        foreach (int side in new[] { -1, 1 })
        {
            Plate(rw, side * 0.53f, 0.022f, new[]
            {
                new Vector2(2.20f, 0.42f), new Vector2(2.64f, 0.42f), new Vector2(2.64f, 1.05f), new Vector2(2.61f, 1.10f),
                new Vector2(2.54f, 1.13f), new Vector2(2.42f, 1.14f), new Vector2(2.31f, 1.125f), new Vector2(2.23f, 1.07f),
            }, endplates);
            // swan-neck pylons hooking over onto the main plane
            Tube(rw, new[] { new Vector3(side * 0.07f, 0.42f, 1.98f), new Vector3(side * 0.07f, 0.86f, 2.12f), new Vector3(side * 0.07f, 1.0f, 2.22f), new Vector3(side * 0.07f, 0.95f, 2.31f) }, 0.014f, 0.03f, 6, Carbon);
        }
        WingElement(rw, RearSpan, 2.28f, 2.50f, 0.92f, 0.975f, -0.06f, -0.06f, wing, Droop);
        rw.Box(new Vector3(0, 0.44f, 2.3f), new Vector3(0.52f, 0.01f, 0.06f), new Basis(Vector3.Right, -0.2f), Carbon, bottom: true);
        rw.Box(new Vector3(0, 0.49f, 2.44f), new Vector3(0.52f, 0.01f, 0.05f), new Basis(Vector3.Right, -0.45f), Carbon, bottom: true);
        return rw;
    }

    private static LowPolyMesh FlapMesh(Color c)
    {
        var flap = new LowPolyMesh();
        WingElement(flap, RearSpan.Select(x => x * 0.98f).ToArray(), 0, FlapChord.Z, 0, FlapChord.Y, -0.05f, -0.05f, c, Droop);
        return flap;
    }

    /// <summary>
    /// One wing element across the span stations <paramref name="xs"/>: chord from <paramref name="zLe"/> to <paramref name="zTe"/>,
    /// leading/trailing edge heights changing by <paramref name="riseLe"/>/<paramref name="riseTe"/> × shape(x).
    /// </summary>
    private static void WingElement(LowPolyMesh m, float[] xs, float zLe, float zTe, float yLe, float yTe, float riseLe, float riseTe, Color c, Func<float, float> shape)
    {
        const float thick = 0.014f;
        Vector3 Le(float x) { float u = shape(x); return new(x, yLe + riseLe * u, zLe + 0.03f * Mathf.Max(u, 0)); }
        Vector3 Te(float x) { float u = shape(x); return new(x, yTe + riseTe * u, zTe); }
        var down = new Vector3(0, -thick, 0);
        for (int j = 0; j < xs.Length - 1; j++)
        {
            float x0 = xs[j], x1 = xs[j + 1];
            m.QuadUp(Le(x0), Le(x1), Te(x1), Te(x0), c);
            m.QuadFacing(Le(x0), Le(x1), Le(x1) + down, Le(x0) + down, Vector3.Forward, c.Darkened(0.25f));
            m.QuadFacing(Te(x0), Te(x1), Te(x1) + down, Te(x0) + down, Vector3.Back, c.Darkened(0.3f));
        }
    }

    // ================================================================================================================
    // lettering

    private static Font? _logoFont;

    /// <summary>Bold, slanted version of the default font for sponsor-style lettering (same on every platform).</summary>
    private static Font LogoFont => _logoFont ??= new FontVariation
    {
        BaseFont = ThemeDB.FallbackFont,
        VariationEmbolden = 0.8f,
        VariationTransform = new Transform2D(1.0f, 0.22f, 0.0f, 1.0f, 0.0f, 0.0f),
    };

    /// <summary>Adds text sized to fit <paramref name="maxWidth"/> × <paramref name="maxHeight"/> metres, facing along the basis Z.</summary>
    private static void AddLabel(Node parent, string text, Vector3 pos, Basis basis, Color color, float maxWidth, float maxHeight)
    {
        const int size = 96;
        var font = LogoFont;
        float w = Mathf.Max(font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X, 1f);
        float h = Mathf.Max(font.GetHeight(size), 1f);
        parent.AddChild(new Label3D
        {
            Text = text, Font = font, FontSize = size, PixelSize = Mathf.Min(maxWidth / w, maxHeight / h),
            Modulate = color, OutlineSize = 0, Shaded = true, DoubleSided = false, VisibilityRangeEnd = 120,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Transform = new Transform3D(basis, pos),
        });
    }

    private static readonly Dictionary<string, Texture2D?> DecalCache = new();

    /// <summary>Adds a logo image (PNG with transparency) sized to fit <paramref name="maxWidth"/> × <paramref name="maxHeight"/>
    /// metres. Paths may be res://, user:// or absolute; a missing image is skipped with a warning.</summary>
    private static void AddDecal(Node parent, string path, Vector3 pos, Basis basis, float maxWidth, float maxHeight)
    {
        if (!DecalCache.TryGetValue(path, out var tex))
        {
            tex = null;
            string file = path.StartsWith("res://") || path.StartsWith("user://") ? ProjectSettings.GlobalizePath(path) : path;
            var img = System.IO.File.Exists(file) ? Image.LoadFromFile(file) : null;
            if (img != null && !img.IsEmpty()) tex = ImageTexture.CreateFromImage(img);
            else GD.PushWarning($"Livery decal not found: {path}");
            DecalCache[path] = tex;
        }
        if (tex == null) return;
        var size = tex.GetSize();
        parent.AddChild(new Sprite3D
        {
            Texture = tex, PixelSize = Mathf.Min(maxWidth / size.X, maxHeight / size.Y), Shaded = true, DoubleSided = false,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 120, Transform = new Transform3D(basis, pos),
        });
    }

    /// <summary>Basis for a Label3D: text faces along <paramref name="normal"/> with its top towards <paramref name="up"/>.</summary>
    private static Basis LabelBasis(Vector3 normal, Vector3 up)
    {
        var z = normal.Normalized();
        var x = up.Cross(z).Normalized();
        return new Basis(x, z.Cross(x), z);
    }

    /// <summary>Keeps the livery's text colour unless it would vanish on <paramref name="bg"/>; then picks white or black.</summary>
    private static Color Readable(Color text, Color bg)
    {
        static float Contrast(Color a, Color b)
        {
            float la = a.Luminance + 0.05f, lb = b.Luminance + 0.05f;
            return Mathf.Max(la, lb) / Mathf.Min(la, lb);
        }
        float dist = new Vector3(text.R - bg.R, text.G - bg.G, text.B - bg.B).Length();
        if (dist > 0.45f || Contrast(text, bg) >= 2.2f) return text;
        var white = new Color(0.97f, 0.97f, 0.97f);
        var black = new Color(0.07f, 0.07f, 0.08f);
        return Contrast(white, bg) >= Contrast(black, bg) ? white : black;
    }

    // ================================================================================================================
    // shared shapes

    private static void Slab(LowPolyMesh m, Vector2[] outline, float y0, float y1, Color c)
    {
        var center = Vector2.Zero;
        foreach (var p in outline) center += p;
        center /= outline.Length;
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Length];
            m.TriUp(new Vector3(center.X, y1, center.Y), new Vector3(a.X, y1, a.Y), new Vector3(b.X, y1, b.Y), c);
            m.QuadFacing(new Vector3(center.X, y0, center.Y), new Vector3(a.X, y0, a.Y), new Vector3(b.X, y0, b.Y), new Vector3(center.X, y0, center.Y), Vector3.Down, c.Darkened(0.3f));
            var mid = (a + b) * 0.5f - center;
            m.QuadFacing(new Vector3(a.X, y0, a.Y), new Vector3(b.X, y0, b.Y), new Vector3(b.X, y1, b.Y), new Vector3(a.X, y1, a.Y), new Vector3(mid.X, 0, mid.Y), c.Darkened(0.2f));
        }
    }

    /// <summary>Box along a segment with a w × h cross-section (wishbones).</summary>
    private static void Strut(LowPolyMesh m, Vector3 a, Vector3 b, float w, float h, Color color)
    {
        var dir = b - a;
        float len = dir.Length();
        if (len < 1e-4f) return;
        var x = dir / len;
        var refUp = Mathf.Abs(x.Dot(Vector3.Up)) > 0.9f ? Vector3.Forward : Vector3.Up;
        var z = x.Cross(refUp).Normalized();
        var y = z.Cross(x);
        m.Box((a + b) * 0.5f, new Vector3(len * 0.5f, h * 0.5f, w * 0.5f), new Basis(x, y, z), color, bottom: true);
    }

    /// <summary>Faceted tube along a polyline with an elliptical cross-section (rw across, rh up), closed at both ends.</summary>
    private static void Tube(LowPolyMesh m, IList<Vector3> path, float rw, float rh, int sides, Color c)
    {
        var rings = new Vector3[path.Count][];
        for (int i = 0; i < path.Count; i++)
        {
            var t = (path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)]).Normalized();
            var refUp = Mathf.Abs(t.Dot(Vector3.Up)) > 0.95f ? Vector3.Forward : Vector3.Up;
            var n = (refUp - t * t.Dot(refUp)).Normalized();
            var b = t.Cross(n);
            rings[i] = new Vector3[sides];
            for (int j = 0; j < sides; j++)
            {
                float a = Mathf.Tau * (j + 0.5f) / sides;
                rings[i][j] = path[i] + n * (Mathf.Cos(a) * rh) + b * (Mathf.Sin(a) * rw);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
        {
            var axis = (path[i] + path[i + 1]) * 0.5f;
            for (int j = 0; j < sides; j++)
            {
                int j1 = (j + 1) % sides;
                var p0 = rings[i][j]; var p1 = rings[i][j1]; var q0 = rings[i + 1][j]; var q1 = rings[i + 1][j1];
                m.QuadFacing(p0, p1, q1, q0, (p0 + p1 + q0 + q1) * 0.25f - axis, j % 2 == 0 ? c : c.Darkened(0.04f));
            }
        }
        BodyLoft.Cap(m, rings[0], path[0] - path[1], c.Darkened(0.1f));
        BodyLoft.Cap(m, rings[^1], path[^1] - path[^2], c.Darkened(0.1f));
    }

    /// <summary>Vertical plate in the plane x = <paramref name="x"/>: convex outline given as (z, y) points.</summary>
    private static void Plate(LowPolyMesh m, float x, float thickness, Vector2[] zy, Color c)
    {
        var centroid = Vector2.Zero;
        foreach (var p in zy) centroid += p;
        centroid /= zy.Length;
        Vector3 P(Vector2 p, float s) => new(x + s * thickness * 0.5f, p.Y, p.X);
        foreach (int s in new[] { -1, 1 })
            for (int i = 1; i < zy.Length - 1; i++)
                m.QuadFacing(P(zy[0], s), P(zy[i], s), P(zy[i + 1], s), P(zy[0], s), new Vector3(s, 0, 0), s * x > 0 ? c : c.Darkened(0.12f));
        for (int i = 0; i < zy.Length; i++)
        {
            var a = zy[i]; var b = zy[(i + 1) % zy.Length];
            var mid = (a + b) * 0.5f - centroid;
            m.QuadFacing(P(a, -1), P(b, -1), P(b, 1), P(a, 1), new Vector3(0, mid.Y, mid.X), c.Darkened(0.08f));
        }
    }

    /// <summary>Flat n-gon facing <paramref name="facing"/> (in the XY plane for ±Z, YZ plane for ±X).</summary>
    private static void Disc(LowPolyMesh m, Vector3 center, float radius, int sides, Vector3 facing, Color c)
    {
        bool zFacing = Mathf.Abs(facing.Z) > 0.5f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * (i + 0.5f) / sides, a1 = Mathf.Tau * (i + 1.5f) / sides;
            Vector3 P(float a) => center + (zFacing ? new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) : new Vector3(0, Mathf.Sin(a), Mathf.Cos(a))) * radius;
            m.QuadFacing(center, P(a0), P(a1), center, facing, c);
        }
    }

    private static void Halo(LowPolyMesh m, Color c)
    {
        // hoop round the head, highest at the front and dropping towards the rear legs (so from the cockpit it arches
        // down into the corners); rear legs on the cockpit sides; centre pillar leaning forward down to the chassis
        const float frontY = 0.995f, rearDrop = 0.1f, rx = 0.31f, rz = 0.6f, cz = 0.18f;
        var hoop = new List<Vector3>();
        for (int k = 0; k <= 16; k++)
        {
            float a = Mathf.Pi * k / 16f; // 0 = right rear, pi = left rear, through the front
            hoop.Add(new Vector3(Mathf.Cos(a) * rx, frontY - (1 - Mathf.Sin(a)) * rearDrop, -Mathf.Sin(a) * rz + cz));
        }
        Tube(m, hoop, 0.042f, 0.028f, 8, c);
        foreach (int side in new[] { -1, 1 })
        {
            var top = hoop[side > 0 ? 0 : 16];
            Tube(m, new[] { new Vector3(side * 0.27f, 0.68f, cz + 0.06f), new Vector3(side * 0.29f, 0.8f, cz + 0.03f), top }, 0.032f, 0.036f, 8, c);
        }
        Tube(m, new[] { new Vector3(0, 0.62f, -0.92f), new Vector3(0, 0.84f, -0.62f), new Vector3(0, frontY, cz - rz) }, 0.032f, 0.028f, 8, c);
    }

    /// <summary>Faceted helmet in team colours with the driver's colour on the crown and as a stripe round it.</summary>
    private static void HelmetShell(LowPolyMesh m, Color crown, Color lower)
    {
        const int rings = 6, segs = 12;
        var rad = new Vector3(0.15f, 0.15f, 0.17f);
        var pts = new Vector3[rings + 1, segs];
        for (int r = 0; r <= rings; r++)
        {
            float phi = Mathf.Pi * r / rings;
            for (int s = 0; s < segs; s++)
            {
                float th = Mathf.Tau * s / segs + (r % 2) * Mathf.Pi / segs;
                pts[r, s] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th) * rad.X, Mathf.Cos(phi) * rad.Y, Mathf.Sin(phi) * Mathf.Sin(th) * rad.Z);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segs; s++)
            {
                int s1 = (s + 1) % segs;
                var c = (r == 0 || r == 2 ? crown : lower).Darkened(0.04f * ((r + s) % 3));
                if (r == 0) m.Tri(pts[0, 0], pts[1, s], pts[1, s1], c);
                else if (r == rings - 1) m.Tri(pts[r, s], pts[rings, 0], pts[r, s1], c);
                else
                {
                    m.Tri(pts[r, s], pts[r + 1, s1], pts[r, s1], c);
                    m.Tri(pts[r, s], pts[r + 1, s], pts[r + 1, s1], c.Darkened(0.03f));
                }
            }
    }

    // ================================================================================================================
    // steering wheel

    private static readonly Vector3 SteeringWheelPos = new(0, 0.7f, -0.46f);
    private const float SteeringWheelTiltDeg = -58f;

    /// <summary>
    /// Steering wheel in its own frame (face towards +Z = the driver, top +Y): carbon body with a screen, blue shift
    /// lights, two columns of coloured buttons each side, rotaries below, rubber grips, gloved hands and forearms.
    /// The live screen (<see cref="F1.Game.UI.SteeringWheelDisplay"/>) is laid over the blank one on the player's car.
    /// </summary>
    private static LowPolyMesh SteeringWheelMesh(TeamData team)
    {
        var sw = new LowPolyMesh();
        const float face = 0.02f;
        // body: main plate, narrower chin, grips splayed outwards
        sw.Box(Vector3.Zero, new Vector3(0.125f, 0.07f, face), Basis.Identity, Carbon, bottom: true);
        sw.Box(new Vector3(0, -0.08f, -0.002f), new Vector3(0.075f, 0.025f, 0.017f), Basis.Identity, Carbon, bottom: true);
        foreach (int side in new[] { -1, 1 })
            sw.Box(new Vector3(side * 0.15f, -0.012f, 0.004f), new Vector3(0.03f, 0.085f, 0.028f), new Basis(Vector3.Back, -side * 0.12f), Dark, bottom: true);
        // blank screen (the player's live display sits just in front) and the blue shift lights beside it
        sw.QuadFacing(new Vector3(-0.077f, -0.026f, face + 0.0015f), new Vector3(0.077f, -0.026f, face + 0.0015f),
            new Vector3(0.077f, 0.05f, face + 0.0015f), new Vector3(-0.077f, 0.05f, face + 0.0015f), Vector3.Back, new Color(0.03f, 0.03f, 0.05f));
        foreach (int side in new[] { -1, 1 })
            sw.Box(new Vector3(side * 0.085f, 0.03f, face + 0.002f), new Vector3(0.0035f, 0.014f, 0.002f), new Color(0.3f, 0.75f, 1f));
        // buttons: two columns each side
        Color[] buttons =
        {
            new(1f, 0.85f, 0.1f), new(0.95f, 0.15f, 0.12f), new(0.95f, 0.95f, 0.95f), new(0.2f, 0.8f, 0.3f),
            new(1f, 0.55f, 0.1f), new(0.2f, 0.55f, 1f), new(0.7f, 0.35f, 0.95f), new(0.25f, 0.85f, 0.9f),
        };
        int bi = 0;
        foreach (int side in new[] { -1, 1 })
            foreach (float x in new[] { 0.094f, 0.112f })
                foreach (float y in new[] { 0.052f, 0.026f, 0.0f, -0.026f })
                {
                    if (x > 0.1f && y < 0) continue; // outer column is shorter, the grips are in the way
                    sw.Box(new Vector3(side * x, y, face + 0.003f), new Vector3(0.0075f, 0.0075f, 0.003f), buttons[bi++ % buttons.Length]);
                }
        // rotaries: a rainbow one in the middle, coloured ones either side and below
        Color[] rainbow = { new(0.95f, 0.2f, 0.15f), new(1f, 0.6f, 0.1f), new(1f, 0.9f, 0.1f), new(0.3f, 0.85f, 0.3f), new(0.2f, 0.75f, 0.95f), new(0.3f, 0.4f, 1f), new(0.65f, 0.35f, 0.95f), new(0.95f, 0.35f, 0.7f) };
        Rotary(sw, new Vector3(0, -0.055f, face), 0.022f, rainbow);
        Rotary(sw, new Vector3(-0.048f, -0.05f, face), 0.013f, new[] { new Color(0.25f, 0.55f, 1f) });
        Rotary(sw, new Vector3(0.048f, -0.05f, face), 0.013f, new[] { new Color(0.3f, 0.85f, 0.4f) });
        Rotary(sw, new Vector3(-0.03f, -0.088f, face), 0.012f, new[] { new Color(1f, 0.8f, 0.15f) });
        Rotary(sw, new Vector3(0.03f, -0.088f, face), 0.012f, new[] { new Color(1f, 0.5f, 0.15f) });

        // gloved hands with long gauntlets, then race-suit sleeves running back towards the driver
        var glove = Units.Hex(team.Secondary);
        if (glove.Luminance < 0.08f || glove.Luminance > 0.85f) glove = Units.Hex(team.Accent);
        var suit = Units.Hex(team.Primary);
        foreach (int side in new[] { -1, 1 })
        {
            sw.Box(new Vector3(side * 0.158f, 0.012f, 0.04f), new Vector3(0.036f, 0.05f, 0.026f), new Basis(Vector3.Back, -side * 0.12f), glove, bottom: true);
            sw.Box(new Vector3(side * 0.128f, 0.045f, 0.04f), new Vector3(0.01f, 0.018f, 0.01f), new Basis(Vector3.Back, side * 0.6f), glove.Darkened(0.1f));
            var wrist = new Vector3(side * 0.165f, -0.035f, 0.045f);
            var elbow = new Vector3(side * 0.23f, -0.4f, 0.09f);
            var cuff = wrist.Lerp(elbow, 0.38f);
            Strut(sw, wrist, cuff, 0.07f, 0.06f, glove.Darkened(0.05f));
            Strut(sw, cuff, elbow, 0.08f, 0.07f, suit.Lightened(0.04f));
        }
        return sw;
    }

    /// <summary>Rotary switch on the wheel face: a ring of coloured segments round a dark knob.</summary>
    private static void Rotary(LowPolyMesh m, Vector3 c, float r, Color[] ring)
    {
        const int n = 8;
        float z = c.Z + 0.0025f;
        for (int i = 0; i < n; i++)
        {
            float a0 = Mathf.Tau * i / n, a1 = Mathf.Tau * (i + 1) / n;
            Vector3 P(float rad, float a, float zz) => new(c.X + Mathf.Cos(a) * rad, c.Y + Mathf.Sin(a) * rad, zz);
            m.QuadFacing(P(r, a0, z), P(r, a1, z), P(r * 0.62f, a1, z), P(r * 0.62f, a0, z), Vector3.Back, ring[i % ring.Length]);
            m.QuadFacing(P(r * 0.55f, a0, z + 0.006f), P(r * 0.55f, a1, z + 0.006f), new Vector3(c.X, c.Y, z + 0.006f), new Vector3(c.X, c.Y, z + 0.006f), Vector3.Back, new Color(0.16f, 0.16f, 0.18f));
            m.QuadFacing(P(r * 0.55f, a0, z), P(r * 0.55f, a1, z), P(r * 0.55f, a1, z + 0.006f), P(r * 0.55f, a0, z + 0.006f), P(1, (a0 + a1) / 2, 0) - new Vector3(c.X, c.Y, 0), Dark);
        }
    }

    // ================================================================================================================
    // wheels

    private static ArrayMesh WheelMesh(Color rimRing, Color hub, bool rear)
    {
        var m = new LowPolyMesh();
        float r = Car.WheelRadius, w = rear ? 0.4f : 0.3f, rimR = r * 0.62f, cham = 0.035f;
        const int sides = 16;
        float hw = w / 2, xRing = -hw * 0.97f, xCover = -hw * 0.9f, ringIn = rimR * 0.84f;
        Vector3 P(float rad, float a, float x) => new(x, Mathf.Sin(a) * rad, Mathf.Cos(a) * rad);
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides, am = (a0 + a1) / 2;
            var radial = P(1, am, 0);
            var shade = i % 2 == 0 ? Tyre : Tyre.Lightened(0.03f);
            // tread, chamfered shoulders and a sidewall that bulges slightly before tucking in to the rim
            m.QuadFacing(P(r, a0, -hw + cham), P(r, a1, -hw + cham), P(r, a1, hw - cham), P(r, a0, hw - cham), radial, shade);
            foreach (float sgn in new[] { -1f, 1f })
            {
                var axial = new Vector3(sgn, 0, 0);
                m.QuadFacing(P(r, a0, sgn * (hw - cham)), P(r, a1, sgn * (hw - cham)), P(r - cham, a1, sgn * hw), P(r - cham, a0, sgn * hw), radial + axial, shade.Darkened(0.05f));
                float rb = (r - cham + rimR) * 0.5f;
                m.QuadFacing(P(r - cham, a0, sgn * hw), P(r - cham, a1, sgn * hw), P(rb, a1, sgn * hw * 1.02f), P(rb, a0, sgn * hw * 1.02f), axial, Tyre.Darkened(0.08f));
                m.QuadFacing(P(rb, a0, sgn * hw * 1.02f), P(rb, a1, sgn * hw * 1.02f), P(rimR, a1, sgn * hw * 0.97f), P(rimR, a0, sgn * hw * 0.97f), axial, Tyre.Darkened(0.12f));
            }
            // outer face (−X): coloured rim ring, a short step, the wheel cover and the hub
            m.QuadFacing(P(rimR, a0, xRing), P(rimR, a1, xRing), P(ringIn, a1, xRing), P(ringIn, a0, xRing), Vector3.Left, rimRing);
            m.QuadFacing(P(ringIn, a0, xRing), P(ringIn, a1, xRing), P(ringIn, a1, xCover), P(ringIn, a0, xCover), -radial, Dark);
            m.QuadFacing(P(ringIn, a0, xCover), P(ringIn, a1, xCover), new Vector3(xCover, 0, 0), new Vector3(xCover, 0, 0), Vector3.Left, i % 2 == 0 ? WheelCover : WheelCover.Lightened(0.03f));
            m.QuadFacing(P(0.055f, a0, xCover - 0.012f), P(0.055f, a1, xCover - 0.012f), new Vector3(xCover - 0.012f, 0, 0), new Vector3(xCover - 0.012f, 0, 0), Vector3.Left, hub);
            m.QuadFacing(P(0.055f, a0, xCover), P(0.055f, a1, xCover), P(0.055f, a1, xCover - 0.012f), P(0.055f, a0, xCover - 0.012f), radial, hub.Darkened(0.2f));
            // inner face (+X)
            m.QuadFacing(P(rimR, a0, hw * 0.97f), P(rimR, a1, hw * 0.97f), new Vector3(hw * 0.9f, 0, 0), new Vector3(hw * 0.9f, 0, 0), Vector3.Right, Dark);
        }
        return m.ToMesh(Materials.VertexColorDoubleSided);
    }

    // ================================================================================================================
    // runtime

    /// <summary>Per-frame visual update: wheel spin/steer/suspension, DRS flap, steering wheel, rain light.</summary>
    public void UpdateVisuals(Car car, double dt)
    {
        for (int i = 0; i < 4; i++)
        {
            var w = car.Wheels[i];
            var p = _wheelRoots[i].Position;
            p.Y = Car.HardpointHeight - (float)w.CenterDistance;
            _wheelRoots[i].Position = p;
            _wheelSteer[i].Rotation = new Vector3(0, (float)w.SteerAngle, 0);
            _wheelSpin[i].Rotation = new Vector3(-(float)(w.SpinAngle % Math.Tau), 0, 0);
        }
        float target = car.DrsOpen ? 0.75f : 0f;
        _drsAngle = Mathf.MoveToward(_drsAngle, target, (float)dt * 6f);
        _drsFlap.Rotation = new Vector3(_drsAngle, 0, 0);
        _steeringWheel.Rotation = new Vector3(Mathf.DegToRad(SteeringWheelTiltDeg), 0, 0);
        _steeringWheel.RotateObjectLocal(Vector3.Forward, -car.SteerApplied * 1.6f);

        bool rain = car.Wetness > 0.15f;
        bool limiterBlink = car.PitLimiter && ((_blink += dt) % 0.5) < 0.25;
        _rainLight.MaterialOverride = rain || limiterBlink ? _rainOn : _rainOff;
    }

    /// <summary>True while the showroom holds the DRS flap open.</summary>
    public bool DrsOpenPreview { get; private set; }

    /// <summary>Snaps the DRS flap open or shut without a car driving it (showroom preview).</summary>
    public void SetDrsOpen(bool open)
    {
        DrsOpenPreview = open;
        _drsAngle = open ? 0.75f : 0f;
        _drsFlap.Rotation = new Vector3(_drsAngle, 0, 0);
    }

    /// <summary>Hides the front wing (heavy damage) or shows it again (after a pit stop).</summary>
    public void SetFrontWingAttached(bool attached) => FrontWing.Visible = attached;
}
