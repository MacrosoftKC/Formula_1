using Godot;

namespace F1.Game.Core;

/// <summary>Entry scene: routes to the main menu, or runs an automated test when started with --autotest=NAME.</summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        var game = App.Instance;
        string? test = game.Arg("autotest");
        if (test == "boot")
        {
            var t = game.Track;
            GD.Print($"BOOT OK: {game.Teams.Teams.Count} teams, regs {game.Regulations.Season}, track {t.Name} {t.Centerline.Count} samples, {t.Corners.Count} corners");
            game.Quit(0);
            return;
        }
        if (test == "trackshots" || game.Arg("scene") == "track")
        {
            game.ChangeScene("res://scenes/TrackPreview.tscn");
            return;
        }
        if (test == "tt" || game.Arg("scene") == "tt")
        {
            game.Session = new SessionConfig { Mode = SessionMode.TimeTrial };
            game.ChangeScene("res://scenes/TimeTrial.tscn");
            return;
        }
        GD.Print("Low-Poly F1 started");
        // main menu arrives in M7; go straight to Time Trial for now
        game.ChangeScene("res://scenes/TimeTrial.tscn");
    }
}
