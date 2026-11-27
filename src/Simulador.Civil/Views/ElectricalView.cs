using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Civil.Model;
using Simulador.Civil.Model.Electrical;

namespace Simulador.Civil.Views;

/// <summary>
/// Pestaña «Instalación eléctrica»: circuitos de cada centro de carga (editables), cuadro de cargas con balance de fases
/// y caída de tensión, canalizaciones con su relleno y las comprobaciones de la NOM-001 (orientativas).
/// Con la simulación en marcha muestra en vivo la corriente de cada circuito y permite abrir o rearmar su interruptor.
/// </summary>
public sealed class ElectricalView : ScrollViewer
{
    private readonly BuildingDocument _doc;
    private readonly Func<InstallationSimulation?> _sim;
    private readonly Action<string> _goTo;
    private readonly Action<string> _status;
    private readonly Action _exportCsv, _exportSchematic;
    private readonly StackPanel _root = new() { Margin = new Thickness(12), Spacing = 8 };
    private readonly List<Action> _live = [];
    private bool _dirty = true;

    public ElectricalView(BuildingDocument doc, Func<InstallationSimulation?> sim, Action<string> goTo, Action<string> status,
        Action exportCsv, Action exportSchematic)
    {
        _doc = doc; _sim = sim; _goTo = goTo; _status = status; _exportCsv = exportCsv; _exportSchematic = exportSchematic;
        Content = _root;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsVisible && _dirty) Rebuild(); };
        AttachedToVisualTree += (_, _) => { if (_dirty) Rebuild(); };
    }

    /// <summary>Marca para reconstruir (al cambiar el documento). Si está a la vista, se rehace ya.</summary>
    public void Refresh()
    {
        _dirty = true;
        if (IsEffectivelyVisible) Rebuild();
    }

    /// <summary>Actualiza solo los valores en vivo de la simulación.</summary>
    public void UpdateLive()
    {
        if (!IsEffectivelyVisible) return;
        foreach (var a in _live) a();
    }

    private static TextBlock T(string text, double size = 13, FontWeight weight = FontWeight.Normal, double opacity = 1) =>
        new() { Text = text, FontSize = size, FontWeight = weight, Opacity = opacity, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    private void Rebuild()
    {
        _dirty = false;
        _live.Clear();
        _root.Children.Clear();
        var schedule = LoadSchedule.Compute(_doc);

        _root.Children.Add(T("Instalación eléctrica", 18, FontWeight.SemiBold));
        _root.Children.Add(T("Circuitos que salen de cada centro de carga, con su interruptor termomagnético, calibre de cable y carga. Los cálculos siguen la NOM-001-SEDE-2012 de forma orientativa (180 VA por contacto, 3 % de caída de tensión, 40 % de relleno de tubo): un proyecto real lo firma un perito o una unidad de verificación.", 12, opacity: 0.75));

        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(Button("⚡ Diseñar automáticamente", () => _status(ElectricalDesigner.DesignAll(_doc)),
            "Enlaza cada luminaria con el apagador de su habitación, reparte todo en circuitos (alumbrado 15 A, contactos 20 A, GFCI en baño y cocina, un circuito por equipo fijo), traza la tubería que falta y ajusta los diámetros."));
        bar.Children.Add(Button("📄 Cuadro de cargas (CSV)…", _exportCsv, "Para abrirlo en Excel."));
        bar.Children.Add(Button("🔌 Esquema para Instalaciones eléctricas (.selx)…", _exportSchematic,
            "Genera el esquema con acometida, termomagnéticos, cables, apagadores, lámparas y contactos para abrirlo en la pestaña Instalaciones eléctricas (y de ahí en Tableros)."));
        foreach (var c in bar.Children) c.Margin = new Thickness(0, 0, 8, 6);
        _root.Children.Add(bar);

        if (schedule.Panels.Count == 0)
        {
            _root.Children.Add(T("No hay ningún centro de carga. Coloca uno en la planta (Instalaciones › Tablero y acometida › Centro de carga) junto a la entrada.", 13, opacity: 0.8));
        }
        foreach (var p in schedule.Panels) _root.Children.Add(PanelSection(p));

        _root.Children.Add(ConduitSection(schedule));
        _root.Children.Add(ChecksSection(schedule));
    }

    private static Button Button(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text };
        b.Click += (_, _) => click();
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    // ------------------------------------------------------------------ tableros y circuitos

    private static readonly string[] Headers =
        ["Circ.", "Nombre", "Uso", "Tensión", "Fase", "Interruptor", "Cable", "GFCI", "Elementos", "Carga", "Amperes", "Caída", "Simulación", ""];
    private const string Columns = "48,150,112,88,72,80,92,44,104,66,58,54,160,32";

    private Control PanelSection(PanelReport p)
    {
        var box = new StackPanel { Spacing = 4, Margin = new Thickness(0, 10, 0, 0) };
        box.Children.Add(T($"Centro de carga {p.Panel.ComponentId}", 15, FontWeight.SemiBold));
        string phases = p.PhaseVA[2] > 0
            ? $"Fase A {p.PhaseVA[0]:0} VA · Fase B {p.PhaseVA[1]:0} VA · Fase C {p.PhaseVA[2]:0} VA"
            : $"Fase A {p.PhaseVA[0]:0} VA · Fase B {p.PhaseVA[1]:0} VA";
        box.Children.Add(T($"Carga instalada {p.TotalVA:0} VA · demanda {p.DemandVA:0} VA · {phases} · desbalance {p.ImbalancePercent:0.0} % · interruptor principal sugerido {p.SuggestedMainA:0} A ({(p.PhaseVA[2] > 0 ? "3F-4H 220/127 V" : "1F-3H 220/127 V")})", 12, opacity: 0.85));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns) };
        int row = 0;
        void Row() { grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
        void Cell(Control c, int col)
        {
            Grid.SetRow(c, row); Grid.SetColumn(c, col);
            c.Margin = new Thickness(2);
            grid.Children.Add(c);
        }
        Row();
        for (int i = 0; i < Headers.Length; i++) Cell(T(Headers[i], 12, FontWeight.SemiBold, 0.8), i);

        foreach (var cr in p.Circuits)
        {
            row++; Row();
            var c = cr.Circuit;
            string id = c.Id;
            void Set(Func<Circuit, Circuit> change) => _doc.Edit(e =>
            {
                var cur = e.Electrical.Circuits.FirstOrDefault(x => x.Id == id);
                if (cur is not null) e.Electrical.Set(change(cur));
            });

            Cell(T(c.Id, 13, FontWeight.SemiBold), 0);
            var name = new TextBox { Text = c.Name };
            name.LostFocus += (_, _) => { if (name.Text is { Length: > 0 } t && t != c.Name) Set(x => x with { Name = t }); };
            Cell(name, 1);
            Cell(Combo(Enum.GetNames<CircuitUse>(), (int)c.Use, i => Set(x => x with { Use = (CircuitUse)i })), 2);
            Cell(Combo(["127 V", "220 V", "3F 220"], c.Poles - 1, i => Set(x => x with { Poles = i + 1 })), 3);
            Cell(Combo(c.Poles == 3 ? ["ABC"] : c.Poles == 2 ? ["AB", "BC", "CA"] : ["A", "B", "C"], c.Poles == 3 ? 0 : (int)c.Phase, i => Set(x => x with { Phase = (Phase)i })), 4);
            var brk = ElectricalTables.Breakers.ToList();
            if (!brk.Contains(c.BreakerA)) brk.Insert(0, c.BreakerA);
            Cell(Combo(brk.Select(b => $"{b:0} A").ToArray(), brk.IndexOf(c.BreakerA), i => Set(x => x with { BreakerA = brk[i] })), 5);
            Cell(Combo(ElectricalTables.Awg.Select(a => $"{a} AWG").ToArray(), Math.Max(0, Array.IndexOf(ElectricalTables.Awg, c.WireAwg)), i => Set(x => x with { WireAwg = ElectricalTables.Awg[i] })), 6);
            var gf = new CheckBox { IsChecked = c.GroundFault, IsEnabled = c.Poles == 1 };
            ToolTip.SetTip(gf, "Protección contra falla a tierra en todo el circuito (interruptor GFCI en el tablero).");
            gf.IsCheckedChanged += (_, _) => { if (gf.IsChecked != c.GroundFault) Set(x => x with { GroundFault = gf.IsChecked == true }); };
            Cell(gf, 7);
            var parts = new List<string>();
            if (cr.Lights > 0) parts.Add($"{cr.Lights} luz");
            if (cr.Outlets > 0) parts.Add($"{cr.Outlets} cont.");
            if (cr.Loads > 0) parts.Add($"{cr.Loads} equipo");
            if (cr.Switches > 0) parts.Add($"{cr.Switches} apag.");
            var members = new Button { Content = parts.Count == 0 ? "vacío" : string.Join(", ", parts), Background = Brushes.Transparent, Padding = new Thickness(2) };
            ToolTip.SetTip(members, "Ir al primero en la planta.");
            members.Click += (_, _) => { if (cr.Members.Count > 0) _goTo(cr.Members[0]); };
            Cell(members, 8);
            Cell(T($"{cr.LoadVA:0} VA"), 9);
            var cur = T($"{cr.Current:0.0} A");
            if (cr.Current > c.BreakerA) cur.Foreground = Brushes.Firebrick;
            else if (cr.Current > 0.8 * c.BreakerA) cur.Foreground = Brushes.DarkGoldenrod;
            Cell(cur, 10);
            var drop = T($"{cr.DropPercent:0.00} %{(cr.Routed ? "" : "*")}");
            if (cr.DropPercent > 3) drop.Foreground = Brushes.DarkGoldenrod;
            if (!cr.Routed) ToolTip.SetTip(drop, "* Hay elementos sin tubería: longitud estimada.");
            Cell(drop, 11);

            // En vivo.
            var live = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var liveText = T("—", 12);
            var toggle = new Button { Content = "Abrir", Padding = new Thickness(6, 1), FontSize = 11 };
            toggle.Click += (_, _) => { _sim()?.ToggleBreaker(id); UpdateLive(); };
            live.Children.Add(toggle);
            live.Children.Add(liveText);
            _live.Add(() =>
            {
                var sim = _sim();
                toggle.IsVisible = sim is not null;
                if (sim is null) { liveText.Text = "—"; liveText.Foreground = Brushes.Gray; return; }
                var st = sim.BreakerStatus(id);
                double a = sim.CircuitCurrent(id), v = sim.CircuitEndVoltage(id);
                toggle.Content = st.Closed ? "Abrir" : "Rearmar";
                liveText.Text = st.Tripped ? $"⛔ disparado" : !st.Closed ? "abierto" : $"{a:0.0} A{(v > 1 ? $" · {v:0} V" : "")}";
                liveText.Foreground = st.Tripped ? Brushes.Firebrick : !st.Closed ? Brushes.Gray : st.Thermal > 0.3 ? Brushes.DarkGoldenrod : Brushes.SeaGreen;
            });
            Cell(live, 12);
            var del = new Button { Content = "🗑", Padding = new Thickness(4, 1) };
            ToolTip.SetTip(del, "Quitar el circuito (sus elementos quedan sin circuito).");
            del.Click += (_, _) => _doc.Edit(e => e.Delete(id));
            Cell(del, 13);
        }
        box.Children.Add(new Border { Child = grid, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(4) });

        var panelId = p.Panel.Id;
        var add = Button("+ Circuito", () => _doc.Edit(e =>
        {
            var c = new Circuit(e.Electrical.NewCircuitId(), "Circuito nuevo", panelId);
            e.Electrical.Circuits.Add(c);
        }), "Circuito vacío; asígnale elementos desde su panel de propiedades en la planta.");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        box.Children.Add(add);
        UpdateLive();
        return box;
    }

    private static ComboBox Combo(string[] items, int selected, Action<int> set)
    {
        var c = new ComboBox { ItemsSource = items, SelectedIndex = Math.Clamp(selected, 0, items.Length - 1), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Padding = new Thickness(6, 2, 0, 2), MinWidth = 0 };
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0 && c.SelectedIndex != selected) set(c.SelectedIndex); };
        return c;
    }

    // ------------------------------------------------------------------ canalizaciones

    private Control ConduitSection(LoadSchedule schedule)
    {
        var box = new StackPanel { Spacing = 4, Margin = new Thickness(0, 14, 0, 0) };
        box.Children.Add(T("Canalizaciones", 15, FontWeight.SemiBold));
        var routes = _doc.Routes.Where(r => r.ModuleId == "electrico").ToList();
        if (routes.Count == 0) { box.Children.Add(T("Aún no hay tubería. Usa «Trazar tubería» en la paleta o «Diseñar automáticamente».", 12, opacity: 0.75)); return box; }
        var totals = routes.GroupBy(r => _doc.Electrical.ConduitOf(r.Id) is var s ? (s.Type, s.DiameterMm) : default)
            .OrderBy(g => g.Key.Type).ThenBy(g => g.Key.DiameterMm)
            .Select(g => $"{ElectricalTables.ConduitName(g.Key.Type)} {(g.Key.Type == ConduitType.Charola ? "" : ElectricalTables.Inch(g.Key.DiameterMm))}: {g.Sum(r => r.Length):0.0} m");
        box.Children.Add(T("Metros por tipo: " + string.Join(" · ", totals), 12, opacity: 0.85));
        double wire = schedule.Conduits.Sum(c => c.Conductors * (_doc.Routes.FirstOrDefault(r => r.Id == c.RouteId)?.Length ?? 0));
        box.Children.Add(T($"Cable total aproximado (todos los conductores): {wire * 1.1:0} m (incluye 10 % de desperdicio y colas).", 12, opacity: 0.85));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,120,250,70,90,90,90") };
        int row = 0;
        void Cell(Control c, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); c.Margin = new Thickness(2); grid.Children.Add(c); }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] h = ["Tubo", "De → a", "Tipo", "Diámetro", "Conductores", "Relleno", "Longitud"];
        for (int i = 0; i < h.Length; i++) Cell(T(h[i], 12, FontWeight.SemiBold, 0.8), i);
        var fixtures = _doc.Fixtures.ToDictionary(f => f.Id);
        foreach (var r in routes.Take(200))
        {
            row++;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var spec = _doc.Electrical.ConduitOf(r.Id);
            var rep = schedule.Conduits.FirstOrDefault(c => c.RouteId == r.Id);
            var link = new Button { Content = r.Id, Background = Brushes.Transparent, Padding = new Thickness(2) };
            string rid = r.Id;
            link.Click += (_, _) => _goTo(rid);
            Cell(link, 0);
            Cell(T($"{fixtures.GetValueOrDefault(r.FromFixtureId)?.ComponentId} → {fixtures.GetValueOrDefault(r.ToFixtureId)?.ComponentId}", 12), 1);
            Cell(T(ElectricalTables.ConduitName(spec.Type), 12), 2);
            Cell(T(spec.Type == ConduitType.Charola ? "—" : ElectricalTables.Inch(spec.DiameterMm), 12), 3);
            Cell(T(rep is null ? "—" : $"{rep.Conductors} × {rep.WireAwg} AWG", 12), 4);
            var fill = T(rep is null || spec.Type == ConduitType.Charola ? "—" : $"{rep.FillPercent:0} % / {rep.MaxFillPercent:0} %", 12);
            if (rep is not null && rep.FillPercent > rep.MaxFillPercent) fill.Foreground = Brushes.Firebrick;
            Cell(fill, 5);
            Cell(T($"{r.Length:0.00} m", 12), 6);
        }
        box.Children.Add(new Border { Child = grid, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(4) });
        return box;
    }

    // ------------------------------------------------------------------ comprobaciones

    private Control ChecksSection(LoadSchedule schedule)
    {
        var box = new StackPanel { Spacing = 2, Margin = new Thickness(0, 14, 0, 0) };
        box.Children.Add(T("Comprobaciones eléctricas", 15, FontWeight.SemiBold));
        if (schedule.Checks.Count == 0) { box.Children.Add(new TextBlock { Text = "✔ Sin avisos.", Foreground = Brushes.SeaGreen }); return box; }
        foreach (var c in schedule.Checks.OrderByDescending(c => c.Severity).Take(80))
        {
            var b = new Button
            {
                Content = new TextBlock
                {
                    Text = (c.Severity switch { CheckSeverity.Error => "⛔ ", CheckSeverity.Warning => "⚠ ", _ => "ℹ " }) + c.Message,
                    TextWrapping = TextWrapping.Wrap,
                },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, Padding = new Thickness(4, 2),
                Foreground = c.Severity switch { CheckSeverity.Error => Brushes.Firebrick, CheckSeverity.Warning => Brushes.DarkGoldenrod, _ => Brushes.SlateGray },
            };
            string id = c.ElementId;
            b.Click += (_, _) => _goTo(id);
            box.Children.Add(b);
        }
        return box;
    }
}
