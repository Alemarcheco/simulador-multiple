using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Simulador.Cad3D.Rendering;

/// <summary>
/// Contenedor del visor 3D: intenta OpenGL y, si el equipo no lo tiene (o CAD3D_SOFTWARE_VIEW=1), usa el visor por software.
/// Gestiona el ratón y la cámara para los dos.
/// </summary>
public sealed class ViewportHost : Border
{
    private readonly OrbitCamera _camera = new();
    private readonly Viewport3D _software;
    private GlViewport3D? _gl;
    private IReadOnlyList<RenderItem> _items = Array.Empty<RenderItem>();

    /// <summary>Se lanza cuando cambia el motor de dibujo (para mostrarlo en la barra de estado).</summary>
    public event Action<string>? RendererChanged;

    public string RendererName { get; private set; } = "iniciando…";

    public ViewportHost()
    {
        Background = Viewport3D.Background;
        ClipToBounds = true;
        Focusable = true;
        _software = new Viewport3D(_camera);
        _camera.Changed += Redraw;
        _camera.AttachInput(this);

        if (Environment.GetEnvironmentVariable("CAD3D_SOFTWARE_VIEW") == "1")
        {
            Child = _software;
            RendererName = "software";
            return;
        }
        _gl = new GlViewport3D(_camera);
        _gl.Failed += reason => FallBack($"OpenGL no disponible: {reason}");
        Child = _gl;
        // Si OpenGL no arranca en unos segundos desde que el visor se ve (equipo sin aceleración), se pasa al visor por software.
        bool timerStarted = false;
        LayoutUpdated += (_, _) =>
        {
            if (timerStarted || _gl is null || Bounds.Width < 1 || !IsEffectivelyVisible) return;
            timerStarted = true;
            DispatcherTimer.RunOnce(() =>
            {
                if (_gl is { GlReady: true }) { RendererName = _gl.Renderer; RendererChanged?.Invoke(RendererName); }
                else if (_gl is not null) FallBack("OpenGL no arrancó");
            }, TimeSpan.FromSeconds(3));
        };
    }

    private void FallBack(string reason)
    {
        if (_gl is null) return;
        _gl = null;
        Child = _software;
        _software.SetItems(_items);
        RendererName = $"software ({reason})";
        RendererChanged?.Invoke(RendererName);
    }

    private void Redraw()
    {
        if (_gl is not null) _gl.RequestNextFrameRendering();
        else _software.InvalidateVisual();
    }

    public void SetItems(IReadOnlyList<RenderItem> items)
    {
        _items = items;
        if (_gl is not null) _gl.SetItems(items);
        else _software.SetItems(items);
    }

    public void FitAll() => _camera.Fit(_items.Where(i => !i.Overlay).Select(i => i.Mesh));
    public void SetView(double yaw, double pitch) => _camera.SetView(yaw, pitch);
}
