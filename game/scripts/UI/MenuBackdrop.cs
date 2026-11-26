using F1.Core.Track;
using Godot;

namespace F1.Game.UI;

/// <summary>Static vector artwork for the paddock and its circuit cards. Maps use the drivable centerline.</summary>
public partial class MenuBackdrop : Control
{
    public override void _Draw()
    {
        var s = Size;
        DrawPolygon(new[] { Vector2.Zero, new Vector2(s.X, 0), s, new Vector2(0, s.Y) },
            new[] { Color.FromHtml("#161323"), Color.FromHtml("#72202e"), Color.FromHtml("#28101d"), Color.FromHtml("#100f1a") });
        void Facet(float x, float y, float w, float h, Color c) => DrawColoredPolygon(new[] {
            new Vector2(x, y), new Vector2(x+w, y), new Vector2(x+w-h*.82f, y+h), new Vector2(x-h*.82f, y+h) }, c);
        Facet(1420, 0, 300, 810, new Color(.9f, .19f, .27f, .055f));
        Facet(1750, 0, 35, 810, new Color(.98f, .26f, .27f, .08f));
        Facet(1820, 0, 550, 810, new Color(.8f, .1f, .16f, .06f));
        for (int y = 154; y < 650; y += 20)
            for (int x = 1580; x < 1860; x += 20)
                DrawCircle(new Vector2(x, y), 1.3f, new Color(1, .83f, .86f, .10f));
        DrawLine(new Vector2(760, 580), new Vector2(1920, 580), new Color(1, .24f, .28f, .22f), 2);
        // A soft elliptical contact shadow beneath the transparent showroom render.
        DrawSetTransform(new Vector2(1290, 581), 0, new Vector2(1, .072f));
        for (int i = 0; i < 12; i++) DrawCircle(Vector2.Zero, 515 - i * 16, new Color(.02f, .01f, .04f, .065f), true, -1, true);
        DrawSetTransform(Vector2.Zero);
    }
}

public partial class CircuitDiagram : Control
{
    public TrackData Circuit { get; set; } = null!;
    public bool Sectors { get; set; }
    private static readonly Color[] Sector = { Color.FromHtml("#ff626b"), Color.FromHtml("#60c8de"), Color.FromHtml("#f2c668") };

    public override void _Draw()
    {
        if (Circuit == null) return;
        var pts = Circuit.Centerline;
        // Orient Spa the same way as the supplied broadcast map. Keep aspect ratio and handedness.
        float angle = Circuit.Id == "spa" ? -Mathf.DegToRad(95.3f) : -.25f;
        var line = new List<Vector2>();
        int stride = Math.Max(1, pts.Count / 420);
        for (int i = 0; i < pts.Count; i += stride)
            line.Add(new Vector2((float)pts[i].X, (float)-pts[i].Y).Rotated(angle));
        var min = new Vector2(line.Min(v => v.X), line.Min(v => v.Y));
        var max = new Vector2(line.Max(v => v.X), line.Max(v => v.Y));
        var bounds = max - min;
        float fit = Mathf.Min((Size.X - 18) / bounds.X, (Size.Y - 18) / bounds.Y);
        var pad = (Size - bounds * fit) * .5f;
        var path = line.Select(v => (v - min) * fit + pad).ToList();
        path.Add(path[0]);
        DrawPolyline(path.ToArray(), new Color(.05f, .04f, .08f, .9f), Sectors ? 10 : 6, true);
        if (Sectors)
        {
            for (int k = 0; k < 3; k++)
            {
                int start = (int)Math.Round(Circuit.SectorStarts[k] / Circuit.SampleSpacing / stride);
                int end = k == 2 ? path.Count - 1 : (int)Math.Round(Circuit.SectorStarts[k + 1] / Circuit.SampleSpacing / stride);
                DrawPolyline(path.GetRange(start, end - start + 1).ToArray(), Sector[k], 4, true);
            }
        }
        else DrawPolyline(path.ToArray(), Colors.White, 3, true);
        DrawCircle(path[0], Sectors ? 6 : 4, Colors.White, true, -1, true);
    }
}
