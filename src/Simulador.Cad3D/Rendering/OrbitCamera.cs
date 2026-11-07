using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Simulador.Cad3D.Geometry;

namespace Simulador.Cad3D.Rendering;

/// <summary>Cámara orbital compartida por los dos visores (OpenGL y software).</summary>
public sealed class OrbitCamera
{
    public double Yaw { get; set; } = -55;
    public double Pitch { get; set; } = 30;
    public double Distance { get; set; } = 250;
    public Vec3 Target { get; set; }

    /// <summary>Distancia focal en píxeles = factor × lado menor de la vista.</summary>
    public const double FocalFactor = 1.2;

    public event Action? Changed;

    public (Vec3 Eye, Vec3 Right, Vec3 Up, Vec3 Forward) Basis()
    {
        double yaw = Yaw * Math.PI / 180, pitch = Pitch * Math.PI / 180;
        var dir = new Vec3(Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch));
        var eye = Target + dir * Distance;
        var forward = (-dir).Normalized();
        var right = forward.Cross(Vec3.UnitZ).Normalized();
        if (right.Length < 1e-9) right = Vec3.UnitX;
        var up = right.Cross(forward).Normalized();
        return (eye, right, up, forward);
    }

    public void Fit(IEnumerable<Mesh> meshes)
    {
        var list = meshes.Where(m => m.Vertices.Count > 0).ToList();
        if (list.Count == 0) { Target = Vec3.Zero; Distance = 250; Changed?.Invoke(); return; }
        var min = list.Select(m => m.Bounds().Min).Aggregate(Vec3.Min);
        var max = list.Select(m => m.Bounds().Max).Aggregate(Vec3.Max);
        Target = (min + max) / 2;
        Distance = Math.Max(20, (max - min).Length * 2.0);
        Changed?.Invoke();
    }

    public void SetView(double yaw, double pitch) { Yaw = yaw; Pitch = pitch; Changed?.Invoke(); }

    public void Orbit(double dx, double dy)
    {
        Yaw -= dx * 0.4;
        Pitch = Math.Clamp(Pitch + dy * 0.4, -89, 89);
        Changed?.Invoke();
    }

    public void Pan(double dx, double dy, Size viewSize)
    {
        var (_, right, up, _) = Basis();
        double scale = Distance / (Math.Max(1, Math.Min(viewSize.Width, viewSize.Height)) * FocalFactor);
        Target = Target - right * (dx * scale) + up * (dy * scale);
        Changed?.Invoke();
    }

    public void Zoom(double wheelDelta)
    {
        Distance = Math.Clamp(Distance * Math.Pow(0.88, wheelDelta), 5, 5000);
        Changed?.Invoke();
    }

    /// <summary>Ratón: izquierdo gira, derecho/central o Mayús desplaza, rueda acerca.</summary>
    public void AttachInput(Control control)
    {
        Point? last = null;
        bool panning = false;
        control.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(control).Properties;
            panning = props.IsRightButtonPressed || props.IsMiddleButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            last = e.GetPosition(control);
            e.Pointer.Capture(control);
            control.Focus();
        };
        control.PointerMoved += (_, e) =>
        {
            if (last is not { } l) return;
            var pos = e.GetPosition(control);
            var d = pos - l;
            last = pos;
            if (panning) Pan(d.X, d.Y, control.Bounds.Size); else Orbit(d.X, d.Y);
        };
        control.PointerReleased += (_, e) => { last = null; e.Pointer.Capture(null); };
        control.PointerWheelChanged += (_, e) => Zoom(e.Delta.Y);
    }
}
