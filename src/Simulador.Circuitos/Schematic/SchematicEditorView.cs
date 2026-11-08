using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Simulador.Circuitos.Catalog;
using Simulador.Core.Libraries.Model;
using Simulador.Core.Modules;

namespace Simulador.Circuitos.Schematic;

/// <summary>Pestaña "Esquemático": paleta de componentes, lienzo y panel de propiedades.</summary>
public sealed class SchematicEditorView : DockPanel, IMenuSource
{
    private readonly IModuleHost _host;
    private readonly ComponentCatalog _catalog;
    private readonly SchematicCanvas _canvas;
    private readonly ListBox _palette = new();
    private readonly TextBox _search = new() { Watermark = "Buscar componente…" };
    private readonly StackPanel _properties = new() { Spacing = 6, Margin = new Thickness(10) };
    private readonly TextBox _labelName = new() { Text = "SALIDA", Width = 110, VerticalAlignment = VerticalAlignment.Center };
    private string? _filePath;

    public SchematicCanvas Canvas => _canvas;

    public SchematicEditorView(IModuleHost host, ComponentCatalog catalog)
    {
        _host = host;
        _catalog = catalog;
        _canvas = new SchematicCanvas(catalog);
        _canvas.SelectionChanged += (_, _) => ShowProperties();
        _canvas.DocumentChanged += (_, _) => { if (_canvas.Selected is not null) ShowProperties(); };
        _canvas.Status += (_, msg) => _host.SetStatus(msg);
        _canvas.ToolChanged += (_, _) => { if (_canvas.Tool != SchematicTool.Place) _palette.SelectedItem = null; };
        _catalog.Changed += (_, _) => RefreshPalette();
        _labelName.TextChanged += (_, _) => _canvas.LabelName = string.IsNullOrWhiteSpace(_labelName.Text) ? "RED" : _labelName.Text.Trim();

        ToolTip.SetTip(_liveButton, "Simula el circuito y anima la corriente por los cables (los puntos van más rápido cuanta más corriente). Pulsa otra vez para parar.");
        _liveButton.Click += async (_, _) => { await ToggleLiveAsync(); _canvas.Focus(); };
        _canvas.AnimationChanged += (_, _) => _liveButton.Content = _canvas.Animation is null ? "⚡ Ver la corriente" : "■ Parar la corriente";

        var toolbar = BuildToolbar();
        SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);

        var paletteHelp = new TextBlock
        {
            Text = "Elige un componente y haz clic en el esquema para colocarlo.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
        };
        var left = new DockPanel { Width = 230, Margin = new Thickness(0, 0, 8, 0) };
        SetDock(_search, Dock.Top);
        SetDock(paletteHelp, Dock.Bottom);
        left.Children.Add(_search);
        left.Children.Add(paletteHelp);
        left.Children.Add(_palette);
        SetDock(left, Dock.Left);
        Children.Add(left);

        var right = new Border
        {
            Width = 250,
            Child = new ScrollViewer { Content = _properties },
            BorderThickness = new Thickness(1, 0, 0, 0),
            BorderBrush = Brushes.Gray,
        };
        SetDock(right, Dock.Right);
        Children.Add(right);

        Children.Add(new Border { Child = _canvas, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray });

        _search.TextChanged += (_, _) => RefreshPalette();
        _palette.SelectionChanged += (_, _) =>
        {
            if (_palette.SelectedItem is ListBoxItem { Tag: SymbolDefinition def })
            {
                _canvas.SetTool(SchematicTool.Place, def);
                _canvas.Focus();
            }
        };
        RefreshPalette();
        ShowProperties();
    }

    private Control BuildToolbar()
    {
        Button B(string text, string tip, Action action)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 4) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { action(); _canvas.Focus(); };
            return b;
        }
        Control Sep() => new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(4, 2), Opacity = 0.5 };

        return new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Orientation = Orientation.Horizontal,
            Children =
            {
                B("Seleccionar", "Seleccionar y mover (Esc)", () => { _palette.SelectedItem = null; _canvas.SetTool(SchematicTool.Select); }),
                B("Cable", "Dibujar cables (W)", () => _canvas.SetTool(SchematicTool.Wire)),
                B("Etiqueta", "Etiqueta de red (L): une puntos con el mismo nombre", () => _canvas.SetTool(SchematicTool.Label)),
                _labelName,
                Sep(),
                B("Girar", "Girar 90° (R)", _canvas.RotateSelected),
                B("Espejo", "Reflejar (M)", _canvas.MirrorSelected),
                B("Borrar", "Borrar lo seleccionado (Supr)", _canvas.DeleteSelected),
                Sep(),
                _liveButton,
            },
        };
    }

    private readonly Button _liveButton = new()
    {
        Content = "⚡ Ver la corriente", Padding = new Thickness(10, 4), FontWeight = FontWeight.SemiBold,
        Background = new SolidColorBrush(Color.Parse("#FFE58A")),
    };

    private async Task ToggleLiveAsync()
    {
        if (_canvas.Animation is not null) { _canvas.SetAnimation(null); _canvas.SetOperatingPoint(null); return; }
        var settings = Simulation.LiveRun.Suggest(_canvas.Document);
        _liveButton.IsEnabled = false;
        _host.SetStatus("Simulando…");
        try
        {
            var doc = _canvas.Document;
            var outcome = await Task.Run(() => Simulation.LiveRun.Run(doc, _catalog, settings));
            if (!ReferenceEquals(doc, _canvas.Document)) return;
            _canvas.SetAnimation(outcome.Animation);
            if (outcome.Animation is { IsStatic: true } && outcome.Result is { } r)
                _canvas.SetOperatingPoint(outcome.Animation.Voltages.Where(kv => kv.Key != Connectivity.GroundNet).ToDictionary(kv => kv.Key, kv => kv.Value[0]));
            _host.SetStatus(outcome.Message);
        }
        catch (Simulation.NgSpiceNotFoundException ex)
        {
            _host.SetStatus(ex.Message);
        }
        finally
        {
            _liveButton.IsEnabled = true;
        }
    }

    /// <summary>Órdenes de la barra de menús de arriba (Archivo, Editar, Ver, Herramientas).</summary>
    public IEnumerable<MenuCommand> GetMenuCommands()
    {
        yield return new MenuCommand("Nuevo esquema", New) { ToolTip = "Esquema vacío" };
        yield return new MenuCommand("Abrir…", () => _ = OpenAsync());
        yield return new MenuCommand("Guardar", () => _ = SaveAsync(false)) { Shortcut = "Ctrl+S" };
        yield return new MenuCommand("Guardar como…", () => _ = SaveAsync(true));
        yield return MenuCommand.Submenu("Ejemplos", Examples.All.Select(e => new MenuCommand(e.Item1, () =>
        {
            _filePath = null;
            _canvas.Load(e.Item2(_catalog));
            ShowProperties();
            _host.SetStatus("Ejemplo cargado: " + e.Item1);
        }))) with { SeparatorBefore = true };
        yield return new MenuCommand("Deshacer", _canvas.Undo) { Menu = MenuNames.Edit, Shortcut = "Ctrl+Z" };
        yield return new MenuCommand("Rehacer", _canvas.Redo) { Menu = MenuNames.Edit, Shortcut = "Ctrl+Y" };
        yield return new MenuCommand("Ver todo el esquema", _canvas.ZoomToFit) { Menu = MenuNames.View, Shortcut = "F" };
        yield return new MenuCommand("Netlist SPICE…", ShowNetlist) { Menu = MenuNames.Tools, ToolTip = "Ver la netlist que se enviará al simulador" };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = SaveAsync(false);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void RefreshPalette()
    {
        var f = _search.Text?.Trim() ?? "";
        var items = _catalog.Symbols
            .Where(s => f.Length == 0 ||
                        s.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                        s.Description.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                        s.Library.Contains(f, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Library == ComponentCatalog.BuiltInLibrary ? 0 : 1)
            .ThenBy(s => s.Library, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .Select(s => new ListBoxItem
            {
                Tag = s,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = s.Name, FontWeight = FontWeight.SemiBold },
                        new TextBlock
                        {
                            Text = string.IsNullOrEmpty(s.Description) ? s.Library : $"{s.Description} · {s.Library}",
                            FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                },
            })
            .ToList();
        _palette.ItemsSource = items;
    }

    private void ShowProperties()
    {
        _properties.Children.Clear();
        void Title(string t) => _properties.Children.Add(new TextBlock { Text = t, FontSize = 16, FontWeight = FontWeight.SemiBold });
        void Info(string t) => _properties.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 });
        void Field(string label, string value, Action<string> apply)
        {
            _properties.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 0) });
            var box = new TextBox { Text = value };
            void Commit()
            {
                var v = box.Text ?? "";
                if (v == value) return;
                _canvas.Checkpoint();
                apply(v);
                value = v;
                _canvas.Changed();
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); _canvas.Focus(); e.Handled = true; } };
            _properties.Children.Add(box);
        }

        switch (_canvas.Selected)
        {
            case PlacedSymbol s:
                Title(s.Symbol?.Name ?? s.SymbolName);
                if (s.Symbol is { } def && def.Description.Length > 0) Info(def.Description);
                Field("Referencia", s.Reference, v => s.Reference = v.Trim());
                Field("Valor", s.Value, v => s.Value = v.Trim());
                Field("Huella (PCB)", s.Footprint.Length > 0 ? s.Footprint : s.Symbol?.Footprint ?? "", v => s.Footprint = v.Trim());
                Info("Valores admitidos: 4.7k, 100n, 10u, 2meg… (sufijos SPICE: f p n u m k meg g).");
                var pins = SymbolTransform.Pins(s).ToList();
                if (pins.Count > 0)
                {
                    var lines = pins.Select(p =>
                    {
                        var net = _canvas.Connectivity.NetOf(s, p.Pin.Number)?.Name ?? "—";
                        var name = p.Pin.Name is "~" or "" ? "" : $" ({p.Pin.Name})";
                        return $"Pin {p.Pin.Number}{name}: {net}";
                    });
                    _properties.Children.Add(new TextBlock { Text = "Conexiones", Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeight.SemiBold });
                    Info(string.Join("\n", lines));
                }
                break;
            case NetLabel l:
                Title("Etiqueta de red");
                Field("Nombre", l.Name, v => l.Name = v.Trim());
                Info("Todos los puntos con la misma etiqueta quedan conectados. GND equivale a masa.");
                break;
            case Wire w:
                Title("Cable");
                Info(FormattableString.CurrentCulture($"De ({w.A.X:0.##}, {w.A.Y:0.##}) a ({w.B.X:0.##}, {w.B.Y:0.##}) mm"));
                break;
            default:
                Title("Esquema");
                Field("Título", _canvas.Document.Title, v => _canvas.Document.Title = v);
                Info($"{_canvas.Document.Symbols.Count(s => s.Symbol?.IsPower != true)} componentes · {_canvas.Connectivity.Nets.Count} redes");
                Info("Atajos: R girar · M espejo · W cable · L etiqueta · Supr borrar · Ctrl+Z deshacer · F ajustar vista · rueda zoom · botón derecho arrastrar para moverte.");
                break;
        }
    }

    private void New()
    {
        _filePath = null;
        _canvas.Load(new SchematicDocument());
        ShowProperties();
        _host.SetStatus("Esquema nuevo.");
    }

    private async Task OpenAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir esquema",
            FileTypeFilter = [new FilePickerFileType("Esquema") { Patterns = ["*" + SchematicDocument.FileExtension] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            _canvas.Load(SchematicDocument.FromJson(await File.ReadAllTextAsync(path)));
            _filePath = path;
            ShowProperties();
            _host.SetStatus("Abierto: " + path);
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Text.Json.JsonException)
        {
            _host.SetStatus("No se pudo abrir el esquema: " + ex.Message);
        }
    }

    private async Task SaveAsync(bool saveAs)
    {
        if (saveAs || _filePath is null)
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar esquema",
                SuggestedFileName = _canvas.Document.Title + SchematicDocument.FileExtension,
                DefaultExtension = SchematicDocument.FileExtension.TrimStart('.'),
                FileTypeChoices = [new FilePickerFileType("Esquema") { Patterns = ["*" + SchematicDocument.FileExtension] }],
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            _filePath = path;
        }
        try
        {
            await File.WriteAllTextAsync(_filePath, _canvas.Document.ToJson());
            _canvas.IsDirty = false;
            _host.SetStatus("Guardado: " + _filePath);
        }
        catch (IOException ex)
        {
            _host.SetStatus("No se pudo guardar: " + ex.Message);
        }
    }

    private void ShowNetlist()
    {
        var netlist = SpiceNetlister.Build(_canvas.Document, _catalog, [".op"]);
        var text = netlist.Warnings.Count == 0
            ? netlist.Text
            : "* Avisos:\n" + string.Join("\n", netlist.Warnings.Select(w => "*  - " + w)) + "\n\n" + netlist.Text;
        var window = new Window
        {
            Title = "Netlist SPICE",
            Width = 640,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                TextWrapping = TextWrapping.NoWrap,
            },
        };
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }
}
