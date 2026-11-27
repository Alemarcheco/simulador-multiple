using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>Cómo se monta un elemento de instalación.</summary>
public enum MountKind
{
    /// <summary>En la cara de un muro o de una columna, a <see cref="FixtureType.DefaultHeight"/> sobre el piso.</summary>
    Wall,
    /// <summary>En el techo (o bajo una viga si se coloca encima de ella); la altura es negativa, bajo el techo.</summary>
    Ceiling,
    /// <summary>En el piso.</summary>
    Floor,
    /// <summary>Colgado de una viga o del techo, típico de naves (campanas industriales).</summary>
    Hung,
    /// <summary>Elemento libre en el recinto (tanques).</summary>
    Free,
}

/// <summary>Papel del elemento en la instalación eléctrica (decide cómo se simula y cómo cuenta en el cuadro de cargas).</summary>
public enum ElecRole { None, Light, Outlet, Switch, Box, Panel, Meter, Load }

/// <summary>Tipo de apagador: cuántas lámparas manda y cómo se conecta.</summary>
public enum SwitchKind { None, Single, ThreeWay, FourWay, Double, Dimmer, Motion }

/// <summary>
/// Tipo de elemento de instalación que se puede colocar desde la pestaña Edificación.
/// <paramref name="DefaultHeight"/>: altura sobre el piso (muro), bajo el techo (techo, valor negativo) o bajo la viga (colgado).
/// <paramref name="PowerW"/>: potencia (luminarias, cargas) o carga de cálculo en VA (contactos: 180 VA según la NOM-001).
/// <paramref name="Phases"/>: 1 = 127 V fase-neutro, 2 = 220 V entre dos fases, 3 = 220 V trifásico.
/// </summary>
public sealed record FixtureType(string Kind, string Name, string ModuleId, string ComponentPrefix, double DefaultHeight, string Icon,
    MountKind Mount = MountKind.Wall, string Group = "", ElecRole Role = ElecRole.None, double PowerW = 0, int Phases = 1,
    SwitchKind Switch = SwitchKind.None, string Help = "", double Size = 0.1)
{
    /// <summary>Tensión nominal en México (127 V fase-neutro, 220 V entre fases).</summary>
    public double Volts => Phases == 1 ? 127 : 220;
}

public static class FixtureCatalog
{
    public const string GLights = "Luminarias";
    public const string GOutlets = "Contactos";
    public const string GSwitches = "Apagadores";
    public const string GBoxes = "Cajas y registros";
    public const string GPanels = "Tablero y acometida";
    public const string GLoads = "Equipos fijos";
    public const string GWater = "Hidráulica";

    // ------------------------------------------------------------ luminarias
    public static readonly FixtureType Luminaria = new("luminaria", "Plafón / foco de techo", "electrico", "E", -0.05, "💡",
        MountKind.Ceiling, GLights, ElecRole.Light, 15, Help: "Salida de centro con plafón o foco LED de 15 W.", Size: 0.30);
    public static readonly FixtureType Colgante = new("lampara_colgante", "Lámpara colgante", "electrico", "E", -0.70, "🛎",
        MountKind.Ceiling, GLights, ElecRole.Light, 20, Help: "Lámpara suspendida (comedor, barra). Cuelga 70 cm del techo.", Size: 0.40);
    public static readonly FixtureType Empotrado = new("empotrado_led", "Empotrado LED (downlight)", "electrico", "E", -0.01, "◉",
        MountKind.Ceiling, GLights, ElecRole.Light, 12, Help: "Luminaria redonda empotrada en plafón o falso techo.", Size: 0.17);
    public static readonly FixtureType PanelLed = new("panel_led", "Panel LED 60×60", "electrico", "E", -0.01, "▣",
        MountKind.Ceiling, GLights, ElecRole.Light, 40, Help: "Panel para oficinas y locales, en plafón reticular.", Size: 0.60);
    public static readonly FixtureType Lineal = new("lineal_led", "Lineal LED / tubo de 1.20 m", "electrico", "E", -0.05, "▬",
        MountKind.Ceiling, GLights, ElecRole.Light, 36, Help: "Luminaria de tubo LED (antes fluorescente): talleres, bodegas, cocheras.", Size: 1.20);
    public static readonly FixtureType Spot = new("spot", "Spot dirigible", "electrico", "E", -0.02, "✧",
        MountKind.Ceiling, GLights, ElecRole.Light, 7, Help: "Luz de acento orientable (riel o base de techo).", Size: 0.12);
    public static readonly FixtureType Arbotante = new("arbotante", "Arbotante de pared", "electrico", "E", 2.00, "◐",
        MountKind.Wall, GLights, ElecRole.Light, 10, Help: "Luminaria de muro interior o exterior, a unos 2 m.", Size: 0.20);
    public static readonly FixtureType Reflector = new("reflector", "Reflector LED", "electrico", "E", 3.00, "⛯",
        MountKind.Wall, GLights, ElecRole.Light, 50, Help: "Iluminación exterior de patios, fachadas y estacionamientos.", Size: 0.25);
    public static readonly FixtureType Campana = new("campana_industrial", "Campana industrial LED", "electrico", "E", 0.50, "🔔",
        MountKind.Hung, GLights, ElecRole.Light, 150, Help: "Luminaria de gran altura (high bay) para naves. Se cuelga de la viga o del techo.", Size: 0.45);
    public static readonly FixtureType Emergencia = new("emergencia", "Luminaria de emergencia", "electrico", "E", 2.30, "🆘",
        MountKind.Wall, GLights, ElecRole.Light, 5, Help: "Con batería: se enciende sola cuando falta la energía del circuito.", Size: 0.30);
    public static readonly FixtureType Ventilador = new("ventilador_techo", "Ventilador de techo", "electrico", "E", -0.40, "✣",
        MountKind.Ceiling, GLights, ElecRole.Light, 75, Help: "Se manda con apagador igual que una lámpara.", Size: 1.20);

    // ------------------------------------------------------------ contactos
    public static readonly FixtureType Tomacorriente = new("tomacorriente", "Contacto dúplex 127 V", "electrico", "X", 0.30, "🔌",
        MountKind.Wall, GOutlets, ElecRole.Outlet, 180, Help: "Contacto doble polarizado con tierra (NEMA 5-15). Cuenta 180 VA en el cuadro de cargas.");
    public static readonly FixtureType ContactoGfci = new("contacto_gfci", "Contacto con protección GFCI", "electrico", "X", 1.10, "🛡",
        MountKind.Wall, GOutlets, ElecRole.Outlet, 180, Help: "Con interruptor de falla a tierra (ICFT). Obligatorio en baños, cocinas, cocheras y exteriores.");
    public static readonly FixtureType Contacto220 = new("contacto_220", "Contacto 220 V", "electrico", "X", 0.30, "⚡",
        MountKind.Wall, GOutlets, ElecRole.Outlet, 180, 2, Help: "Para estufa eléctrica, secadora o aire acondicionado (NEMA 6-20).");
    public static readonly FixtureType ContactoTrifasico = new("contacto_trifasico", "Contacto trifásico industrial", "electrico", "X", 1.20, "⏣",
        MountKind.Wall, GOutlets, ElecRole.Outlet, 180, 3, Help: "Contacto de 3 fases + tierra, 220 V, para máquinas portátiles de taller.");
    public static readonly FixtureType ContactoPiso = new("contacto_piso", "Contacto de piso", "electrico", "X", 0, "⊡",
        MountKind.Floor, GOutlets, ElecRole.Outlet, 180, Help: "Caja de piso con tapa, en oficinas y salas grandes.", Size: 0.12);
    public static readonly FixtureType ContactoTierraAislada = new("contacto_tierra_aislada", "Contacto de tierra aislada", "electrico", "X", 0.30, "🖥",
        MountKind.Wall, GOutlets, ElecRole.Outlet, 180, Help: "Color naranja, para equipo de cómputo y electrónica sensible.");

    // ------------------------------------------------------------ apagadores
    public static readonly FixtureType Interruptor = new("interruptor", "Apagador sencillo", "electrico", "S", 1.20, "⏻",
        MountKind.Wall, GSwitches, ElecRole.Switch, Switch: SwitchKind.Single, Help: "Enciende y apaga una o varias lámparas desde un lugar.");
    public static readonly FixtureType Escalera = new("apagador_escalera", "Apagador de escalera (3 vías)", "electrico", "S", 1.20, "⇅",
        MountKind.Wall, GSwitches, ElecRole.Switch, Switch: SwitchKind.ThreeWay, Help: "Se usan en pareja para mandar la misma luz desde dos lugares (escaleras, pasillos).");
    public static readonly FixtureType CuatroVias = new("apagador_4vias", "Apagador de 4 vías", "electrico", "S", 1.20, "✚",
        MountKind.Wall, GSwitches, ElecRole.Switch, Switch: SwitchKind.FourWay, Help: "Va entre dos de escalera para mandar la luz desde tres o más lugares.");
    public static readonly FixtureType Doble = new("apagador_doble", "Apagador doble", "electrico", "S", 1.20, "⏻⏻",
        MountKind.Wall, GSwitches, ElecRole.Switch, Switch: SwitchKind.Double, Help: "Dos apagadores en una placa: cada tecla manda su grupo de lámparas.");
    public static readonly FixtureType Atenuador = new("atenuador", "Atenuador (dimmer)", "electrico", "S", 1.20, "☼",
        MountKind.Wall, GSwitches, ElecRole.Switch, Switch: SwitchKind.Dimmer, Help: "Regula la intensidad de la luz. En la simulación, cada clic baja un paso: 100 %, 60 %, 30 %, apagado.");
    public static readonly FixtureType Sensor = new("sensor_movimiento", "Sensor de presencia", "electrico", "S", 2.40, "👁",
        MountKind.Wall, GSwitches, ElecRole.Switch, Switch: SwitchKind.Motion, Help: "Enciende la luz al detectar movimiento y la apaga sola (en la simulación, 20 s después de cada clic).");

    // ------------------------------------------------------------ cajas
    public static readonly FixtureType Chalupa = new("chalupa", "Caja chalupa (2×4\")", "electrico", "CJ", 0.30, "▯",
        MountKind.Wall, GBoxes, ElecRole.Box, Help: "Caja rectangular donde va un contacto o un apagador.", Size: 0.06);
    public static readonly FixtureType CajaCuadrada = new("caja_cuadrada", "Caja cuadrada de conexión (4×4\")", "electrico", "CJ", 2.40, "▢",
        MountKind.Wall, GBoxes, ElecRole.Box, Help: "Caja de conexión o derivación con tapa: aquí se empalman los conductores.", Size: 0.10);
    public static readonly FixtureType CajaOctagonal = new("caja_octagonal", "Caja octagonal (salida de centro)", "electrico", "CJ", -0.01, "⯃",
        MountKind.Ceiling, GBoxes, ElecRole.Box, Help: "Caja de techo para colgar la lámpara.", Size: 0.10);
    public static readonly FixtureType CajaPaso = new("caja_paso", "Caja de paso / registro", "electrico", "CJ", 2.40, "⊞",
        MountKind.Wall, GBoxes, ElecRole.Box, Help: "Registro para jalar el cable en tramos largos o con muchas curvas (máx. 360° entre registros).", Size: 0.20);
    public static readonly FixtureType RegistroPiso = new("registro_piso", "Registro de piso", "electrico", "CJ", 0, "⊟",
        MountKind.Floor, GBoxes, ElecRole.Box, Help: "Registro en piso o banqueta para tubería enterrada.", Size: 0.40);
    public static readonly FixtureType Condulet = new("condulet", "Condulet (caja industrial)", "electrico", "CJ", 1.20, "⊓",
        MountKind.Wall, GBoxes, ElecRole.Box, Help: "Caja de aluminio roscada para tubería conduit metálica a la vista, en columnas o muros de nave.", Size: 0.12);

    // ------------------------------------------------------------ tablero y acometida
    public static readonly FixtureType Tablero = new("tablero", "Centro de carga / tablero", "electrico", "Q", 1.50, "▦",
        MountKind.Wall, GPanels, ElecRole.Panel, Help: "De aquí salen los circuitos, cada uno con su interruptor termomagnético.", Size: 0.35);
    public static readonly FixtureType Medidor = new("base_medidor", "Base de medidor (acometida)", "electrico", "QM", 1.70, "⌚",
        MountKind.Wall, GPanels, ElecRole.Meter, Help: "Base para el medidor de la compañía suministradora, en el límite del predio.", Size: 0.25);
    public static readonly FixtureType Desconectador = new("interruptor_seguridad", "Interruptor de seguridad", "electrico", "QS", 1.50, "⏏",
        MountKind.Wall, GPanels, ElecRole.Box, Help: "Desconectador con fusibles junto a motores o en la acometida de la nave.", Size: 0.30);

    // ------------------------------------------------------------ equipos fijos
    public static readonly FixtureType AireAcondicionado = new("aire_acondicionado", "Minisplit / aire acondicionado", "electrico", "M", 2.20, "❄",
        MountKind.Wall, GLoads, ElecRole.Load, 1200, 2, Help: "Equipo de 1 ton a 220 V. Lleva su propio circuito.", Size: 0.80);
    public static readonly FixtureType Calentador = new("calentador", "Calentador eléctrico de agua", "electrico", "M", 1.50, "♨",
        MountKind.Wall, GLoads, ElecRole.Load, 3000, 2, Help: "Calentador de paso a 220 V.", Size: 0.40);
    public static readonly FixtureType Bomba = new("bomba_agua", "Bomba de agua", "electrico", "M", 0, "⛲",
        MountKind.Floor, GLoads, ElecRole.Load, 750, Help: "Bomba de 1 HP a 127 V para subir agua al tinaco.", Size: 0.40);
    public static readonly FixtureType Motor = new("motor_industrial", "Motor / máquina trifásica", "electrico", "M", 0, "⚙",
        MountKind.Floor, GLoads, ElecRole.Load, 5600, 3, Help: "Máquina de taller de 7.5 HP a 220 V trifásico.", Size: 0.80);

    // ------------------------------------------------------------ hidráulica
    public static readonly FixtureType PuntoAgua = new("punto_agua", "Punto de agua", "hidraulica", "PA", 0.60, "🚰", Group: GWater);
    public static readonly FixtureType Desague = new("desague", "Desagüe", "hidraulica", "D", 0.0, "⊚", MountKind.Floor, GWater);
    public static readonly FixtureType Tanque = new("tanque", "Tanque", "hidraulica", "T", 0.0, "🛢", MountKind.Free, GWater);

    public static IReadOnlyList<FixtureType> All { get; } =
    [
        Luminaria, Colgante, Empotrado, PanelLed, Lineal, Spot, Arbotante, Reflector, Campana, Emergencia, Ventilador,
        Tomacorriente, ContactoGfci, Contacto220, ContactoTrifasico, ContactoPiso, ContactoTierraAislada,
        Interruptor, Escalera, CuatroVias, Doble, Atenuador, Sensor,
        Chalupa, CajaCuadrada, CajaOctagonal, CajaPaso, RegistroPiso, Condulet,
        Tablero, Medidor, Desconectador,
        AireAcondicionado, Calentador, Bomba, Motor,
        PuntoAgua, Desague, Tanque,
    ];

    public static IReadOnlyList<string> Groups { get; } = [GLights, GOutlets, GSwitches, GBoxes, GPanels, GLoads, GWater];

    public static FixtureType? ByKind(string kind) => All.FirstOrDefault(t => t.Kind == kind);

    public static ElecRole RoleOf(string kind) => ByKind(kind)?.Role ?? ElecRole.None;

    public static ElecRole RoleOf(PlacedFixture f) => f.ModuleId == "electrico" ? RoleOf(f.Kind) : ElecRole.None;

    /// <summary>Si el elemento se monta en muro (o columna) (true) o en el piso/techo del recinto (false).</summary>
    public static bool IsWallMounted(FixtureType t) => t.Mount == MountKind.Wall;
}

/// <summary>Por dónde va la canalización entre dos elementos.</summary>
public enum RouteStyle
{
    /// <summary>Sube por el muro, corre por la losa o el plafón y baja (lo habitual en vivienda).</summary>
    Ceiling,
    /// <summary>Baja por el muro y corre bajo el piso o el firme (contactos de piso, instalaciones bajo losa).</summary>
    Floor,
    /// <summary>Horizontal por el muro a la altura de los elementos (por ejemplo, de contacto a contacto).</summary>
    Wall,
    /// <summary>Tubería a la vista bajo las vigas, bajando por columnas y muros (naves industriales).</summary>
    Structure,
}

public static class RouteBuilder
{
    /// <summary>Holgura bajo el techo por donde corren canalizaciones y tuberías (m).</summary>
    public const double CeilingGap = 0.10;

    /// <summary>Profundidad de la tubería bajo el piso terminado (m).</summary>
    public const double FloorDepth = 0.05;

    public static string StyleName(RouteStyle s) => s switch
    {
        RouteStyle.Ceiling => "Por el techo (losa o plafón)",
        RouteStyle.Floor => "Por el piso",
        RouteStyle.Wall => "Por el muro (horizontal)",
        RouteStyle.Structure => "Bajo vigas (a la vista, naves)",
        _ => s.ToString(),
    };

    /// <summary>Cota a la que corre la tubería en un nivel según el estilo.</summary>
    public static double RunHeight(BuildingDocument doc, string levelId, RouteStyle style, double fromZ, double toZ)
    {
        var l = doc.GetLevel(levelId);
        return style switch
        {
            RouteStyle.Floor => l.Elevation - FloorDepth,
            RouteStyle.Wall => (fromZ + toZ) / 2,
            RouteStyle.Structure => l.Elevation + l.Height - CeilingGap
                - (doc.Beams.Where(b => b.LevelId == levelId).Select(b => b.Depth).DefaultIfEmpty(0).Max() + (doc.Beams.Any(b => b.LevelId == levelId) ? 0.05 : 0)),
            _ => l.Elevation + l.Height - CeilingGap,
        };
    }

    public static RouteMedium RunMedium(RouteStyle style) => style switch
    {
        RouteStyle.Floor => RouteMedium.Ground,
        RouteStyle.Wall => RouteMedium.Wall,
        RouteStyle.Structure => RouteMedium.Air,
        _ => RouteMedium.Ceiling,
    };

    /// <summary>Estilo con el que se trazó una ruta, deducido de sus tramos horizontales.</summary>
    public static RouteStyle GuessStyle(Route r)
    {
        var horizontal = r.Segments.Where(s => Math.Abs(s.Start.Z - s.End.Z) < 1e-6 && s.Start.Xy.DistanceTo(s.End.Xy) > 1e-6).ToList();
        if (horizontal.Count == 0) return RouteStyle.Ceiling;
        return horizontal[0].Medium switch
        {
            RouteMedium.Ground => RouteStyle.Floor,
            RouteMedium.Wall => RouteStyle.Wall,
            RouteMedium.Air => RouteStyle.Structure,
            _ => RouteStyle.Ceiling,
        };
    }

    /// <summary>Recorrido según el estilo elegido.</summary>
    public static IReadOnlyList<RouteSegment> Build(BuildingDocument doc, PlacedFixture from, PlacedFixture to, RouteStyle style) =>
        style == RouteStyle.Ceiling ? ViaCeiling(doc, from, to) : Via(doc, from, to, style);

    /// <summary>
    /// Recorrido típico de canalización empotrada: sube por el muro desde el elemento de origen hasta el techo, corre por
    /// el techo en dos tramos ortogonales y baja por el muro hasta el destino.
    /// </summary>
    public static IReadOnlyList<RouteSegment> ViaCeiling(BuildingDocument doc, PlacedFixture from, PlacedFixture to) =>
        Via(doc, from, to, RouteStyle.Ceiling);

    private static IReadOnlyList<RouteSegment> Via(BuildingDocument doc, PlacedFixture from, PlacedFixture to, RouteStyle style)
    {
        var a = from.Position;
        var b = to.Position;
        double zf = RunHeight(doc, from.LevelId, style, a.Z, b.Z);
        double zt = from.LevelId == to.LevelId ? zf : RunHeight(doc, to.LevelId, style, a.Z, b.Z);
        var medium = RunMedium(style);
        var segs = new List<RouteSegment>();
        void Add(Point3 p, Point3 q, RouteMedium m, string? host = null)
        {
            if (p.DistanceTo(q) > 1e-6) segs.Add(new RouteSegment(p, q, m, host));
        }
        RouteMedium Drop(PlacedFixture f) => f.HostId is null ? (style == RouteStyle.Structure ? RouteMedium.Air : medium == RouteMedium.Ground ? RouteMedium.Ground : RouteMedium.Air) : RouteMedium.Wall;

        var a1 = new Point3(a.X, a.Y, zf);
        Add(a, a1, Drop(from), from.HostId);
        var corner = new Point3(b.X, a.Y, zf);
        if (from.LevelId == to.LevelId)
        {
            var b1 = new Point3(b.X, b.Y, zt);
            Add(a1, corner, medium, style == RouteStyle.Wall ? from.HostId : null);
            Add(corner, b1, medium, style == RouteStyle.Wall ? to.HostId : null);
            Add(b1, b, Drop(to), to.HostId);
        }
        else
        {
            // Distinto nivel: tramo horizontal en el origen, montante vertical y tramo en el destino.
            var riserTop = new Point3(b.X, b.Y, zf);
            var b1 = new Point3(b.X, b.Y, zt);
            Add(a1, corner, medium);
            Add(corner, riserTop, medium);
            Add(riserTop, b1, RouteMedium.Slab);
            Add(b1, b, Drop(to), to.HostId);
        }
        return segs;
    }
}
