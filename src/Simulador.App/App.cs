using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Simulador.Core.Modules;

namespace Simulador.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        // Tema Fluent con el azul de la aplicación como color de acento (botones activos, selección, casillas).
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources
        {
            Accent = Color.Parse("#1F6FEB"),
            RegionColor = Color.Parse("#FFFFFF"),
        };
        Styles.Add(fluent);
        // Aspecto propio común a todos los simuladores: cabecera, botones, pestañas, barra de estado.
        Styles.Add(new StyleInclude(new Uri("avares://SimuladorMultiple/"))
        {
            Source = new Uri("avares://SimuladorMultiple/Tema/Estilo.axaml"),
        });
        // Siempre en modo claro: los planos y esquemas se dibujan sobre "papel" blanco y
        // en modo oscuro de Windows quedaban textos y paneles mezclados (claro sobre oscuro).
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var errors = new List<string>();
            var modules = ModuleLoader.LoadFromDirectory(
                AppContext.BaseDirectory,
                (source, ex) => errors.Add($"{Path.GetFileName(source)}: {ex.Message}"));
            desktop.MainWindow = new MainWindow(modules, errors);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
