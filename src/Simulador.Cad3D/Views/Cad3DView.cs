using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Export;
using Simulador.Cad3D.Geometry.Model;
using Simulador.Cad3D.Geometry.Templates;
using Simulador.Cad3D.Rendering;
using Simulador.Core.Modules;

namespace Simulador.Cad3D.Views;

/// <summary>Vista principal del módulo: barra de herramientas, árbol de piezas y operaciones, visor 3D y propiedades.</summary>
internal sealed class Cad3DView : DockPanel
{
    private readonly IModuleHost _host;
    private readonly IGeometryKernel _kernel;
    private readonly DocumentEvaluator _evaluator;
    private readonly ViewportHost _viewport = new();
    private bool _pendingFit;
    private readonly PropertyEditor _properties = new();
    private readonly ListBox _parts = new() { MinHeight = 80 };
    private readonly ListBox _features = new();
    private readonly TextBlock _info = new() { Opacity = 0.8, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4) };
    private readonly DispatcherTimer _rebuildTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dictionary<Part, Mesh?> _meshes = new();
    private CadDocument _doc = new();
    private string? _path;
    private int _version;
    private bool _refreshing;

    public Cad3DView(IModuleHost host)
    {
        _host = host;
        _kernel = KernelFactory.CreateBest(msg => host.SetStatus($"Diseño 3D: {msg}"));
        _evaluator = new DocumentEvaluator(_kernel);
        _rebuildTimer.Tick += (_, _) => { _rebuildTimer.Stop(); _ = RebuildAsync(); };
        _properties.Changed += ScheduleRebuild;
        _properties.Renamed += RefreshLists;
        _properties.Refresh += OnFeatureSelected;

        var toolbar = BuildToolbar();
        SetDock(toolbar, Dock.Top);
        Children.Add(toolbar);

        var left = BuildLeftPanel();
        SetDock(left, Dock.Left);
        Children.Add(left);

        var right = new Border { Width = 280, Child = _properties, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1, 0, 0, 0) };
        SetDock(right, Dock.Right);
        Children.Add(right);

        var status = new Border { Child = _info, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 1, 0, 0) };
        SetDock(status, Dock.Bottom);
        Children.Add(status);

        Children.Add(_viewport);

        _parts.SelectionChanged += (_, _) => { if (!_refreshing) OnPartSelected(); };
        _features.SelectionChanged += (_, _) => { if (!_refreshing) OnFeatureSelected(); };

        _viewport.RendererChanged += name => { _ = RebuildAsync(); };
        LoadDocument(Examples.ElectronicsBox(_kernel is IExactKernel), null);
    }

    // ---------- Construcción de la interfaz ----------

    private Control BuildToolbar()
    {
        Button B(string text, Func<Task> action, string? tip = null)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
            if (tip is not null) ToolTip.SetTip(b, tip);
            b.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex) { _host.SetStatus($"Diseño 3D: {ex.Message}"); }
            };
            return b;
        }
        Button S(string text, Action action, string? tip = null) => B(text, () => { action(); return Task.CompletedTask; }, tip);

        return new WrapPanel
        {
            Margin = new Thickness(8),
            Orientation = Orientation.Horizontal,
            Children =
            {
                S("Nuevo", () => LoadDocument(new CadDocument { Parts = { new Part { Name = "Pieza 1" } } }, null)),
                B("Abrir…", OpenAsync),
                B("Guardar…", SaveAsync),
                new Separator { Width = 12 },
                ExamplesButton(),
                B("Caja con tapa…", NewEnclosureAsync, "Encapsulado para electrónica: caja, tapa con reborde, postes y ventilación"),
                B("Caja para placa…", BoxForBoardAsync, "Abre una placa (.placa) del módulo de circuitos y crea una caja a su medida"),
                new Separator { Width = 12 },
                B("Exportar STL…", ExportStlAsync, "Todas las piezas visibles en un único STL"),
                B("Exportar 3MF…", Export3mfAsync, "Una pieza por objeto, con nombre y color"),
                B("Exportar STEP…", ExportStepAsync, "Geometría exacta para otros programas CAD (necesita OpenCASCADE)"),
                B("Importar STEP…", ImportStepAsync, "Añade un modelo STEP a la pieza seleccionada (p. ej. modelos 3D de KiCad)"),
                new Separator { Width = 12 },
                S("Encuadrar", _viewport.FitAll),
                S("Iso", () => _viewport.SetView(-55, 30)),
                S("Frente", () => _viewport.SetView(-90, 0)),
                S("Arriba", () => _viewport.SetView(-90, 89)),
                S("Lado", () => _viewport.SetView(0, 0)),
            },
        };
    }

    private Control BuildLeftPanel()
    {
        Button Small(string text, Action action, string tip)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 2), MinWidth = 30, Margin = new Thickness(0, 0, 4, 4) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => action();
            return b;
        }

        var addPart = Small("+", AddPart, "Añadir pieza");
        var delPart = Small("−", DeletePart, "Eliminar pieza");

        var addMenu = new WrapPanel
        {
            Children =
            {
                Small("Caja", () => AddFeature(new BoxFeature { Name = "Caja" }), "Añadir caja"),
                Small("Redondeada", () => AddFeature(new RoundedBoxFeature { Name = "Caja redondeada" }), "Caja con esquinas verticales redondeadas"),
                Small("Cilindro", () => AddFeature(new CylinderFeature { Name = "Cilindro" }), "Cilindro o cono"),
                Small("Esfera", () => AddFeature(new SphereFeature { Name = "Esfera" }), "Añadir esfera"),
                Small("Extrusión", () => AddFeature(new ExtrudeFeature { Name = "Extrusión" }), "Perfil 2D libre extruido"),
                Small("Agujero", () => AddFeature(new CylinderFeature { Name = "Agujero", Mode = CombineMode.Hueco, Diameter = 3, Height = 30, Position = new Vec3(0, 0, -5), Segments = 32 }), "Cilindro que resta material"),
                Small("Revolución", () => AddFeature(new RevolveFeature { Name = "Revolución" }), "Perfil girado alrededor de Z (necesita OpenCASCADE)"),
                Small("Redondeo", () => AddFeature(new FilletFeature { Name = "Redondeo" }), "Redondea aristas de lo anterior (necesita OpenCASCADE)"),
                Small("Chaflán", () => AddFeature(new ChamferFeature { Name = "Chaflán" }), "Achaflana aristas de lo anterior (necesita OpenCASCADE)"),
                Small("Vaciado", () => AddFeature(new ShellFeature { Name = "Vaciado" }), "Vacía lo anterior dejando la parte de arriba abierta (necesita OpenCASCADE)"),
                Small("Repetir", () => AddFeature(new PatternFeature { Name = "Repetición" }), "Repite la operación anterior en fila, en rejilla o en círculo"),
                Small("Simetría", () => AddFeature(new MirrorFeature { Name = "Simetría" }), "Añade la imagen en espejo de lo anterior"),
                Small("Texto", () => AddFeature(new TextFeature { Name = "Texto" }), "Letras en relieve o grabadas (modo Hueco)"),
            },
        };

        var featureButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                Small("↑", () => MoveFeature(-1), "Subir en el historial"),
                Small("↓", () => MoveFeature(1), "Bajar en el historial"),
                Small("⧉", DuplicateFeature, "Duplicar"),
                Small("🗑", DeleteFeature, "Eliminar"),
            },
        };

        var stack = new DockPanel { Margin = new Thickness(8, 0, 8, 8) };
        var top = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new DockPanel
                {
                    Children = { Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { addPart, delPart } }, Dock.Right), new TextBlock { Text = "Piezas", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } },
                },
                _parts,
                new TextBlock { Text = "Operaciones (en orden)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) },
                addMenu,
                featureButtons,
            },
        };
        SetDock(top, Dock.Top);
        stack.Children.Add(top);
        stack.Children.Add(_features);
        return new Border { Width = 260, Child = stack, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 1, 0) };
    }

    private static Control Docked(Control c, Dock dock) { SetDock(c, dock); return c; }

    private Control ExamplesButton()
    {
        var menu = new MenuFlyout();
        foreach (var ex in Examples.All)
        {
            var item = new MenuItem { Header = ex.Name };
            ToolTip.SetTip(item, ex.Description);
            item.Click += (_, _) =>
            {
                LoadDocument(ex.Create(_kernel is IExactKernel), null);
                _host.SetStatus($"Ejemplo: {ex.Name}. {ex.Description}");
            };
            menu.Items.Add(item);
        }
        var b = new Button { Content = "Ejemplos ▾", Flyout = menu, Margin = new Thickness(0, 0, 6, 6) };
        ToolTip.SetTip(b, "Piezas prediseñadas para abrir, cambiar e imprimir");
        return b;
    }

    // ---------- Documento ----------

    private void LoadDocument(CadDocument doc, string? path)
    {
        _doc = doc;
        _path = path;
        _meshes.Clear();
        RefreshLists();
        if (_doc.Parts.Count > 0) _parts.SelectedIndex = 0;
        OnPartSelected();
        _ = RebuildAsync(fit: true);
    }

    private Part? CurrentPart => _parts.SelectedIndex >= 0 && _parts.SelectedIndex < _doc.Parts.Count ? _doc.Parts[_parts.SelectedIndex] : null;

    private Feature? CurrentFeature =>
        CurrentPart is { } p && _features.SelectedIndex >= 0 && _features.SelectedIndex < p.Features.Count ? p.Features[_features.SelectedIndex] : null;

    private void RefreshLists()
    {
        _refreshing = true;
        int partIdx = _parts.SelectedIndex, featIdx = _features.SelectedIndex;
        _parts.ItemsSource = _doc.Parts.Select(p => (p.Visible ? "● " : "○ ") + p.Name).ToList();
        _parts.SelectedIndex = Math.Min(Math.Max(partIdx, _doc.Parts.Count > 0 ? 0 : -1), _doc.Parts.Count - 1);
        var part = CurrentPart;
        _features.ItemsSource = part?.Features.Select(Describe).ToList() ?? new List<string>();
        _features.SelectedIndex = part is null ? -1 : Math.Min(featIdx, part.Features.Count - 1);
        _refreshing = false;
    }

    private static string Describe(Feature f)
    {
        string mode = f is ModifierFeature ? "◇" : f is PatternFeature ? "⁂" : f.Mode switch { CombineMode.Hueco => "−", CombineMode.Interseccion => "∩", _ => "+" };
        string name = string.IsNullOrWhiteSpace(f.Name) ? f.KindName : f.Name;
        return f.Enabled ? $"{mode} {name}" : $"{mode} {name} (desactivada)";
    }

    private void OnPartSelected()
    {
        _refreshing = true;
        _features.ItemsSource = CurrentPart?.Features.Select(Describe).ToList() ?? new List<string>();
        _features.SelectedIndex = -1;
        _refreshing = false;
        if (CurrentPart is { } p) _properties.ShowPart(p); else _properties.ShowNothing();
        UpdateViewport();
    }

    private void OnFeatureSelected()
    {
        if (CurrentFeature is { } f) _properties.ShowFeature(f);
        else if (CurrentPart is { } p) _properties.ShowPart(p);
        UpdateViewport();
    }

    private void AddPart()
    {
        _doc.Parts.Add(new Part { Name = $"Pieza {_doc.Parts.Count + 1}", Color = Palette[_doc.Parts.Count % Palette.Length] });
        RefreshLists();
        _parts.SelectedIndex = _doc.Parts.Count - 1;
        ScheduleRebuild();
    }

    private static readonly string[] Palette = { "#4A90D9", "#E8A33D", "#5BBF6A", "#C9534F", "#9B6FD0", "#3FB6B2" };

    private void DeletePart()
    {
        if (CurrentPart is not { } p) return;
        _doc.Parts.Remove(p);
        _meshes.Remove(p);
        RefreshLists();
        OnPartSelected();
        ScheduleRebuild();
    }

    private void AddFeature(Feature f)
    {
        if (CurrentPart is not { } p)
        {
            p = new Part { Name = "Pieza 1" };
            _doc.Parts.Add(p);
            RefreshLists();
            _parts.SelectedIndex = 0;
        }
        p.Features.Add(f);
        RefreshLists();
        _features.SelectedIndex = p.Features.Count - 1;
        ScheduleRebuild();
    }

    private void MoveFeature(int delta)
    {
        if (CurrentPart is not { } p || CurrentFeature is not { } f) return;
        int i = p.Features.IndexOf(f), j = i + delta;
        if (j < 0 || j >= p.Features.Count) return;
        (p.Features[i], p.Features[j]) = (p.Features[j], p.Features[i]);
        RefreshLists();
        _features.SelectedIndex = j;
        ScheduleRebuild();
    }

    private void DuplicateFeature()
    {
        if (CurrentPart is not { } p || CurrentFeature is not { } f) return;
        var copy = f is ExtrudeFeature ex ? ex.Clone() : f.Clone();
        copy.Name = f.Name + " (copia)";
        copy.Position += new Vec3(5, 5, 0);
        p.Features.Insert(p.Features.IndexOf(f) + 1, copy);
        RefreshLists();
        _features.SelectedIndex = p.Features.IndexOf(copy);
        ScheduleRebuild();
    }

    private void DeleteFeature()
    {
        if (CurrentPart is not { } p || CurrentFeature is not { } f) return;
        p.Features.Remove(f);
        RefreshLists();
        OnFeatureSelected();
        ScheduleRebuild();
    }

    // ---------- Reconstrucción y visor ----------

    private void ScheduleRebuild()
    {
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private async Task RebuildAsync(bool fit = false)
    {
        int version = ++_version;
        // Si otro cálculo reemplaza a este, el encuadre pendiente se hace en el siguiente.
        _pendingFit |= fit;
        _info.Text = "Calculando…";
        var errors = new List<string>();
        var results = new Dictionary<Part, Mesh?>();
        foreach (var part in _doc.Parts.ToList())
        {
            var r = await _evaluator.EvaluateAsync(part.DeepClone());
            if (version != _version) return; // hubo otro cambio mientras calculaba
            results[part] = r.Mesh;
            errors.AddRange(r.Errors.Select(e => $"{part.Name}: {e}"));
        }
        _meshes.Clear();
        foreach (var (k, v) in results) _meshes[k] = v;
        UpdateViewport();
        if (_pendingFit) { _viewport.FitAll(); _pendingFit = false; }

        var visible = _doc.Parts.Where(p => p.Visible && _meshes.GetValueOrDefault(p) is not null).ToList();
        int tris = visible.Sum(p => _meshes[p]!.TriangleCount);
        double vol = visible.Sum(p => _meshes[p]!.Volume()) / 1000.0;
        bool watertight = visible.All(p => _meshes[p]!.CountOpenEdges() == 0);
        _info.Text = $"Motor: {_kernel.Name} · Visor: {_viewport.RendererName} · {visible.Count} pieza(s) · {tris:N0} triángulos · volumen {vol:0.##} cm³ · " +
                     (watertight ? "malla cerrada, lista para imprimir" : "atención: la malla tiene aberturas") +
                     (errors.Count > 0 ? " · Errores: " + string.Join(" | ", errors) : "");
    }

    private void UpdateViewport()
    {
        var items = new List<RenderItem>();
        foreach (var part in _doc.Parts)
        {
            if (!part.Visible || _meshes.GetValueOrDefault(part) is not { } mesh) continue;
            var color = Color.TryParse(part.Color, out var c) ? c : Colors.SteelBlue;
            items.Add(new RenderItem(mesh, color));
        }
        // La operación seleccionada se resalta en transparente (así se ven también los huecos).
        if (CurrentFeature is PatternFeature pattern && CurrentPart is { } part0)
        {
            // Muestra dónde caen las copias.
            try
            {
                var before = part0.Features.TakeWhile(x => x != pattern)
                    .Where(x => x.Enabled && x is not ModifierFeature and not PatternFeature).ToList();
                foreach (var g in before.Skip(Math.Max(0, before.Count - pattern.Operations)))
                {
                    var local = g.Build(_kernel);
                    var col = g.Mode == CombineMode.Hueco ? Color.FromArgb(90, 255, 80, 60) : Color.FromArgb(90, 255, 200, 0);
                    foreach (var t in pattern.CopyTransforms().Take(500))
                        items.Add(new RenderItem(_kernel.ToMesh(_kernel.Transform(local, t)), col, Overlay: true));
                }
            }
            catch { /* medidas no válidas mientras se escribe */ }
        }
        else if (CurrentFeature is { } f and not ModifierFeature and not PatternFeature)
        {
            try
            {
                var m = _kernel.ToMesh(f.Build(_kernel));
                var col = f.Mode == CombineMode.Hueco ? Color.FromArgb(90, 255, 80, 60) : Color.FromArgb(90, 255, 200, 0);
                items.Add(new RenderItem(m, col, Overlay: true));
            }
            catch { /* medidas no válidas mientras se escribe */ }
        }
        _viewport.SetItems(items);
    }

    // ---------- Archivos ----------

    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    private async Task OpenAsync()
    {
        if (Storage is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir diseño 3D",
            FileTypeFilter = [new FilePickerFileType("Diseño 3D") { Patterns = ["*" + CadDocument.FileExtension] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        LoadDocument(CadDocument.Load(path), path);
        _host.SetStatus($"Abierto {Path.GetFileName(path)}");
    }

    private async Task SaveAsync()
    {
        if (Storage is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar diseño 3D",
            SuggestedFileName = Path.GetFileName(_path) ?? _doc.Name + CadDocument.FileExtension,
            DefaultExtension = CadDocument.FileExtension,
            FileTypeChoices = [new FilePickerFileType("Diseño 3D") { Patterns = ["*" + CadDocument.FileExtension] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        _doc.Save(path);
        _path = path;
        _host.SetStatus($"Guardado {Path.GetFileName(path)}");
    }

    private async Task NewEnclosureAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var p = await new EnclosureDialog().ShowDialog<EnclosureParameters?>(owner);
        if (p is null) return;
        LoadDocument(EnclosureTemplate.Create(p), null);
    }

    private async Task BoxForBoardAsync()
    {
        if (Storage is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir placa para crear su caja",
            FileTypeFilter = [new FilePickerFileType("Placa") { Patterns = ["*" + Simulador.Circuitos.Pcb.PcbDocument.FileExtension] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        var (doc, missing) = BoardEnclosure.FromPlacaFile(path, _host.GetDataDirectory("circuitos"));
        LoadDocument(doc, null);
        _host.SetStatus(missing.Count == 0
            ? $"Caja creada para {Path.GetFileName(path)}. Revisa los huecos de los conectores antes de imprimir."
            : $"Caja creada, pero faltan huellas en las librerías ({string.Join(", ", missing)}): sus taladros no se han tenido en cuenta.");
    }

    private List<Part> ExportableParts()
    {
        var parts = _doc.Parts.Where(p => p.Visible && _meshes.GetValueOrDefault(p) is not null).ToList();
        if (parts.Count == 0) throw new InvalidOperationException("No hay piezas visibles con geometría para exportar.");
        return parts;
    }

    private async Task ExportStlAsync()
    {
        var parts = ExportableParts();
        if (Storage is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar STL",
            SuggestedFileName = _doc.Name + ".stl",
            DefaultExtension = ".stl",
            FileTypeChoices = [new FilePickerFileType("STL") { Patterns = ["*.stl"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        StlWriter.Save(path, parts.Select(p => _meshes[p]!));
        _host.SetStatus($"Exportado {Path.GetFileName(path)}");
    }

    private async Task ExportStepAsync()
    {
        if (_kernel is not IExactKernel exact) throw new InvalidOperationException("Exportar STEP necesita el motor OpenCASCADE.");
        var parts = ExportableParts();
        if (Storage is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar STEP",
            SuggestedFileName = _doc.Name + ".step",
            DefaultExtension = ".step",
            FileTypeChoices = [new FilePickerFileType("STEP") { Patterns = ["*.step", "*.stp"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var solids = new List<(string, ISolid)>();
        foreach (var p in parts)
            if (_evaluator.BuildSolid(p.DeepClone()) is { } s) solids.Add((p.Name, s));
        exact.ExportStep(path, solids);
        _host.SetStatus($"Exportado {Path.GetFileName(path)}");
    }

    private async Task ImportStepAsync()
    {
        if (_kernel is not IExactKernel) throw new InvalidOperationException("Importar STEP necesita el motor OpenCASCADE.");
        if (Storage is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar modelo STEP",
            FileTypeFilter = [new FilePickerFileType("STEP") { Patterns = ["*.step", "*.stp"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        AddFeature(new StepImportFeature { Name = Path.GetFileNameWithoutExtension(path), FilePath = path });
    }

    private async Task Export3mfAsync()
    {
        var parts = ExportableParts();
        if (Storage is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar 3MF",
            SuggestedFileName = _doc.Name + ".3mf",
            DefaultExtension = ".3mf",
            FileTypeChoices = [new FilePickerFileType("3MF") { Patterns = ["*.3mf"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        ThreeMfWriter.Save(path, parts.Select(p => new ThreeMfWriter.Item(p.Name, _meshes[p]!, p.Color)).ToList());
        _host.SetStatus($"Exportado {Path.GetFileName(path)}");
    }
}
