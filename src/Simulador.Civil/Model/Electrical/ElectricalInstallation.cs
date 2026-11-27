namespace Simulador.Civil.Model.Electrical;

/// <summary>Uso del circuito derivado (como se agrupan en el cuadro de cargas).</summary>
public enum CircuitUse { Alumbrado, Contactos, Especial, Fuerza }

/// <summary>Fase del centro de carga a la que va un circuito de 127 V (los de 220 V toman dos o tres fases).</summary>
public enum Phase { A, B, C }

/// <summary>
/// Circuito derivado que sale de un centro de carga con su interruptor termomagnético.
/// <paramref name="Poles"/>: 1 = 127 V (fase + neutro), 2 = 220 V (dos fases), 3 = trifásico 220 V.
/// <paramref name="WireAwg"/>: calibre del conductor (14, 12, 10, 8, 6…).
/// </summary>
public sealed record Circuit(string Id, string Name, string PanelId, CircuitUse Use = CircuitUse.Alumbrado,
    double BreakerA = 15, int Poles = 1, Phase Phase = Phase.A, int WireAwg = 14, bool GroundFault = false)
{
    public double Volts => Poles == 1 ? 127 : 220;
}

/// <summary>
/// Datos eléctricos de un elemento colocado: a qué circuito va, qué apagadores lo mandan y su potencia si se cambia.
/// <paramref name="ControlledBy"/>: ids de los apagadores (elementos colocados). Para la segunda tecla de un apagador
/// doble se escribe "F12#2".
/// </summary>
public sealed record FixtureElectrical(string FixtureId, string? CircuitId = null, double? PowerW = null,
    IReadOnlyList<string>? ControlledBy = null);

/// <summary>Tipo de canalización.</summary>
public enum ConduitType { PvcLigero, PvcPesado, PoliducFlexible, ParedDelgada, ParedGruesa, FlexibleMetalico, Charola }

/// <summary>Datos de la canalización de una ruta eléctrica: tipo, diámetro y conductores que lleva.</summary>
/// <param name="DiameterMm">Diámetro comercial (16 = 1/2", 21 = 3/4", 27 = 1", 35 = 1 1/4", 41 = 1 1/2", 53 = 2").</param>
/// <param name="Conductors">Conductores dentro (null = los calcula el programa).</param>
public sealed record ConduitSpec(string RouteId, ConduitType Type = ConduitType.PvcLigero, int DiameterMm = 16, int? Conductors = null, int? WireAwg = null);

/// <summary>Instalación eléctrica de la edificación: circuitos y datos que no caben en el modelo geométrico común.</summary>
public sealed class ElectricalInstallation
{
    public List<Circuit> Circuits { get; set; } = [];
    public List<FixtureElectrical> Fixtures { get; set; } = [];
    public List<ConduitSpec> Conduits { get; set; } = [];

    public FixtureElectrical? Of(string fixtureId) => Fixtures.FirstOrDefault(f => f.FixtureId == fixtureId);

    public Circuit? CircuitOf(string fixtureId) =>
        Of(fixtureId)?.CircuitId is { } c ? Circuits.FirstOrDefault(x => x.Id == c) : null;

    public ConduitSpec ConduitOf(string routeId) => Conduits.FirstOrDefault(c => c.RouteId == routeId) ?? new ConduitSpec(routeId);

    public void Set(FixtureElectrical f)
    {
        Fixtures.RemoveAll(x => x.FixtureId == f.FixtureId);
        Fixtures.Add(f);
    }

    public void Set(ConduitSpec c)
    {
        Conduits.RemoveAll(x => x.RouteId == c.RouteId);
        Conduits.Add(c);
    }

    public void Set(Circuit c)
    {
        int i = Circuits.FindIndex(x => x.Id == c.Id);
        if (i >= 0) Circuits[i] = c; else Circuits.Add(c);
    }

    public string NewCircuitId()
    {
        for (int i = 1; ; i++)
            if (Circuits.All(c => c.Id != "C" + i)) return "C" + i;
    }

    /// <summary>Quita lo que apunta a elementos o rutas que ya no existen.</summary>
    public void Prune(ISet<string> fixtureIds, ISet<string> routeIds)
    {
        Fixtures.RemoveAll(f => !fixtureIds.Contains(f.FixtureId));
        for (int i = 0; i < Fixtures.Count; i++)
        {
            var f = Fixtures[i];
            if (f.ControlledBy is { Count: > 0 } cb && cb.Any(s => !fixtureIds.Contains(SwitchBase(s))))
                Fixtures[i] = f with { ControlledBy = cb.Where(s => fixtureIds.Contains(SwitchBase(s))).ToList() };
            if (f.CircuitId is { } c && Circuits.All(x => x.Id != c)) Fixtures[i] = Fixtures[i] with { CircuitId = null };
        }
        Conduits.RemoveAll(c => !routeIds.Contains(c.RouteId));
        Circuits.RemoveAll(c => !fixtureIds.Contains(c.PanelId));
    }

    /// <summary>Id del apagador sin la tecla ("F12#2" → "F12").</summary>
    public static string SwitchBase(string s) => s.Split('#')[0];

    /// <summary>Tecla del apagador ("F12#2" → 2; "F12" → 1).</summary>
    public static int SwitchKey(string s) => s.Contains('#') && int.TryParse(s.Split('#')[1], out var k) ? k : 1;
}

/// <summary>Tablas orientativas de la NOM-001-SEDE-2012 (conductores de cobre THW-LS/THHN, 75 °C).</summary>
public static class ElectricalTables
{
    /// <summary>Calibres AWG habituales en instalaciones (de menor a mayor sección).</summary>
    public static readonly int[] Awg = [14, 12, 10, 8, 6, 4, 2];

    /// <summary>Sección del conductor en mm².</summary>
    public static double SectionMm2(int awg) => awg switch
    {
        14 => 2.08, 12 => 3.31, 10 => 5.26, 8 => 8.37, 6 => 13.3, 4 => 21.2, 2 => 33.6, _ => 2.08,
    };

    /// <summary>
    /// Corriente máxima que protege el termomagnético para ese calibre (tabla 310-15(b)(16) a 75 °C, con la limitación
    /// de 240-4(d): 14 AWG → 15 A, 12 AWG → 20 A, 10 AWG → 30 A).
    /// </summary>
    public static double Ampacity(int awg) => awg switch
    {
        14 => 15, 12 => 20, 10 => 30, 8 => 50, 6 => 65, 4 => 85, 2 => 115, _ => 15,
    };

    /// <summary>Área exterior del conductor con aislamiento THW-LS (mm², capítulo 10, tabla 5).</summary>
    public static double InsulatedAreaMm2(int awg) => awg switch
    {
        14 => 8.97, 12 => 11.68, 10 => 15.68, 8 => 28.19, 6 => 46.84, 4 => 62.77, 2 => 86.0, _ => 8.97,
    };

    /// <summary>Diámetros comerciales de tubo conduit (mm) y su designación en pulgadas.</summary>
    public static readonly (int Mm, string Inch)[] ConduitSizes =
        [(13, "3/8\""), (16, "1/2\""), (21, "3/4\""), (27, "1\""), (35, "1 1/4\""), (41, "1 1/2\""), (53, "2\""), (63, "2 1/2\""), (78, "3\"")];

    public static string Inch(int mm) => ConduitSizes.FirstOrDefault(s => s.Mm == mm).Inch ?? $"{mm} mm";

    /// <summary>Área interior aproximada del tubo (mm², capítulo 10, tabla 4, tubo de pared delgada/PVC).</summary>
    public static double ConduitInnerAreaMm2(int mm) => mm switch
    {
        13 => 100, 16 => 196, 21 => 343, 27 => 556, 35 => 968, 41 => 1314, 53 => 2165, 63 => 3783, 78 => 5701,
        _ => Math.PI * mm * mm / 4 * 0.85,
    };

    /// <summary>Relleno máximo del tubo según el número de conductores (capítulo 10, tabla 1).</summary>
    public static double MaxFill(int conductors) => conductors switch { 1 => 0.53, 2 => 0.31, _ => 0.40 };

    /// <summary>Calibres de interruptor termomagnético comerciales (A).</summary>
    public static readonly double[] Breakers = [15, 20, 30, 40, 50, 60, 70, 100];

    /// <summary>Resistividad del cobre a 75 °C (Ω·mm²/m).</summary>
    public const double CopperResistivity = 0.0214;

    public static string ConduitName(ConduitType t) => t switch
    {
        ConduitType.PvcLigero => "PVC ligero (empotrado)",
        ConduitType.PvcPesado => "PVC pesado",
        ConduitType.PoliducFlexible => "Poliducto flexible naranja",
        ConduitType.ParedDelgada => "Conduit galvanizado pared delgada (EMT)",
        ConduitType.ParedGruesa => "Conduit galvanizado pared gruesa",
        ConduitType.FlexibleMetalico => "Tubo flexible metálico (licuatite)",
        ConduitType.Charola => "Charola portacables",
        _ => t.ToString(),
    };

    /// <summary>Kind de la ruta (texto en el archivo) para cada tipo de canalización.</summary>
    public static string RouteKind(ConduitType t) => t == ConduitType.Charola ? "charola" : "tubo_electrico";

    /// <summary>Color en el plano y en 3D (0xAARRGGBB).</summary>
    public static uint ConduitColor(ConduitType t) => t switch
    {
        ConduitType.PvcLigero or ConduitType.PvcPesado => 0xFFE65028,
        ConduitType.PoliducFlexible => 0xFFF29A1F,
        ConduitType.ParedDelgada or ConduitType.ParedGruesa or ConduitType.FlexibleMetalico => 0xFF7C8A96,
        ConduitType.Charola => 0xFF5A6B7A,
        _ => 0xFFE65028,
    };
}
