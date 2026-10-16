using F1.Game.Core;
using F1.Game.Controls;
using F1.Game.Vehicle;
using Godot;

namespace F1.Game.Camera;

/// <summary>
/// Player camera: cockpit (with g-force head movement), helmet onboard, T-cam, nose, near/far chase and trackside TV
/// cameras. FOV and offsets come from the settings; supports look back / left / right.
/// </summary>
public partial class CameraRig : Node3D
{
    public Car? Target { get; set; }
    public CameraMode Mode { get; private set; }
    public Camera3D Camera { get; private set; } = null!;
    public List<Vector3> TvPositions { get; } = new();

    private GameSettings S => App.Instance.Settings;
    private Vector3 _chasePos;
    private Vector3 _chaseLook;
    private Vector2 _headOffset;
    private float _shakeTime;
    private bool _initialised;
    private static readonly CameraMode[] Cycle = { CameraMode.Cockpit, CameraMode.Helmet, CameraMode.TCam, CameraMode.Nose, CameraMode.ChaseNear, CameraMode.ChaseFar, CameraMode.TV };

    public override void _Ready()
    {
        Camera = new Camera3D { Name = "Camera", Current = true, Far = 9000 };
        AddChild(Camera);
        SetMode(S.DefaultCamera);
    }

    public void SetMode(CameraMode mode)
    {
        Mode = mode;
        Camera.Fov = S.FovFor(mode);
        Camera.Near = mode is CameraMode.Cockpit or CameraMode.Helmet ? 0.03f : 0.1f;
        if (Target?.Model is { } model) model.DriverHead.Visible = mode != CameraMode.Cockpit;
        _initialised = false;
    }

    public void Next(int dir)
    {
        int i = Array.IndexOf(Cycle, Mode);
        SetMode(Cycle[(i + dir + Cycle.Length) % Cycle.Length]);
    }

    public override void _Process(double delta)
    {
        if (Godot.Input.IsActionJustPressed(InputService.CameraNext)) Next(1);
        if (Godot.Input.IsActionJustPressed(InputService.CameraPrev)) Next(-1);
        if (Target == null || !IsInstanceValid(Target)) return;

        float dt = (float)delta;
        var car = Target.GlobalTransform;
        var basis = car.Basis.Orthonormalized();
        bool lookBack = Godot.Input.IsActionPressed(InputService.LookBack);
        float lookSide = Godot.Input.GetActionStrength(InputService.LookLeft) - Godot.Input.GetActionStrength(InputService.LookRight);
        float yaw = lookBack ? Mathf.Pi : lookSide * Mathf.DegToRad(75);

        switch (Mode)
        {
            case CameraMode.Cockpit:
            {
                // head is thrown around by lateral/longitudinal g (smoothed)
                var targetHead = new Vector2(-Target.LateralG * 0.012f, -Target.LongitudinalG * 0.01f) * S.HeadMovement;
                _headOffset = _headOffset.Lerp(targetHead, Math.Min(1, dt * 6));
                var shake = Shake(dt) * S.CameraShake;
                var local = new Vector3(_headOffset.X + shake.X, 0.8f + S.SeatHeight + shake.Y, 0.02f - S.SeatForward + _headOffset.Y);
                var b = basis * new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.DegToRad(-5f)) * new Basis(Vector3.Forward, -Target.LateralG * 0.006f * S.HeadMovement);
                Camera.GlobalTransform = new Transform3D(b, car * local);
                break;
            }
            case CameraMode.Helmet:
                Camera.GlobalTransform = new Transform3D(basis * new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.DegToRad(-6)), car * new Vector3(0, 1.08f, 0.3f));
                break;
            case CameraMode.TCam:
                Camera.GlobalTransform = new Transform3D(basis * new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.DegToRad(-4)), car * new Vector3(0, 1.13f, 0.18f));
                break;
            case CameraMode.Nose:
                Camera.GlobalTransform = new Transform3D(basis * new Basis(Vector3.Up, yaw), car * new Vector3(0, 0.42f, -2.35f));
                break;
            case CameraMode.ChaseNear:
            case CameraMode.ChaseFar:
                Chase(car, basis, dt, lookBack, Mode == CameraMode.ChaseFar ? 1.6f : 1f);
                break;
            case CameraMode.TV:
                Tv(car.Origin, dt);
                break;
        }
    }

    private Vector2 Shake(float dt)
    {
        _shakeTime += dt;
        float speed = Target!.SpeedMs;
        float kerb = 0;
        foreach (var w in Target.Wheels) if (w.Grounded && w.Surface.Bump > 0) kerb += (float)w.Surface.Bump * 25;
        float amp = 0.0004f * Math.Clamp(speed / 80f, 0, 1) + kerb * 0.003f;
        return new Vector2(Mathf.Sin(_shakeTime * 61f) * amp, Mathf.Sin(_shakeTime * 47f + 1.3f) * amp * 1.4f);
    }

    private void Chase(Transform3D car, Basis basis, float dt, bool lookBack, float scale)
    {
        var fwd = -basis.Z;
        fwd.Y = 0;
        fwd = fwd.Normalized();
        if (lookBack) fwd = -fwd;
        float dist = S.ChaseDistance * scale, height = S.ChaseHeight * scale;
        var desired = car.Origin - fwd * dist + Vector3.Up * height;
        var look = car.Origin + Vector3.Up * 0.8f + fwd * 4f;
        if (!_initialised) { _chasePos = desired; _chaseLook = look; _initialised = true; }
        float k = 1 - Mathf.Exp(-dt * 9f);
        _chasePos = _chasePos.Lerp(desired, k);
        _chaseLook = _chaseLook.Lerp(look, 1 - Mathf.Exp(-dt * 14f));
        Camera.GlobalPosition = _chasePos;
        Camera.LookAt(_chaseLook, Vector3.Up);
        Camera.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(S.ChasePitch + 6f));
    }

    private void Tv(Vector3 carPos, float dt)
    {
        if (TvPositions.Count == 0) { Chase(Target!.GlobalTransform, Target.GlobalBasis, dt, false, 1.5f); return; }
        var best = TvPositions.OrderBy(p => p.DistanceSquaredTo(carPos)).First();
        Camera.GlobalPosition = best;
        Camera.LookAt(carPos + Vector3.Up * 0.5f, Vector3.Up);
        float d = best.DistanceTo(carPos);
        Camera.Fov = Math.Clamp(Mathf.RadToDeg(2 * Mathf.Atan(14f / Math.Max(d, 1f))), 8f, S.FovFor(CameraMode.TV));
    }
}
