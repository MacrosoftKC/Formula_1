using F1.Game.Core;
using F1.Game.Track.Builders;
using Godot;

namespace F1.Game.Track;

/// <summary>
/// Builds the whole circuit and its surroundings from the track data at runtime: road, kerbs, barriers, lake,
/// grandstands, pit complex, trees, city skyline, lights and sky.
/// </summary>
public partial class TrackWorld : Node3D
{
    public TrackSurface Surface { get; private set; } = null!;
    public StartLights StartLights { get; private set; } = null!;
    public List<MarshalPanel> MarshalPanels { get; private set; } = new();
    public DirectionalLight3D Sun { get; private set; } = null!;
    public WorldEnvironment Environment { get; private set; } = null!;

    /// <summary>Set false to skip the heavy decorative scenery (used by fast headless tests).</summary>
    [Export] public bool BuildScenery { get; set; } = true;

    public override void _Ready()
    {
        var started = Time.GetTicksMsec();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var timings = new List<string>();
        void Mark(string what) { timings.Add($"{what} {timer.ElapsedMilliseconds}ms"); timer.Restart(); }

        Surface = new TrackSurface(App.Instance.Track);
        var frames = new TrackFrames(Surface);
        Mark("data");
        (Environment, Sun) = EnvironmentBuilder.Build(this);
        NatureBuilder.BuildGround(this, Surface); Mark("ground");
        RoadBuilder.Build(this, frames); Mark("road");
        BarrierBuilder.Build(this, frames); Mark("barriers");
        StartLights = FurnitureBuilder.BuildStartGantry(this, Surface);
        MarshalPanels = FurnitureBuilder.BuildMarshalPosts(this, Surface); Mark("furniture");
        var pitFootprint = PitBuilder.Build(this, Surface); Mark("pits");

        if (BuildScenery)
        {
            LakeBuilder.Build(this, Surface); Mark("lake");
            FurnitureBuilder.BuildBoards(this, Surface); Mark("boards");
            var bands = StandsBuilder.Build(this, frames, StandsBuilder.AlbertParkStands(Surface)); Mark("stands");

            var mask = new SceneryMask(Surface);
            foreach (var b in bands) mask.BlockStrip(Surface.Center, b.From, b.To, b.Side * b.DMin, b.Side * b.DMax);
            mask.BlockStrip(Surface.Pit, 0, Surface.Pit.Length, 0, -pitFootprint.Depth);
            mask.BlockDisk(pitFootprint.Tower, 25);
            Mark("mask");
            NatureBuilder.BuildTrees(this, Surface, mask); Mark("trees");
            CityBuilder.Build(this, Surface, mask); Mark("city");
        }
        GD.Print($"TrackWorld built in {Time.GetTicksMsec() - started} ms ({string.Join(", ", timings)})");
    }
}
