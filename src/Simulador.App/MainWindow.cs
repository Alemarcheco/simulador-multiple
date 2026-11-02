using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Core.Modules;

namespace Simulador.App;

/// <summary>Ventana principal: una pestaña por simulador; cada vista se crea la primera vez que se abre.</summary>
public sealed class MainWindow : Window, IModuleHost
{
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center };

    public IReadOnlyList<IModule> Modules { get; }

    public MainWindow(IReadOnlyList<IModule> modules, IReadOnlyList<string> loadErrors)
    {
        Modules = modules;
        Title = "Simulador múltiple";
        Width = 1280;
        Height = 800;
        MinWidth = 800;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var statusBar = new Border
        {
            Child = _status,
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = Brushes.Gray,
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);

        var root = new DockPanel();
        root.Children.Add(statusBar);
        root.Children.Add(modules.Count == 0 ? EmptyState() : BuildTabs(modules));
        Content = root;

        SetStatus(loadErrors.Count == 0
            ? $"{modules.Count} simulador(es) cargado(s)."
            : $"Avisos al cargar módulos: {string.Join(" | ", loadErrors)}");
    }

    private TabControl BuildTabs(IReadOnlyList<IModule> modules)
    {
        var tabs = new TabControl { Padding = new Thickness(0) };
        foreach (var module in modules)
        {
            tabs.Items.Add(new TabItem
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = module.Icon, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = module.Name, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                Tag = module,
            });
        }
        tabs.SelectionChanged += (_, _) => EnsureContent(tabs.SelectedItem as TabItem);
        tabs.SelectedIndex = 0;
        EnsureContent(tabs.SelectedItem as TabItem);
        return tabs;
    }

    private void EnsureContent(TabItem? tab)
    {
        if (tab is null || tab.Content is not null || tab.Tag is not IModule module) return;
        try
        {
            tab.Content = module.CreateRootView(this);
        }
        catch (Exception ex)
        {
            tab.Content = new TextBlock
            {
                Text = $"No se pudo abrir «{module.Name}»:\n{ex.Message}",
                Margin = new Thickness(20),
                TextWrapping = TextWrapping.Wrap,
            };
        }
    }

    private static Control EmptyState() => new TextBlock
    {
        Text = "No se encontró ningún simulador. Comprueba que los archivos Simulador.*.dll están junto al ejecutable.",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public string GetDataDirectory(string moduleId)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimuladorMultiple", moduleId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void SetStatus(string text) => _status.Text = text;
}
