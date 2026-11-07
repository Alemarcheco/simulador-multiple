using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Core.Modules;
using Simulador.Help;

namespace Simulador.App;

/// <summary>Ventana principal: una pestaña por simulador; cada vista se crea la primera vez que se abre.</summary>
public sealed class MainWindow : Window, IModuleHost
{
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center };
    private TabControl? _tabs;

    public IReadOnlyList<IModule> Modules { get; }

    public MainWindow(IReadOnlyList<IModule> modules, IReadOnlyList<string> loadErrors)
    {
        Modules = modules;
        Title = "Simulador múltiple";
        // Ventana normal de Windows: barra de título con minimizar, maximizar/restaurar y cerrar,
        // redimensionable, y abierta maximizada dentro del área de trabajo (sin tapar la barra de tareas).
        SystemDecorations = SystemDecorations.Full;
        ExtendClientAreaToDecorationsHint = false;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FitToScreen();
        WindowState = WindowState.Maximized;

        // Ayuda: F1 en cualquier sitio (también dentro de un módulo) o el botón de la barra de estado.
        AddHandler(KeyDownEvent, OnHelpKey, RoutingStrategies.Tunnel);
        var helpButton = new Button
        {
            Content = "❓ Ayuda (F1)",
            Padding = new Thickness(10, 2),
            Margin = new Thickness(4, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(helpButton, "Guías de cada pestaña, preguntas frecuentes y buscador");
        helpButton.Click += (_, _) => ShowHelp();
        DockPanel.SetDock(helpButton, Dock.Right);
        var statusContent = new DockPanel();
        statusContent.Children.Add(helpButton);
        statusContent.Children.Add(_status);

        var statusBar = new Border
        {
            Child = statusContent,
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

    /// <summary>
    /// Ajusta el tamaño "restaurado" y los mínimos a la pantalla, para que en portátiles pequeños
    /// o con escala de 125–150 % la ventana nunca sea más grande que la pantalla y los botones
    /// de la barra de título queden siempre a la vista.
    /// </summary>
    private void FitToScreen()
    {
        const double preferredWidth = 1280, preferredHeight = 800;
        const double preferredMinWidth = 800, preferredMinHeight = 500;

        var screens = Screens;
        var screen = screens?.ScreenFromWindow(this) ?? screens?.Primary;
        if (screen is null)
        {
            Width = preferredWidth;
            Height = preferredHeight;
            MinWidth = preferredMinWidth;
            MinHeight = preferredMinHeight;
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var availableWidth = screen.WorkingArea.Width / scaling;
        var availableHeight = screen.WorkingArea.Height / scaling;

        MinWidth = Math.Min(preferredMinWidth, availableWidth * 0.6);
        MinHeight = Math.Min(preferredMinHeight, availableHeight * 0.6);
        Width = Math.Max(MinWidth, Math.Min(preferredWidth, availableWidth * 0.9));
        Height = Math.Max(MinHeight, Math.Min(preferredHeight, availableHeight * 0.9));
    }

    private TabControl BuildTabs(IReadOnlyList<IModule> modules)
    {
        var tabs = new TabControl { Padding = new Thickness(0) };
        _tabs = tabs;
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

    private void OnHelpKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F1 || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = true;
        ShowHelp();
    }

    /// <summary>Abre la ayuda en la guía de la pestaña activa (y de su sub-pestaña visible, si la guía la tiene).</summary>
    private void ShowHelp()
    {
        var tab = _tabs?.SelectedItem as TabItem;
        HelpWindow.ShowFor(this, (tab?.Tag as IModule)?.Id, tab?.Content as Control);
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
