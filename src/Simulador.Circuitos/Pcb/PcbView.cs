using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Schematic;
using Simulador.Circuitos.Simulation;
using Simulador.Core.Modules;

namespace Simulador.Circuitos.Pcb;

/// <summary>Pestaña "PCB": pasar el esquema a la placa, colocar componentes, rutear pistas y comprobar reglas.</summary>
public sealed class PcbView : DockPanel, IMenuSource
{
    private readonly IModuleHost _host;
    private readonly ComponentCatalog _catalog;
    private readonly SchematicCanvas _schematic;
    private readonly PcbCanvas _canvas;
    private readonly StackPanel _properties = new() { Spacing = 6, Margin = new Thickness(10) };
    private readonly TextBlock _counter = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
    private string? _filePath;

    public PcbCanvas Canvas => _canvas;

    public PcbView(IModuleHost host, ComponentCatalog catalog, SchematicCanvas schematic)
    {
        _host = host;
        _catalog = catalog;
        _schematic = schematic;
        _canvas = new PcbCanvas(catalog);
        _canvas.Status += (_, s) => _host.SetStatus(s);
        _canvas.SelectionChanged += (_, _) => ShowProperties();
        _canvas.DocumentChanged += (_, _) => { UpdateCounter(); ShowProperties(); };

        var toolbar = BuildToolbar();
        SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);

        var right = new Border
        {
            Width = 270,
            Child = new ScrollViewer { Content = _properties },
            BorderThickness = new Thickness(1, 0, 0, 0),
            BorderBrush = Brushes.Gray,
        };
        SetDock(right, Dock.Right);
        Children.Add(right);
        Children.Add(new Border { Child = _canvas, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray });

        UpdateCounter();
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
            Children =
            {
                B("⟳ Actualizar desde esquema", "Añade los componentes y conexiones del esquema a la placa", SyncFromSchematic),
                Sep(),
                B("Seleccionar", "Seleccionar y mover componentes (Esc)", () => _canvas.SetTool(PcbTool.Select)),
                B("Pista", "Dibujar pistas de cobre (X)", () => _canvas.SetTool(PcbTool.Track)),
                B("Girar", "Girar 90° (R)", _canvas.RotateSelected),
                B("Borrar", "Borrar lo seleccionado (Supr)", _canvas.DeleteSelected),
                Sep(),
                B("✔ Comprobar (DRC)", "Comprobar separaciones, cortos y conexiones pendientes", RunDrc),
                Sep(),
                B("🧩 Colocar", "Coloca los componentes de forma compacta para que las pistas no se crucen (borra las pistas)", () => _ = AutomaticAsync(place: true, route: false)),
                B("✨ Autorrutear", "Traza solas las pistas que faltan; si alguna no cabe, propone un puente de cable", () => _ = AutomaticAsync(place: false, route: true)),
                B("▣ Ajustar placa", "Recorta la placa al tamaño justo de los componentes", FitBoard),
                _counter,
            },
        };
    }

    /// <summary>Órdenes de la barra de menús de arriba.</summary>
    public IEnumerable<MenuCommand> GetMenuCommands()
    {
        yield return new MenuCommand("Nueva placa", () => { _filePath = null; _canvas.Load(new PcbDocument()); }) { ToolTip = "Placa vacía" };
        yield return new MenuCommand("Abrir placa…", () => _ = OpenAsync());
        yield return new MenuCommand("Guardar placa", () => _ = SaveAsync(false));
        yield return new MenuCommand("Guardar placa como…", () => _ = SaveAsync(true));
        yield return new MenuCommand("Deshacer", _canvas.Undo) { Menu = MenuNames.Edit, Shortcut = "Ctrl+Z" };
        yield return new MenuCommand("Rehacer", _canvas.Redo) { Menu = MenuNames.Edit, Shortcut = "Ctrl+Y" };
        yield return new MenuCommand("Ver toda la placa", _canvas.ZoomToFit) { Menu = MenuNames.View, Shortcut = "F" };
    }

    private void UpdateCounter()
    {
        int n = _canvas.RatsnestLines.Count;
        _counter.Text = _canvas.Document.Footprints.Count == 0 ? "" : n == 0 ? "Todo conectado ✓" : $"Conexiones sin rutear: {n}";
    }

    private void SyncFromSchematic()
    {
        _canvas.Checkpoint();
        var report = PcbSync.Update(_canvas.Document, _schematic.Document, _catalog);
        bool first = report.Added > 0 && report.Added == _canvas.Document.Footprints.Count;
        _canvas.Changed();
        if (first) _canvas.ZoomToFit();
        _host.SetStatus($"Placa actualizada: {report.Added} añadidos, {report.Updated} actualizados, {report.Removed} quitados." +
                        (report.Warnings.Count > 0 ? " Avisos: " + string.Join(" ", report.Warnings) : ""));
        ShowProperties(report.Warnings);
    }

    private bool _busy;

    /// <summary>Coloca y/o rutea en segundo plano sobre una copia, y aplica el resultado de una vez (se puede deshacer).</summary>
    private async Task AutomaticAsync(bool place, bool route)
    {
        if (_busy || _canvas.Document.Footprints.Count == 0)
        {
            if (!_busy) _host.SetStatus("Primero pasa los componentes con «Actualizar desde esquema».");
            return;
        }
        _busy = true;
        _host.SetStatus(place ? "Buscando la colocación más compacta…" : "Trazando pistas…");
        try
        {
            var copy = PcbDocument.FromJson(_canvas.Document.ToJson());
            copy.ResolveFootprints(_catalog.FindFootprint);
            AutoRouteReport? report = null;
            await Task.Run(() =>
            {
                if (place) AutoPlacer.Place(copy);
                if (route) report = AutoRouter.Route(copy, allowJumpers: copy.SingleSided);
            });

            _canvas.Checkpoint();
            var doc = _canvas.Document;
            foreach (var f in doc.Footprints)
                if (copy.Footprints.FirstOrDefault(c => c.Id == f.Id) is { } c) { f.Position = c.Position; f.Rotation = c.Rotation; }
            doc.Tracks = copy.Tracks;
            doc.BoardWidth = copy.BoardWidth;
            doc.BoardHeight = copy.BoardHeight;
            _canvas.Changed();
            _canvas.ZoomToFit();

            if (place) _host.SetStatus($"Componentes colocados en {doc.BoardWidth:0.#} × {doc.BoardHeight:0.#} mm. Ahora pulsa «Autorrutear».");
            else if (report is not null)
                _host.SetStatus(report.Failed > 0
                    ? $"Rutadas {report.Routed} conexiones; {report.Failed} no caben ({string.Join(", ", report.FailedNets)}). Mueve o gira componentes y vuelve a probar."
                    : report.Jumpers > 0
                        ? $"Todo conectado con {report.Jumpers} puente(s) de cable (líneas azules discontinuas): suéldalos por arriba con un trozo de cable."
                        : "Todo conectado ✓");
            ShowProperties();
        }
        finally
        {
            _busy = false;
        }
    }

    private void FitBoard()
    {
        if (_canvas.Document.Footprints.Count == 0) return;
        _canvas.Checkpoint();
        double saved = AutoRouter.FitBoard(_canvas.Document);
        _canvas.Changed();
        _canvas.ZoomToFit();
        _host.SetStatus($"Placa ajustada a {_canvas.Document.BoardWidth:0.#} × {_canvas.Document.BoardHeight:0.#} mm" + (saved > 0 ? $" ({saved / 100:0.#} cm² menos)." : "."));
        ShowProperties();
    }

    private void RunDrc()
    {
        var v = _canvas.RunDrc();
        _host.SetStatus(v.Count == 0 ? "Comprobación correcta: sin errores." : $"Comprobación: {v.Count} problema(s), marcados en rojo.");
        ShowProperties();
    }

    private void ShowProperties(IReadOnlyList<string>? syncWarnings = null)
    {
        _properties.Children.Clear();
        void Title(string t) => _properties.Children.Add(new TextBlock { Text = t, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        void Info(string t) => _properties.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 });
        void Field(string label, string value, Func<string, bool> apply)
        {
            _properties.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 0) });
            var box = new TextBox { Text = value };
            void Commit()
            {
                var v = box.Text ?? "";
                if (v == value) return;
                _canvas.Checkpoint();
                if (apply(v)) { value = v; _canvas.Changed(); }
                else { _canvas.Undo(); box.Text = value; _host.SetStatus($"Valor no válido: {v}"); }
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            _properties.Children.Add(box);
        }
        bool Mm(string text, Action<double> set, double min = 0.05)
        {
            var d = EngineeringFormat.ParseSpice(text.Replace(',', '.').Replace("mm", ""));
            if (d is null || d < min) return false;
            set(d.Value);
            return true;
        }
        string F(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);
        var doc = _canvas.Document;

        switch (_canvas.Selected)
        {
            case PcbFootprint f:
                Title(f.Reference);
                Info($"{f.Value} · {f.FootprintName}");
                Field("Huella", f.FootprintName, v =>
                {
                    var def = _catalog.FindFootprint(v.Trim());
                    if (def is null) return false;
                    f.FootprintName = def.FullName;
                    f.Definition = def;
                    return true;
                });
                Field("Rotación (°)", F(f.Rotation), v => double.TryParse(v, NumberStyles.Float, CultureInfo.CurrentCulture, out var r) && (f.Rotation = r) == r);
                Info(string.Join("\n", FootprintTransform.Pads(f).Select(p => $"Pad {p.Pad.Number}: {(p.Net.Length == 0 ? "—" : p.Net == "0" ? "GND" : p.Net)}")));
                break;
            case PcbTrack t:
                Title("Pista");
                Info($"Red {(t.Net.Length == 0 ? "(sin red)" : t.Net == "0" ? "GND" : t.Net)} · cara {(t.Layer == Layers.Bottom ? "inferior" : "superior")}");
                Field("Ancho (mm)", F(t.Width), v => Mm(v, w => t.Width = w, 0.15));
                break;
            default:
                Title("Placa");
                Field("Ancho (mm)", F(doc.BoardWidth), v => Mm(v, w => doc.BoardWidth = w, 5));
                Field("Alto (mm)", F(doc.BoardHeight), v => Mm(v, h => doc.BoardHeight = h, 5));
                Title("Reglas");
                Field("Ancho de pista (mm)", F(doc.Rules.TrackWidth), v => Mm(v, w => doc.Rules.TrackWidth = w, 0.15));
                Field("Separación mínima (mm)", F(doc.Rules.Clearance), v => Mm(v, c => doc.Rules.Clearance = c, 0.1));
                Info("Para transferencia térmica: pistas de 0,8 mm o más y separación de 0,5 mm o más. Placa de una cara: las pistas van por abajo.");
                Info("Atajos: X pista · R girar · Supr borrar · Espacio cambia la esquina · F ajustar · rueda zoom · botón derecho arrastrar para moverte.");
                break;
        }

        if (syncWarnings is { Count: > 0 })
        {
            Title("Avisos al actualizar");
            Info(string.Join("\n", syncWarnings));
        }
        if (_canvas.Violations is { } drc)
        {
            Title(drc.Count == 0 ? "DRC: sin errores ✓" : $"DRC: {drc.Count} problema(s)");
            foreach (var group in drc.GroupBy(v => v.Message).Take(30))
                Info("• " + group.Key + (group.Count() > 1 ? $" (×{group.Count()})" : ""));
        }
    }

    private async Task OpenAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir placa",
            FileTypeFilter = [new FilePickerFileType("Placa") { Patterns = ["*" + PcbDocument.FileExtension] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            _canvas.Load(PcbDocument.FromJson(await File.ReadAllTextAsync(path)));
            _filePath = path;
            _host.SetStatus("Abierta: " + path);
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Text.Json.JsonException)
        {
            _host.SetStatus("No se pudo abrir la placa: " + ex.Message);
        }
    }

    private async Task SaveAsync(bool saveAs)
    {
        if (saveAs || _filePath is null)
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar placa",
                SuggestedFileName = _canvas.Document.Title + PcbDocument.FileExtension,
                DefaultExtension = PcbDocument.FileExtension.TrimStart('.'),
                FileTypeChoices = [new FilePickerFileType("Placa") { Patterns = ["*" + PcbDocument.FileExtension] }],
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            _filePath = path;
        }
        try
        {
            await File.WriteAllTextAsync(_filePath, _canvas.Document.ToJson());
            _canvas.IsDirty = false;
            _host.SetStatus("Guardada: " + _filePath);
        }
        catch (IOException ex)
        {
            _host.SetStatus("No se pudo guardar: " + ex.Message);
        }
    }
}
