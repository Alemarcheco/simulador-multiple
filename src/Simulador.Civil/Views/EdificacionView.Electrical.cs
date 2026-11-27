using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Simulador.Civil.Model;
using Simulador.Civil.Model.Electrical;
using Simulador.Core.Building;

namespace Simulador.Civil.Views;

/// <summary>Estructura, instalación eléctrica y su simulación en la pestaña Edificación.</summary>
public sealed partial class EdificacionView
{
    private ElectricalView? _electricalView;
    private InstallationSimulation? _sim;
    private DispatcherTimer? _simTimer;
    private readonly StackPanel _simPanel = new() { Spacing = 4, IsVisible = false };
    private ToggleButton? _simToggle;
    private CheckBox? _roofBox;
    private double _simSpeed = 1;
    private string _simLastAction = "";

    // ------------------------------------------------------------ paleta: estructura

    private void BuildStructurePalette(StackPanel p)
    {
        p.Children.Add(Header("Estructura"));
        p.Children.Add(ToolButton("▪ Columna", PlanTool.Column));
        p.Children.Add(ToolButton("═ Viga (de columna a columna)", PlanTool.Beam));
        p.Children.Add(Choice("Material", ["Concreto", "Acero", "Madera"], 0, i =>
        {
            _plan.StructureMaterial = StructureMaterials.Column[i];
            if (i == 1) { _plan.ColumnWidth = _plan.ColumnDepth = 0.35; _plan.BeamDepth = 0.60; }
        }));
        p.Children.Add(Num("Sección de columna (m)", 0.30, 0.1, 2, v => _plan.ColumnWidth = _plan.ColumnDepth = v));
        p.Children.Add(Num("Peralte de viga (m)", 0.45, 0.1, 2, v => _plan.BeamDepth = v));
        p.Children.Add(Num("Ancho de viga (m)", 0.25, 0.1, 1, v => _plan.BeamWidth = v));
    }

    // ------------------------------------------------------------ paleta: instalaciones

    private void BuildInstallationPalette(StackPanel p)
    {
        p.Children.Add(Header("Instalaciones"));
        var group = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = FixtureCatalog.Groups, SelectedIndex = 0 };
        var kind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11 };
        void FillKinds()
        {
            kind.Items.Clear();
            var g = group.SelectedItem as string ?? FixtureCatalog.GLights;
            foreach (var t in FixtureCatalog.All.Where(t => t.Group == g)) kind.Items.Add(new ComboBoxItem { Content = $"{t.Icon} {t.Name}", Tag = t });
            kind.SelectedIndex = 0;
        }
        group.SelectionChanged += (_, _) => FillKinds();
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is not ComboBoxItem { Tag: FixtureType t }) return;
            _plan.FixtureType = t;
            string power = t.Role switch
            {
                ElecRole.Light or ElecRole.Load => $" {t.PowerW:0} W a {t.Volts:0} V.",
                ElecRole.Outlet => $" {t.Volts:0} V.",
                _ => "",
            };
            help.Text = t.Help + power;
            SetTool(PlanTool.Fixture);
        };
        FillKinds();
        p.Children.Add(group);
        p.Children.Add(kind);
        p.Children.Add(help);
        p.Children.Add(ToolButton("＋ Colocar elemento", PlanTool.Fixture));

        p.Children.Add(new TextBlock { Text = "Tubería", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        var styles = Enum.GetValues<RouteStyle>();
        p.Children.Add(Choice("Por dónde va", styles.Select(RouteBuilder.StyleName).ToArray(), 0, i => _plan.RouteStyle = styles[i]));
        var types = Enum.GetValues<ConduitType>();
        p.Children.Add(Choice("Tipo de tubo", types.Select(ElectricalTables.ConduitName).ToArray(), 0, i => _plan.ConduitType = types[i]));
        var sizes = ElectricalTables.ConduitSizes.Where(s => s.Mm >= 13).ToArray();
        p.Children.Add(Choice("Diámetro", sizes.Select(s => $"{s.Inch} ({s.Mm} mm)").ToArray(), Array.FindIndex(sizes, s => s.Mm == 16), i => _plan.ConduitDiameter = sizes[i].Mm));
        p.Children.Add(ToolButton("⤳ Trazar tubería / tubería de agua", PlanTool.Route));
        p.Children.Add(ToolButton("🔗 Enlazar apagador con luminarias", PlanTool.Link));
        var auto = Btn("⚡ Diseñar instalación automáticamente", () => _host.SetStatus(ElectricalDesigner.DesignAll(_doc)));
        ToolTip.SetTip(auto, "Para empezar rápido: une cada luminaria con el apagador de su habitación, crea los circuitos en el centro de carga, traza la tubería y ajusta sus diámetros. Después puedes cambiarlo todo.");
        auto.HorizontalAlignment = HorizontalAlignment.Stretch;
        p.Children.Add(auto);
        p.Children.Add(new TextBlock
        {
            Text = "Los elementos quedan enlazados con el esquema del módulo eléctrico o hidráulico por su designación (X1, S1, PA1…). La tubería se encadena: cada clic sigue desde el último elemento; clic derecho para terminar.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11,
        });
    }

    // ------------------------------------------------------------ simulación

    private Control BuildSimToggle()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _simToggle = new ToggleButton { Content = "▶ Simular instalación eléctrica" };
        ToolTip.SetTip(_simToggle, "Clic en los apagadores para encender las luces, en los contactos para enchufar aparatos y en los equipos para encenderlos. Los interruptores del tablero se disparan si hay sobrecarga.");
        _simToggle.IsCheckedChanged += (_, _) => { if (_simToggle.IsChecked == true) StartSimulation(); else StopSimulation(); };
        var speed = new ComboBox { ItemsSource = new[] { "Tiempo real", "× 10", "× 60" }, SelectedIndex = 0 };
        ToolTip.SetTip(speed, "Acelera el tiempo para ver antes el disparo térmico de una sobrecarga.");
        speed.SelectionChanged += (_, _) => _simSpeed = speed.SelectedIndex switch { 1 => 10, 2 => 60, _ => 1 };
        row.Children.Add(_simToggle);
        row.Children.Add(speed);
        return row;
    }

    private void StartSimulation()
    {
        try
        {
            _sim = new InstallationSimulation(_doc);
        }
        catch (Exception ex)
        {
            _host.SetStatus($"No se pudo simular la instalación: {ex.Message}");
            if (_simToggle is not null) _simToggle.IsChecked = false;
            return;
        }
        _plan.Simulation = _sim;
        _view3d.Glow = id => _sim?.Brightness(id) ?? 0;
        // Sin cubierta se ven las luces por dentro en la vista 3D.
        if (_roofBox is not null) _roofBox.IsChecked = false;
        _simTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => SimTick());
        _simTimer.Start();
        _simPanel.IsVisible = true;
        _simLastAction = _sim.NotSimulated.Count > 0
            ? $"{_sim.NotSimulated.Count} elemento(s) sin circuito no se simulan."
            : "Haz clic en un apagador para encender la luz.";
        RefreshSimPanel(full: true);
        _view3d.Refresh();
        _plan.InvalidateVisual();
        _host.SetStatus("Simulación en marcha: clic en apagadores, contactos y equipos. Mayús + clic en un contacto GFCI = botón TEST.");
    }

    private void StopSimulation()
    {
        _simTimer?.Stop();
        _sim = null;
        _plan.Simulation = null;
        _view3d.Glow = null;
        _simPanel.IsVisible = false;
        _host.SetStatus("Simulación detenida.");
        _view3d.Refresh();
        _plan.InvalidateVisual();
        _electricalView?.UpdateLive();
    }

    /// <summary>Tras un cambio del edificio con la simulación en marcha, se rehace con el esquema nuevo.</summary>
    private void RestartSimulation()
    {
        try { _sim = new InstallationSimulation(_doc); }
        catch (Exception) { StopSimulation(); if (_simToggle is not null) _simToggle.IsChecked = false; return; }
        _plan.Simulation = _sim;
        RefreshSimPanel(full: true);
    }

    private string _lastGlowKey = "";

    private void SimTick()
    {
        if (_sim is null) return;
        _sim.Run(0.1 * _simSpeed);
        // La planta se redibuja siempre; la malla 3D solo si cambian las luces encendidas.
        _plan.InvalidateVisual();
        var key = string.Concat(_doc.Fixtures.Where(f => FixtureCatalog.RoleOf(f) == ElecRole.Light).Select(f => _sim.Brightness(f.Id) > 0.02 ? (int)(_sim.Brightness(f.Id) * 10) : 0));
        if (key != _lastGlowKey) { _lastGlowKey = key; _view3d.Refresh(); }
        RefreshSimPanel(full: false);
        _electricalView?.UpdateLive();
    }

    private readonly TextBlock _simInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _simEvents = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.85 };
    private readonly StackPanel _simCircuits = new() { Spacing = 2 };
    private readonly List<Action> _simLive = [];

    private void RefreshSimPanel(bool full)
    {
        if (_sim is null) return;
        if (full)
        {
            _simPanel.Children.Clear();
            _simPanel.Children.Add(Header("⚡ Simulación"));
            _simPanel.Children.Add(_simInfo);
            _simPanel.Children.Add(_simCircuits);
            _simPanel.Children.Add(_simEvents);
            _simCircuits.Children.Clear();
            _simLive.Clear();
            foreach (var c in _doc.Electrical.Circuits)
            {
                string id = c.Id;
                var row = new DockPanel();
                var btn = new Button { Content = "Abrir", Padding = new Thickness(6, 0), FontSize = 11 };
                btn.Click += (_, _) => { _sim?.ToggleBreaker(id); RefreshSimPanel(false); };
                DockPanel.SetDock(btn, Dock.Right);
                var text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                row.Children.Add(btn);
                row.Children.Add(text);
                _simCircuits.Children.Add(row);
                _simLive.Add(() =>
                {
                    if (_sim is null) return;
                    var st = _sim.BreakerStatus(id);
                    double a = _sim.CircuitCurrent(id);
                    btn.Content = st.Closed ? "Abrir" : "Rearmar";
                    text.Text = $"{id} {c.Name}: " + (st.Tripped ? "⛔ disparado" : !st.Closed ? "abierto" : $"{a:0.0} A de {c.BreakerA:0} A");
                    text.Foreground = st.Tripped ? Brushes.Firebrick : !st.Closed ? Brushes.Gray : a > c.BreakerA ? Brushes.DarkOrange : Brushes.Black;
                });
            }
        }
        _simInfo.Text = $"Tiempo {_sim.Time:0.0} s · consumo {_sim.TotalPower():0} W\n{_simLastAction}";
        foreach (var a in _simLive) a();
        var ev = _sim.Events.TakeLast(5).Reverse().Select(e => $"{e.Time:0.0} s · {e.Text}");
        _simEvents.Text = string.Join("\n", ev);
    }

    private void OnSimClick(string fixtureId, KeyModifiers keys)
    {
        if (_sim is null) return;
        var f = _doc.Fixtures.FirstOrDefault(x => x.Id == fixtureId);
        if (f is null) return;
        var t = FixtureCatalog.ByKind(f.Kind);
        bool shift = keys.HasFlag(KeyModifiers.Shift);
        var circuit = _doc.Electrical.CircuitOf(f.Id);
        switch (FixtureCatalog.RoleOf(f))
        {
            case ElecRole.Switch:
            {
                int key = t?.Switch == SwitchKind.Double && shift ? 2 : 1;
                _simLastAction = $"{f.ComponentId}{(key == 2 ? " tecla 2" : "")}: {_sim.ClickSwitch(f.Id, key)}" + (t?.Switch == SwitchKind.Double ? " (Mayús + clic: tecla 2)" : "");
                break;
            }
            case ElecRole.Outlet:
            {
                var gfci = _sim.GfciStatus(f.Id, circuit?.Id);
                if (gfci is not null && (shift || gfci.Tripped))
                {
                    bool test = shift && !gfci.Tripped;
                    _sim.PressGfci(f.Id, circuit?.Id, test);
                    _simLastAction = test ? $"{f.ComponentId}: botón TEST del GFCI, simula una fuga a tierra de 20 mA." : $"{f.ComponentId}: GFCI rearmado (RESET).";
                }
                else
                {
                    var pl = _sim.CycleOutlet(f.Id);
                    _simLastAction = $"{f.ComponentId}: {pl.Name}{(pl.Watts > 0 ? $" ({pl.Watts:0} W)" : "")}. Clic otra vez para enchufar otro aparato.";
                }
                break;
            }
            case ElecRole.Load:
                _simLastAction = $"{f.ComponentId} {t?.Name}: {(_sim.ToggleLoad(f.Id) ? "encendido" : "apagado")}.";
                break;
            case ElecRole.Light when _sim.IsBurnt(f.Id):
                _simLastAction = $"{f.ComponentId}: lámpara fundida.";
                break;
            case ElecRole.Light:
                _simLastAction = $"{f.ComponentId}: {_sim.VoltageAt(f.Id):0} V, brillo {_sim.Brightness(f.Id) * 100:0} %.";
                break;
            case ElecRole.Panel:
                _simLastAction = "Abre o rearma cada circuito con sus botones en el panel de la derecha o en la pestaña Instalación eléctrica.";
                break;
        }
        _sim.Run(0.05);
        RefreshSimPanel(false);
        _plan.InvalidateVisual();
        _view3d.Refresh();
        _electricalView?.UpdateLive();
    }

    // ------------------------------------------------------------ propiedades

    private void AddFixtureProps(PlacedFixture fx)
    {
        string id = fx.Id;
        var t = FixtureCatalog.ByKind(fx.Kind);
        var lvl = _doc.GetLevel(fx.LevelId);
        Title($"{t?.Name ?? fx.Kind} {fx.ComponentId}");
        if (t?.Help is { Length: > 0 } h) _props.Children.Add(Info(h));
        string host = fx.HostId is null ? "En el recinto"
            : _doc.Columns.Any(c => c.Id == fx.HostId) ? "En la columna " + fx.HostId
            : _doc.Beams.Any(b => b.Id == fx.HostId) ? "Bajo la viga " + fx.HostId
            : "En el muro " + fx.HostId;
        _props.Children.Add(Info($"Altura sobre el piso: {fx.Position.Z - lvl.Elevation:0.00} m · {host}\nEspacio: {_doc.SpaceAt(fx.LevelId, fx.Position.Xy)?.Name ?? "—"}"));

        PlacedFixture Cur() => _doc.Fixtures.First(f => f.Id == id);

        // Designación.
        var tag = new TextBox { Text = fx.ComponentId };
        tag.LostFocus += (_, _) =>
        {
            var v = tag.Text?.Trim();
            if (string.IsNullOrEmpty(v) || v == Cur().ComponentId) return;
            if (_doc.Fixtures.Any(f => f.ComponentId == v)) { _host.SetStatus($"Ya hay un elemento {v}."); tag.Text = Cur().ComponentId; return; }
            _doc.Edit(e => e.Replace(Cur(), Cur() with { ComponentId = v }));
        };
        _props.Children.Add(new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "Designación", FontSize = 12, Opacity = 0.8 }, tag } });

        // Tipo (dentro del mismo grupo y montaje).
        if (t is not null)
        {
            var same = FixtureCatalog.All.Where(x => x.Group == t.Group && x.Mount == t.Mount).ToList();
            if (same.Count > 1)
                _props.Children.Add(Choice("Tipo", same.Select(x => $"{x.Icon} {x.Name}").ToArray(), same.IndexOf(t), i =>
                {
                    if (same[i].Kind != Cur().Kind) _doc.Edit(e => e.Replace(Cur(), Cur() with { Kind = same[i].Kind, Width = Math.Max(0.1, same[i].Size), Depth = Cur().HostId is null ? Math.Max(0.1, same[i].Size) : Cur().Depth }));
                }));

            // Altura.
            if (t.Mount is MountKind.Wall)
                _props.Children.Add(Num("Altura sobre el piso (m)", fx.Position.Z - lvl.Elevation, 0, 20, v =>
                {
                    var c = Cur();
                    double z = _doc.GetLevel(c.LevelId).Elevation + v;
                    if (Math.Abs(c.Position.Z - z) > 1e-6) _doc.Edit(e => e.Replace(c, c with { Position = c.Position with { Z = z } }));
                }));
            else if (t.Mount is MountKind.Ceiling or MountKind.Hung)
            {
                double top = lvl.Elevation + lvl.Height - (_doc.Beams.FirstOrDefault(b => b.Id == fx.HostId)?.Depth ?? 0);
                _props.Children.Add(Num(t.Mount == MountKind.Hung ? "Cuelga bajo la viga o el techo (m)" : "Bajo el techo (m)", top - fx.Position.Z, 0, 20, v =>
                {
                    var c = Cur();
                    var l = _doc.GetLevel(c.LevelId);
                    double tp = l.Elevation + l.Height - (_doc.Beams.FirstOrDefault(b => b.Id == c.HostId)?.Depth ?? 0);
                    if (Math.Abs(c.Position.Z - (tp - v)) > 1e-6) _doc.Edit(e => e.Replace(c, c with { Position = c.Position with { Z = tp - v } }));
                }, 0.05));
            }
        }

        if (fx.ModuleId == "electrico" && t is not null) AddElectricalFixtureProps(fx, t);

        foreach (var r in _doc.Routes.Where(r => r.FromFixtureId == id || r.ToFixtureId == id))
        {
            var b = new Button { Content = $"Tubo {r.Id}: {r.Length:0.00} m", Background = Brushes.Transparent, Padding = new Thickness(2) };
            string rid = r.Id;
            b.Click += (_, _) => _plan.Select(rid);
            _props.Children.Add(b);
        }
    }

    private void AddElectricalFixtureProps(PlacedFixture fx, FixtureType t)
    {
        string id = fx.Id;
        var el = _doc.Electrical;
        var data = el.Of(id) ?? new FixtureElectrical(id);
        void SetData(Func<FixtureElectrical, FixtureElectrical> change) =>
            _doc.Edit(e => e.Electrical.Set(change(e.Electrical.Of(id) ?? new FixtureElectrical(id))));

        if (t.Role is ElecRole.Light or ElecRole.Load or ElecRole.Outlet)
        {
            double w = LoadSchedule.PowerOf(_doc, fx);
            _props.Children.Add(Num(t.Role == ElecRole.Outlet ? "Carga de cálculo (VA)" : "Potencia (W)", w, 0, 50000, v =>
            {
                if (Math.Abs(v - LoadSchedule.PowerOf(_doc, _doc.Fixtures.First(f => f.Id == id))) > 1e-6) SetData(d => d with { PowerW = v });
            }, t.Role == ElecRole.Load ? 50 : 1));
        }

        if (t.Role is ElecRole.Light or ElecRole.Load or ElecRole.Outlet or ElecRole.Switch)
        {
            var circuits = el.Circuits.ToList();
            var labels = new[] { "(sin circuito)" }.Concat(circuits.Select(c =>
                $"{c.Id} · {c.Name} · {c.BreakerA:0} A {c.Volts:0} V ({_doc.Fixtures.FirstOrDefault(f => f.Id == c.PanelId)?.ComponentId})")).ToArray();
            int sel = data.CircuitId is null ? 0 : circuits.FindIndex(c => c.Id == data.CircuitId) + 1;
            _props.Children.Add(Choice("Circuito", labels, Math.Max(0, sel), i =>
            {
                string? cid = i == 0 ? null : circuits[i - 1].Id;
                if (cid != (_doc.Electrical.Of(id)?.CircuitId)) SetData(d => d with { CircuitId = cid });
            }));
            if (circuits.Count == 0) _props.Children.Add(Info("Aún no hay circuitos: pon un centro de carga y usa «Diseñar instalación automáticamente» o añade circuitos en la pestaña Instalación eléctrica."));
        }

        if (t.Role == ElecRole.Light && t.Kind != FixtureCatalog.Emergencia.Kind)
        {
            _props.Children.Add(new TextBlock { Text = "Se enciende con", FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0) });
            var switches = _doc.Fixtures.Where(f => f.LevelId == fx.LevelId && FixtureCatalog.RoleOf(f) == ElecRole.Switch)
                .OrderBy(f => f.Position.Xy.DistanceTo(fx.Position.Xy)).Take(12).ToList();
            if (switches.Count == 0) _props.Children.Add(Info("No hay apagadores en este nivel."));
            var current = data.ControlledBy ?? [];
            foreach (var s in switches)
            {
                var st = FixtureCatalog.ByKind(s.Kind)!;
                var keys = st.Switch == SwitchKind.Double ? new[] { s.Id, s.Id + "#2" } : [s.Id];
                foreach (var k in keys)
                {
                    var cb = new CheckBox
                    {
                        Content = $"{s.ComponentId}{(k.Contains('#') ? " (tecla 2)" : st.Switch == SwitchKind.Double ? " (tecla 1)" : "")} · {st.Name} · {_doc.SpaceAt(s.LevelId, s.Position.Xy)?.Name ?? "—"}",
                        IsChecked = current.Contains(k), FontSize = 12,
                    };
                    string key = k;
                    cb.IsCheckedChanged += (_, _) => SetData(d =>
                    {
                        var list = (d.ControlledBy ?? []).ToList();
                        list.Remove(key);
                        if (cb.IsChecked == true) list.Add(key);
                        return d with { ControlledBy = list };
                    });
                    _props.Children.Add(cb);
                }
            }
        }
        else if (t.Role == ElecRole.Switch)
        {
            var lamps = el.Fixtures.Where(d => d.ControlledBy?.Any(k => ElectricalInstallation.SwitchBase(k) == id) == true)
                .Select(d => _doc.Fixtures.FirstOrDefault(f => f.Id == d.FixtureId)?.ComponentId).Where(x => x is not null).ToList();
            _props.Children.Add(Info(lamps.Count == 0 ? "No manda ninguna luminaria. Usa «Enlazar apagador con luminarias» o selecciona la luminaria y marca este apagador." : $"Manda: {string.Join(", ", lamps)}"));
        }
        else if (t.Role == ElecRole.Panel)
        {
            var mine = el.Circuits.Where(c => c.PanelId == id).ToList();
            _props.Children.Add(Info(mine.Count == 0 ? "Sin circuitos todavía." : string.Join("\n", mine.Select(c => $"{c.Id} {c.Name}: {c.BreakerA:0} A, {c.WireAwg} AWG"))));
            _props.Children.Add(Btn("+ Circuito en este tablero", () => _doc.Edit(e => e.Electrical.Circuits.Add(new Circuit(e.Electrical.NewCircuitId(), "Circuito nuevo", id)))));
        }

        if (_sim is not null)
        {
            string live = t.Role switch
            {
                ElecRole.Light => $"Simulación: {_sim.VoltageAt(id):0} V, brillo {_sim.Brightness(id) * 100:0} %",
                ElecRole.Outlet => $"Simulación: {_sim.VoltageAt(id):0} V · {_sim.OutletLoad(id).Name} · {_sim.PowerAt(id):0} W",
                ElecRole.Load => $"Simulación: {(_sim.LoadOn(id) ? "encendido" : "apagado")} · {_sim.VoltageAt(id):0} V · {_sim.PowerAt(id):0} W",
                _ => "",
            };
            if (live.Length > 0) _props.Children.Add(Info(live));
        }
    }

    private void AddRouteProps(Route route)
    {
        string id = route.Id;
        bool elec = route.ModuleId == "electrico";
        var from = _doc.Fixtures.FirstOrDefault(f => f.Id == route.FromFixtureId);
        var to = _doc.Fixtures.FirstOrDefault(f => f.Id == route.ToFixtureId);
        Title(elec ? $"Tubo {route.Id}" : $"Tubería {route.Id}");
        _props.Children.Add(Info($"{from?.ComponentId} → {to?.ComponentId}\nLongitud real: {route.Length:0.00} m\nDesnivel: {route.DeltaZ:+0.00;-0.00} m\nTramos: {route.Segments.Count}"));
        Route Cur() => _doc.Routes.First(r => r.Id == id);

        var styles = Enum.GetValues<RouteStyle>();
        _props.Children.Add(Choice("Por dónde va", styles.Select(RouteBuilder.StyleName).ToArray(), Array.IndexOf(styles, RouteBuilder.GuessStyle(route)), i =>
        {
            var r = Cur();
            if (RouteBuilder.GuessStyle(r) == styles[i] || from is null || to is null) return;
            _doc.Edit(e => e.ReplaceRoute(r, r with { Segments = RouteBuilder.Build(_doc, from, to, styles[i]) }));
        }));

        if (!elec)
        {
            _props.Children.Add(Info("Estos valores se pasan a la tubería del banco de pruebas (pérdidas de carga, altura geométrica)."));
            return;
        }
        var spec = _doc.Electrical.ConduitOf(id);
        void SetSpec(Func<ConduitSpec, ConduitSpec> change) => _doc.Edit(e =>
        {
            var s = change(e.Electrical.ConduitOf(id));
            e.Electrical.Set(s);
            var r = _doc.Routes.First(x => x.Id == id);
            if (r.Kind != ElectricalTables.RouteKind(s.Type)) e.ReplaceRoute(r, r with { Kind = ElectricalTables.RouteKind(s.Type) });
        });
        var types = Enum.GetValues<ConduitType>();
        _props.Children.Add(Choice("Tipo de tubo", types.Select(ElectricalTables.ConduitName).ToArray(), Array.IndexOf(types, spec.Type), i =>
        {
            if (_doc.Electrical.ConduitOf(id).Type != types[i]) SetSpec(s => s with { Type = types[i] });
        }));
        var sizes = ElectricalTables.ConduitSizes;
        _props.Children.Add(Choice("Diámetro", sizes.Select(s => $"{s.Inch} ({s.Mm} mm)").ToArray(), Math.Max(0, Array.FindIndex(sizes, s => s.Mm == spec.DiameterMm)), i =>
        {
            if (_doc.Electrical.ConduitOf(id).DiameterMm != sizes[i].Mm) SetSpec(s => s with { DiameterMm = sizes[i].Mm });
        }));
        var rep = LoadSchedule.Compute(_doc).Conduits.FirstOrDefault(c => c.RouteId == id);
        if (rep is not null)
        {
            _props.Children.Add(Info($"Conductores: {rep.Conductors} × {rep.WireAwg} AWG{(spec.Conductors is null ? " (calculados)" : "")}\n" +
                (spec.Type == ConduitType.Charola ? "Charola: sin límite de relleno de tubo." : $"Relleno: {rep.FillPercent:0} % (máximo {rep.MaxFillPercent:0} %) · mínimo recomendado {ElectricalTables.Inch(rep.SuggestedMm)}")));
            _props.Children.Add(Num("Conductores (0 = calcular)", spec.Conductors ?? 0, 0, 40, v =>
            {
                int? n = v < 0.5 ? null : (int)Math.Round(v);
                if (n != _doc.Electrical.ConduitOf(id).Conductors) SetSpec(s => s with { Conductors = n });
            }, 1));
        }
        _props.Children.Add(Info("Con la longitud real se calcula la caída de tensión del circuito; en el banco de pruebas se pasa al cable."));
    }

    private void AddColumnProps(Column col)
    {
        string id = col.Id;
        Title($"Columna {col.Id}");
        Column Cur() => _doc.Columns.First(c => c.Id == id);
        _props.Children.Add(Num("Ancho (m)", col.Width, 0.1, 3, v => { if (Math.Abs(Cur().Width - v) > 1e-6) _doc.Edit(e => e.Replace(Cur(), Cur() with { Width = v })); }));
        _props.Children.Add(Num("Fondo (m)", col.Depth, 0.1, 3, v => { if (Math.Abs(Cur().Depth - v) > 1e-6) _doc.Edit(e => e.Replace(Cur(), Cur() with { Depth = v })); }));
        _props.Children.Add(Num("Giro (°)", col.Rotation * 180 / Math.PI, -180, 180, v =>
        {
            double r = v * Math.PI / 180;
            if (Math.Abs(Cur().Rotation - r) > 1e-6) _doc.Edit(e => e.Replace(Cur(), Cur() with { Rotation = r }));
        }, 5));
        _props.Children.Add(Choice("Material", StructureMaterials.Column, Math.Max(0, Array.IndexOf(StructureMaterials.Column, col.Material)), i =>
        {
            if (Cur().Material != StructureMaterials.Column[i]) _doc.Edit(e => e.Replace(Cur(), Cur() with { Material = StructureMaterials.Column[i] }));
        }));
        int n = _doc.Fixtures.Count(f => f.HostId == id);
        if (n > 0) _props.Children.Add(Info($"Lleva {n} elemento(s) de instalación."));
    }

    private void AddBeamProps(Beam beam)
    {
        string id = beam.Id;
        Title($"Viga {beam.Id} · {beam.Length:0.00} m");
        Beam Cur() => _doc.Beams.First(b => b.Id == id);
        _props.Children.Add(Num("Ancho (m)", beam.Width, 0.1, 2, v => { if (Math.Abs(Cur().Width - v) > 1e-6) _doc.Edit(e => e.Replace(Cur(), Cur() with { Width = v })); }));
        _props.Children.Add(Num("Peralte (m)", beam.Depth, 0.1, 3, v => { if (Math.Abs(Cur().Depth - v) > 1e-6) _doc.Edit(e => e.Replace(Cur(), Cur() with { Depth = v })); }));
        _props.Children.Add(Choice("Material", StructureMaterials.Beam, Math.Max(0, Array.IndexOf(StructureMaterials.Beam, beam.Material)), i =>
        {
            if (Cur().Material != StructureMaterials.Beam[i]) _doc.Edit(e => e.Replace(Cur(), Cur() with { Material = StructureMaterials.Beam[i] }));
        }));
        int n = _doc.Fixtures.Count(f => f.HostId == id);
        _props.Children.Add(Info($"Parte inferior a {_doc.GetLevel(beam.LevelId).Height - beam.Depth:0.00} m del piso." + (n > 0 ? $" Lleva {n} elemento(s) colgados." : "")));
    }

    // ------------------------------------------------------------ exportar

    private async Task ExportCsvAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar cuadro de cargas", SuggestedFileName = _doc.Name + " - cuadro de cargas.csv", DefaultExtension = ".csv",
            FileTypeChoices = [new FilePickerFileType("CSV (Excel)") { Patterns = ["*.csv"] }],
        });
        if (file is null) return;
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(LoadSchedule.Compute(_doc).ToCsv())).ToArray();
            await using (var s = await file.OpenWriteAsync()) await s.WriteAsync(bytes);
            _host.SetStatus($"Cuadro de cargas exportado: {file.Name}.");
        }
        catch (Exception ex) { _host.SetStatus($"No se pudo exportar: {ex.Message}"); }
    }

    private async Task ExportSchematicAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        if (_doc.Electrical.Circuits.Count == 0) { _host.SetStatus("No hay circuitos: usa primero «Diseñar instalación automáticamente»."); return; }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar esquema eléctrico", SuggestedFileName = _doc.Name + ".selx", DefaultExtension = ".selx",
            FileTypeChoices = [new FilePickerFileType("Esquema eléctrico (Simulador múltiple)") { Patterns = ["*.selx"] }],
        });
        if (file is null) return;
        try
        {
            var sim = new InstallationSimulation(_doc);
            await using (var s = await file.OpenWriteAsync())
            await using (var w = new StreamWriter(s)) await w.WriteAsync(sim.Schematic.ToJson());
            _host.SetStatus($"Esquema exportado: {file.Name} ({sim.Schematic.Elements.Count} elementos). Ábrelo en la pestaña Instalaciones eléctricas.");
        }
        catch (Exception ex) { _host.SetStatus($"No se pudo exportar: {ex.Message}"); }
    }
}
