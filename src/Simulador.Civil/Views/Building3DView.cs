using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Simulador.Civil.Model;
using Simulador.Civil.Rendering;
using Simulador.Core.Rendering;

namespace Simulador.Civil.Views;

/// <summary>
/// Vista 3D del edificio con rasterizado por software y búfer de profundidad (sin OpenGL): las caras se tapan bien
/// desde cualquier ángulo y el encuadre no cambia al girar. Izquierdo = girar, derecho o central = desplazar, rueda = zoom.
/// Con <see cref="WalkMode"/> se recorre el edificio en primera persona (teclas W A S D o flechas y ratón para mirar).
/// </summary>
public sealed class Building3DView : Control
{
    private readonly BuildingDocument _doc;
    private readonly OrbitCamera _camera = BuildingRenderer.CreateCamera();
    private readonly ZBufferRasterizer _renderer = BuildingRenderer.CreateRasterizer();
    private BuildingMesh? _mesh;
    /// <summary>Caja del último encuadre automático; null = encuadrar en el próximo dibujo.</summary>
    private (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max)? _fitted;
    /// <summary>true cuando el usuario ha desplazado o acercado: los cambios del edificio ya no reencuadran.</summary>
    private bool _userMoved;
    private WriteableBitmap? _bitmap;
    private Point? _drag;
    private bool _dragPan;

    public Building3DView(BuildingDocument doc)
    {
        _doc = doc;
        ClipToBounds = true;
        Focusable = true;
        doc.Changed += (_, _) => Invalidate();
        _walkTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Render, (_, _) => WalkTick());
    }

    // ------------------------------------------------------------------ recorrido en primera persona

    private readonly OrbitCamera _walkCamera = new() { Orthographic = false, FieldOfView = 70, MinPitch = -89, MaxPitch = 89 };
    private readonly HashSet<Key> _keys = [];
    private readonly DispatcherTimer _walkTimer;
    private DateTime _lastTick;
    private Walker? _walker;

    /// <summary>Se dispara al salir del recorrido con Esc (para que la barra desmarque el botón).</summary>
    public event EventHandler? WalkModeExited;

    /// <summary>Recorrido en primera persona: empieza delante de la puerta de entrada.</summary>
    public bool WalkMode
    {
        get => _walker is not null;
        set
        {
            if (value == WalkMode) return;
            _keys.Clear();
            if (value)
            {
                _walker = Walker.AtEntrance(_doc);
                _lastTick = DateTime.Now;
                _walkTimer.Start();
                Focus();
            }
            else
            {
                _walker = null;
                _walkTimer.Stop();
            }
            Invalidate();
        }
    }

    /// <summary>Velocidad de paso (m/s); con Mayúsculas, el doble.</summary>
    public const double WalkSpeed = 1.4;

    private void WalkTick()
    {
        var now = DateTime.Now;
        double dt = Math.Clamp((now - _lastTick).TotalSeconds, 0, 0.1);
        _lastTick = now;
        if (_walker is null || _keys.Count == 0) return;
        double speed = WalkSpeed * (_keys.Contains(Key.LeftShift) || _keys.Contains(Key.RightShift) ? 2 : 1) * dt;
        double fwd = 0, side = 0, turn = 0, look = 0;
        if (_keys.Contains(Key.W) || _keys.Contains(Key.Up)) fwd += 1;
        if (_keys.Contains(Key.S) || _keys.Contains(Key.Down)) fwd -= 1;
        if (_keys.Contains(Key.D)) side += 1;
        if (_keys.Contains(Key.A)) side -= 1;
        if (_keys.Contains(Key.Left)) turn += 1;
        if (_keys.Contains(Key.Right)) turn -= 1;
        if (_keys.Contains(Key.PageUp)) look += 1;
        if (_keys.Contains(Key.PageDown)) look -= 1;
        if (fwd == 0 && side == 0 && turn == 0 && look == 0) return;
        _walker.Turn(turn * 1.6 * dt, look * 1.2 * dt);
        if (fwd != 0 || side != 0)
        {
            double k = fwd != 0 && side != 0 ? Math.Sqrt(0.5) : 1;
            _walker.Move(fwd * speed * k, side * speed * k);
        }
        InvalidateVisual();
    }

    /// <summary>Coloca la cámara en los ojos de quien camina.</summary>
    public static void AimCamera(OrbitCamera cam, Walker w)
    {
        var (lx, ly, lz) = w.LookDirection;
        var eye = new System.Numerics.Vector3((float)w.Position.X, (float)w.Position.Y, (float)w.EyeZ);
        cam.Distance = 1;
        cam.Target = eye + new System.Numerics.Vector3((float)lx, (float)ly, (float)lz);
        // La cámara orbital mira desde Target + dir·Distance hacia Target: el ojo queda en su sitio con el rumbo opuesto.
        cam.Yaw = w.Heading * 180 / Math.PI + 180;
        cam.Pitch = -w.Pitch * 180 / Math.PI;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_walker is null) return;
        if (e.Key == Key.Escape)
        {
            WalkMode = false;
            WalkModeExited?.Invoke(this, EventArgs.Empty);
        }
        else _keys.Add(e.Key);
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _keys.Remove(e.Key);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _keys.Clear(); // que no se quede andando solo al cambiar de ventana
    }

    private string? _upTo;
    private bool _showRoof = true;

    /// <summary>Muestra los niveles hasta este (incluido); null = todos.</summary>
    public string? UpToLevelId { get => _upTo; set { _upTo = value; Invalidate(); } }
    public bool ShowRoof { get => _showRoof; set { _showRoof = value; Invalidate(); } }

    /// <summary>La malla se reconstruye solo cuando cambia el edificio o el filtro; al girar se reutiliza.</summary>
    private void Invalidate()
    {
        _mesh = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int w = (int)Math.Ceiling(Bounds.Width * scaling), h = (int)Math.Ceiling(Bounds.Height * scaling);
        if (w < 2 || h < 2) return;

        if (_walker is not null)
        {
            // Dentro se ve todo el edificio, con cubierta, y sin supermuestreo para que el paso sea fluido.
            _mesh ??= BuildingMesh.Build(_doc, null, true);
            AimCamera(_walkCamera, _walker);
            _renderer.Supersampling = 1;
            _renderer.Resize(w, h);
            BuildingRenderer.Render(_renderer, _mesh, _walkCamera, edges: true);
            Blit(ctx, w, h, scaling);
            var lvl = _walker.Level;
            var hud = new FormattedText(
                $"{lvl?.Name ?? ""}  ·  W A S D o flechas para andar · arrastra para mirar · Mayús = correr · Esc = salir",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.White);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(150, 20, 30, 40)), new Rect(8, 8, hud.Width + 16, hud.Height + 10), 4);
            ctx.DrawText(hud, new Point(16, 13));
            // Punto de mira.
            var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), null, c, 2.5, 2.5);
            return;
        }

        _mesh ??= BuildingMesh.Build(_doc, UpToLevelId, ShowRoof);
        var bounds = BuildingRenderer.Bounds(_mesh);
        // Encuadre con la esfera envolvente (fijo al girar). Se rehace si el edificio cambia de tamaño, salvo que el
        // usuario haya movido la vista a mano.
        if (_fitted is null || (!_userMoved && _fitted.Value != bounds))
        {
            _camera.Fit(bounds.Min, bounds.Max);
            _fitted = bounds;
        }

        // Suavizado de bordes por supermuestreo 2× mientras el lienzo no sea enorme.
        _renderer.Supersampling = w * h <= 1_500_000 ? 2 : 1;
        _renderer.Resize(w, h);
        BuildingRenderer.Render(_renderer, _mesh, _camera, edges: true);
        Blit(ctx, w, h, scaling);
    }

    private void Blit(DrawingContext ctx, int w, int h, double scaling)
    {
        var pixels = _renderer.Pixels;

        if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96 * scaling, 96 * scaling), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using (var fb = _bitmap.Lock())
        {
            var bytes = MemoryMarshal.AsBytes(pixels.AsSpan());
            for (int y = 0; y < h; y++)
                unsafe
                {
                    bytes.Slice(y * w * 4, w * 4).CopyTo(new Span<byte>((byte*)fb.Address + (long)y * fb.RowBytes, w * 4));
                }
        }
        ctx.DrawImage(_bitmap, new Rect(0, 0, w, h), new Rect(Bounds.Size));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (_walker is not null) Focus();
        _drag = e.GetPosition(this);
        _dragPan = props.IsMiddleButtonPressed || props.IsRightButtonPressed;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } d) return;
        var p = e.GetPosition(this);
        if (_walker is not null)
        {
            _walker.Turn(-(p.X - d.X) * 0.005, -(p.Y - d.Y) * 0.005);
        }
        else if (_dragPan)
        {
            _camera.Pan(p.X - d.X, p.Y - d.Y, (int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
            _userMoved = true;
        }
        else _camera.Orbit((p.X - d.X) * 0.5, (p.Y - d.Y) * 0.5);
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
        if (_walker is not null)
        {
            // En el recorrido la rueda da pasos adelante o atrás.
            _walker.Move(e.Delta.Y > 0 ? 0.5 : -0.5, 0);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        _camera.Zoom(e.Delta.Y > 0 ? 1.1 : -1.1);
        _userMoved = true;
        InvalidateVisual();
        e.Handled = true;
    }

    public void ResetView()
    {
        var fresh = BuildingRenderer.CreateCamera();
        _camera.Yaw = fresh.Yaw;
        _camera.Pitch = fresh.Pitch;
        _userMoved = false;
        _fitted = null;
        Invalidate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _walkTimer.Stop();
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
