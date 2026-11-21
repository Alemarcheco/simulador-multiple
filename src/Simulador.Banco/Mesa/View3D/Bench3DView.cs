using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Simulador.Banco.Mesa.Ui;
using Simulador.Hidraulica.View3D;

namespace Simulador.Banco.Mesa.View3D;

/// <summary>
/// Vista 3D de la mesa: los mismos equipos y cables que el diagrama de bloques, como objetos sobre un tablero.
/// Ratón: arrastrar el fondo gira, botón derecho desplaza, rueda acerca, doble clic encuadra. Se puede elegir una
/// pieza, moverla por la mesa, unir bornes arrastrando de uno a otro y poner piezas nuevas; en marcha, pulsar
/// sobre un pulsador o selector lo acciona.
/// </summary>
public sealed class Bench3DView : Control
{
    private static readonly Typeface Font = new("Inter, Segoe UI, Arial");
    private static readonly Typeface Bold = new("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush LabelSel = new SolidColorBrush(Color.FromArgb(0xF0, 0xD6, 0xE6, 0xFB));
    private static readonly IBrush LabelInk = new SolidColorBrush(Color.Parse("#1F2933"));
    private static readonly IBrush HelpInk = new SolidColorBrush(Color.Parse("#E8EEF3"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#FFB300"));
    private const double TerminalHit = 11, DragThreshold = 4;

    private enum Drag { None, Orbit, Pan, Move, Wire, Press }

    private readonly Rasterizer _raster = new();
    private readonly Dictionary<string, double> _angles = [];
    private WriteableBitmap? _bitmap;
    private BenchScene? _last;
    private BenchProject _project = new();
    private bool _fitted;
    private double _phase;

    private Drag _drag;
    private Point _down, _prev;
    private Vector3 _grabTable;
    private (double X, double Y) _partStart;
    private (string Part, string Port)? _wireFrom;
    private (string Part, string Port)? _hoverTerminal;
    private Point _wireEnd;
    private string? _pressed;

    public OrbitCamera Camera { get; } = new() { Yaw = -62, Pitch = 34 };

    public BenchProject Project
    {
        get => _project;
        set { _project = value; _angles.Clear(); _fitted = false; InvalidateVisual(); }
    }

    public bool Running { get; set; }
    public bool ShowLabels { get; set; } = true;
    /// <summary>Pieza elegida en la lista para poner (la comparte con el diagrama de bloques).</summary>
    public Func<PartType?>? GetPending { get; set; }
    /// <summary>Quita la pieza pendiente de poner.</summary>
    public Action? ClearPending { get; set; }
    private PartType? Pending => GetPending?.Invoke();
    public Func<string, PartLook?>? LookOf { get; set; }
    public Func<string, IssueLevel?>? IssueOf { get; set; }
    public Func<BenchWire, bool>? WireActive { get; set; }
    /// <summary>Pieza seleccionada (la comparte con el diagrama de bloques).</summary>
    public Func<BenchPart?>? SelectedPart { get; set; }

    public event EventHandler<BenchPart?>? PartSelected;
    public event EventHandler? ProjectChanged;
    public event EventHandler<string>? Message;
    public event EventHandler<(string PartId, bool Down)>? PartPressed;
    public event EventHandler? PlacementDone;
    public event EventHandler? DeleteRequested;

    public Bench3DView()
    {
        ClipToBounds = true;
        Focusable = true;
        _raster.Ambient = 0.5f;
    }

    /// <summary>Avanza la animación: los ejes giran según la velocidad que publica cada pieza.</summary>
    public void Animate(double seconds)
    {
        if (seconds > 0)
        {
            _phase += seconds;
            if (Running && LookOf is not null)
                foreach (var p in _project.Parts)
                    if (LookOf(p.Id)?.Spin is double spin && Math.Abs(spin) > 1e-4)
                        // A velocidad nominal da ~1,5 vueltas por segundo en pantalla: se ve girar sin marear.
                        _angles[p.Id] = (_angles.GetValueOrDefault(p.Id) + spin * seconds * Math.PI * 3) % (Math.PI * 2);
        }
        if (IsVisible) InvalidateVisual();
    }

    public void FitAll()
    {
        var scene = BuildScene();
        Fit(scene);
        InvalidateVisual();
    }

    private void Fit(BenchScene scene)
    {
        if (scene.Min.X > scene.Max.X) Camera.Fit(new(-3, -2, 0), new(3, 2, 1));
        else
        {
            Camera.Fit(scene.Min, scene.Max);
            Camera.Distance = Math.Max(4, Camera.Distance * 0.85); // la mesa llena la vista; los bordes pueden quedar fuera
        }
        _fitted = true;
    }

    public void ResetFraming() { _fitted = false; InvalidateVisual(); }

    private BenchScene BuildScene() => BenchScene3D.Build(_project, new BenchLive
    {
        LookOf = Running ? LookOf : null,
        IssueOf = IssueOf,
        WireActive = Running ? WireActive : null,
        AngleOf = id => _angles.GetValueOrDefault(id),
        Phase = _phase,
        SelectedId = SelectedPart?.Invoke()?.Id,
        Running = Running,
    });

    // ---------------------------------------------------------------- dibujo

    public override void Render(DrawingContext ctx)
    {
        int w = Math.Max(1, (int)Bounds.Width), h = Math.Max(1, (int)Bounds.Height);
        if (w < 4 || h < 4) return;
        var scene = BuildScene();
        _last = scene;
        if (!_fitted) Fit(scene);

        _raster.Resize(w, h);
        _raster.Render(scene.Scene, Camera, 0xFF5E6E7E, 0xFF2C3540);
        if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(w, h), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }
        using (var fb = _bitmap.Lock())
        {
            var bytes = MemoryMarshal.AsBytes(_raster.Pixels.AsSpan());
            for (int y = 0; y < h; y++)
            {
                var src = bytes.Slice(y * w * 4, w * 4);
                unsafe
                {
                    var dst = new Span<byte>((byte*)fb.Address + (long)y * fb.RowBytes, w * 4);
                    src.CopyTo(dst);
                }
            }
        }
        ctx.DrawImage(_bitmap, new Rect(0, 0, w, h));

        if (ShowLabels)
            foreach (var label in scene.Scene.Labels)
            {
                if (_raster.Project(label.Position) is not { } p) continue;
                var text = new FormattedText(label.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, label.Highlight ? Bold : Font, 11.5, LabelInk);
                var r = new Rect(p.X - text.Width / 2 - 5, p.Y - text.Height - 3, text.Width + 10, text.Height + 3);
                ctx.DrawRectangle(label.Highlight ? LabelSel : LabelBack, null, r, 4, 4);
                ctx.DrawText(text, new Point(r.X + 5, r.Y + 1.5));
            }

        // Borne bajo el ratón y cable que se está tendiendo.
        if (_hoverTerminal is { } ht && scene.Terminals.TryGetValue(ht, out var hp) && _raster.Project(hp) is { } hs)
            ctx.DrawEllipse(null, new Pen(Accent, 2.2), new Point(hs.X, hs.Y), 9, 9);
        if (_drag == Drag.Wire && _wireFrom is { } wf && scene.Terminals.TryGetValue(wf, out var wp) && _raster.Project(wp) is { } ws)
        {
            var end = _hoverTerminal is { } t2 && scene.Terminals.TryGetValue(t2, out var tp) && _raster.Project(tp) is { } ts ? new Point(ts.X, ts.Y) : _wireEnd;
            ctx.DrawLine(new Pen(Accent, 2.5, new DashStyle([4, 3], 0)), new Point(ws.X, ws.Y), end);
        }

        string help = Pending is not null
            ? $"Haz clic en la mesa para poner: {Pending.Name}   (Esc para cancelar)"
            : Running
                ? "En marcha: pulsa los pulsadores y selectores sobre la mesa · arrastra el fondo para girar · rueda para acercar"
                : "Arrastra el fondo para girar · botón derecho para desplazar · rueda para acercar · arrastra una pieza para moverla · de borne a borne para unir · doble clic encuadra";
        var ft = new FormattedText(help, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Pending is null ? Font : Bold, 12, Pending is null ? HelpInk : Accent);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x90, 0x1A, 0x20, 0x28)), null, new Rect(6, h - ft.Height - 14, ft.Width + 12, ft.Height + 8), 4, 4);
        ctx.DrawText(ft, new Point(12, h - ft.Height - 10));
    }

    // ---------------------------------------------------------------- elegir con el ratón

    /// <summary>Rayo desde el ojo por el píxel (x, y) de la vista.</summary>
    private (Vector3 Origin, Vector3 Dir) Ray(Point screen)
    {
        int w = Math.Max(1, (int)Bounds.Width), h = Math.Max(1, (int)Bounds.Height);
        var (eye, right, up, forward) = Camera.Frame();
        float scale = (float)Camera.PixelsPerMeter(w, h);
        float vx = ((float)screen.X - w / 2f) / scale, vy = (h / 2f - (float)screen.Y) / scale;
        if (Camera.Orthographic) return (eye + right * vx + up * vy, forward);
        scale *= (float)Camera.Distance;
        vx = ((float)screen.X - w / 2f) / scale;
        vy = (h / 2f - (float)screen.Y) / scale;
        return (eye, Vector3.Normalize(forward + right * vx + up * vy));
    }

    /// <summary>Punto de la mesa (z = <paramref name="z"/>) bajo el ratón.</summary>
    private Vector3? TablePoint(Point screen, float z = BenchScene3D.PlateTop)
    {
        var (o, d) = Ray(screen);
        if (MathF.Abs(d.Z) < 1e-5f) return null;
        float t = (z - o.Z) / d.Z;
        return t <= 0 ? null : o + d * t;
    }

    private (string Part, string Port)? TerminalAt(Point screen)
    {
        if (_last is null) return null;
        (string, string)? best = null;
        double bestD = TerminalHit;
        foreach (var (key, p) in _last.Terminals)
        {
            if (_raster.Project(p) is not { } s) continue;
            double d = Math.Sqrt((s.X - screen.X) * (s.X - screen.X) + (s.Y - screen.Y) * (s.Y - screen.Y));
            if (d < bestD) { bestD = d; best = key; }
        }
        return best;
    }

    private BenchPart? PartAt(Point screen)
    {
        if (_last is null) return null;
        var (o, d) = Ray(screen);
        BenchPart? best = null;
        float bestT = float.MaxValue;
        foreach (var part in _project.Parts)
        {
            if (!_last.Parts.TryGetValue(part.Id, out var b)) continue;
            if (RayBox(o, d, b.Min, b.Max) is { } t && t < bestT) { bestT = t; best = part; }
        }
        return best;
    }

    private static float? RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max)
    {
        float t0 = 0, t1 = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float oi = i == 0 ? o.X : i == 1 ? o.Y : o.Z, di = i == 0 ? d.X : i == 1 ? d.Y : d.Z;
            float lo = i == 0 ? min.X : i == 1 ? min.Y : min.Z, hi = i == 0 ? max.X : i == 1 ? max.Y : max.Z;
            if (MathF.Abs(di) < 1e-8f) { if (oi < lo || oi > hi) return null; continue; }
            float a = (lo - oi) / di, b = (hi - oi) / di;
            if (a > b) (a, b) = (b, a);
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            if (t0 > t1) return null;
        }
        return t0;
    }

    // ---------------------------------------------------------------- ratón y teclado

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        _down = _prev = pt.Position;
        e.Pointer.Capture(this);

        if (pt.Properties.IsRightButtonPressed || pt.Properties.IsMiddleButtonPressed) { _drag = Drag.Pan; return; }
        if (e.ClickCount == 2 && Pending is null && PartAt(pt.Position) is null) { FitAll(); _drag = Drag.None; return; }

        if (Pending is not null)
        {
            _drag = Drag.None;
            if (Running) { Message?.Invoke(this, "Detén la simulación (⏹) para añadir piezas."); return; }
            if (TablePoint(pt.Position) is not { } at) return;
            var t = Pending;
            double x = Snap(at.X / BenchScene3D.Scale - BenchCanvas.PartWidth / 2), y = Snap(-at.Y / BenchScene3D.Scale - 60);
            var part = t.NewPart(_project.NextId(t.Prefix), x, y);
            _project.Parts.Add(part);
            if ((e.KeyModifiers & KeyModifiers.Shift) == 0) { ClearPending?.Invoke(); PlacementDone?.Invoke(this, EventArgs.Empty); }
            PartSelected?.Invoke(this, part);
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (!Running && TerminalAt(pt.Position) is { } term)
        {
            _drag = Drag.Wire;
            _wireFrom = term;
            _wireEnd = pt.Position;
            return;
        }

        if (PartAt(pt.Position) is { } hit)
        {
            if (SelectedPart?.Invoke() != hit) PartSelected?.Invoke(this, hit);
            if (Running)
            {
                _drag = Drag.Press;
                _pressed = hit.Id;
                PartPressed?.Invoke(this, (hit.Id, true));
            }
            else if (TablePoint(pt.Position) is { } grab)
            {
                _drag = Drag.Move;
                _grabTable = grab;
                _partStart = (hit.X, hit.Y);
            }
            InvalidateVisual();
            return;
        }

        _drag = Drag.Orbit;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var d = pos - _prev;
        _prev = pos;
        switch (_drag)
        {
            case Drag.Orbit:
                Camera.Orbit(d.X * 0.4, d.Y * 0.4);
                break;
            case Drag.Pan:
                Camera.Pan(d.X, d.Y, (int)Bounds.Width, (int)Bounds.Height);
                break;
            case Drag.Move when SelectedPart?.Invoke() is { } sel && TablePoint(pos) is { } now:
                if (Math.Abs(pos.X - _down.X) + Math.Abs(pos.Y - _down.Y) < DragThreshold) return;
                sel.X = Snap(_partStart.X + (now.X - _grabTable.X) / BenchScene3D.Scale);
                sel.Y = Snap(_partStart.Y - (now.Y - _grabTable.Y) / BenchScene3D.Scale);
                break;
            case Drag.Wire:
                _wireEnd = pos;
                var target = TerminalAt(pos);
                _hoverTerminal = target is { } t && t != _wireFrom ? t : null;
                break;
            case Drag.None:
                var hover = Running ? null : TerminalAt(pos);
                if (hover == _hoverTerminal) return;
                _hoverTerminal = hover;
                ToolTip.SetTip(this, hover is { } h ? TerminalTip(h.Part, h.Port) : null);
                Cursor = hover is not null ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
                break;
            default:
                return;
        }
        InvalidateVisual();
    }

    private string TerminalTip(string part, string port)
    {
        if (BenchRules.PortOf(_project, part, port) is not { } def) return $"{part}.{port}";
        var links = _project.WiresAt(part, port).Select(w => w.Other(part, port)).Select(o => $"{o.Part}.{o.Port}").ToList();
        return $"{part}.{def.Text}: {BenchRules.KindName(def)}" + (links.Count > 0 ? $"\nunido a {string.Join(", ", links)}" : "\nlibre: arrastra desde aquí hasta otro borne");
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);
        var drag = _drag;
        _drag = Drag.None;
        e.Pointer.Capture(null);
        switch (drag)
        {
            case Drag.Press when _pressed is not null:
                PartPressed?.Invoke(this, (_pressed, false));
                _pressed = null;
                break;
            case Drag.Move when SelectedPart?.Invoke() is { } sel && (sel.X != _partStart.X || sel.Y != _partStart.Y):
                ProjectChanged?.Invoke(this, EventArgs.Empty);
                break;
            case Drag.Orbit when Math.Abs(pos.X - _down.X) + Math.Abs(pos.Y - _down.Y) < DragThreshold:
                // Clic en el fondo sin girar: quita la selección.
                if (SelectedPart?.Invoke() is not null) PartSelected?.Invoke(this, null);
                break;
            case Drag.Wire when _wireFrom is { } from:
                _hoverTerminal = null;
                if (TerminalAt(pos) is { } to && to != from)
                {
                    var why = BenchRules.CanConnect(_project, from.Part, from.Port, to.Part, to.Port);
                    if (why is not null) Message?.Invoke(this, "No se puede unir: " + why);
                    else
                    {
                        _project.Wires.Add(new BenchWire(from.Part, from.Port, to.Part, to.Port));
                        ProjectChanged?.Invoke(this, EventArgs.Empty);
                        Message?.Invoke(this, $"Unido {from.Part}.{from.Port} con {to.Part}.{to.Port}.");
                    }
                }
                _wireFrom = null;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera.Zoom(e.Delta.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            ClearPending?.Invoke(); _drag = Drag.None; _wireFrom = null;
            PlacementDone?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
        if (e.Key is Key.Delete or Key.Back) { DeleteRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }

    private static double Snap(double v) => Math.Round(v / BenchCanvas.Grid) * BenchCanvas.Grid;
}
