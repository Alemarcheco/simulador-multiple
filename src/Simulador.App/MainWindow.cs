using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Simulador.App.Tema;
using Simulador.Core.Modules;

namespace Simulador.App;

/// <summary>Ventana principal: una pestaña por simulador; cada vista se crea la primera vez que se abre.</summary>
public sealed class MainWindow : Window, IModuleHost
{
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(12, 5),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        MaxLines = 1,
    };

    public IReadOnlyList<IModule> Modules { get; }

    public MainWindow(IReadOnlyList<IModule> modules, IReadOnlyList<string> loadErrors)
    {
        Modules = modules;
        Title = "Simulador múltiple";
        Icon = LoadIcon();
        // Ventana normal de Windows: barra de título con minimizar, maximizar/restaurar y cerrar,
        // redimensionable, y abierta maximizada dentro del área de trabajo (sin tapar la barra de tareas).
        SystemDecorations = SystemDecorations.Full;
        ExtendClientAreaToDecorationsHint = false;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FitToScreen();
        WindowState = WindowState.Maximized;

        var statusBar = new Border { Child = _status };
        statusBar.Classes.Add("statusbar");
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
        // Pestañas principales dentro de la cabecera azul marino (ver Tema/Estilo.axaml, clase "shell").
        var tabs = new TabControl();
        tabs.Classes.Add("shell");
        tabs.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new WrapPanel());

        void Open(IModule module)
        {
            foreach (var item in tabs.Items)
                if (item is TabItem t && ReferenceEquals(t.Tag, module)) { tabs.SelectedItem = t; return; }
        }

        // La primera pestaña es el propio logotipo: lleva a la pantalla de inicio.
        var home = new TabItem { Header = Brand(), Content = new HomeView(modules, Open) };
        home.Classes.Add("brand");
        ToolTip.SetTip(home, "Inicio");
        tabs.Items.Add(home);
        foreach (var module in modules)
        {
            tabs.Items.Add(new TabItem
            {
                Header = TabHeader(module.Icon, module.Name),
                Tag = module,
            });
        }
        tabs.SelectionChanged += (_, _) => EnsureContent(tabs.SelectedItem as TabItem);
        tabs.SelectedIndex = 0;
        return tabs;
    }

    private static Control TabHeader(string icon, string name) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new TextBlock { Text = icon, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    /// <summary>Logotipo y nombre de la aplicación, a la izquierda de la cabecera.</summary>
    private static Control Brand() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Margin = new Thickness(2, 0, 6, 0),
        Children =
        {
            Logo.Create(26),
            new TextBlock
            {
                Text = "Simulador múltiple",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    };

    private static WindowIcon? LoadIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://SimuladorMultiple/Assets/icono.ico"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null; // sin icono propio, Windows muestra el genérico
        }
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

    public void SetStatus(string text)
    {
        // Una sola línea (los avisos largos no empujan el contenido); el texto completo, al pasar el ratón.
        var firstLine = text.Split('\n', 2)[0].TrimEnd();
        _status.Text = firstLine.Length < text.TrimEnd().Length ? firstLine + " …" : firstLine;
        ToolTip.SetTip(_status, text.Length > firstLine.Length || text.Length > 120 ? text : null);
    }
}
