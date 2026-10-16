using F1.Core.Data;
using F1.Game.Core;
using F1.Game.Track;
using Godot;

namespace F1.Game.Vehicle;

/// <summary>
/// Procedural low-poly 2022+ F1 car in team livery: lofted nose/chassis/sidepods/engine cover, multi-element wings,
/// halo, floor, mirrors, suspension, driver and wheels. Wheels steer, spin and follow the suspension; the DRS flap opens.
/// Local frame: forward −Z, right +X, up +Y, origin on the ground under the centre of the car.
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

    private static readonly Color Carbon = new(0.11f, 0.11f, 0.12f);
    private static readonly Color Dark = new(0.06f, 0.06f, 0.07f);
    private static readonly Color Tyre = new(0.13f, 0.13f, 0.14f);

    private static Color C(string? hex, string fallback) => Units.Hex(string.IsNullOrEmpty(hex) ? fallback : hex);

    public void Build(TeamData team, DriverData driver)
    {
        var lv = team.Livery;
        var primary = Units.Hex(team.Primary);
        var secondary = Units.Hex(team.Secondary);
        var accent = Units.Hex(team.Accent);
        var noseTip = C(lv.NoseTip, team.Primary);
        var sidepod = C(lv.Sidepod, team.Primary);
        var engine = C(lv.EngineCover, team.Primary);
        var airbox = C(lv.Airbox, lv.EngineCover ?? team.Primary);
        var frontWing = C(lv.FrontWing, team.Secondary);
        var endplates = C(lv.Endplates, team.Primary);
        var rearWing = C(lv.RearWing, team.Secondary);
        var halo = C(lv.Halo, team.Primary);
        var text = C(lv.Text, "#FFFFFF");

        var m = new LowPolyMesh();

        // floor and plank
        Slab(m, new[] { new Vector2(-0.45f, -1.45f), new Vector2(0.45f, -1.45f), new Vector2(0.82f, -0.6f), new Vector2(0.82f, 1.55f), new Vector2(0.5f, 2.05f), new Vector2(-0.5f, 2.05f), new Vector2(-0.82f, 1.55f), new Vector2(-0.82f, -0.6f) }, 0.035f, 0.075f, Carbon);
        // diffuser ramp
        m.QuadFacing(new Vector3(-0.5f, 0.075f, 1.75f), new Vector3(0.5f, 0.075f, 1.75f), new Vector3(0.5f, 0.32f, 2.3f), new Vector3(-0.5f, 0.32f, 2.3f), new Vector3(0, -1, 1), Carbon);

        // nose and chassis
        Loft(m, new[]
        {
            new Station(-2.62f, 0, 0.17f, 0.16f, 0.26f, 0.10f),
            new Station(-2.1f, 0, 0.28f, 0.15f, 0.35f, 0.18f),
            new Station(-1.4f, 0, 0.42f, 0.13f, 0.47f, 0.26f),
            new Station(-0.85f, 0, 0.58f, 0.11f, 0.6f, 0.4f),
            new Station(0.25f, 0, 0.72f, 0.1f, 0.64f, 0.52f),
            new Station(0.55f, 0, 0.62f, 0.12f, 0.6f, 0.46f),
        }, i => i == 0 ? noseTip : primary, i => i == 0 ? noseTip : primary);
        // cockpit opening (dark grey padding) with body-coloured rims
        m.QuadUp(new Vector3(-0.22f, 0.615f, -0.5f), new Vector3(0.22f, 0.615f, -0.5f), new Vector3(0.25f, 0.645f, 0.2f), new Vector3(-0.25f, 0.645f, 0.2f), new Color(0.2f, 0.2f, 0.23f));
        foreach (int side in new[] { -1, 1 })
            m.Box(new Vector3(side * 0.27f, 0.655f, -0.15f), new Vector3(0.035f, 0.02f, 0.36f), primary);

        // engine cover with airbox
        Loft(m, new[]
        {
            new Station(0.12f, 0, 0.44f, 0.55f, 0.98f, 0.26f),
            new Station(0.45f, 0, 0.56f, 0.45f, 0.92f, 0.32f),
            new Station(1.2f, 0, 0.46f, 0.32f, 0.7f, 0.22f),
            new Station(2.05f, 0, 0.2f, 0.25f, 0.46f, 0.1f),
        }, i => i == 0 ? airbox : engine, i => i == 0 ? airbox : engine);
        m.QuadFacing(new Vector3(-0.12f, 0.82f, 0.115f), new Vector3(0.12f, 0.82f, 0.115f), new Vector3(0.12f, 0.95f, 0.115f), new Vector3(-0.12f, 0.95f, 0.115f), Vector3.Forward, Dark);

        // sidepods
        foreach (int side in new[] { -1, 1 })
        {
            Loft(m, new[]
            {
                new Station(-0.45f, side * 0.6f, 0.34f, 0.13f, 0.56f, 0.28f),
                new Station(0.15f, side * 0.65f, 0.42f, 0.1f, 0.53f, 0.36f),
                new Station(0.9f, side * 0.55f, 0.36f, 0.1f, 0.43f, 0.26f),
                new Station(1.65f, side * 0.33f, 0.2f, 0.12f, 0.3f, 0.12f),
            }, i => primary, i => sidepod);
            // sidepod inlet
            m.QuadFacing(new Vector3(side * 0.45f, 0.2f, -0.455f), new Vector3(side * 0.76f, 0.2f, -0.455f), new Vector3(side * 0.74f, 0.5f, -0.455f), new Vector3(side * 0.47f, 0.5f, -0.455f), Vector3.Forward, Dark);
            // mirror on a stalk
            m.Beam(new Vector3(side * 0.3f, 0.6f, -0.55f), new Vector3(side * 0.56f, 0.66f, -0.55f), 0.03f, 0.03f, Carbon);
            m.Box(new Vector3(side * 0.6f, 0.71f, -0.55f), new Vector3(0.07f, 0.04f, 0.03f), primary);
            // suspension arms
            Arm(m, new Vector3(side * 0.22f, 0.35f, -1.75f), new Vector3(side * 0.72f, 0.36f, -1.8f));
            Arm(m, new Vector3(side * 0.2f, 0.18f, -1.6f), new Vector3(side * 0.72f, 0.24f, -1.8f));
            Arm(m, new Vector3(side * 0.25f, 0.4f, 1.6f), new Vector3(side * 0.7f, 0.42f, 1.8f));
            Arm(m, new Vector3(side * 0.25f, 0.2f, 1.7f), new Vector3(side * 0.7f, 0.24f, 1.8f));
        }

        // front wing: main plane plus two flaps rising to the endplates
        FrontWing = new Node3D { Name = "FrontWing" };
        AddChild(FrontWing);
        var fw = new LowPolyMesh();
        for (int e = 0; e < 3; e++)
        {
            float z0 = -2.9f + e * 0.13f, z1 = z0 + 0.16f, y = 0.075f + e * 0.05f;
            foreach (int side in new[] { -1, 1 })
            {
                float inner = 0.12f, outer = 0.97f;
                var a = new Vector3(side * inner, y - 0.01f, z0); var b = new Vector3(side * outer, y + 0.04f * e, z0);
                var c = new Vector3(side * outer, y + 0.03f + 0.04f * e, z1); var d = new Vector3(side * inner, y + 0.02f, z1);
                fw.QuadUp(a, b, c, d, e == 0 ? frontWing : frontWing.Darkened(0.06f * e));
                fw.QuadFacing(a, b, b + new Vector3(0, -0.015f, 0), a + new Vector3(0, -0.015f, 0), Vector3.Forward, frontWing.Darkened(0.2f));
            }
        }
        fw.Box(new Vector3(0, 0.07f, -2.78f), new Vector3(0.14f, 0.02f, 0.14f), frontWing);
        foreach (int side in new[] { -1, 1 })
            fw.Box(new Vector3(side * 0.985f, 0.17f, -2.72f), new Vector3(0.018f, 0.13f, 0.21f), Basis.Identity, endplates);
        var fwInst = fw.ToInstance("FrontWingMesh", Materials.VertexColorDoubleSided);
        FrontWing.AddChild(fwInst);

        // rear wing: endplates, main plane, beam wing, swan-neck pylons; the DRS flap is a separate pivot
        foreach (int side in new[] { -1, 1 })
            m.Box(new Vector3(side * 0.53f, 0.76f, 2.55f), new Vector3(0.018f, 0.27f, 0.2f), Basis.Identity, rearWing.Darkened(0.15f));
        m.Box(new Vector3(0, 0.86f, 2.6f), new Vector3(0.52f, 0.02f, 0.13f), rearWing);
        m.Box(new Vector3(0, 0.42f, 2.45f), new Vector3(0.45f, 0.015f, 0.07f), Carbon);
        foreach (int side in new[] { -1, 1 })
            m.Beam(new Vector3(side * 0.08f, 0.45f, 2.05f), new Vector3(side * 0.08f, 0.45f, 2.5f), 0.025f, 0.42f, Carbon);
        _drsFlap = new Node3D { Name = "DrsFlap", Position = new Vector3(0, 0.9f, 2.48f) };
        AddChild(_drsFlap);
        var flap = new LowPolyMesh();
        flap.Box(new Vector3(0, 0.03f, 0.1f), new Vector3(0.51f, 0.018f, 0.11f), rearWing);
        _drsFlap.AddChild(flap.ToInstance("Flap", Materials.VertexColor));

        // halo
        Halo(m, halo);

        // rain light
        _rainOff = Materials.Solid(new Color(0.25f, 0.02f, 0.02f), 0.4f);
        _rainOn = Materials.Emissive(new Color(1f, 0.05f, 0.05f), 6f);
        _rainLight = new MeshInstance3D { Name = "RainLight", Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.06f, 0.03f) }, MaterialOverride = _rainOff, Position = new Vector3(0, 0.36f, 2.31f) };
        AddChild(_rainLight);

        // driver: helmet and steering wheel
        var helmetColor = Units.Hex(driver.Helmet);
        DriverHead = new Node3D { Name = "DriverHead", Position = new Vector3(0, 0.78f, -0.0f) };
        AddChild(DriverHead);
        var head = new LowPolyMesh();
        head.Blob(Vector3.Zero, new Vector3(0.15f, 0.15f, 0.17f), helmetColor, 3, 8, driver.Number);
        head.Box(new Vector3(0, 0.0f, -0.13f), new Vector3(0.11f, 0.035f, 0.05f), Dark);
        DriverHead.AddChild(head.ToInstance("Helmet"));

        _steeringWheel = new Node3D { Name = "SteeringWheel", Position = new Vector3(0, 0.62f, -0.4f) };
        _steeringWheel.Rotation = new Vector3(Mathf.DegToRad(-62), 0, 0);
        AddChild(_steeringWheel);
        var sw = new LowPolyMesh();
        sw.Box(Vector3.Zero, new Vector3(0.14f, 0.075f, 0.02f), Carbon);
        sw.Box(new Vector3(-0.15f, 0, 0), new Vector3(0.035f, 0.08f, 0.03f), Dark);
        sw.Box(new Vector3(0.15f, 0, 0), new Vector3(0.035f, 0.08f, 0.03f), Dark);
        // gloved hands on the grips (team accent colour like the reference cockpit shot)
        var glove = Units.Hex(team.Livery.NoseTip ?? team.Accent);
        if (glove.Luminance < 0.08f) glove = Units.Hex(team.Secondary);
        foreach (int side in new[] { -1, 1 })
        {
            sw.Box(new Vector3(side * 0.155f, 0.0f, 0.035f), new Vector3(0.045f, 0.06f, 0.035f), glove);
            sw.Box(new Vector3(side * 0.2f, -0.05f, 0.09f), new Vector3(0.035f, 0.035f, 0.08f), glove.Darkened(0.15f));
        }
        _steeringWheel.AddChild(sw.ToInstance("Wheel"));
        WheelDisplay = new Node3D { Name = "Display", Position = new Vector3(0, 0.005f, 0.022f) };
        _steeringWheel.AddChild(WheelDisplay);

        AddChild(m.ToInstance("Body"));

        // labels: team name on the sidepods and rear wing, number on the nose
        AddLabel(team.Name.ToUpperInvariant(), new Vector3(-0.735f, 0.32f, 0.35f), new Vector3(0, -Mathf.Pi / 2, 0), text, 0.0019f);
        AddLabel(team.Name.ToUpperInvariant(), new Vector3(0.735f, 0.32f, 0.35f), new Vector3(0, Mathf.Pi / 2, 0), text, 0.0019f);
        AddLabel(team.Name.ToUpperInvariant(), new Vector3(0, 0.875f, 2.55f), new Vector3(-Mathf.Pi / 2, 0, 0), text, 0.0024f);
        AddLabel(driver.Number.ToString(), new Vector3(0, 0.39f, -1.75f), new Vector3(-Mathf.Pi / 2 + 0.25f, 0, 0), text, 0.0022f);

        // wheels
        var wheelMesh = WheelMesh(Units.Hex(team.Rim), false);
        var rearWheelMesh = WheelMesh(Units.Hex(team.Rim), true);
        Vector3[] pos =
        {
            new(-Car.FrontTrackHalf, Car.WheelRadius, Car.FrontAxleZ), new(Car.FrontTrackHalf, Car.WheelRadius, Car.FrontAxleZ),
            new(-Car.RearTrackHalf, Car.WheelRadius, Car.RearAxleZ), new(Car.RearTrackHalf, Car.WheelRadius, Car.RearAxleZ),
        };
        for (int i = 0; i < 4; i++)
        {
            _wheelRoots[i] = new Node3D { Name = $"Wheel{i}", Position = pos[i] };
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

    private void AddLabel(string text, Vector3 pos, Vector3 rot, Color color, float pixel)
    {
        var l = new Label3D
        {
            Text = text, FontSize = 64, PixelSize = pixel, Modulate = color, OutlineSize = 0,
            Shaded = true, DoubleSided = false, Position = pos, Rotation = rot, VisibilityRangeEnd = 120,
        };
        AddChild(l);
    }

    private readonly record struct Station(float Z, float Cx, float W, float Yb, float Yt, float TopW);

    /// <summary>Lofts hexagonal cross-sections along Z (low-poly body panel).</summary>
    private static void Loft(LowPolyMesh m, Station[] st, Func<int, Color> top, Func<int, Color> side)
    {
        Vector3[] Ring(Station s)
        {
            float mid = s.Yb + (s.Yt - s.Yb) * 0.55f;
            return new[]
            {
                new Vector3(s.Cx - s.W * 0.42f, s.Yb, s.Z), new Vector3(s.Cx + s.W * 0.42f, s.Yb, s.Z),
                new Vector3(s.Cx + s.W * 0.5f, mid, s.Z), new Vector3(s.Cx + s.TopW * 0.5f, s.Yt, s.Z),
                new Vector3(s.Cx - s.TopW * 0.5f, s.Yt, s.Z), new Vector3(s.Cx - s.W * 0.5f, mid, s.Z),
            };
        }
        for (int k = 0; k < st.Length - 1; k++)
        {
            var a = Ring(st[k]); var b = Ring(st[k + 1]);
            var center = new Vector3((st[k].Cx + st[k + 1].Cx) * 0.5f, (st[k].Yb + st[k].Yt) * 0.5f, (st[k].Z + st[k + 1].Z) * 0.5f);
            for (int e = 0; e < 6; e++)
            {
                int f = (e + 1) % 6;
                var faceMid = (a[e] + a[f] + b[e] + b[f]) * 0.25f;
                var outward = faceMid - center;
                outward.Z = 0;
                var color = e == 3 ? top(k) : e == 0 ? Carbon : side(k);
                if (e == 2 || e == 4) color = top(k).Lerp(side(k), 0.5f);
                m.QuadFacing(a[e], a[f], b[f], b[e], outward.LengthSquared() > 1e-6f ? outward : Vector3.Up, color);
            }
        }
        // end caps
        var first = Ring(st[0]); var last = Ring(st[^1]);
        for (int e = 1; e < 5; e++)
        {
            m.QuadFacing(first[0], first[e], first[e + 1], first[0], Vector3.Forward, top(0).Darkened(0.1f));
            m.QuadFacing(last[0], last[e], last[e + 1], last[0], Vector3.Back, side(st.Length - 2).Darkened(0.15f));
        }
    }

    private static void Slab(LowPolyMesh m, Vector2[] outline, float y0, float y1, Color c)
    {
        var center = Vector2.Zero;
        foreach (var p in outline) center += p;
        center /= outline.Length;
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Length];
            m.TriUp(new Vector3(center.X, y1, center.Y), new Vector3(a.X, y1, a.Y), new Vector3(b.X, y1, b.Y), c);
            var mid = (a + b) * 0.5f - center;
            m.QuadFacing(new Vector3(a.X, y0, a.Y), new Vector3(b.X, y0, b.Y), new Vector3(b.X, y1, b.Y), new Vector3(a.X, y1, a.Y), new Vector3(mid.X, 0, mid.Y), c.Darkened(0.2f));
        }
    }

    private static void Arm(LowPolyMesh m, Vector3 a, Vector3 b)
    {
        var dir = b - a;
        float len = dir.Length();
        var basis = new Basis(Vector3.Right.Cross(dir).Normalized() is var ax && ax.LengthSquared() > 1e-6f ? ax : Vector3.Up, Vector3.Right.AngleTo(dir));
        m.Box((a + b) * 0.5f, new Vector3(len * 0.5f, 0.012f, 0.018f), basis, Carbon);
    }

    private static void Halo(LowPolyMesh m, Color c)
    {
        // centre pillar rising from the chassis in front of the driver, then a hoop around the head above eye level
        const float hoopY = 0.93f;
        m.Box(new Vector3(0, (0.6f + hoopY) * 0.5f, -0.6f), new Vector3(0.03f, (hoopY - 0.6f) * 0.5f, 0.025f), Basis.Identity, c);
        var pts = new List<Vector3>();
        for (int k = 0; k <= 12; k++)
        {
            float a = Mathf.Pi * k / 12f; // 0 = right rear, pi = left rear, through the front
            float x = Mathf.Cos(a) * 0.34f;
            float z = -Mathf.Sin(a) * 0.6f + 0.18f;
            float y = hoopY - (1 - Mathf.Sin(a)) * 0.06f;
            pts.Add(new Vector3(x, y, z));
        }
        for (int k = 0; k < pts.Count - 1; k++)
            m.Beam(pts[k], pts[k + 1], 0.045f, 0.035f, c);
        foreach (int side in new[] { -1, 1 })
            m.Box(new Vector3(side * 0.34f, (0.62f + hoopY - 0.06f) * 0.5f, 0.2f), new Vector3(0.025f, (hoopY - 0.06f - 0.62f) * 0.5f, 0.03f), Basis.Identity, c);
    }

    private static ArrayMesh WheelMesh(Color rim, bool rear)
    {
        var m = new LowPolyMesh();
        float r = Car.WheelRadius, w = rear ? 0.4f : 0.3f, rimR = r * 0.62f;
        const int sides = 12;
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
            Vector3 P(float rad, float a, float x) => new(x, Mathf.Sin(a) * rad, Mathf.Cos(a) * rad);
            var shade = i % 2 == 0 ? Tyre : Tyre.Lightened(0.04f);
            // tread
            m.QuadFacing(P(r, a0, -w / 2), P(r, a1, -w / 2), P(r, a1, w / 2), P(r, a0, w / 2), P(1, (a0 + a1) / 2, 0), shade);
            // sidewalls and rims (outer side = -X, inner = +X)
            foreach (float sgn in new[] { -1f, 1f })
            {
                float x = sgn * w / 2;
                m.QuadFacing(P(r, a0, x), P(r, a1, x), P(rimR, a1, x), P(rimR, a0, x), new Vector3(sgn, 0, 0), Tyre.Darkened(0.1f));
                var hubColor = i % 3 == 0 ? rim.Darkened(0.2f) : rim;
                m.Tri(P(rimR, a0, x * 0.85f), P(rimR, a1, x * 0.85f), new Vector3(x * 0.6f, 0, 0), sgn < 0 ? hubColor : Dark);
                m.Tri(P(rimR, a1, x * 0.85f), P(rimR, a0, x * 0.85f), new Vector3(x * 0.6f, 0, 0), sgn < 0 ? hubColor : Dark);
            }
        }
        return m.ToMesh(Materials.VertexColorDoubleSided);
    }

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
        _steeringWheel.Rotation = new Vector3(Mathf.DegToRad(-62), 0, 0);
        _steeringWheel.RotateObjectLocal(Vector3.Forward, -car.SteerApplied * 1.6f);

        bool rain = car.Wetness > 0.15f;
        bool limiterBlink = car.PitLimiter && ((_blink += dt) % 0.5) < 0.25;
        _rainLight.MaterialOverride = rain || limiterBlink ? _rainOn : _rainOff;
    }

    /// <summary>Hides the front wing (heavy damage) or shows it again (after a pit stop).</summary>
    public void SetFrontWingAttached(bool attached) => FrontWing.Visible = attached;
}
