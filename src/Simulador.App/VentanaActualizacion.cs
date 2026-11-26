using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Simulador.App.Tema;

namespace Simulador.App;

/// <summary>
/// "Buscar actualizaciones": mira en GitHub si hay una versión nueva y, si el usuario quiere,
/// la descarga, cierra el programa, la instala encima y lo vuelve a abrir.
/// </summary>
public sealed class VentanaActualizacion : Window
{
    private readonly TextBlock _titulo = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _texto = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _barra = new() { Minimum = 0, Maximum = 100, Height = 8, IsVisible = false };
    private readonly StackPanel _botones = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
    private CancellationTokenSource? _cancelar;

    private VentanaActualizacion()
    {
        Title = "Buscar actualizaciones";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 14,
                    Children = { Logo.Create(40), new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 340, Children = { _titulo } } },
                },
                _texto,
                _barra,
                _botones,
            },
        };
        Closed += (_, _) => _cancelar?.Cancel();
    }

    /// <summary>Abre la ventana y empieza a buscar (o muestra directamente la versión ya encontrada).</summary>
    public static void Mostrar(Window owner)
    {
        var w = new VentanaActualizacion();
        _ = w.ShowDialog(owner);
        if (Actualizador.Disponible is { } ya) w.MostrarNueva(ya);
        else _ = w.BuscarAsync();
    }

    private async Task BuscarAsync()
    {
        Estado("Buscando actualizaciones…", $"Tienes la versión {Actualizador.VersionActual}. Preguntando a GitHub cuál es la última…");
        _barra.IsVisible = true;
        _barra.IsIndeterminate = true;
        Botones(("Cerrar", Close, true));
        try
        {
            var publicada = await Actualizador.BuscarAsync();
            _barra.IsVisible = false;
            if (publicada is null)
                Error("No se encontró ninguna versión publicada.");
            else if (Actualizador.EsMasNueva(publicada.Version, Actualizador.VersionActual))
                MostrarNueva(publicada);
            else
            {
                Estado("Ya tienes la última versión", $"Tu versión ({Actualizador.VersionActual}) es la más reciente publicada. No hace falta hacer nada.");
                Botones(("Cerrar", Close, true));
            }
        }
        catch (Exception ex)
        {
            Error("No se pudo comprobar si hay versiones nuevas. Revisa que el ordenador tenga conexión a internet.\n\nDetalle: " + ex.Message);
        }
    }

    private void MostrarNueva(VersionPublicada v)
    {
        _barra.IsVisible = false;
        var tamano = v.Bytes > 0 ? $" (unos {v.Bytes / 1_000_000.0:0} MB)" : "";
        if (v.Instalador is null)
        {
            Estado($"Hay una versión nueva: {v.Version}",
                $"Tienes la {Actualizador.VersionActual}. Esta versión aún no tiene instalador listo; prueba dentro de unos minutos o ábrela en la página de descargas.");
            Botones(("Abrir página de descargas", () => Actualizador.AbrirEnNavegador(v.Pagina), false), ("Cerrar", Close, true));
            return;
        }
        Estado($"Hay una versión nueva: {v.Version}",
            $"Tienes la {Actualizador.VersionActual}. Al pulsar «Actualizar ahora» se descarga{tamano}, el programa se cierra, " +
            "se instala encima y se vuelve a abrir solo. Tus archivos guardados no se tocan.\n\n" +
            "Guarda antes lo que tengas abierto: lo que no esté guardado se perderá al cerrar.");
        Botones(("Ver novedades", () => Actualizador.AbrirEnNavegador(v.Pagina), false),
                 ("Ahora no", Close, false),
                 ("Actualizar ahora", () => _ = DescargarEInstalarAsync(v), true));
    }

    private async Task DescargarEInstalarAsync(VersionPublicada v)
    {
        _cancelar = new CancellationTokenSource();
        Estado($"Descargando la versión {v.Version}…", "Puedes seguir trabajando mientras tanto. Cuando termine se cerrará el programa para instalarla.");
        _barra.IsIndeterminate = false;
        _barra.Value = 0;
        _barra.IsVisible = true;
        var cancelar = _cancelar;
        Botones(("Cancelar", () => cancelar.Cancel(), false));
        try
        {
            var avance = new Progress<double>(p => _barra.Value = p * 100);
            var archivo = await Actualizador.DescargarAsync(v, avance, cancelar.Token);
            Estado("Instalando…", "El programa se cierra ahora y se vuelve a abrir cuando termine la instalación (menos de un minuto).");
            Botones();
            Actualizador.Instalar(archivo);
            await Task.Delay(800);
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch (OperationCanceledException)
        {
            if (IsVisible) MostrarNueva(v);
        }
        catch (Exception ex)
        {
            Error("No se pudo descargar o abrir el instalador.\n\nDetalle: " + ex.Message, v.Pagina);
        }
    }

    private void Error(string texto, string? pagina = null)
    {
        _barra.IsVisible = false;
        Estado("No se pudo actualizar", texto + "\n\nTambién puedes descargar el instalador a mano desde la página de descargas.");
        Botones(("Abrir página de descargas", () => Actualizador.AbrirEnNavegador(pagina ?? Actualizador.PaginaDescargas), false),
                 ("Cerrar", Close, true));
    }

    private void Estado(string titulo, string texto)
    {
        Dispatcher.UIThread.VerifyAccess();
        _titulo.Text = titulo;
        _texto.Text = texto;
    }

    private void Botones(params (string Texto, Action Accion, bool Principal)[] botones)
    {
        _botones.Children.Clear();
        foreach (var (texto, accion, principal) in botones)
        {
            var b = new Button { Content = texto, IsDefault = principal };
            if (principal) b.Classes.Add("accent");
            b.Click += (_, _) => accion();
            _botones.Children.Add(b);
        }
    }
}
