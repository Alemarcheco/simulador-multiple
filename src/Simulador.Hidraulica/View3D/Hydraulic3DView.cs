using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Solver;

namespace Simulador.Hidraulica.View3D;

/// <summary>
/// Vista 3D de la instalación. Ratón: izquierdo gira, derecho o central desplaza, rueda acerca; doble clic encuadra.
/// Durante la simulación se redibuja en cada fotograma (niveles, caudal y bombas girando).
/// </summary>
public sealed class Hydraulic3DView : Control
{
    private static readonly Typeface Font = new("Inter, Segoe UI, Arial");
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush LabelInk = new SolidColorBrush(Color.Parse("#24292F"));
    private static readonly IBrush LabelAccent = new SolidColorBrush(Color.Parse("#E8590C"));
    private static readonly IBrush HelpInk = new SolidColorBrush(Color.Parse("#57606A"));

    private readonly Rasterizer _raster = new();
    private WriteableBitmap? _bitmap;
    private Point? _last;
    private bool _panning;
    private bool _fitted;

    public OrbitCamera Camera { get; } = new();
    public HydraulicNetwork? Network { get; set; }
    public HydraulicSimulation? Simulation { get; set; }
    public string? SelectedId { get; set; }
    public bool ShowLabels { get; set; } = true;
    /// <summary>Otra fuente de escena (la usa la pestaña Neumática): recibe el tiempo de animación y el elemento seleccionado.</summary>
    public Func<double, string?, Scene>? SceneProvider { get; set; }
    private double _animation;

    public Hydraulic3DView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>Avanza la animación (bandas de flujo, ventiladores de bombas).</summary>
    public void Animate(double seconds)
    {
        _animation += seconds;
        InvalidateVisual();
    }

    /// <summary>Encuadra toda la instalación.</summary>
    public void FitAll()
    {
        if (BuildScene() is not { } scene) return;
        Camera.Fit(scene.Min, scene.Max);
        _fitted = true;
        InvalidateVisual();
    }

    public void SetView(double yaw, double pitch)
    {
        Camera.Yaw = yaw;
        Camera.Pitch = pitch;
        InvalidateVisual();
    }

    /// <summary>Cambió el diseño completo (nuevo proyecto): vuelve a encuadrar la próxima vez.</summary>
    public void ResetFraming() { _fitted = false; InvalidateVisual(); }

    private Scene? BuildScene() =>
        SceneProvider?.Invoke(_animation, SelectedId) ?? (Network is null ? null : SceneBuilder.Build(Network, Simulation, _animation, SelectedId));

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        int w = Math.Max(1, (int)size.Width), h = Math.Max(1, (int)size.Height);
        if (w < 4 || h < 4 || BuildScene() is not { } scene) return;
        if (!_fitted) { Camera.Fit(scene.Min, scene.Max); _fitted = true; }

        _raster.Resize(w, h);
        _raster.Render(scene, Camera);
        if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
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
            foreach (var label in scene.Labels)
            {
                if (_raster.Project(label.Position) is not { } p || !_raster.IsVisible(p.X, p.Y, p.Depth)) continue;
                var text = new FormattedText(label.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 11,
                    label.Highlight ? LabelAccent : LabelInk);
                var r = new Rect(p.X - text.Width / 2 - 4, p.Y - text.Height - 2, text.Width + 8, text.Height + 2);
                ctx.DrawRectangle(LabelBack, null, r, 3, 3);
                ctx.DrawText(text, new Point(r.X + 4, r.Y + 1));
            }

        var help = new FormattedText("Arrastra para girar · botón derecho para desplazar · rueda para acercar · doble clic para encuadrar",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 11, HelpInk);
        ctx.DrawText(help, new Point(10, h - help.Height - 8));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount == 2) { FitAll(); return; }
        var props = e.GetCurrentPoint(this).Properties;
        _panning = props.IsRightButtonPressed || props.IsMiddleButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _last = e.GetPosition(this);
        e.Pointer.Capture(this);
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_last is not { } last) return;
        var pos = e.GetPosition(this);
        var d = pos - last;
        _last = pos;
        if (_panning) Camera.Pan(d.X, d.Y, (int)Bounds.Height);
        else Camera.Orbit(d.X * 0.4, d.Y * 0.4);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _last = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera.Zoom(e.Delta.Y);
        InvalidateVisual();
    }
}
