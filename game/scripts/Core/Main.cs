using Godot;

namespace F1.Game.Core;

/// <summary>Entry scene: routes to the main menu, or straight to a scene / automated test (--scene=menu|tt|cars|track,
/// --autotest=boot|tt|ttplayer|trackshots|carshots|menushot).</summary>
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
        if (test == "menushot" || game.Arg("scene") == "menu")
        {
            game.ChangeScene("res://scenes/MainMenu.tscn");
            return;
        }
        if (test == "carshots" || game.Arg("scene") == "cars")
        {
            game.ChangeScene("res://scenes/CarShowroom.tscn");
            return;
        }
        if (test is "tt" or "ttplayer" || game.Arg("scene") == "tt")
        {
            game.Session = new SessionConfig { Mode = SessionMode.TimeTrial, TeamId = game.Arg("team") ?? "mclaren" };
            game.ChangeScene("res://scenes/TimeTrial.tscn");
            return;
        }
        GD.Print("Low-Poly F1 started");
        game.ChangeScene("res://scenes/MainMenu.tscn");
    }
}
