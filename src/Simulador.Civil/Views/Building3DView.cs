using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Views;

/// <summary>
/// Vista 3D del edificio dibujada con caras sombreadas y orden de pintor (sin OpenGL), suficiente para revisar
/// volúmenes, huecos, escaleras e instalaciones. Arrastrar con el izquierdo gira; con el central desplaza; rueda = zoom.
/// Cuando exista el visor 3D común (módulo de diseño 3D) se podrá sustituir por él usando la misma malla.
/// </summary>
public sealed class Building3DView : Control
{
    private readonly BuildingDocument _doc;
    private double _yaw = -0.6, _pitch = 0.6, _zoom = 1;
    private Vector _pan;
    private Point? _drag;
    private bool _dragPan;

    public Building3DView(BuildingDocument doc)
    {
        _doc = doc;
        ClipToBounds = true;
        doc.Changed += (_, _) => InvalidateVisual();
    }

    /// <summary>Muestra los niveles hasta este (incluido); null = todos.</summary>
    public string? UpToLevelId { get; set; }
    public bool ShowRoof { get; set; } = true;

    private readonly record struct Face(Point3[] Pts, Color Color);

    private static readonly Color WallColor = Color.FromRgb(232, 224, 208);
    private static readonly Color FloorColor = Color.FromRgb(190, 190, 185);
    private static readonly Color RoofColor = Color.FromRgb(196, 110, 80);
    private static readonly Color StairColor = Color.FromRgb(170, 130, 90);
    private static readonly Color ElecColor = Color.FromRgb(230, 80, 40);
    private static readonly Color HydroColor = Color.FromRgb(40, 140, 230);

    private List<Face> BuildFaces()
    {
        var faces = new List<Face>();
        var levels = _doc.Levels.OrderBy(l => l.Elevation).ToList();
        if (UpToLevelId is not null)
        {
            var cut = levels.FirstOrDefault(l => l.Id == UpToLevelId);
            if (cut is not null) levels = levels.Where(l => l.Elevation <= cut.Elevation + 1e-6).ToList();
        }
        var shown = levels.Select(l => l.Id).ToHashSet();
        var topShown = levels.LastOrDefault();

        foreach (var w in _doc.Walls.Where(w => shown.Contains(w.LevelId)))
        {
            double z0 = _doc.GetLevel(w.LevelId).Elevation;
            var ops = _doc.Openings.Where(o => o.HostWallId == w.Id).OrderBy(o => o.Offset).ToList();
            double pos = -w.Thickness / 2;
            foreach (var o in ops)
            {
                double lo = Math.Max(0, o.Offset - o.Width / 2), hi = Math.Min(w.Length, o.Offset + o.Width / 2);
                if (lo > pos) AddBox(faces, WallQuad(w, pos, lo), z0, z0 + w.Height, WallColor);
                if (o.SillHeight > 0) AddBox(faces, WallQuad(w, lo, hi), z0, z0 + o.SillHeight, WallColor);
                if (o.SillHeight + o.Height < w.Height) AddBox(faces, WallQuad(w, lo, hi), z0 + o.SillHeight + o.Height, z0 + w.Height, WallColor);
                pos = Math.Max(pos, hi);
            }
            AddBox(faces, WallQuad(w, pos, w.Length + w.Thickness / 2), z0, z0 + w.Height, WallColor);
        }

        foreach (var s in _doc.Slabs.Where(s => shown.Contains(s.LevelId)))
        {
            var lvl = _doc.GetLevel(s.LevelId);
            var outline = Geo.SignedArea(s.Outline) < 0 ? s.Outline.Reverse().ToArray() : s.Outline.ToArray();
            if (s.Kind == SlabKind.Floor) AddBox(faces, outline, lvl.Elevation - s.Thickness, lvl.Elevation, FloorColor);
            else if (s.Kind == SlabKind.Roof && ShowRoof && lvl == topShown && lvl.Id == _doc.Levels.MaxBy(l => l.Elevation)!.Id)
                AddBox(faces, outline, lvl.Elevation + lvl.Height - s.Thickness, lvl.Elevation + lvl.Height, RoofColor);
        }

        foreach (var st in _doc.Stairs.Where(s => shown.Contains(s.LevelId)))
        {
            var d = st.Design;
            double z0 = _doc.GetLevel(st.LevelId).Elevation;
            var dir = st.Direction;
            var n = dir.Perp() * (st.Width / 2);
            for (int i = 0; i < d.Risers - 1; i++)
            {
                var a = st.Start + dir * (i * d.Tread);
                var b = st.Start + dir * ((i + 1) * d.Tread);
                AddBox(faces, [a - n, b - n, b + n, a + n], z0, z0 + (i + 1) * d.Riser, StairColor);
            }
        }

        foreach (var f in _doc.Fixtures.Where(f => shown.Contains(f.LevelId)))
        {
            var c = f.ModuleId == "hidraulica" ? HydroColor : ElecColor;
            double half = f.Kind == "tanque" ? f.Width / 2 : 0.06;
            double h = f.Kind == "tanque" ? 1.2 : 0.1;
            var p = f.Position.Xy;
            Point2[] q = [new(p.X - half, p.Y - half), new(p.X + half, p.Y - half), new(p.X + half, p.Y + half), new(p.X - half, p.Y + half)];
            double z = f.Kind == "tanque" ? f.Position.Z : f.Position.Z - h / 2;
            AddBox(faces, q, z, z + h, c);
        }
        return faces;
    }

    private static Point2[] WallQuad(Wall w, double from, double to)
    {
        var d = w.Direction;
        var n = d.Perp() * (w.Thickness / 2);
        var a = w.Start + d * from;
        var b = w.Start + d * to;
        return [a - n, b - n, b + n, a + n];
    }

    /// <summary>Prisma de base <paramref name="b"/> (antihoraria vista desde arriba) entre z0 y z1, con caras hacia fuera.</summary>
    private static void AddBox(List<Face> faces, IReadOnlyList<Point2> b, double z0, double z1, Color c)
    {
        if (z1 - z0 < 1e-6) return;
        faces.Add(new(b.Select(p => new Point3(p, z1)).ToArray(), c));
        faces.Add(new(b.Reverse().Select(p => new Point3(p, z0)).ToArray(), c));
        for (int i = 0; i < b.Count; i++)
        {
            var p = b[i];
            var q = b[(i + 1) % b.Count];
            faces.Add(new([new(p, z0), new(q, z0), new(q, z1), new(p, z1)], c));
        }
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(224, 234, 242)), new Rect(Bounds.Size));
        var faces = BuildFaces();
        if (faces.Count == 0) return;

        double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw), cp = Math.Cos(_pitch), sp = Math.Sin(_pitch);
        var cam = (X: -cp * sy, Y: -cp * cy, Z: sp);
        var light = Normalize((0.4, -0.6, 0.8));

        (double X, double Y, double Depth) Project(Point3 p)
        {
            double xr = p.X * cy - p.Y * sy;
            double yr = p.X * sy + p.Y * cy;
            return (xr, -(p.Z * cp + yr * sp), yr * cp - p.Z * sp);
        }

        // Encaje automático en la vista.
        var all = faces.SelectMany(f => f.Pts).Select(Project).ToList();
        double minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
        double fit = Math.Min((Bounds.Width - 40) / Math.Max(maxX - minX, 1e-3), (Bounds.Height - 40) / Math.Max(maxY - minY, 1e-3)) * _zoom;
        var center = new Point((minX + maxX) / 2, (minY + maxY) / 2);
        Point Screen((double X, double Y, double Depth) p) =>
            new(Bounds.Width / 2 + (p.X - center.X) * fit + _pan.X, Bounds.Height / 2 + (p.Y - center.Y) * fit + _pan.Y);

        var visible = new List<(double Depth, StreamGeometry Geo, IBrush Brush)>();
        foreach (var f in faces)
        {
            var n = Normal(f.Pts);
            if (n.X * cam.X + n.Y * cam.Y + n.Z * cam.Z <= 1e-9) continue; // cara de espaldas
            var proj = f.Pts.Select(Project).ToArray();
            double shade = 0.6 + 0.4 * Math.Max(0, n.X * light.X + n.Y * light.Y + n.Z * light.Z);
            var col = Color.FromRgb((byte)(f.Color.R * shade), (byte)(f.Color.G * shade), (byte)(f.Color.B * shade));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(Screen(proj[0]), true);
                foreach (var p in proj.Skip(1)) c.LineTo(Screen(p));
                c.EndFigure(true);
            }
            visible.Add((proj.Average(p => p.Depth), g, new SolidColorBrush(col)));
        }

        var edge = new Pen(new SolidColorBrush(Color.FromArgb(90, 40, 40, 40)), 0.6);
        foreach (var (_, g, brush) in visible.OrderByDescending(v => v.Depth))
            ctx.DrawGeometry(brush, edge, g);

        // Rutas de instalaciones encima de todo para que se vean siempre.
        var shownLevels = UpToLevelId is null ? null : _doc.Levels.Where(l => l.Elevation <= _doc.GetLevel(UpToLevelId).Elevation + 1e-6).Select(l => l.Id).ToHashSet();
        foreach (var r in _doc.Routes)
        {
            var from = _doc.Fixtures.FirstOrDefault(f => f.Id == r.FromFixtureId);
            if (shownLevels is not null && from is not null && !shownLevels.Contains(from.LevelId)) continue;
            var pen = new Pen(new SolidColorBrush(r.ModuleId == "hidraulica" ? HydroColor : ElecColor), 2.5);
            foreach (var s in r.Segments) ctx.DrawLine(pen, Screen(Project(s.Start)), Screen(Project(s.End)));
        }
    }

    private static (double X, double Y, double Z) Normal(Point3[] pts)
    {
        // Método de Newell.
        double x = 0, y = 0, z = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Length];
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        return Normalize((x, y, z));
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return l < 1e-12 ? (0, 0, 0) : (v.X / l, v.Y / l, v.Z / l);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        _drag = e.GetPosition(this);
        _dragPan = props.IsMiddleButtonPressed || props.IsRightButtonPressed;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } d) return;
        var p = e.GetPosition(this);
        if (_dragPan) _pan += p - d;
        else
        {
            _yaw -= (p.X - d.X) * 0.01;
            _pitch = Math.Clamp(_pitch + (p.Y - d.Y) * 0.01, 0.05, 1.5);
        }
        _drag = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.2, 20);
        InvalidateVisual();
        e.Handled = true;
    }

    public void ResetView()
    {
        _yaw = -0.6; _pitch = 0.6; _zoom = 1; _pan = default;
        InvalidateVisual();
    }
}
