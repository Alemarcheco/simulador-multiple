using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Simulador.Tableros.Model;
using Simulador.Tableros.Rendering;

namespace Simulador.Tableros.Ui;

/// <summary>
/// Vista frontal editable del tablero: la placa de montaje (carriles, canaletas, aparatos y recorrido de los hilos)
/// o la puerta (mandos y pilotos, con la silueta de la placa detrás). Arrastrar mueve, con imán al carril y a los
/// aparatos vecinos; rueda = zoom; botón central o derecho = desplazar; flechas = mover 1 mm (Mayús, 10 mm).
/// </summary>
internal sealed class PanelCanvas : Control
{
    public enum Mode { Plate, Door }

    private readonly Mode _mode;
    private PanelDocument _doc = new();
    private IReadOnlyList<WireRoute> _routes = [];
    private HashSet<string> _problemTags = [];
    private double _scale = 1, _ox, _oy;
    private bool _userZoom;
    private Point? _panFrom;
    private (object Item, double Dx, double Dy, double X0, double Y0)? _drag;
    private bool _moved;

    public object? Selected { get; private set; }
    public bool ShowWires { get; set; } = true;
    /// <summary>Cara que se dibuja y edita: false = delantera, true = trasera (armario de doble frente).</summary>
    public bool Rear { get; set; }
    public event Action? SelectionChanged;
    /// <summary>El usuario ha movido algo (hay que rehacer el cableado).</summary>
    public event Action<string>? Modified;
    public event Action<string>? Hint;

    public PanelCanvas(Mode mode)
    {
        _mode = mode;
        Focusable = true;
        ClipToBounds = true;
    }

    public void Load(PanelDocument doc, IReadOnlyList<WireRoute> routes, IEnumerable<string> problemTags)
    {
        if (!ReferenceEquals(doc, _doc)) { _userZoom = false; Selected = null; }
        _doc = doc;
        _routes = routes;
        _problemTags = problemTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Selected is PanelDevice d && !doc.Devices.Contains(d)) Selected = null;
        InvalidateVisual();
    }

    public void Select(object? item, bool notify = true)
    {
        Selected = item;
        if (notify) SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void Fit() { _userZoom = false; InvalidateVisual(); }

    // ------------------------------------------------------------------ coordenadas

    /// <summary>Origen de la placa dentro de la envolvente (la puerta usa el de la envolvente).</summary>
    private double OffX => _mode == Mode.Plate ? _doc.Enclosure.PlateLeft : 0;
    private double OffY => _mode == Mode.Plate ? _doc.Enclosure.PlateTop : 0;

    private Point ToPx(double xmm, double ymm) => new(_ox + (xmm + OffX) * _scale, _oy + (ymm + OffY) * _scale);
    private (double X, double Y) ToMm(Point p) => ((p.X - _ox) / _scale - OffX, (p.Y - _oy) / _scale - OffY);
    private Rect R(double x, double y, double w, double h) => new(ToPx(x, y), new Size(Math.Max(0, w * _scale), Math.Max(0, h * _scale)));

    private void AutoFit()
    {
        if (_userZoom || Bounds.Width < 10) return;
        var e = _doc.Enclosure;
        // En la placa se deja sitio a la derecha para los aparatos aún sin colocar.
        var extraRight = _mode == Mode.Plate && _doc.PlateOf(Rear).Any(d => d.X > e.PlateWidth) ? _doc.PlateOf(Rear).Max(d => d.X + d.Width) - e.PlateWidth + 20 : 0;
        double w = e.Width + extraRight + 60, h = e.Height + 60;
        _scale = Math.Min(Bounds.Width / w, Bounds.Height / h);
        _ox = (Bounds.Width - (e.Width + extraRight) * _scale) / 2;
        _oy = (Bounds.Height - e.Height * _scale) / 2;
    }

    // ------------------------------------------------------------------ dibujo

    private static readonly IBrush Paper = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF4));
    private static readonly IBrush EnclosureFill = new SolidColorBrush(Color.FromRgb(0xD5, 0xD8, 0xD4));
    private static readonly IBrush PlateFill = new SolidColorBrush(Color.FromRgb(0xEA, 0xDF, 0xBA));
    private static readonly IBrush RailFill = new SolidColorBrush(Color.FromRgb(0xC3, 0xC9, 0xCF));
    private static readonly IBrush DuctFill = new SolidColorBrush(Color.FromRgb(0xA9, 0xAF, 0xB5));
    private static readonly IPen Thin = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x66)), 1);
    private static readonly IPen DuctSlot = new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x96)), 1);
    private static readonly IPen SelectPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB)), 3);
    private static readonly IPen ErrorPen = new Pen(Brushes.Red, 2, new DashStyle([3, 2], 0));
    private static readonly IBrush Ghost = new SolidColorBrush(Color.FromArgb(40, 30, 40, 60));

    public override void Render(DrawingContext ctx)
    {
        AutoFit();
        ctx.FillRectangle(Paper, new Rect(Bounds.Size));
        var e = _doc.Enclosure;
        // Envolvente (vista frontal sin puerta, o la puerta).
        var outer = new Rect(new Point(_ox, _oy), new Size(e.Width * _scale, e.Height * _scale));
        ctx.DrawRectangle(EnclosureFill, new Pen(Brushes.DimGray, 2), outer, 4, 4);
        if (_mode == Mode.Plate) RenderPlate(ctx);
        else RenderDoor(ctx);
        DrawText(ctx, (e.Reference is null ? "" : $"{e.Brand} {e.Reference} · ") + $"{e.Width:0} × {e.Height:0} × {e.Depth:0} mm (an × al × f) · placa {e.PlateWidth:0} × {e.PlateHeight:0}", outer.BottomLeft + new Point(0, 4), 11, Brushes.DimGray);
    }

    private void RenderPlate(DrawingContext ctx)
    {
        var e = _doc.Enclosure;
        ctx.DrawRectangle(PlateFill, Thin, R(0, 0, e.PlateWidth, e.PlateHeight));

        foreach (var r in _doc.RailsOf(Rear))
        {
            var rect = R(r.X, r.Y, r.Length, DinRail.RailHeight);
            ctx.DrawRectangle(RailFill, Selected == r ? SelectPen : Thin, rect);
            // Agujeros alargados del carril perforado.
            if (_scale > 0.6)
                for (var x = r.X + 10; x < r.X + r.Length - 15; x += 25)
                    ctx.FillRectangle(Brushes.DimGray, R(x, r.Y + 15, 12, 5));
        }
        foreach (var k in _doc.DuctsOf(Rear))
        {
            var rect = R(k.X, k.Y, k.W, k.H);
            ctx.DrawRectangle(DuctFill, Selected == k ? SelectPen : Thin, rect);
            // Ranuras de la canaleta.
            if (_scale > 0.4)
            {
                if (k.Vertical)
                    for (var y = k.Y + 8; y < k.Y + k.Length - 4; y += 12)
                    {
                        ctx.DrawLine(DuctSlot, ToPx(k.X + 3, y), ToPx(k.X + 10, y));
                        ctx.DrawLine(DuctSlot, ToPx(k.X + k.Width - 10, y), ToPx(k.X + k.Width - 3, y));
                    }
                else
                    for (var x = k.X + 8; x < k.X + k.Length - 4; x += 12)
                    {
                        ctx.DrawLine(DuctSlot, ToPx(x, k.Y + 3), ToPx(x, k.Y + 10));
                        ctx.DrawLine(DuctSlot, ToPx(x, k.Y + k.Width - 10), ToPx(x, k.Y + k.Width - 3));
                    }
            }
        }

        foreach (var d in _doc.PlateOf(Rear)) DrawDevice(ctx, d);

        if (ShowWires)
            foreach (var r in _routes)
            {
                var pts = r.PointsOn(Rear);
                if (pts.Count < 2) continue;
                var c = PanelScene.WireColor(r.Wire.Color);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, (byte)(c >> 16), (byte)(c >> 8), (byte)c)), Math.Clamp(r.Wire.Section * 0.6 * _scale, 1, 3));
                var sel = Selected is PanelDevice sd && (r.Wire.From.Tag == sd.Tag || r.Wire.To.Tag == sd.Tag);
                if (sel) pen = new Pen(pen.Brush, pen.Thickness + 1.5);
                for (var i = 0; i + 1 < pts.Count; i++)
                    ctx.DrawLine(pen, ToPx(pts[i].X, pts[i].Y), ToPx(pts[i + 1].X, pts[i + 1].Y));
            }
    }

    private void RenderDoor(DrawingContext ctx)
    {
        var e = _doc.Enclosure;
        // Silueta de lo que hay detrás en la placa, para no chocar con los mandos.
        foreach (var d in _doc.PlateOf(Rear))
            ctx.FillRectangle(Ghost, R(d.X + e.PlateLeft, d.Y + e.PlateTop, d.Width, d.Height));
        if (e.Doors > 1)
        {
            // Dos hojas: junta en el centro, manetas a los lados de la junta y bisagras a los dos lados.
            ctx.DrawLine(new Pen(Brushes.DimGray, 2), ToPx(e.Width / 2, 0), ToPx(e.Width / 2, e.Height));
            ctx.FillRectangle(Brushes.DimGray, R(e.Width / 2 - 40, e.Height / 2 - 60, 18, 120), 4);
            for (var y = 0.15; y < 1; y += 0.7)
            {
                ctx.FillRectangle(Brushes.Gray, R(-6, e.Height * y, 8, 50));
                ctx.FillRectangle(Brushes.Gray, R(e.Width - 2, e.Height * y, 8, 50));
            }
        }
        else
        {
            // Maneta.
            ctx.FillRectangle(Brushes.DimGray, R(e.Width - 45, e.Height / 2 - 60, 18, 120), 4);
            // Bisagras a la izquierda.
            for (var y = 0.15; y < 1; y += 0.7) ctx.FillRectangle(Brushes.Gray, R(-6, e.Height * y, 8, 50));
        }
        foreach (var d in _doc.DoorOf(Rear)) DrawDevice(ctx, d);
    }

    private void DrawDevice(DrawingContext ctx, PanelDevice d)
    {
        var model = DeviceLibrary.Get(d.Kind);
        var rect = R(d.X, d.Y, d.Width, d.Height);
        var body = Brush(model.Body);
        var front = Brush(model.Front);
        var dark = IsDark(model.Body);
        if (d.Location == DeviceLocation.Door)
        {
            // Mandos redondos: anillo y botón.
            var c = rect.Center;
            var r = Math.Min(rect.Width, rect.Height) / 2;
            if (d.Kind == "controlador")
            {
                // Equipo de panel (Woodward 505, ProTech…): marco, pantalla arriba y teclado abajo.
                ctx.DrawRectangle(body, Thin, rect, 4, 4);
                var screen = new Rect(rect.X + rect.Width * 0.1, rect.Y + rect.Height * 0.1, rect.Width * 0.8, rect.Height * 0.38);
                ctx.DrawRectangle(Brush(0xFF9DB7A6), Thin, screen, 2, 2);
                for (var i = 0; i < 3; i++)
                for (var j = 0; j < 4; j++)
                    ctx.DrawRectangle(front, null, new Rect(rect.X + rect.Width * (0.14 + j * 0.19), rect.Y + rect.Height * (0.56 + i * 0.12), rect.Width * 0.14, rect.Height * 0.08), 1, 1);
            }
            else if (d.Kind == "instrumento")
            {
                ctx.DrawRectangle(body, Thin, rect, 3, 3);
                ctx.DrawRectangle(front, null, rect.Deflate(r * 0.15));
                ctx.DrawLine(new Pen(Brushes.Black, 1.5), new Point(c.X, c.Y + r * 0.5), new Point(c.X + r * 0.45, c.Y - r * 0.35));
            }
            else
            {
                IBrush button = d.Kind switch
                {
                    "piloto" => Brush(PanelScene.PilotColor(d.Description)),
                    "pulsador" => Brush(d.Description.Contains("rojo") ? 0xFFD32F2F : 0xFF2E9E4D),
                    "seta" => Brush(0xFFD32F2F),
                    _ => Brush(0xFF2B2F33),
                };
                if (d.Kind == "seta") ctx.DrawRectangle(Brush(0xFFE8C21F), Thin, rect.Inflate(r * 0.3), 4, 4);
                ctx.DrawEllipse(Brush(0xFF2B2F33), Thin, c, r, r);
                ctx.DrawEllipse(button, null, c, r * 0.75, r * 0.75);
                if (d.Kind == "selector") ctx.FillRectangle(Brushes.Black, new Rect(c.X - r * 0.15, c.Y - r * 0.6, r * 0.3, r * 1.2));
            }
            DrawText(ctx, d.Tag, new Point(rect.Center.X, rect.Bottom + 3), 11, Brushes.Black, center: true, bold: true);
        }
        else
        {
            ctx.DrawRectangle(body, Thin, rect, 2, 2);
            if (d.Kind == "regleta")
            {
                var n = d.Terminals.Count / 2;
                for (var i = 0; i < n; i++)
                {
                    var label = d.Terminals[2 * i].Label;
                    var fill = label is "N" || label.StartsWith("XN") ? Brush(0xFF4F8FD6) : label == "PE" ? Brush(0xFF7DBB2E) : Brush(0xFFBFC4C9);
                    var tr = R(d.X + 9 + i * DeviceLibrary.TerminalPitch + 0.2, d.Y, DeviceLibrary.TerminalPitch - 0.4, d.Height);
                    ctx.DrawRectangle(fill, Thin, tr);
                    if (_scale > 1.6) DrawText(ctx, label, new Point(tr.Center.X, tr.Center.Y - 6), 8, Brushes.Black, center: true);
                }
            }
            else
            {
                // Frente (zona de la maneta o del display).
                var inner = R(d.X + d.Width * 0.18, d.Y + d.Height * 0.36, d.Width * 0.64, d.Height * 0.16);
                ctx.FillRectangle(front, inner);
            }
            var tagBrush = dark ? Brushes.White : Brushes.Black;
            var tagY = d.Kind == "regleta" ? rect.Top - 14 : rect.Top + rect.Height * 0.12;
            DrawText(ctx, d.Tag, new Point(rect.Center.X, tagY), Math.Clamp(11 * _scale, 9, 14), d.Kind == "regleta" ? Brushes.Black : tagBrush, center: true, bold: true);
            if (_scale > 1.3 && d.Kind != "regleta")
                DrawText(ctx, Short(d), new Point(rect.Center.X, rect.Top + rect.Height * 0.62), 9, tagBrush, center: true, maxWidth: rect.Width - 2);
            // Bornes.
            if (_scale > 1.1)
                foreach (var t in d.Terminals)
                {
                    var (x, y) = d.TerminalPoint(t);
                    var p = ToPx(x, y);
                    ctx.DrawEllipse(Brushes.Goldenrod, null, p, 1.6, 1.6);
                    if (_scale > 2.2 && d.Kind != "regleta")
                        DrawText(ctx, t.Label, new Point(p.X, t.Side == TerminalSide.Top ? p.Y + 1 : p.Y - 11), 8, tagBrush, center: true);
                }
        }
        if (_problemTags.Contains(d.Tag)) ctx.DrawRectangle(null, ErrorPen, rect.Inflate(3));
        if (Selected == d) ctx.DrawRectangle(null, SelectPen, rect.Inflate(2));
    }

    /// <summary>Texto corto del aparato para dibujarlo encima (calibre o tipo).</summary>
    private static string Short(PanelDevice d) => d.Kind switch
    {
        "magnetotermico" or "fusible" or "diferencial" or "guardamotor" or "rele_termico" or "contactor" when d.RatedA > 0 => $"{d.RatedA.ToString("0.#", CultureInfo.GetCultureInfo("es-ES"))} A",
        "plc" => "PLC",
        "mcu" => "µC",
        "variador" => "VFD",
        "fuente" => "24 V DC",
        _ => "",
    };

    private static bool IsDark(uint argb) => ((argb >> 16) & 0xFF) * 0.3 + ((argb >> 8) & 0xFF) * 0.59 + (argb & 0xFF) * 0.11 < 120;

    private static IBrush Brush(uint argb) => new SolidColorBrush(Color.FromUInt32(argb));

    private static void DrawText(DrawingContext ctx, string text, Point at, double size, IBrush brush, bool center = false, bool bold = false, double maxWidth = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, brush);
        if (maxWidth > 0 && ft.Width > maxWidth) return;
        ctx.DrawText(ft, center ? new Point(at.X - ft.Width / 2, at.Y) : at);
    }

    // ------------------------------------------------------------------ ratón y teclado

    private object? HitTest(double x, double y)
    {
        var devices = _mode == Mode.Plate ? _doc.PlateOf(Rear) : _doc.DoorOf(Rear);
        foreach (var d in devices.Reverse())
            if (x >= d.X && x <= d.X + d.Width && y >= d.Y && y <= d.Y + d.Height) return d;
        if (_mode == Mode.Door) return null;
        foreach (var k in _doc.DuctsOf(Rear))
            if (x >= k.X && x <= k.X + k.W && y >= k.Y && y <= k.Y + k.H) return k;
        foreach (var r in _doc.RailsOf(Rear))
            if (x >= r.X && x <= r.X + r.Length && y >= r.Y && y <= r.Y + DinRail.RailHeight) return r;
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed)
        {
            _panFrom = pt.Position;
            e.Pointer.Capture(this);
            return;
        }
        var (x, y) = ToMm(pt.Position);
        var hit = HitTest(x, y);
        Select(hit);
        if (hit is not null)
        {
            var (ix, iy) = Position(hit);
            _drag = (hit, x - ix, y - iy, ix, iy);
            _moved = false;
            e.Pointer.Capture(this);
            Hint?.Invoke(hit switch
            {
                PanelDevice d => $"{d.Tag}: {d.Description}. Arrastra para moverlo; se ajusta solo al carril y a los aparatos vecinos.",
                DinRail => "Carril DIN de 35 mm. Arrastra para moverlo; cambia su largo en Propiedades.",
                Duct => "Canaleta. Arrastra para moverla; cambia sus medidas en Propiedades.",
                _ => "",
            });
        }
    }

    private static (double X, double Y) Position(object o) => o switch
    {
        PanelDevice d => (d.X, d.Y),
        DinRail r => (r.X, r.Y),
        Duct k => (k.X, k.Y),
        _ => (0, 0),
    };

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_panFrom is { } from)
        {
            _ox += p.X - from.X;
            _oy += p.Y - from.Y;
            _panFrom = p;
            _userZoom = true;
            InvalidateVisual();
            return;
        }
        if (_drag is not { } drag) return;
        var (x, y) = ToMm(p);
        double nx = x - drag.Dx, ny = y - drag.Dy;
        switch (drag.Item)
        {
            case PanelDevice d:
                (d.X, d.Y) = Snap(d, nx, ny);
                break;
            case DinRail r:
                r.X = Math.Round(nx / 5) * 5;
                r.Y = Math.Round(ny / 5) * 5;
                break;
            case Duct k:
                k.X = Math.Round(nx / 5) * 5;
                k.Y = Math.Round(ny / 5) * 5;
                break;
        }
        _moved = true;
        InvalidateVisual();
    }

    /// <summary>Imán: al carril más cercano (centrado) y a los cantos de los aparatos vecinos del mismo carril.</summary>
    private (double X, double Y) Snap(PanelDevice d, double x, double y)
    {
        x = Math.Round(x * 2) / 2;
        y = Math.Round(y * 2) / 2;
        if (d.Location != DeviceLocation.Plate) return (Math.Round(x), Math.Round(y));
        if (d.OnRail)
        {
            var center = y + d.Height / 2;
            var rail = _doc.RailsOf(Rear).Where(r => x + d.Width > r.X && x < r.X + r.Length)
                .OrderBy(r => Math.Abs(r.CenterY - center)).FirstOrDefault();
            if (rail is not null && Math.Abs(rail.CenterY - center) < 30) y = rail.CenterY - d.Height / 2;
        }
        foreach (var o in _doc.PlateOf(Rear))
        {
            if (o == d || Math.Abs(o.Y + o.Height / 2 - (y + d.Height / 2)) > 20) continue;
            if (Math.Abs(x - (o.X + o.Width)) < 4) x = o.X + o.Width;
            else if (Math.Abs(x + d.Width - o.X) < 4) x = o.X - d.Width;
        }
        return (x, y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        _panFrom = null;
        if (_drag is { } drag && _moved)
        {
            var name = drag.Item switch { PanelDevice d => d.Tag, DinRail => "carril", Duct => "canaleta", _ => "" };
            Modified?.Invoke($"Mover {name}");
            SelectionChanged?.Invoke();
        }
        _drag = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        var k = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        _ox = p.X - (p.X - _ox) * k;
        _oy = p.Y - (p.Y - _oy) * k;
        _scale *= k;
        _userZoom = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Selected is null) return;
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        double dx = e.Key switch { Key.Left => -step, Key.Right => step, _ => 0 };
        double dy = e.Key switch { Key.Up => -step, Key.Down => step, _ => 0 };
        if (dx == 0 && dy == 0) return;
        switch (Selected)
        {
            case PanelDevice d: d.X += dx; d.Y += dy; break;
            case DinRail r: r.X += dx; r.Y += dy; break;
            case Duct k: k.X += dx; k.Y += dy; break;
        }
        Modified?.Invoke("Mover");
        SelectionChanged?.Invoke();
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateVisual();
    }
}
