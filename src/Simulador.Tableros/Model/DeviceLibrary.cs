using System.Globalization;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;

namespace Simulador.Tableros.Model;

/// <summary>Modelo genérico de aparato: medidas típicas de catálogo (sin marca), montaje y aspecto.</summary>
/// <param name="Kind">Clave ("contactor", "magnetotermico"…).</param>
/// <param name="Name">Nombre para la lista de materiales y la librería.</param>
/// <param name="Location">Dónde va normalmente.</param>
/// <param name="Body">Color del cuerpo (ARGB).</param>
/// <param name="Front">Color del frente o de la maneta (ARGB).</param>
public sealed record DeviceModel(
    string Kind, string Name, DeviceLocation Location, double Width, double Height, double Depth, double HeatW,
    bool OnRail, uint Body, uint Front, string TagPrefix);

/// <summary>
/// Librería de aparatos genéricos con medidas habituales del mercado (módulos de 18 mm, contactores de 45 mm,
/// pulsadores de Ø22 mm…). No reproduce el catálogo de ningún fabricante: el usuario escribe su referencia.
/// </summary>
public static class DeviceLibrary
{
    /// <summary>Ancho de un módulo DIN (mm).</summary>
    public const double Module = 18;
    /// <summary>Paso de un borne de carril para 2,5 mm² (mm).</summary>
    public const double TerminalPitch = 5.2;

    private const uint Grey = 0xFFE4E6E8, DarkGrey = 0xFF4A5058, Black = 0xFF2B2F33, Blue = 0xFF2F6DB5, Beige = 0xFFD9D2C3;

    public static IReadOnlyList<DeviceModel> All { get; } =
    [
        new("magnetotermico", "Interruptor automático magnetotérmico", DeviceLocation.Plate, Module, 85, 70, 2, true, Grey, Black, "Q"),
        new("diferencial", "Interruptor diferencial", DeviceLocation.Plate, 2 * Module, 85, 70, 3, true, Grey, Blue, "Q"),
        new("guardamotor", "Guardamotor (disyuntor motor)", DeviceLocation.Plate, 45, 90, 97, 6, true, Grey, Black, "Q"),
        new("fusible", "Base portafusibles cilíndrica 10×38", DeviceLocation.Plate, Module, 81, 58, 1.5, true, Grey, DarkGrey, "F"),
        new("contactor", "Contactor tripolar", DeviceLocation.Plate, 45, 85, 86, 4, true, DarkGrey, Grey, "KM"),
        new("rele_termico", "Relé térmico de sobrecarga", DeviceLocation.Plate, 45, 70, 95, 4, true, DarkGrey, Blue, "F"),
        new("rele", "Relé de interfaz", DeviceLocation.Plate, 15.8, 80, 90, 0.6, true, DarkGrey, Grey, "KA"),
        new("temporizador", "Relé temporizador multifunción", DeviceLocation.Plate, 22.5, 90, 100, 1.2, true, Grey, Blue, "KT"),
        new("telerruptor", "Telerruptor modular", DeviceLocation.Plate, Module, 85, 65, 1, true, Grey, DarkGrey, "KL"),
        new("trafo_mando", "Transformador de mando", DeviceLocation.Plate, 84, 78, 85, 8, false, Beige, DarkGrey, "T"),
        new("trafo_potencia", "Transformador trifásico", DeviceLocation.Plate, 240, 220, 150, 120, false, Beige, DarkGrey, "T"),
        new("variador", "Variador de frecuencia", DeviceLocation.Plate, 130, 260, 180, 60, false, Black, DarkGrey, "U"),
        new("fuente", "Fuente de alimentación 24 V DC", DeviceLocation.Plate, 45, 90, 100, 6, true, Grey, DarkGrey, "G"),
        new("plc", "Autómata programable compacto", DeviceLocation.Plate, 110, 100, 75, 7, true, Grey, DarkGrey, "KF"),
        new("mcu", "Microcontrolador con placa de E/S y relés", DeviceLocation.Plate, 106, 90, 60, 3, true, 0xFF1D6B4F, DarkGrey, "KF"),
        new("regleta", "Regleta de bornes de carril", DeviceLocation.Plate, 30, 48, 45, 0, true, Grey, Blue, "X"),
        new("barra_pe", "Barra de tierra (PE)", DeviceLocation.Plate, 100, 20, 25, 0, false, 0xFFC8A24A, 0xFF3C8C3C, "PE"),
        new("aparato", "Aparato genérico de carril", DeviceLocation.Plate, 2 * Module, 85, 70, 1, true, Grey, DarkGrey, "A"),
        // Puerta
        new("pulsador", "Pulsador Ø22 mm", DeviceLocation.Door, 30, 30, 45, 0, false, Black, 0xFF2E9E4D, "S"),
        new("seta", "Pulsador de seta de emergencia Ø40 mm", DeviceLocation.Door, 40, 40, 60, 0, false, 0xFFE8C21F, 0xFFD32F2F, "S"),
        new("selector", "Selector Ø22 mm", DeviceLocation.Door, 30, 30, 45, 0, false, Black, Black, "S"),
        new("piloto", "Piloto de señalización LED Ø22 mm", DeviceLocation.Door, 30, 30, 40, 0.5, false, Black, 0xFF2E9E4D, "H"),
        new("instrumento", "Instrumento de panel 72×72 mm", DeviceLocation.Door, 72, 72, 55, 1, false, Black, 0xFFF5F5F0, "P"),
        new("controlador", "Controlador de panel (turbina, generador, pantalla)", DeviceLocation.Door, 200, 150, 100, 15, false, Black, 0xFF3A4652, "A"),
        // Campo (fuera del tablero)
        new("motor", "Motor", DeviceLocation.Field, 0, 0, 0, 0, false, Blue, Blue, "M"),
        new("receptor", "Receptor", DeviceLocation.Field, 0, 0, 0, 0, false, Grey, Grey, "E"),
        new("captador", "Captador / final de carrera", DeviceLocation.Field, 0, 0, 0, 0, false, Grey, Grey, "B"),
        new("acometida", "Acometida (alimentación)", DeviceLocation.Field, 0, 0, 0, 0, false, Grey, Grey, "XS"),
    ];

    public static DeviceModel Get(string kind) => All.FirstOrDefault(m => m.Kind == kind) ?? All.First(m => m.Kind == "aparato");

    /// <summary>Aparatos que se pueden añadir a mano desde la librería.</summary>
    public static IEnumerable<DeviceModel> Placeable => All.Where(m => m.Location != DeviceLocation.Field);

    /// <summary>Crea un aparato con las medidas del modelo y bornes 1-2 por defecto.</summary>
    public static PanelDevice Create(string kind, string tag)
    {
        var m = Get(kind);
        var d = new PanelDevice
        {
            Tag = tag, Kind = kind, Description = m.Name, Location = m.Location,
            Width = m.Width, Height = m.Height, Depth = m.Depth, HeatW = m.HeatW, OnRail = m.OnRail,
        };
        if (kind == "regleta") SetStripTerminals(d, ["1", "2", "3", "4"]);
        else if (kind == "barra_pe") d.Terminals.Add(new DeviceTerminal { Label = "PE", Side = TerminalSide.Top, Slot = 0.5 });
        else
        {
            d.Terminals.Add(new DeviceTerminal { Label = "1", Side = TerminalSide.Top });
            d.Terminals.Add(new DeviceTerminal { Label = "2", Side = TerminalSide.Bottom });
        }
        return d;
    }

    /// <summary>Regleta: un borne por etiqueta, arriba (interior) y abajo (campo), con tapa final y topes.</summary>
    public static void SetStripTerminals(PanelDevice d, IReadOnlyList<string> labels)
    {
        d.Terminals.Clear();
        const double ends = 2 * 9 + 2; // dos topes de 9 mm y una tapa final
        d.Width = Math.Round(labels.Count * TerminalPitch + ends, 1);
        for (var i = 0; i < labels.Count; i++)
        {
            var slot = (9 + (i + 0.5) * TerminalPitch) / d.Width;
            d.Terminals.Add(new DeviceTerminal { Label = labels[i], Side = TerminalSide.Top, Slot = slot });
            d.Terminals.Add(new DeviceTerminal { Label = labels[i], Side = TerminalSide.Bottom, Slot = slot });
        }
    }

    /// <summary>Calibre de bornes para la sección (mm²) que entra.</summary>
    public static double StripTerminalSection(double wireSection) => wireSection <= 2.5 ? 2.5 : wireSection <= 4 ? 4 : wireSection <= 6 ? 6 : wireSection <= 10 ? 10 : 16;

    // ------------------------------------------------------------------ desde el esquema

    /// <summary>
    /// Convierte un grupo de elementos del esquema con la misma designación en un aparato del tablero:
    /// elige el modelo, las medidas, la descripción y la lista de bornes.
    /// </summary>
    public static PanelDevice FromSchematic(string tag, IReadOnlyList<PlacedElement> elements, ElementCatalog catalog)
    {
        var typed = elements.Select(e => (E: e, T: catalog.TryGet(e.Type, out var t) ? t : null)).Where(p => p.T is not null)
            .Select(p => (p.E, T: p.T!)).ToList();
        var kind = KindFor(typed.Select(p => p.T).ToList(), typed.Select(p => p.E).ToList());
        var d = Create(kind, tag);
        d.Terminals.Clear();
        var main = typed.FirstOrDefault(p => p.T.IsCoil || p.T.Behavior is Behavior.Vfd or Behavior.Plc).E is { } m
            ? typed.First(p => p.E == m) : typed.FirstOrDefault();

        // Bornes: los de arriba del símbolo, arriba del aparato; los demás, abajo.
        foreach (var (e, t) in typed)
        {
            if (t.Terminals.Count == 0) continue;
            var minY = t.Terminals.Min(x => x.Y);
            var maxY = t.Terminals.Max(x => x.Y);
            for (var i = 0; i < t.Terminals.Count; i++)
            {
                var label = TerminalLabel(e, t, i);
                if (string.IsNullOrEmpty(label) || d.Terminal(label) is not null) continue;
                var term = t.Terminals[i];
                var side = maxY == minY || term.Y == minY ? TerminalSide.Top : TerminalSide.Bottom;
                d.Terminals.Add(new DeviceTerminal { Label = label, Side = side });
            }
        }
        SpreadSlots(d);

        if (main.T is not null) Describe(d, kind, main.E, main.T, typed);
        return d;
    }

    /// <summary>Número de borne tal como sale en el esquema (respeta la numeración «13-14» escrita a mano).</summary>
    public static string TerminalLabel(PlacedElement e, ElementType t, int index)
    {
        var term = t.Terminals[index];
        var custom = e.Text("Bornes", t.DefaultTerminalText);
        var parts = custom.Split('-', 2);
        var label = t.Terminals.Count == 2 && parts.Length == 2 && parts[index].Trim().Length > 0 ? parts[index].Trim() : term.Label;
        if (string.IsNullOrEmpty(label)) label = term.Id;
        return label;
    }

    /// <summary>Reparte los bornes a lo ancho del aparato, por orden, en cada cara.</summary>
    public static void SpreadSlots(PanelDevice d)
    {
        foreach (var side in new[] { TerminalSide.Top, TerminalSide.Bottom })
        {
            var list = d.Terminals.Where(t => t.Side == side).ToList();
            for (var i = 0; i < list.Count; i++) list[i].Slot = (i + 0.5) / list.Count;
        }
    }

    private static string KindFor(List<ElementType> types, List<PlacedElement> elems)
    {
        bool Has(params Behavior[] b) => types.Any(t => b.Contains(t.Behavior));
        if (Has(Behavior.CoilContactor) || Has(Behavior.MainPoles)) return "contactor";
        if (Has(Behavior.CoilTimerOn, Behavior.CoilTimerOff, Behavior.CoilFlasher)) return "temporizador";
        if (Has(Behavior.CoilImpulse)) return "telerruptor";
        if (Has(Behavior.CoilRelay)) return "rele";
        if (Has(Behavior.Vfd)) return "variador";
        if (Has(Behavior.Plc)) return elems.Any(e => e.Type == ElementCatalog.McuTypeId) ? "mcu" : "plc";
        if (Has(Behavior.MotorBreaker)) return "guardamotor";
        if (Has(Behavior.ThermalRelay)) return "rele_termico";
        if (Has(Behavior.Rcd)) return "diferencial";
        if (Has(Behavior.Breaker)) return "magnetotermico";
        if (Has(Behavior.Fuse)) return "fusible";
        if (Has(Behavior.Transformer3)) return "trafo_potencia";
        if (Has(Behavior.Transformer1)) return "trafo_mando";
        if (Has(Behavior.SourceDc)) return "fuente";
        if (Has(Behavior.Source3Ph, Behavior.SourceAc1)) return "acometida";
        if (Has(Behavior.EmergencyStop)) return "seta";
        if (Has(Behavior.PushNO, Behavior.PushNC)) return "pulsador";
        if (Has(Behavior.Selector3, Behavior.Switch)) return "selector";
        if (Has(Behavior.Pilot)) return "piloto";
        if (Has(Behavior.Voltmeter, Behavior.Ammeter)) return "instrumento";
        if (Has(Behavior.Motor1, Behavior.Motor3, Behavior.Motor6)) return "motor";
        if (Has(Behavior.LimitNO, Behavior.LimitNC, Behavior.SensorNO, Behavior.SensorNC, Behavior.Transmitter)) return "captador";
        if (Has(Behavior.Lamp, Behavior.Bell, Behavior.Heater, Behavior.Solenoid, Behavior.TwoWay, Behavior.Intermediate)) return "receptor";
        if (Has(Behavior.Junction)) return "regleta";
        return "aparato";
    }

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static string N(double v) => v.ToString("0.##", Es);

    private static string Opt(PlacedElement e, ElementType t, string key)
    {
        var p = t.Params.FirstOrDefault(x => x.Key == key);
        if (p?.Options is null) return "";
        var i = (int)Math.Round(e.Param(t, key));
        return i >= 0 && i < p.Options.Length ? p.Options[i] : "";
    }

    private static void Describe(PanelDevice d, string kind, PlacedElement e, ElementType t, List<(PlacedElement E, ElementType T)> all)
    {
        double P(string k) => e.Param(t, k);
        int no = all.Count(p => p.T.Behavior is Behavior.ContactNO or Behavior.TimedContactNO);
        int nc = all.Count(p => p.T.Behavior is Behavior.ContactNC or Behavior.TimedContactNC);
        string aux = no + nc == 0 ? "" : ", " + string.Join("+", new[] { no > 0 ? $"{no}NA" : "", nc > 0 ? $"{nc}NC" : "" }.Where(x => x.Length > 0));
        string coil(PlacedElement ce, ElementType ct) => $"bobina {N(ce.Param(ct, "V"))} V {(ce.Param(ct, "ac") >= 0.5 ? "AC" : "DC")}";
        var coilPair = all.FirstOrDefault(p => p.T.IsCoil);
        var poles = all.Where(p => p.T.PoleCount > 0).Select(p => p.T.PoleCount).DefaultIfEmpty(0).Max();

        switch (kind)
        {
            case "magnetotermico":
            {
                var curve = Opt(e, t, "curva");
                d.RatedA = P("In");
                d.Width = Module * Math.Max(1, poles);
                d.HeatW = 1.5 * Math.Max(1, poles);
                d.Description = $"Magnetotérmico {Math.Max(1, poles)}P curva {curve.Split(' ')[0]} {N(d.RatedA)} A";
                break;
            }
            case "diferencial":
                d.RatedA = P("In");
                d.Width = poles >= 4 ? 4 * Module : 2 * Module;
                d.Description = $"Diferencial {(poles >= 4 ? "4P" : "2P")} {N(d.RatedA)} A {N(P("Idn"))} mA";
                break;
            case "guardamotor":
                d.RatedA = P("Ir");
                d.Description = $"Guardamotor 3P regulado a {N(d.RatedA)} A{aux}";
                break;
            case "fusible":
                d.RatedA = P("In");
                d.Width = Module * Math.Max(1, poles);
                d.HeatW = 1.5 * Math.Max(1, poles);
                d.Description = $"Portafusibles {Math.Max(1, poles)}P con fusible {Opt(e, t, "tipo").Split(' ')[0]} {N(d.RatedA)} A";
                break;
            case "contactor":
            {
                var mainP = all.FirstOrDefault(p => p.T.Behavior == Behavior.MainPoles);
                d.RatedA = mainP.T is not null ? mainP.E.Param(mainP.T, "In") : 9;
                if (d.RatedA > 25) { d.Width = 55; d.Height = 120; d.Depth = 115; d.HeatW = 8; }
                var c = coilPair.T is not null ? ", " + coil(coilPair.E, coilPair.T) : "";
                d.Description = $"Contactor 3P {N(d.RatedA)} A (AC-3){c}{aux}";
                if (no > 1 || nc > 1) d.Accessories.Add("Bloque de contactos auxiliares frontal 2NA+2NC");
                break;
            }
            case "rele_termico":
                d.RatedA = P("Ir");
                d.Description = $"Relé térmico regulado a {N(d.RatedA)} A, {Opt(e, t, "clase").ToLowerInvariant()}";
                break;
            case "rele":
                d.Description = $"Relé de interfaz, {coil(e, t)}{aux}";
                if (no + nc > 1) { d.Width = 27; d.Description = $"Relé auxiliar 4 contactos, {coil(e, t)}{aux}"; }
                d.Accessories.Add("Zócalo de relé");
                break;
            case "temporizador":
                d.Description = t.Behavior switch
                {
                    Behavior.CoilTimerOff => $"Temporizador a la desconexión {N(P("t"))} s, {coil(e, t)}",
                    Behavior.CoilFlasher => $"Relé intermitente {N(P("ton"))}/{N(P("toff"))} s, {coil(e, t)}",
                    _ => $"Temporizador a la conexión {N(P("t"))} s, {coil(e, t)}",
                };
                break;
            case "telerruptor":
                d.Description = $"Telerruptor, {coil(e, t)}";
                break;
            case "trafo_mando":
            {
                var s = P("S");
                d.Description = $"Transformador de mando {N(s)} VA {N(P("V1"))}/{N(P("V2"))} V";
                d.HeatW = Math.Round(Math.Max(4, s * 0.08), 1);
                var scale = Math.Clamp(Math.Sqrt(Math.Max(s, 20) / 100), 0.8, 2.2);
                d.Width = Math.Round(84 * scale);
                d.Height = Math.Round(78 * scale);
                d.Depth = Math.Round(85 * scale);
                break;
            }
            case "trafo_potencia":
                d.Description = $"Transformador trifásico {N(P("S"))} kVA {N(P("V1"))}/{N(P("V2"))} V";
                d.HeatW = Math.Round(Math.Max(30, P("S") * 1000 * 0.025));
                break;
            case "variador":
            {
                var kw = P("Pn");
                d.RatedA = P("In");
                d.Description = $"Variador de frecuencia {N(kw)} kW, red {N(P("Vin"))} V";
                d.HeatW = Math.Round(Math.Max(25, kw * 1000 * 0.03));
                d.Width = kw <= 2.2 ? 90 : kw <= 7.5 ? 130 : 180;
                d.Height = kw <= 2.2 ? 230 : kw <= 7.5 ? 290 : 400;
                d.Depth = kw <= 2.2 ? 170 : 200;
                break;
            }
            case "fuente":
                d.Description = $"Fuente de alimentación conmutada {N(P("V"))} V DC";
                break;
            case "plc":
                d.Description = "Autómata compacto 8 entradas 24 V DC / 8 salidas a relé";
                break;
            case "mcu":
                d.Description = $"{Opt(e, t, "placa")} sobre placa de E/S 24 V con 8 relés";
                break;
            case "pulsador":
            {
                bool nO = all.Any(p => p.T.Behavior == Behavior.PushNO);
                d.Description = nO ? "Pulsador rasante verde Ø22, 1NA" : "Pulsador rasante rojo Ø22, 1NC";
                break;
            }
            case "seta":
                d.Description = "Seta de emergencia Ø40 enclavada, giro para desenclavar, 1NC";
                break;
            case "selector":
                d.Description = t.Behavior == Behavior.Selector3 ? "Selector Ø22 de 3 posiciones I-0-II" : "Selector Ø22 de 2 posiciones";
                break;
            case "piloto":
                d.Description = $"Piloto LED Ø22 {Opt(e, t, "color").ToLowerInvariant()} {N(P("V"))} V";
                break;
            case "instrumento":
                d.Description = t.Behavior == Behavior.Voltmeter ? "Voltímetro analógico 72×72" : "Amperímetro analógico 72×72";
                break;
            case "motor":
                d.Description = t.Behavior == Behavior.Motor1 ? $"Motor monofásico {N(P("Pn"))} kW" : $"Motor trifásico {N(P("Pn"))} kW";
                d.RatedA = t.Behavior == Behavior.Motor1
                    ? P("Pn") * 1000 / (P("V") * P("cos") * P("eta"))
                    : P("Pn") * 1000 / (Math.Sqrt(3) * 400 * P("cos") * P("eta"));
                break;
            default:
                d.Description = t.Name;
                break;
        }
    }
}
