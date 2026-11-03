using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Simulador.Core.Modules;

namespace Simulador.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Default; // sigue el modo claro/oscuro de Windows
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
