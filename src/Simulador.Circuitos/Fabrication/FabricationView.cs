using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Simulador.Circuitos.Pcb;
using Simulador.Core.Libraries.Model;
using Simulador.Core.Modules;

namespace Simulador.Circuitos.Fabrication;

/// <summary>Pestaña Fabricación: impresión para transferencia térmica y Gerber para fabricante.</summary>
public sealed class FabricationView : DockPanel
{
    private readonly IModuleHost _host;
    private readonly PcbCanvas _pcb;
    private readonly TransferOptions _opt = new();
    private readonly PrintPreview _preview;
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
    private readonly CheckBox _mirror = new() { Content = "Espejar la impresión" };
    private readonly TextBlock _mirrorHint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 };

    public FabricationView(IModuleHost host, PcbCanvas pcb)
    {
        _host = host;
        _pcb = pcb;
        _preview = new PrintPreview(pcb, _opt);

        var layer = new ComboBox { ItemsSource = new[] { "Cobre inferior (pistas)", "Cobre superior", "Serigrafía superior" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        layer.SelectionChanged += (_, _) =>
        {
            _opt.Layer = (TransferLayer)Math.Max(0, layer.SelectedIndex);
            _opt.Mirror = null;
            Refresh();
        };
        _mirror.IsCheckedChanged += (_, _) =>
        {
            if (_mirror.IsChecked is { } m && m != _opt.EffectiveMirror) { _opt.Mirror = m; Refresh(); }
        };
        var copies = new NumericUpDown { Minimum = 1, Maximum = 12, Value = 1, Increment = 1, FormatString = "0" };
        copies.ValueChanged += (_, _) => { _opt.Copies = (int)(copies.Value ?? 1); Refresh(); };
        var drill = new CheckBox { Content = "Marcar el centro de los taladros", IsChecked = true };
        drill.IsCheckedChanged += (_, _) => { _opt.DrillMarks = drill.IsChecked == true; Refresh(); };

        var savePdf = new Button { Content = "🖨 Guardar PDF para transferencia", HorizontalAlignment = HorizontalAlignment.Stretch };
        savePdf.Click += async (_, _) => await SavePdf();
        var saveGerber = new Button { Content = "📦 Exportar Gerber y taladros (.zip)", HorizontalAlignment = HorizontalAlignment.Stretch };
        saveGerber.Click += async (_, _) => await SaveGerber();

        var panel = new StackPanel
        {
            Width = 300,
            Spacing = 8,
            Margin = new Thickness(0, 0, 12, 0),
            Children =
            {
                new TextBlock { Text = "Placa casera (transferencia térmica)", FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Capa a imprimir" }, layer,
                _mirror, _mirrorHint,
                new TextBlock { Text = "Copias en la hoja" }, copies,
                drill,
                savePdf,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7,
                    Text = "Imprime con impresora láser al 100 % (tamaño real). Mide la regla de 50 mm antes de planchar. Recorta, apoya el tóner sobre el cobre limpio y plancha unos minutos; después ataca con percloruro.",
                },
                new Separator(),
                new TextBlock { Text = "Fabricante o fresadora CNC", FontSize = 16, FontWeight = FontWeight.SemiBold },
                saveGerber,
                new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Text = "Gerber RS-274X y taladros Excellon en mm, aceptados por cualquier fabricante de PCB o por FlatCAM para fresar." },
                new Separator(),
                _summary,
            },
        };
        SetDock(panel, Dock.Left);
        Children.Add(new ScrollViewer { Content = panel, [DockPanel.DockProperty] = Dock.Left });
        Children.Add(new Border { Background = Brushes.DimGray, Child = _preview, ClipToBounds = true });

        _pcb.DocumentChanged += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _mirror.IsChecked = _opt.EffectiveMirror;
        _mirrorHint.Text = _opt.Layer == TransferLayer.BottomCopper
            ? "El cobre inferior se imprime sin espejo: al apoyar el papel boca abajo sobre la cara de abajo de la placa queda en la posición correcta."
            : "Las capas de arriba se imprimen en espejo para que, al transferirlas, se lean al derecho.";
        var doc = _pcb.Document;
        var drills = Artwork.Drills(doc);
        var sizes = drills.GroupBy(d => Math.Round(d.Diameter, 2)).OrderBy(g => g.Key).Select(g => $"{g.Count()} × {g.Key:0.0#} mm");
        _summary.Text = $"Placa {doc.BoardWidth:0.##} × {doc.BoardHeight:0.##} mm ({doc.BoardWidth * doc.BoardHeight / 100:0.#} cm²)\n" +
                        $"{doc.Footprints.Count} componentes, {doc.Tracks.Count(t => t.Layer != Layers.Jumper)} tramos de pista\n" +
                        (doc.Tracks.Any(t => t.Layer == Layers.Jumper) ? $"Puentes de cable a soldar por arriba: {doc.Tracks.Count(t => t.Layer == Layers.Jumper)}\n" : "") +
                        $"Taladros: {(drills.Count == 0 ? "ninguno" : string.Join(", ", sizes))}\n" +
                        $"Conexiones sin trazar: {_pcb.RatsnestLines.Count}";
        _preview.InvalidateVisual();
    }

    private bool Warn()
    {
        var pending = _pcb.RatsnestLines.Count;
        var drc = PcbAnalysis.CheckRules(_pcb.Document).Count;
        if (pending == 0 && drc == 0) return false;
        _host.SetStatus($"Aviso: {pending} conexiones sin trazar y {drc} errores de reglas. Se exporta igualmente.");
        return true;
    }

    private async Task SavePdf()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar PDF para transferencia",
            SuggestedFileName = $"{GerberWriter.SafeName(_pcb.Document.Title)}-transferencia.pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        bool warned = Warn();
        await File.WriteAllBytesAsync(path, TransferPrint.Pdf(_pcb.Document, _opt));
        if (!warned) _host.SetStatus($"PDF guardado: {path}");
        Open(path);
    }

    private async Task SaveGerber()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar Gerber",
            SuggestedFileName = $"{GerberWriter.SafeName(_pcb.Document.Title)}-gerber.zip",
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        bool warned = Warn();
        await File.WriteAllBytesAsync(path, GerberWriter.Zip(_pcb.Document));
        if (!warned) _host.SetStatus($"Gerber exportado: {path}");
    }

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception) { /* sin visor de PDF: el archivo queda guardado */ }
    }
}

/// <summary>Vista previa de la hoja tal como se imprimirá (tóner negro sobre papel).</summary>
internal sealed class PrintPreview(PcbCanvas pcb, TransferOptions opt) : Control
{
    public override void Render(DrawingContext ctx)
    {
        var doc = pcb.Document;
        double pad = 10;
        double scale = Math.Min((Bounds.Width - 2 * pad) / doc.BoardWidth, (Bounds.Height - 2 * pad) / doc.BoardHeight);
        if (scale <= 0 || double.IsInfinity(scale)) return;
        double ox = (Bounds.Width - doc.BoardWidth * scale) / 2, oy = (Bounds.Height - doc.BoardHeight * scale) / 2;
        Point M(Vec2 v) => new(ox + (opt.EffectiveMirror ? doc.BoardWidth - v.X : v.X) * scale, oy + v.Y * scale);

        ctx.FillRectangle(Brushes.White, new Rect(ox - 6, oy - 6, doc.BoardWidth * scale + 12, doc.BoardHeight * scale + 12));
        ctx.DrawRectangle(new Pen(Brushes.Gray, 1), new Rect(ox, oy, doc.BoardWidth * scale, doc.BoardHeight * scale));
        var layer = opt.Layer switch
        {
            TransferLayer.BottomCopper => ArtworkLayer.BottomCopper,
            TransferLayer.TopCopper => ArtworkLayer.TopCopper,
            _ => ArtworkLayer.TopSilk,
        };
        foreach (var s in Artwork.Layer(doc, layer))
        {
            switch (s)
            {
                case Stroke st:
                    ctx.DrawLine(new Pen(Brushes.Black, st.Width * scale, lineCap: PenLineCap.Round), M(st.A), M(st.B));
                    break;
                case Disc d:
                    ctx.DrawEllipse(Brushes.Black, null, M(d.Center), d.Diameter / 2 * scale, d.Diameter / 2 * scale);
                    break;
                case Polygon p:
                    var g = new StreamGeometry();
                    using (var gc = g.Open())
                    {
                        gc.BeginFigure(M(p.Points[0]), true);
                        foreach (var pt in p.Points.Skip(1)) gc.LineTo(M(pt));
                        gc.EndFigure(true);
                    }
                    ctx.DrawGeometry(Brushes.Black, null, g);
                    break;
            }
        }
        if (opt.DrillMarks && layer != ArtworkLayer.TopSilk)
            foreach (var d in Artwork.Drills(doc))
            {
                double r = Math.Min(opt.DrillMarkDiameter, d.Diameter) / 2 * scale;
                ctx.DrawEllipse(Brushes.White, null, M(d.Center), r, r);
            }
    }
}
