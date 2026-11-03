using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>
/// Reglas del asistente. Los valores por defecto son orientativos y genéricos (no corresponden a la norma de ningún
/// país concreto): se editan en la pestaña Asistente y se guardan con el proyecto, para ajustarlos a la norma local.
/// </summary>
public sealed class AdvisorSettings
{
    // Electricidad
    /// <summary>Un tomacorriente por cada tantos metros de perímetro de la habitación.</summary>
    public double OutletEveryMeters { get; set; } = 4.0;
    public int MinOutletsBedroom { get; set; } = 3;
    public int MinOutletsKitchen { get; set; } = 4;
    public int MinOutletsLiving { get; set; } = 3;
    public int MinOutletsBath { get; set; } = 1;

    // Ventilación e iluminación natural
    /// <summary>Superficie de ventana mínima como fracción del área del piso en habitaciones habitables.</summary>
    public double MinWindowRatio { get; set; } = 1.0 / 8;

    // Agua
    /// <summary>Litros por persona y día.</summary>
    public double WaterPerPersonDay { get; set; } = 150;
    /// <summary>Fracción del consumo diario que guarda el tanque elevado.</summary>
    public double ElevatedTankFraction { get; set; } = 1.0 / 3;
    /// <summary>Presión mínima en el grifo más alto, en metros de columna de agua.</summary>
    public double MinPressureHead { get; set; } = 2.0;

    // Evacuación
    /// <summary>Distancia máxima en línea recta desde cualquier punto de una habitación hasta una salida (m).</summary>
    public double MaxEvacuationDistance { get; set; } = 30;

    // Presupuesto
    public string Currency { get; set; } = "u.m.";
    public Dictionary<string, double> UnitPrices { get; set; } = new()
    {
        ["bloque"] = 0.8, ["ladrillo"] = 0.25, ["mortero_m3"] = 90, ["hormigon_m3"] = 120, ["drywall_m2"] = 8,
        ["piso_m2"] = 18, ["pintura_m2"] = 2.5, ["puerta"] = 120, ["ventana_m2"] = 90,
        ["tubo_electrico_m"] = 0.8, ["cable_m"] = 0.6, ["tomacorriente"] = 4, ["interruptor"] = 3.5, ["luminaria"] = 12,
        ["tablero"] = 60, ["tuberia_m"] = 2.2, ["punto_agua"] = 15, ["desague"] = 10, ["tanque_l"] = 0.25,
    };

    public double Price(string key) => UnitPrices.TryGetValue(key, out var p) ? p : 0;
}

public enum SuggestionKind { Electricidad, Ventilacion, Agua, Evacuacion, Escaleras }

/// <summary>Sugerencia del asistente. Si <see cref="Apply"/> no es null se puede aplicar con un clic.</summary>
public sealed record Suggestion(SuggestionKind Kind, CheckSeverity Severity, string ElementId, string Message, Action<BuildingDocument.Editor>? Apply = null);

/// <summary>Cálculo del agua del edificio.</summary>
public sealed record WaterReport(int Persons, double DailyLiters, double TankLitersNeeded, double TankLitersInstalled,
    double HighestTapElevation, double? TankBaseElevation, double MinTankBaseElevation);

/// <summary>Una línea del presupuesto.</summary>
public sealed record BudgetLine(string Group, string Item, double Quantity, string Unit, string PriceKey, double UnitPrice)
{
    public double Total => Quantity * UnitPrice;
}

/// <summary>
/// Asistente: revisa el edificio con reglas configurables y propone mejoras (tomas, luces, interruptores, ventanas,
/// tanque, evacuación) y calcula materiales y presupuesto.
/// </summary>
public static class BuildingAdvisor
{
    private static readonly HashSet<string> Habitable = ["dormitorio", "estar", "oficina", "habitacion_hotel", "cocina"];

    public static IReadOnlyList<Suggestion> Suggest(BuildingDocument doc)
    {
        var list = new List<Suggestion>();
        foreach (var sp in doc.Spaces)
        {
            if (sp.Use is "circulacion" or "exterior") { AddLightOnly(doc, sp, list); continue; }
            if (sp.Use == "sin_uso")
            {
                list.Add(new(SuggestionKind.Electricidad, CheckSeverity.Info, sp.Id,
                    $"{sp.Name}: indica su uso (herramienta «Nombrar habitación») para que el asistente sepa qué necesita."));
                continue;
            }
            SuggestOutlets(doc, sp, list);
            AddLightOnly(doc, sp, list);
            SuggestSwitch(doc, sp, list);
            SuggestWindow(doc, sp, list);
        }
        SuggestWater(doc, list);
        SuggestWiring(doc, list);
        SuggestEvacuation(doc, list);
        return list;
    }

    // ------------------------------------------------------------ electricidad

    internal static int RequiredOutlets(AdvisorSettings s, Space sp)
    {
        int byPerimeter = (int)Math.Ceiling(sp.Perimeter / s.OutletEveryMeters);
        int min = sp.Use switch
        {
            "dormitorio" or "habitacion_hotel" => s.MinOutletsBedroom,
            "cocina" => s.MinOutletsKitchen,
            "estar" or "oficina" => s.MinOutletsLiving,
            "baño" => s.MinOutletsBath,
            "nave" or "almacen" or "tecnico" => 2,
            _ => 1,
        };
        return sp.Use == "baño" ? min : Math.Max(min, byPerimeter);
    }

    private static List<PlacedFixture> FixturesIn(BuildingDocument doc, Space sp, string kind) =>
        doc.Fixtures.Where(f => f.Kind == kind && f.LevelId == sp.LevelId && Geo.Contains(sp.Outline, f.Position.Xy)).ToList();

    private static void SuggestOutlets(BuildingDocument doc, Space sp, List<Suggestion> list)
    {
        int need = RequiredOutlets(doc.Advisor, sp);
        int have = FixturesIn(doc, sp, "tomacorriente").Count;
        if (have >= need) return;
        int missing = need - have;
        double height = sp.Use is "cocina" or "baño" ? 1.10 : 0.30;
        list.Add(new(SuggestionKind.Electricidad, CheckSeverity.Warning, sp.Id,
            $"{sp.Name}: tiene {have} tomacorriente(s); se recomiendan {need} (perímetro {sp.Perimeter:0.0} m). Faltan {missing}.",
            e => PlaceAlongPerimeter(doc, e, sp, FixtureCatalog.Tomacorriente, missing, height)));
    }

    private static void AddLightOnly(BuildingDocument doc, Space sp, List<Suggestion> list)
    {
        if (FixturesIn(doc, sp, "luminaria").Count > 0) return;
        var lvl = doc.GetLevel(sp.LevelId);
        var c = Geo.Contains(sp.Outline, Geo.Centroid(sp.Outline)) ? Geo.Centroid(sp.Outline) : InteriorPoint(sp.Outline);
        int n = sp.Area > 40 ? (int)Math.Ceiling(sp.Area / 25) : 1;
        list.Add(new(SuggestionKind.Electricidad, CheckSeverity.Warning, sp.Id,
            n == 1 ? $"{sp.Name}: no tiene luminaria; se propone una en el centro." : $"{sp.Name}: {sp.Area:0} m² sin luminarias; se proponen {n} repartidas.",
            e =>
            {
                foreach (var p in GridPoints(sp.Outline, n))
                    e.AddFixture(new PlacedFixture(e.NewId("F"), "electrico", "luminaria", e.NewComponentId("E"), null, sp.LevelId, null,
                        new Point3(p, lvl.Elevation + lvl.Height - 0.05), 0, 0.3, 0.3));
            }));
    }

    private static void SuggestSwitch(BuildingDocument doc, Space sp, List<Suggestion> list)
    {
        if (FixturesIn(doc, sp, "interruptor").Count > 0) return;
        var door = DoorsOf(doc, sp).FirstOrDefault();
        if (door.Opening is null) return;
        list.Add(new(SuggestionKind.Electricidad, CheckSeverity.Info, sp.Id,
            $"{sp.Name}: falta un interruptor junto a la puerta {door.Opening.Id}.",
            e =>
            {
                var (w, o, face) = door;
                // Del lado de la cerradura (opuesto a las bisagras), 20 cm más allá del marco.
                double along = o.HingeAtStart ? o.Offset + o.Width / 2 + 0.2 : o.Offset - o.Width / 2 - 0.2;
                if (along < 0.1 || along > w.Length - 0.1) along = o.HingeAtStart ? o.Offset - o.Width / 2 - 0.2 : o.Offset + o.Width / 2 + 0.2;
                along = Math.Clamp(along, 0.1, w.Length - 0.1);
                var pos = doc.GetWallPoint(w.Id, face, along, FixtureCatalog.Interruptor.DefaultHeight);
                e.AddFixture(new PlacedFixture(e.NewId("F"), "electrico", "interruptor", e.NewComponentId("S"), null, sp.LevelId, w.Id, pos));
            }));
    }

    // ------------------------------------------------------------ ventilación

    private static void SuggestWindow(BuildingDocument doc, Space sp, List<Suggestion> list)
    {
        double windows = BorderOpenings(doc, sp).Where(x => x.Opening.Kind == OpeningKind.Window).Sum(x => x.Opening.Width * x.Opening.Height);
        if (sp.Use == "baño")
        {
            if (windows <= 0)
                list.Add(new(SuggestionKind.Ventilacion, CheckSeverity.Info, sp.Id, $"{sp.Name}: sin ventana; prevé un extractor o una ventana alta."));
            return;
        }
        if (!Habitable.Contains(sp.Use)) return;
        double need = sp.Area * doc.Advisor.MinWindowRatio;
        if (windows + 1e-6 >= need) return;
        list.Add(new(SuggestionKind.Ventilacion, CheckSeverity.Warning, sp.Id,
            $"{sp.Name}: {windows:0.00} m² de ventana para {sp.Area:0.0} m² de piso; se recomiendan {need:0.00} m² (1/{1 / doc.Advisor.MinWindowRatio:0} del piso)."));
    }

    // ------------------------------------------------------------ agua

    public static WaterReport Water(BuildingDocument doc)
    {
        var s = doc.Advisor;
        int persons = 0;
        foreach (var sp in doc.Spaces)
            persons += sp.Use switch
            {
                "dormitorio" => 2,
                "habitacion_hotel" => 2,
                "oficina" => (int)Math.Ceiling(sp.Area / 10),
                "nave" => (int)Math.Ceiling(sp.Area / 40),
                _ => 0,
            };
        if (persons == 0 && doc.Spaces.Count > 0) persons = 2;
        double daily = persons * s.WaterPerPersonDay;
        double need = daily * s.ElevatedTankFraction;
        var tanks = doc.Fixtures.Where(f => f.Kind == "tanque").ToList();
        double installed = tanks.Sum(TankLiters);
        var taps = doc.Fixtures.Where(f => f.Kind == "punto_agua").ToList();
        double highest = taps.Count == 0
            ? doc.Levels.Max(l => l.Elevation) + 1.0
            : taps.Max(t => t.Position.Z);
        double? tankBase = tanks.Count == 0 ? null : tanks.Min(t => t.Position.Z);
        return new WaterReport(persons, daily, need, installed, highest, tankBase, highest + s.MinPressureHead);
    }

    /// <summary>Capacidad aproximada de un tanque colocado: cilindro de diámetro <c>Width</c> y 1,2 m de alto.</summary>
    public static double TankLiters(PlacedFixture f) => Math.PI * Math.Pow(f.Width / 2, 2) * 1.2 * 1000;

    /// <summary>Diámetro de un tanque de 1,2 m de alto para <paramref name="liters"/> litros, redondeado a 10 cm.</summary>
    public static double TankDiameterFor(double liters) => Math.Max(0.6, Math.Ceiling(Math.Sqrt(liters / 1000 / 1.2 / Math.PI) * 2 * 10) / 10);

    private static void SuggestWater(BuildingDocument doc, List<Suggestion> list)
    {
        var w = Water(doc);
        var wet = doc.Spaces.Where(s => s.Use is "baño" or "cocina").ToList();
        if (wet.Count == 0) return;
        if (w.TankLitersInstalled + 1 < w.TankLitersNeeded)
        {
            var top = doc.Levels.MaxBy(l => l.Elevation)!;
            var target = wet.OrderByDescending(s => doc.GetLevel(s.LevelId).Elevation).First();
            var at = Geo.Contains(target.Outline, Geo.Centroid(target.Outline)) ? Geo.Centroid(target.Outline) : InteriorPoint(target.Outline);
            double missing = w.TankLitersNeeded - w.TankLitersInstalled;
            double diameter = TankDiameterFor(missing);
            double z = Math.Max(top.Elevation + top.Height, w.MinTankBaseElevation);
            list.Add(new(SuggestionKind.Agua, CheckSeverity.Warning, "",
                $"{w.Persons} personas × {doc.Advisor.WaterPerPersonDay:0} L/día = {w.DailyLiters:0} L/día. " +
                $"El tanque elevado debería guardar {w.TankLitersNeeded:0} L y hay {w.TankLitersInstalled:0} L. Se propone un tanque de Ø {diameter:0.0} m (~{TankLiters(new PlacedFixture("", "", "", "", null, "", null, default, 0, diameter, diameter)):0} L) sobre la cubierta.",
                e => e.AddFixture(new PlacedFixture(e.NewId("F"), "hidraulica", "tanque", e.NewComponentId("T"), null, top.Id, null,
                    new Point3(at, z), 0, diameter, diameter))));
        }
        if (w.TankBaseElevation is { } baseZ && baseZ < w.MinTankBaseElevation - 1e-6)
            list.Add(new(SuggestionKind.Agua, CheckSeverity.Warning, "",
                $"La base del tanque está a {baseZ:0.00} m y el grifo más alto a {w.HighestTapElevation:0.00} m; " +
                $"para {doc.Advisor.MinPressureHead:0.0} m.c.a. de presión hay que subirlo a {w.MinTankBaseElevation:0.00} m o poner una bomba."));
        var tapsNeeded = wet.Where(sp => FixturesIn(doc, sp, "punto_agua").Count == 0).ToList();
        foreach (var sp in tapsNeeded)
            list.Add(new(SuggestionKind.Agua, CheckSeverity.Warning, sp.Id, $"{sp.Name}: no tiene punto de agua.",
                e => PlaceAlongPerimeter(doc, e, sp, FixtureCatalog.PuntoAgua, sp.Use == "baño" ? 2 : 1, 0.60)));
    }

    // ------------------------------------------------------------ conexiones (tablero → elementos, tanque → grifos)

    private static void SuggestWiring(BuildingDocument doc, List<Suggestion> list)
    {
        var routed = doc.Routes.SelectMany(r => new[] { r.FromFixtureId, r.ToFixtureId }).ToHashSet();
        var elec = doc.Fixtures.Where(f => f.Kind is "tomacorriente" or "interruptor" or "luminaria").ToList();
        var board = doc.Fixtures.FirstOrDefault(f => f.Kind == "tablero");
        if (elec.Count > 0 && board is null)
        {
            var ground = doc.Levels.MinBy(l => l.Elevation)!;
            var entry = ExteriorDoors(doc, ground.Id).FirstOrDefault();
            if (entry.Wall is not null)
                list.Add(new(SuggestionKind.Electricidad, CheckSeverity.Warning, entry.Opening.Id,
                    "No hay tablero eléctrico: se propone uno junto a la entrada, conectado a todos los elementos.",
                    e =>
                    {
                        var (w, o) = entry;
                        var spaces = doc.Spaces.Where(sp => sp.LevelId == w.LevelId).ToList();
                        var c = w.Start + w.Direction * o.Offset;
                        var face = spaces.Any(sp => Geo.Contains(sp.Outline, c + w.Direction.Perp() * (w.Thickness / 2 + 0.1))) ? WallFace.A : WallFace.B;
                        double along = o.Offset + o.Width / 2 + 0.6;
                        if (along > w.Length - 0.2) along = o.Offset - o.Width / 2 - 0.6;
                        var q = e.AddFixture(new PlacedFixture(e.NewId("F"), "electrico", "tablero", e.NewComponentId("Q"), null, w.LevelId, w.Id,
                            doc.GetWallPoint(w.Id, face, Math.Clamp(along, 0.2, w.Length - 0.2), FixtureCatalog.Tablero.DefaultHeight)));
                        foreach (var f in doc.Fixtures.Where(f => f.Kind is "tomacorriente" or "interruptor" or "luminaria").ToList())
                            e.AddRoute(new Route(e.NewId("R"), "electrico", "tubo_electrico", q.Id, f.Id, RouteBuilder.ViaCeiling(doc, q, f)));
                    }));
        }
        else if (board is not null && elec.Any(f => !routed.Contains(f.Id)))
        {
            int n = elec.Count(f => !routed.Contains(f.Id));
            list.Add(new(SuggestionKind.Electricidad, CheckSeverity.Info, board.Id,
                $"{n} elemento(s) eléctricos sin conectar al tablero {board.ComponentId}: se propone trazar sus rutas.",
                e =>
                {
                    foreach (var f in doc.Fixtures.Where(f => f.Kind is "tomacorriente" or "interruptor" or "luminaria" && !routed.Contains(f.Id)).ToList())
                        e.AddRoute(new Route(e.NewId("R"), "electrico", "tubo_electrico", board.Id, f.Id, RouteBuilder.ViaCeiling(doc, board, f)));
                }));
        }

        var tank = doc.Fixtures.Where(f => f.Kind == "tanque").MaxBy(f => f.Position.Z);
        var taps = doc.Fixtures.Where(f => f.Kind == "punto_agua" && !routed.Contains(f.Id)).ToList();
        if (tank is not null && taps.Count > 0)
            list.Add(new(SuggestionKind.Agua, CheckSeverity.Info, tank.Id,
                $"{taps.Count} punto(s) de agua sin tubería desde el tanque {tank.ComponentId}: se propone trazarla.",
                e =>
                {
                    foreach (var f in doc.Fixtures.Where(f => f.Kind == "punto_agua" && !routed.Contains(f.Id)).ToList())
                        e.AddRoute(new Route(e.NewId("R"), "hidraulica", "tuberia", tank.Id, f.Id, RouteBuilder.ViaCeiling(doc, tank, f)));
                }));
    }

    // ------------------------------------------------------------ evacuación

    private static void SuggestEvacuation(BuildingDocument doc, List<Suggestion> list)
    {
        var levels = doc.Levels.OrderBy(l => l.Elevation).ToList();
        if (levels.Count == 0) return;
        var ground = levels[0];
        foreach (var lvl in levels)
        {
            var spaces = doc.Spaces.Where(s => s.LevelId == lvl.Id).ToList();
            if (spaces.Count == 0) continue;
            List<Point2> exits;
            string exitName;
            if (lvl == ground)
            {
                exits = ExteriorDoors(doc, lvl.Id).Select(x => x.Wall.Start + x.Wall.Direction * x.Opening.Offset).ToList();
                exitName = "puerta exterior";
            }
            else
            {
                var below = levels[levels.IndexOf(lvl) - 1];
                // Las escaleras que bajan desde este nivel: las del nivel inferior llegan aquí por su final.
                exits = doc.Stairs.Where(s => s.LevelId == below.Id).Select(s => s.Start + s.Direction * s.Design.RunLength)
                    .Concat(doc.Stairs.Where(s => s.LevelId == lvl.Id).Select(s => s.Start)).ToList();
                exitName = "escalera";
            }
            if (exits.Count == 0)
            {
                list.Add(new(SuggestionKind.Evacuacion, CheckSeverity.Error, spaces[0].Id,
                    lvl == ground ? $"{lvl.Name}: no hay ninguna puerta al exterior." : $"{lvl.Name}: no hay escalera para bajar; no se puede evacuar."));
                continue;
            }
            foreach (var sp in spaces)
            {
                double worst = sp.Outline.Max(p => exits.Min(x => x.DistanceTo(p)));
                if (worst > doc.Advisor.MaxEvacuationDistance)
                    list.Add(new(SuggestionKind.Evacuacion, CheckSeverity.Warning, sp.Id,
                        $"{sp.Name}: el punto más alejado está a {worst:0.0} m (en línea recta) de una {exitName}; máximo {doc.Advisor.MaxEvacuationDistance:0} m."));
            }
            if (lvl == ground && doc.Type is BuildingType.Hotel or BuildingType.Industria or BuildingType.Comercio && exits.Count < 2)
                list.Add(new(SuggestionKind.Evacuacion, CheckSeverity.Warning, spaces[0].Id,
                    $"{lvl.Name}: una sola salida al exterior; en un edificio de uso público conviene al menos una segunda salida."));
        }
    }

    // ------------------------------------------------------------ materiales y presupuesto

    public static IReadOnlyList<BudgetLine> Budget(BuildingDocument doc)
    {
        var s = doc.Advisor;
        var lines = new List<BudgetLine>();
        void Add(string group, string item, double qty, string unit, string key)
        {
            if (qty > 1e-9) lines.Add(new(group, item, qty, unit, key, s.Price(key)));
        }

        foreach (var g in doc.Walls.GroupBy(w => w.Material))
        {
            double area = g.Sum(w => w.Length * w.Height - doc.Openings.Where(o => o.HostWallId == w.Id).Sum(o => o.Width * o.Height));
            double volume = g.Sum(w => (w.Length * w.Height - doc.Openings.Where(o => o.HostWallId == w.Id).Sum(o => o.Width * o.Height)) * w.Thickness);
            switch (g.Key)
            {
                case "bloque":
                    Add("Muros", "Bloques (40×20 cm)", Math.Ceiling(area * 12.5 * 1.05), "ud", "bloque");
                    Add("Muros", "Mortero para bloques", area * 0.015, "m³", "mortero_m3");
                    break;
                case "ladrillo":
                    Add("Muros", "Ladrillos", Math.Ceiling(area * 55 * 1.05), "ud", "ladrillo");
                    Add("Muros", "Mortero para ladrillos", area * 0.03, "m³", "mortero_m3");
                    break;
                case "hormigón":
                    Add("Muros", "Hormigón en muros", volume, "m³", "hormigon_m3");
                    break;
                case "drywall":
                    Add("Muros", "Placa de yeso (dos caras)", area * 2 * 1.1, "m²", "drywall_m2");
                    break;
                default:
                    Add("Muros", $"Muro de {g.Key}", area, "m²", "drywall_m2");
                    break;
            }
            Add("Acabados", $"Pintura de muros de {g.Key} (dos caras)", area * 2, "m²", "pintura_m2");
        }
        Add("Estructura", "Hormigón de losas y cubiertas", doc.Slabs.Sum(sl => Math.Abs(Geo.SignedArea(sl.Outline)) * sl.Thickness), "m³", "hormigon_m3");
        Add("Acabados", "Piso terminado (+5 %)", doc.Spaces.Sum(sp => sp.Area) * 1.05, "m²", "piso_m2");
        Add("Carpintería", "Puertas", doc.Openings.Count(o => o.Kind == OpeningKind.Door), "ud", "puerta");
        Add("Carpintería", "Ventanas", doc.Openings.Where(o => o.Kind == OpeningKind.Window).Sum(o => o.Width * o.Height), "m²", "ventana_m2");

        double conduit = doc.Routes.Where(r => r.ModuleId == "electrico").Sum(r => r.Length);
        Add("Electricidad", "Tubo eléctrico", conduit * 1.1, "m", "tubo_electrico_m");
        Add("Electricidad", "Cable (fase, neutro y tierra)", conduit * 3 * 1.1, "m", "cable_m");
        foreach (var (kind, key, name) in new[] { ("tomacorriente", "tomacorriente", "Tomacorrientes"), ("interruptor", "interruptor", "Interruptores"), ("luminaria", "luminaria", "Luminarias"), ("tablero", "tablero", "Tableros") })
            Add("Electricidad", name, doc.Fixtures.Count(f => f.Kind == kind), "ud", key);

        Add("Hidráulica", "Tubería", doc.Routes.Where(r => r.ModuleId == "hidraulica").Sum(r => r.Length) * 1.1, "m", "tuberia_m");
        Add("Hidráulica", "Puntos de agua", doc.Fixtures.Count(f => f.Kind == "punto_agua"), "ud", "punto_agua");
        Add("Hidráulica", "Desagües", doc.Fixtures.Count(f => f.Kind == "desague"), "ud", "desague");
        Add("Hidráulica", "Tanques (capacidad)", doc.Fixtures.Where(f => f.Kind == "tanque").Sum(TankLiters), "L", "tanque_l");
        return lines;
    }

    // ------------------------------------------------------------ colocación automática

    /// <summary>Muros que forman el borde de una habitación, con la cara que mira hacia dentro.</summary>
    internal static List<(Wall Wall, WallFace Face, double From, double To)> BorderWalls(BuildingDocument doc, Space sp)
    {
        var res = new List<(Wall, WallFace, double, double)>();
        for (int i = 0; i < sp.Outline.Count; i++)
        {
            var a = sp.Outline[i];
            var b = sp.Outline[(i + 1) % sp.Outline.Count];
            var mid = (a + b) * 0.5;
            var w = doc.Walls.FirstOrDefault(w => w.LevelId == sp.LevelId && Geo.DistanceToSegment(w.Start, w.End, mid) < 0.02
                && Math.Abs(Point2.Cross(w.Direction, (b - a).Normalized())) < 1e-3);
            if (w is null) continue;
            double ta = Geo.ProjectT(w.Start, w.End, a) * w.Length, tb = Geo.ProjectT(w.Start, w.End, b) * w.Length;
            // Cara: la que queda del lado del interior de la habitación.
            var probe = mid + (b - a).Normalized().Perp() * 0.05; // izquierda del borde: interior (polígono antihorario)
            var face = Point2.Cross(w.End - w.Start, probe - w.Start) >= 0 ? WallFace.A : WallFace.B;
            res.Add((w, face, Math.Min(ta, tb), Math.Max(ta, tb)));
        }
        return res;
    }

    private static IEnumerable<(Wall Wall, Opening Opening, WallFace Face)> BorderOpenings(BuildingDocument doc, Space sp)
    {
        foreach (var (w, face, from, to) in BorderWalls(doc, sp))
            foreach (var o in doc.Openings.Where(o => o.HostWallId == w.Id && o.Offset > from - 1e-6 && o.Offset < to + 1e-6))
                yield return (w, o, face);
    }

    private static IEnumerable<(Wall Wall, Opening Opening, WallFace Face)> DoorsOf(BuildingDocument doc, Space sp) =>
        BorderOpenings(doc, sp).Where(x => x.Opening.Kind == OpeningKind.Door);

    /// <summary>Puertas en muros que dan al exterior (un lado del muro no pertenece a ninguna habitación).</summary>
    internal static IEnumerable<(Wall Wall, Opening Opening)> ExteriorDoors(BuildingDocument doc, string levelId)
    {
        var spaces = doc.Spaces.Where(s => s.LevelId == levelId).ToList();
        foreach (var o in doc.Openings.Where(o => o.Kind == OpeningKind.Door))
        {
            var w = doc.Walls.FirstOrDefault(w => w.Id == o.HostWallId && w.LevelId == levelId);
            if (w is null) continue;
            var c = w.Start + w.Direction * o.Offset;
            var n = w.Direction.Perp() * (w.Thickness / 2 + 0.1);
            bool a = spaces.Any(s => Geo.Contains(s.Outline, c + n));
            bool b = spaces.Any(s => Geo.Contains(s.Outline, c - n));
            if (a != b) yield return (w, o);
        }
    }

    /// <summary>
    /// Reparte <paramref name="count"/> elementos de pared por el perímetro de la habitación, lejos de esquinas, huecos,
    /// hojas de puerta y de los elementos que ya hay.
    /// </summary>
    internal static void PlaceAlongPerimeter(BuildingDocument doc, BuildingDocument.Editor e, Space sp, FixtureType type, int count, double height)
    {
        var candidates = new List<(Wall Wall, WallFace Face, double Along, double PerimPos)>();
        double perimPos = 0;
        foreach (var (w, face, from, to) in BorderWalls(doc, sp))
        {
            for (double t = from + 0.35; t <= to - 0.35 + 1e-9; t += 0.25)
            {
                var pos = doc.GetWallPoint(w.Id, face, t, height);
                bool blocked = doc.Openings.Where(o => o.HostWallId == w.Id).Any(o =>
                    (t > o.Offset - o.Width / 2 - 0.15 && t < o.Offset + o.Width / 2 + 0.15 && height > o.SillHeight - 0.1 && height < o.SillHeight + o.Height + 0.1)
                    || (o.Kind == OpeningKind.Door && height < o.Height && BuildingChecks.DoorSwingCovers(w, o, pos.Xy)));
                // Huecos de otros muros cuya hoja barre este punto.
                blocked |= doc.Openings.Where(o => o.Kind == OpeningKind.Door && o.HostWallId != w.Id).Any(o =>
                {
                    var hw = doc.Walls.FirstOrDefault(x => x.Id == o.HostWallId && x.LevelId == sp.LevelId);
                    return hw is not null && BuildingChecks.DoorSwingCovers(hw, o, pos.Xy);
                });
                blocked |= doc.Fixtures.Any(f => f.LevelId == sp.LevelId && f.Position.DistanceTo(pos) < 0.5);
                if (!blocked) candidates.Add((w, face, t, perimPos + (t - from)));
            }
            perimPos += to - from;
        }
        if (candidates.Count == 0) return;
        var used = new List<Point3>();
        for (int i = 0; i < count; i++)
        {
            double target = (i + 0.5) * perimPos / count;
            var pick = candidates.Where(c => used.All(u => doc.GetWallPoint(c.Wall.Id, c.Face, c.Along, height).DistanceTo(u) > 0.8))
                .OrderBy(c => Math.Abs(c.PerimPos - target)).FirstOrDefault();
            if (pick.Wall is null) break;
            var pos = doc.GetWallPoint(pick.Wall.Id, pick.Face, pick.Along, height);
            used.Add(pos);
            e.AddFixture(new PlacedFixture(e.NewId("F"), type.ModuleId, type.Kind, e.NewComponentId(type.ComponentPrefix), null, sp.LevelId, pick.Wall.Id, pos));
        }
    }

    /// <summary>Un punto que seguro está dentro del polígono (centroide, o el centro de un triángulo interior).</summary>
    internal static Point2 InteriorPoint(IReadOnlyList<Point2> poly)
    {
        var c = Geo.Centroid(poly);
        if (Geo.Contains(poly, c)) return c;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = (poly[i] + poly[(i + 1) % poly.Count] + poly[(i + 2) % poly.Count]) * (1.0 / 3);
            if (Geo.Contains(poly, p)) return p;
        }
        return poly[0];
    }

    /// <summary>Hasta <paramref name="n"/> puntos en rejilla dentro del polígono (para repartir luminarias).</summary>
    internal static IEnumerable<Point2> GridPoints(IReadOnlyList<Point2> poly, int n)
    {
        if (n <= 1) { yield return InteriorPoint(poly); yield break; }
        double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X), minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        int cols = Math.Max(1, (int)Math.Round(Math.Sqrt(n * w / Math.Max(h, 0.1))));
        int rows = (int)Math.Ceiling(n / (double)cols);
        int given = 0;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            var p = new Point2(minX + w * (c + 0.5) / cols, minY + h * (r + 0.5) / rows);
            if (Geo.Contains(poly, p) && given++ < n) yield return p;
        }
    }
}
