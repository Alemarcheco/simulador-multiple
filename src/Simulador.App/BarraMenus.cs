using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Simulador.App.Tema;
using Simulador.Core.Modules;

namespace Simulador.App;

/// <summary>
/// Barra de menús de arriba (Archivo, Editar, Ver, Herramientas, Ayuda), como en otras aplicaciones.
/// Su contenido sale de las vistas del simulador que se está viendo (<see cref="IMenuSource"/>),
/// así que cambia al cambiar de pestaña o de sub-pestaña (Esquemático, PCB…).
/// </summary>
public sealed class BarraMenus : Border
{
    private readonly Menu _menu = new();
    private readonly Func<Control?> _activeView;
    private readonly IReadOnlyList<MenuItem> _top;
    private readonly MenuItem _help;
    private bool _refreshQueued;

    // Para que le apliquen los estilos de "Border.menubar" (Tema/Estilo.axaml).
    protected override Type StyleKeyOverride => typeof(Border);

    public BarraMenus(Func<Control?> activeView, Action goHome, Window owner, Action? showHelp = null)
    {
        _activeView = activeView;
        Classes.Add("menubar");

        _top = MenuNames.Order.Select(name => new MenuItem { Header = "_" + name }).ToList();
        foreach (var item in _top)
        {
            item.Items.Add(Placeholder());
            item.SubmenuOpened += (_, e) => { if (ReferenceEquals(e.Source, item)) Fill(item); };
            _menu.Items.Add(item);
        }

        _help = new MenuItem { Header = "Ay_uda" };
        var home = new MenuItem { Header = "Ir a la pantalla de inicio" };
        home.Click += (_, _) => goHome();
        var about = new MenuItem { Header = "Acerca de Simulador múltiple…" };
        about.Click += (_, _) => _ = AboutDialog().ShowDialog(owner);
        if (showHelp is not null)
        {
            var guide = new MenuItem { Header = "Ayuda de esta pestaña", InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.F1) };
            guide.Click += (_, _) => showHelp();
            _help.Items.Add(guide);
        }
        _help.Items.Add(home);
        _help.Items.Add(new Separator());
        _help.Items.Add(about);
        _menu.Items.Add(_help);

        Child = new DockPanel
        {
            Children =
            {
                Dock(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(10, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        Logo.Create(20),
                        new TextBlock
                        {
                            Text = "Simulador múltiple",
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brushes.White,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                }),
                _menu,
            },
        };
    }

    private static Control Dock(Control c)
    {
        DockPanel.SetDock(c, Avalonia.Controls.Dock.Left);
        return c;
    }

    /// <summary>Vuelve a mirar qué menús tienen órdenes (se llama al cambiar de pestaña).</summary>
    public void Refresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            var commands = Collect();
            for (var i = 0; i < _top.Count; i++)
                _top[i].IsVisible = i == 0 || commands.Any(c => c.Menu == MenuNames.Order[i]);
        }, DispatcherPriority.Background);
    }

    private List<MenuCommand> Collect()
    {
        var root = _activeView();
        if (root is null) return [];
        var sources = new List<IMenuSource>();
        if (root is IMenuSource self) sources.Add(self);
        sources.AddRange(root.GetVisualDescendants()
            .Where(v => v is IMenuSource && v.IsEffectivelyVisible)
            .Cast<IMenuSource>());
        var list = new List<MenuCommand>();
        foreach (var source in sources.Distinct())
        {
            try { list.AddRange(source.GetMenuCommands()); }
            catch { /* una vista con fallos no debe romper el menú */ }
        }
        return list;
    }

    private void Fill(MenuItem top)
    {
        var name = MenuNames.Order[_top.ToList().IndexOf(top)];
        var commands = Collect().Where(c => c.Menu == name).ToList();
        top.Items.Clear();
        if (commands.Count == 0)
        {
            top.Items.Add(Placeholder());
            return;
        }
        AddItems(top.Items, commands);
    }

    private static void AddItems(ItemCollection target, IEnumerable<MenuCommand> commands)
    {
        foreach (var c in commands)
        {
            if (c.SeparatorBefore && target.Count > 0 && target[^1] is not Separator) target.Add(new Separator());
            target.Add(Build(c));
        }
    }

    private static MenuItem Build(MenuCommand c)
    {
        var item = new MenuItem { Header = c.Header };
        if (c.Shortcut is { Length: > 0 } s)
        {
            try { item.InputGesture = KeyGesture.Parse(s); } catch (ArgumentException) { }
        }
        if (c.ToolTip is { Length: > 0 } tip) ToolTip.SetTip(item, tip);
        if (c.IsEnabled is { } enabled) item.IsEnabled = enabled();
        if (c.Items is { Count: > 0 } children) AddItems(item.Items, children);
        else if (c.Run is { } run) item.Click += (_, _) => run();
        return item;
    }

    private static MenuItem Placeholder() => new()
    {
        Header = "Nada en este menú para la pestaña actual",
        IsEnabled = false,
    };

    private static Window AboutDialog()
    {
        var close = new Button { Content = "Cerrar", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        var w = new Window
        {
            Title = "Acerca de Simulador múltiple",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 14,
                        Children =
                        {
                            Logo.Create(48),
                            new StackPanel
                            {
                                VerticalAlignment = VerticalAlignment.Center,
                                Children =
                                {
                                    new TextBlock { Text = "Simulador múltiple", FontSize = 18, FontWeight = FontWeight.SemiBold },
                                    new TextBlock { Text = "Versión " + (typeof(BarraMenus).Assembly.GetName().Version?.ToString(3) ?? "") },
                                },
                            },
                        },
                    },
                    new TextBlock
                    {
                        Text = "Circuitos y PCB, edificación, instalaciones eléctricas, PLC y microcontroladores, hidráulica, neumática, diseño 3D y banco de pruebas en un solo programa.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    close,
                },
            },
        };
        close.Click += (_, _) => w.Close();
        return w;
    }
}
