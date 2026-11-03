using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Simulador.Cad3D.Geometry;

namespace Simulador.Cad3D.Rendering;

/// <summary>Una malla a dibujar con su color.</summary>
public sealed record RenderItem(Mesh Mesh, Color Color, bool Overlay = false);

/// <summary>
/// Visor 3D por software (sin dependencias nativas): cámara orbital, sombreado plano y orden por profundidad.
/// Ratón: izquierdo gira, derecho o central desplaza, rueda acerca. Suficiente para piezas de miles de triángulos;
/// se sustituirá por un visor OpenGL cuando llegue el motor exacto.
/// </summary>
public sealed class Viewport3D : Control
{
    private IReadOnlyList<RenderItem> _items = Array.Empty<RenderItem>();
    private double _yaw = -55, _pitch = 30, _distance = 250;
    private Vec3 _target = new(0, 0, 0);
    private Point? _lastPointer;
    private bool _panning;

    public static readonly IBrush Background = new ImmutableSolidColorBrush(Color.FromRgb(0x23, 0x27, 0x2E));
    private static readonly IPen GridPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly IPen GridPenMajor = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), 1);

    public Viewport3D()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public void SetItems(IReadOnlyList<RenderItem> items)
    {
        _items = items;
        InvalidateVisual();
    }

    /// <summary>Centra la cámara en todas las piezas.</summary>
    public void FitAll()
    {
        var meshes = _items.Where(i => !i.Overlay && i.Mesh.Vertices.Count > 0).Select(i => i.Mesh).ToList();
        if (meshes.Count == 0) { _target = Vec3.Zero; _distance = 250; InvalidateVisual(); return; }
        var min = meshes.Select(m => m.Bounds().Min).Aggregate(Vec3.Min);
        var max = meshes.Select(m => m.Bounds().Max).Aggregate(Vec3.Max);
        _target = (min + max) / 2;
        _distance = Math.Max(20, (max - min).Length * 2.0);
        InvalidateVisual();
    }

    private (Vec3 Eye, Vec3 Right, Vec3 Up, Vec3 Forward) Camera()
    {
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var dir = new Vec3(Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch));
        var eye = _target + dir * _distance;
        var forward = (-dir).Normalized();
        var right = forward.Cross(Vec3.UnitZ).Normalized();
        if (right.Length < 1e-9) right = Vec3.UnitX;
        var up = right.Cross(forward).Normalized();
        return (eye, right, up, forward);
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(Background, bounds);
        var (eye, right, up, forward) = Camera();
        double focal = Math.Min(bounds.Width, bounds.Height) * 1.2;
        double cx = bounds.Width / 2, cy = bounds.Height / 2;
        const double near = 1;

        (Point P, double Depth)? Project(Vec3 p)
        {
            var d = p - eye;
            double z = d.Dot(forward);
            if (z < near) return null;
            return (new Point(cx + focal * d.Dot(right) / z, cy - focal * d.Dot(up) / z), z);
        }

        DrawGrid(ctx, Project);

        var light = (-forward + up * 0.5 + right * 0.3).Normalized();
        var opaque = new List<(double Depth, Point A, Point B, Point C, Color Color)>();
        var overlay = new List<(double Depth, Point A, Point B, Point C, Color Color)>();
        foreach (var item in _items)
        {
            var list = item.Overlay ? overlay : opaque;
            foreach (var (a, b, c) in item.Mesh.Triangles())
            {
                var n = (b - a).Cross(c - a);
                if (n.Length < 1e-12) continue;
                n = n.Normalized();
                bool facing = n.Dot(eye - a) > 0;
                if (!facing && !item.Overlay) continue; // cara trasera
                var pa = Project(a); var pb = Project(b); var pc = Project(c);
                if (pa is null || pb is null || pc is null) continue;
                double shade = 0.35 + 0.65 * Math.Abs(n.Dot(light));
                var col = item.Color;
                var shaded = Color.FromArgb(col.A, (byte)(col.R * shade), (byte)(col.G * shade), (byte)(col.B * shade));
                list.Add(((pa.Value.Depth + pb.Value.Depth + pc.Value.Depth) / 3, pa.Value.P, pb.Value.P, pc.Value.P, shaded));
            }
        }
        DrawTriangles(ctx, opaque);
        DrawTriangles(ctx, overlay);
        DrawAxes(ctx, Project);
    }

    private static void DrawTriangles(DrawingContext ctx, List<(double Depth, Point A, Point B, Point C, Color Color)> tris)
    {
        tris.Sort((x, y) => y.Depth.CompareTo(x.Depth));
        // Agrupa triángulos consecutivos del mismo color en una sola geometría para dibujar más rápido.
        int i = 0;
        while (i < tris.Count)
        {
            var color = tris[i].Color;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                int start = i;
                while (i < tris.Count && tris[i].Color == color && i - start < 256)
                {
                    g.BeginFigure(tris[i].A, true);
                    g.LineTo(tris[i].B);
                    g.LineTo(tris[i].C);
                    g.EndFigure(true);
                    i++;
                }
            }
            var brush = new ImmutableSolidColorBrush(color);
            // Un trazo fino del mismo color tapa las rendijas entre triángulos vecinos.
            ctx.DrawGeometry(brush, color.A == 255 ? new ImmutablePen(brush, 0.6) : null, geo);
        }
    }

    private static void DrawGrid(DrawingContext ctx, Func<Vec3, (Point P, double Depth)?> project)
    {
        const int half = 100, step = 10;
        for (int v = -half; v <= half; v += step)
        {
            var pen = v % 50 == 0 ? GridPenMajor : GridPen;
            Line(ctx, project, new Vec3(v, -half, 0), new Vec3(v, half, 0), pen);
            Line(ctx, project, new Vec3(-half, v, 0), new Vec3(half, v, 0), pen);
        }
    }

    private static void DrawAxes(DrawingContext ctx, Func<Vec3, (Point P, double Depth)?> project)
    {
        Line(ctx, project, Vec3.Zero, new Vec3(30, 0, 0), new ImmutablePen(Brushes.IndianRed.ToImmutable(), 2));
        Line(ctx, project, Vec3.Zero, new Vec3(0, 30, 0), new ImmutablePen(Brushes.LimeGreen.ToImmutable(), 2));
        Line(ctx, project, Vec3.Zero, new Vec3(0, 0, 30), new ImmutablePen(Brushes.DodgerBlue.ToImmutable(), 2));
    }

    private static void Line(DrawingContext ctx, Func<Vec3, (Point P, double Depth)?> project, Vec3 a, Vec3 b, IPen pen)
    {
        // Recorte sencillo: subdivide para que los tramos detrás de la cámara no rompan la línea entera.
        const int parts = 8;
        Point? prev = null;
        for (int i = 0; i <= parts; i++)
        {
            var p = project(Vec3.Lerp(a, b, i / (double)parts));
            if (p is null) { prev = null; continue; }
            if (prev is not null) ctx.DrawLine(pen, prev.Value, p.Value.P);
            prev = p.Value.P;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        _panning = props.IsRightButtonPressed || props.IsMiddleButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastPointer is not { } last) return;
        var pos = e.GetPosition(this);
        var d = pos - last;
        _lastPointer = pos;
        if (_panning)
        {
            var (_, right, up, _) = Camera();
            double scale = _distance / (Math.Min(Bounds.Width, Bounds.Height) * 1.2);
            _target = _target - right * (d.X * scale) + up * (d.Y * scale);
        }
        else
        {
            _yaw -= d.X * 0.4;
            _pitch = Math.Clamp(_pitch + d.Y * 0.4, -89, 89);
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _lastPointer = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _distance = Math.Clamp(_distance * Math.Pow(0.88, e.Delta.Y), 5, 5000);
        InvalidateVisual();
    }

    /// <summary>Vistas estándar.</summary>
    public void SetView(double yaw, double pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
        InvalidateVisual();
    }
}
