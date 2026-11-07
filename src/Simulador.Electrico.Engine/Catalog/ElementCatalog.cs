namespace Simulador.Electrico.Engine.Catalog;

/// <summary>
/// Librería de componentes incluida. Los símbolos siguen IEC 60617 y las designaciones IEC 81346.
/// Los presets de fabricante (<see cref="ManufacturerCatalog"/>) se apoyan en estos tipos.
/// </summary>
public sealed class ElementCatalog
{
    private readonly Dictionary<string, ElementType> _types;
    private readonly List<ElementType> _list;

    public IReadOnlyList<ElementType> Types => _list;

    public ElementCatalog(IEnumerable<ElementType> types)
    {
        _list = types.ToList();
        _types = _list.ToDictionary(t => t.Id);
    }

    /// <summary>Añade (o sustituye, si ya existe ese Id) un tipo importado de una librería externa.</summary>
    public void Register(ElementType type)
    {
        if (_types.TryGetValue(type.Id, out var old)) _list[_list.IndexOf(old)] = type;
        else _list.Add(type);
        _types[type.Id] = type;
    }

    public ElementType Get(string id) =>
        _types.TryGetValue(id, out var t) ? t : throw new KeyNotFoundException($"Tipo de elemento desconocido: {id}");

    public bool TryGet(string id, out ElementType type) => _types.TryGetValue(id, out type!);

    public IEnumerable<IGrouping<string, ElementType>> ByCategory() => Types.GroupBy(t => t.Category);

    private static ElementCatalog? _default;
    public static ElementCatalog Default => _default ??= new(BuildDefault());

    // ---- Constantes de categoría ----
    public const string CatSources = "Alimentación";
    public const string CatControl = "Mando y captadores";
    public const string CatContacts = "Contactos";
    public const string CatCoils = "Bobinas, relés y temporizadores";
    public const string CatProtection = "Protecciones";
    public const string CatLoads = "Receptores";
    public const string CatMotors = "Motores";
    public const string CatInstruments = "Medida y conexión";
    public const string CatPower = "Transformadores y variadores";
    public const string CatAutomation = "Automatización (PLC)";
    public const string CatImported = "Importados (QElectroTech)";

    /// <summary>Entradas y salidas del PLC compacto de la librería.</summary>
    public const int PlcInputs = 8;
    public const int PlcOutputs = 8;

    /// <summary>Elemento «microcontrolador con placa de relés».</summary>
    public const string McuTypeId = "mcu_reles";

    /// <summary>Placas que admite <see cref="McuTypeId"/>, en el orden del parámetro «placa».</summary>
    public static readonly string[] McuBoards = ["Arduino Uno R3", "Arduino Nano", "Arduino Mega 2560", "ESP32 DevKit V1", "Raspberry Pi Pico"];

    private static readonly string[] OnOff = ["No", "Sí"];
    private static readonly string[] AcDc = ["DC", "AC"];
    private static readonly string[] Curves = ["B (3-5 In)", "C (5-10 In)", "D (10-20 In)"];
    private static readonly string[] Classes = ["Clase 10", "Clase 20", "Clase 30"];
    private static readonly string[] FuseTypes = ["gG (uso general)", "aM (acompañamiento motor)"];
    private static readonly string[] Colors = ["Rojo", "Verde", "Ámbar", "Azul", "Blanco"];
    private static readonly string[] Connections = ["Estrella (Y)", "Triángulo (Δ)"];
    private static readonly string[] LoadTypes = ["Par constante (cinta, elevación)", "Par cuadrático (bomba, ventilador)"];
    private static readonly string[] Directions = ["A derechas", "A izquierdas"];

    private static TerminalDef[] TwoTerm(string a = "1", string b = "2", string la = "1", string lb = "2") =>
        [new(a, 0, 0, la), new(b, 0, 4, lb)];

    private static TerminalDef[] Poles(int n, params string[] labels)
    {
        var list = new List<TerminalDef>();
        for (var i = 0; i < n; i++)
        {
            var top = labels.Length > 2 * i ? labels[2 * i] : (2 * i + 1).ToString();
            var bottom = labels.Length > 2 * i + 1 ? labels[2 * i + 1] : (2 * i + 2).ToString();
            list.Add(new((2 * i + 1).ToString(), 2 * i, 0, top));
            list.Add(new((2 * i + 2).ToString(), 2 * i, 4, bottom));
        }
        return [.. list];
    }

    private static ParamDef P(string key, string name, string unit, double def, double min, double max) => new(key, name, unit, def, min, max);
    private static ParamDef O(string key, string name, double def, string[] options) => new(key, name, "", def, 0, options.Length - 1, options);

    private static ParamDef[] CoilParams(double v = 230, double s = 7, bool ac = true) =>
        [P("V", "Tensión de bobina", "V", v, 5, 690), O("ac", "Tipo de corriente", ac ? 1 : 0, AcDc), P("S", "Consumo de mantenimiento", "VA/W", s, 0.2, 200)];

    private static IEnumerable<ElementType> BuildDefault()
    {
        // ---------------- Alimentación ----------------
        yield return new("red_3f", "Red trifásica 3F+N+PE", CatSources, Behavior.Source3Ph, "XS",
            [new("L1", 0, 2, "L1"), new("L2", 2, 2, "L2"), new("L3", 4, 2, "L3"), new("N", 6, 2, "N"), new("PE", 8, 2, "PE")],
            [P("V", "Tensión entre fases", "V", 400, 100, 1000), P("f", "Frecuencia", "Hz", 50, 1, 400), P("Zsc", "Impedancia de cortocircuito", "Ω", 0.05, 0.001, 10)],
            "Red de distribución con neutro y tierra unidos en origen (esquema TN-S).");
        yield return new("fuente_ac1", "Alimentación monofásica", CatSources, Behavior.SourceAc1, "XS",
            [new("L", 0, 2, "L"), new("N", 2, 2, "N")],
            [P("V", "Tensión", "V", 230, 5, 1000), P("f", "Frecuencia", "Hz", 50, 1, 400), P("Zsc", "Impedancia interna", "Ω", 0.1, 0.001, 100)],
            "Fase y neutro, p. ej. 230 V para el circuito de mando o la iluminación.");
        yield return new("fuente_dc", "Fuente de corriente continua", CatSources, Behavior.SourceDc, "G",
            [new("+", 0, 2, "+"), new("-", 2, 2, "−")],
            [P("V", "Tensión", "V", 24, 1, 1000), P("Zsc", "Resistencia interna", "Ω", 0.1, 0.001, 100)],
            "Fuente de mando de 24 V DC (o la tensión que elijas), con el negativo a tierra (0 V).");

        // ---------------- Mando ----------------
        yield return new("pulsador_na", "Pulsador NA (marcha)", CatControl, Behavior.PushNO, "S", TwoTerm("1", "2", "13", "14"), [],
            "Cierra mientras se mantiene pulsado.", "13-14");
        yield return new("pulsador_nc", "Pulsador NC (paro)", CatControl, Behavior.PushNC, "S", TwoTerm("1", "2", "11", "12"), [],
            "Abre mientras se mantiene pulsado.", "11-12");
        yield return new("seta_emergencia", "Seta de emergencia", CatControl, Behavior.EmergencyStop, "S", TwoTerm("1", "2", "11", "12"), [],
            "Contacto NC con enclavamiento: al pulsarla queda abierta hasta que se rearma con otro clic.", "11-12");
        yield return new("interruptor", "Interruptor / selector 2 posiciones", CatControl, Behavior.Switch, "S", TwoTerm("1", "2", "13", "14"), [],
            "Contacto NA que se queda en la posición elegida. Sirve como interruptor de luz o selector Man-Auto.", "13-14");
        yield return new("conmutador", "Conmutador", CatControl, Behavior.TwoWay, "S",
            [new("1", 0, 0, "1"), new("2", 2, 0, "2"), new("C", 0, 4, "C")], [],
            "Conmuta el común C entre las salidas 1 y 2. Dos conmutadores encienden una luz desde dos puntos.");
        yield return new("cruzamiento", "Cruzamiento", CatControl, Behavior.Intermediate, "S",
            [new("1", 0, 0, "1"), new("2", 2, 0, "2"), new("3", 0, 4, "3"), new("4", 2, 4, "4")], [],
            "Une 1-3 y 2-4, o los cruza (1-4 y 2-3). Entre dos conmutadores permite tres o más puntos de luz.");
        yield return new("selector_3p", "Selector 3 posiciones (I-0-II)", CatControl, Behavior.Selector3, "S",
            [new("C", 0, 0, "C"), new("1", 0, 4, "1"), new("2", 2, 4, "2")], [],
            "Posición I une C-1, posición 0 abre, posición II une C-2. Típico en marcha manual/automático.");
        yield return new("final_carrera_na", "Final de carrera NA", CatControl, Behavior.LimitNO, "SQ", TwoTerm("1", "2", "13", "14"), [],
            "Clic para accionarlo (la pieza llega) y otro clic para liberarlo.", "13-14");
        yield return new("final_carrera_nc", "Final de carrera NC", CatControl, Behavior.LimitNC, "SQ", TwoTerm("1", "2", "11", "12"), [],
            "Abre cuando se acciona. Clic para accionar y liberar.", "11-12");
        yield return new("sensor_na", "Captador NA (flotador, presostato, detector)", CatControl, Behavior.SensorNO, "B", TwoTerm("1", "2", "13", "14"), [],
            "Contacto de un captador: flotador, presostato, termostato, detector inductivo o fotocélula. Cambia el rótulo en Propiedades.", "13-14");
        yield return new("sensor_nc", "Captador NC (flotador, presostato, detector)", CatControl, Behavior.SensorNC, "B", TwoTerm("1", "2", "11", "12"), [],
            "Igual que el captador NA pero con contacto normalmente cerrado.", "11-12");

        // ---------------- Contactos asociados ----------------
        yield return new("contacto_na", "Contacto auxiliar NA", CatContacts, Behavior.ContactNO, "KM", TwoTerm("1", "2", "13", "14"), [],
            "Cierra cuando actúa el aparato con su misma designación (bobina KM1, relé KA1, térmico F2, magnetotérmico Q1…).", "13-14");
        yield return new("contacto_nc", "Contacto auxiliar NC", CatContacts, Behavior.ContactNC, "KM", TwoTerm("1", "2", "21", "22"), [],
            "Abre cuando actúa el aparato con su misma designación. Con un relé térmico es el 95-96.", "21-22");
        yield return new("contacto_temp_na", "Contacto temporizado NA", CatContacts, Behavior.TimedContactNO, "KT", TwoTerm("1", "2", "17", "18"), [],
            "Contacto de un temporizador KT: cierra cuando vence el tiempo.", "17-18");
        yield return new("contacto_temp_nc", "Contacto temporizado NC", CatContacts, Behavior.TimedContactNC, "KT", TwoTerm("1", "2", "15", "16"), [],
            "Contacto de un temporizador KT: abre cuando vence el tiempo.", "15-16");
        yield return new("contactos_principales", "Contactos principales 3P", CatContacts, Behavior.MainPoles, "KM", Poles(3),
            [P("In", "Corriente asignada AC-3", "A", 9, 1, 800)],
            "Polos de potencia 1-2, 3-4, 5-6 de un contactor. Pon la misma designación que su bobina.");

        // ---------------- Bobinas ----------------
        yield return new("bobina_contactor", "Bobina de contactor", CatCoils, Behavior.CoilContactor, "KM", TwoTerm("A1", "A2", "A1", "A2"), CoilParams(),
            "Al recibir al menos el 85 % de su tensión cierra todos los contactos con su designación (tarda unos 20 ms).");
        yield return new("bobina_rele", "Relé auxiliar", CatCoils, Behavior.CoilRelay, "KA", TwoTerm("A1", "A2", "A1", "A2"), CoilParams(24, 0.5, false),
            "Relé de mando: igual que un contactor pero sin polos de potencia.");
        yield return new("bobina_ton", "Temporizador a la conexión", CatCoils, Behavior.CoilTimerOn, "KT", TwoTerm("A1", "A2", "A1", "A2"),
            [.. CoilParams(230, 2), P("t", "Retardo", "s", 5, 0.05, 3600)],
            "Sus contactos temporizados cambian cuando la bobina lleva el tiempo ajustado alimentada; vuelven al quitarle tensión.");
        yield return new("bobina_tof", "Temporizador a la desconexión", CatCoils, Behavior.CoilTimerOff, "KT", TwoTerm("A1", "A2", "A1", "A2"),
            [.. CoilParams(230, 2), P("t", "Retardo", "s", 5, 0.05, 3600)],
            "Sus contactos cambian al alimentarlo y vuelven el tiempo ajustado después de quitarle tensión.");
        yield return new("bobina_intermitente", "Relé intermitente", CatCoils, Behavior.CoilFlasher, "KT", TwoTerm("A1", "A2", "A1", "A2"),
            [.. CoilParams(230, 2), P("ton", "Tiempo cerrado", "s", 1, 0.05, 3600), P("toff", "Tiempo abierto", "s", 1, 0.05, 3600)],
            "Mientras está alimentado, sus contactos abren y cierran con la cadencia ajustada.");
        yield return new("bobina_telerruptor", "Telerruptor", CatCoils, Behavior.CoilImpulse, "KL", TwoTerm("A1", "A2", "A1", "A2"), CoilParams(230, 4),
            "Cada impulso en la bobina cambia el estado de sus contactos. Para encender luces desde muchos pulsadores.");

        // ---------------- Protecciones ----------------
        yield return new("fusible", "Fusible", CatProtection, Behavior.Fuse, "F", Poles(1),
            [P("In", "Calibre", "A", 10, 0.5, 1250), O("tipo", "Tipo", 0, FuseTypes)],
            "Funde según su curva tiempo-corriente. Clic para reponerlo cuando esté fundido.");
        yield return new("fusible_3p", "Fusibles 3P (seccionador portafusibles)", CatProtection, Behavior.Fuse, "F", Poles(3),
            [P("In", "Calibre", "A", 16, 0.5, 1250), O("tipo", "Tipo", 1, FuseTypes)],
            "Tres fusibles en un portafusibles. Clic para reponerlos.");
        foreach (var (n, id, name) in new[] { (1, "magnetotermico_1p", "Magnetotérmico 1P"), (2, "magnetotermico_2p", "Magnetotérmico 2P (F+N)"), (3, "magnetotermico_3p", "Magnetotérmico 3P"), (4, "magnetotermico_4p", "Magnetotérmico 4P") })
        {
            yield return new(id, name, CatProtection, Behavior.Breaker, "Q", Poles(n),
                [P("In", "Calibre", "A", n == 3 ? 16 : 10, 0.5, 125), O("curva", "Curva", 1, Curves)],
                "Disparo térmico por sobrecarga y magnético instantáneo por cortocircuito. Clic para abrirlo o rearmarlo.");
        }
        yield return new("guardamotor", "Guardamotor (disyuntor motor)", CatProtection, Behavior.MotorBreaker, "Q", Poles(3),
            [P("Ir", "Ajuste térmico", "A", 8, 0.1, 100), P("Im", "Disparo magnético", "× Ir", 13, 5, 20)],
            "Protección térmica de clase 10 y magnética de motor en un solo aparato. Clic para abrir o rearmar.");
        yield return new("rele_termico", "Relé térmico", CatProtection, Behavior.ThermalRelay, "F", Poles(3),
            [P("Ir", "Ajuste", "A", 8, 0.1, 800), O("clase", "Clase de disparo", 0, Classes)],
            "Mide la corriente de las tres fases. Al dispararse cambia sus contactos 95-96 (NC) y 97-98 (NA): ponles la misma designación. Clic para rearmar.");
        yield return new("diferencial_2p", "Interruptor diferencial 2P", CatProtection, Behavior.Rcd, "Q", Poles(2, "L", "L", "N", "N"),
            [P("In", "Corriente asignada", "A", 40, 6, 125), P("Idn", "Sensibilidad", "mA", 30, 10, 1000)],
            "Dispara si la corriente que sale no vuelve (fuga a tierra). Mayús+clic simula el botón de prueba; clic para abrir o rearmar.");
        yield return new("diferencial_4p", "Interruptor diferencial 4P", CatProtection, Behavior.Rcd, "Q", Poles(4, "1", "2", "3", "4", "5", "6", "N", "N"),
            [P("In", "Corriente asignada", "A", 40, 6, 125), P("Idn", "Sensibilidad", "mA", 300, 10, 1000)],
            "Diferencial trifásico con neutro. Mayús+clic simula el botón de prueba.");

        // ---------------- Receptores ----------------
        yield return new("lampara", "Lámpara", CatLoads, Behavior.Lamp, "E", TwoTerm("1", "2", "X1", "X2"),
            [P("V", "Tensión asignada", "V", 230, 1, 1000), P("P", "Potencia", "W", 60, 0.1, 10000)],
            "Luz de alumbrado. Brilla según la tensión que recibe y se funde con sobretensión mantenida.");
        yield return new("piloto", "Piloto de señalización", CatLoads, Behavior.Pilot, "H", TwoTerm("1", "2", "X1", "X2"),
            [P("V", "Tensión asignada", "V", 230, 1, 1000), P("P", "Potencia", "W", 1.5, 0.1, 100), O("color", "Color", 1, Colors)],
            "Señalización en el cuadro: marcha (verde), paro o defecto (rojo), aviso (ámbar).");
        yield return new("timbre", "Timbre / zumbador", CatLoads, Behavior.Bell, "HA", TwoTerm("1", "2", "1", "2"),
            [P("V", "Tensión asignada", "V", 230, 1, 1000), P("P", "Potencia", "W", 8, 0.1, 500)], "Aviso acústico.");
        yield return new("resistencia", "Resistencia / calefactor", CatLoads, Behavior.Heater, "R", TwoTerm(),
            [P("R", "Resistencia", "Ω", 50, 0.001, 1e9)],
            "Resistencia pura. Con un valor alto entre fase y tierra simula una fuga para probar el diferencial.");
        yield return new("electrovalvula", "Electroválvula", CatLoads, Behavior.Solenoid, "YV", TwoTerm("A1", "A2", "A1", "A2"), CoilParams(24, 4, false),
            "Bobina de válvula: abre cuando recibe al menos el 85 % de su tensión.");

        var motorParams = new[]
        {
            P("Pn", "Potencia", "kW", 4, 0.06, 1000), P("Uw", "Tensión de devanado", "V", 230, 24, 6000),
            P("rpm", "Velocidad asignada", "rpm", 1440, 100, 6000), P("cos", "Factor de potencia", "", 0.82, 0.3, 1),
            P("eta", "Rendimiento", "", 0.86, 0.3, 1), P("kI", "Corriente de arranque", "× In", 6.5, 2, 12),
            P("f", "Frecuencia asignada", "Hz", 50, 1, 400), P("J", "Inercia (motor + carga)", "kg·m²", 0.15, 0.0001, 1000),
            P("carga", "Carga", "% del par asignado", 80, 0, 200), O("tipoCarga", "Tipo de carga", 0, LoadTypes),
        };
        yield return new("motor_3f", "Motor trifásico (3 bornes)", CatMotors, Behavior.Motor3, "M",
            [new("U", 0, 0, "U1"), new("V", 2, 0, "V1"), new("W", 4, 0, "W1")],
            [.. motorParams, O("conexion", "Conexión interna", 0, Connections)],
            "Motor de inducción de jaula. La tensión de devanado es la menor de la placa (230 en un motor 230/400 V).");
        yield return new("motor_3f_6b", "Motor trifásico (6 bornes, para estrella-triángulo)", CatMotors, Behavior.Motor6, "M",
            [new("U1", 0, 0, "U1"), new("V1", 2, 0, "V1"), new("W1", 4, 0, "W1"), new("U2", 0, 6, "U2"), new("V2", 2, 6, "V2"), new("W2", 4, 6, "W2")],
            [.. motorParams.Select(p => p.Key == "Uw" ? p with { Default = 400 } : p)],
            "Motor con los seis extremos de devanado accesibles (U1-U2, V1-V2, W1-W2) para conectarlo en estrella o triángulo desde fuera.");

        yield return new("motor_1f", "Motor monofásico (condensador permanente)", CatMotors, Behavior.Motor1, "M",
            [new("U1", 0, 0, "U1"), new("U2", 2, 0, "U2")],
            [P("Pn", "Potencia", "kW", 0.75, 0.02, 5), P("V", "Tensión asignada", "V", 230, 24, 400),
             P("rpm", "Velocidad asignada", "rpm", 1420, 100, 6000), P("cos", "Factor de potencia", "", 0.92, 0.3, 1),
             P("eta", "Rendimiento", "", 0.72, 0.3, 1), P("kI", "Corriente de arranque", "× In", 4.5, 2, 10),
             P("f", "Frecuencia asignada", "Hz", 50, 1, 400), P("J", "Inercia (motor + carga)", "kg·m²", 0.02, 0.0001, 100),
             P("carga", "Carga", "% del par asignado", 80, 0, 200), O("tipoCarga", "Tipo de carga", 1, LoadTypes),
             O("giro", "Sentido de giro", 0, Directions)],
            "Motor de inducción monofásico con condensador de marcha (bombas, ventiladores, compresores domésticos). Se conecta entre fase y neutro.");

        // ---------------- Transformadores y variadores ----------------
        yield return new("trafo_1f", "Transformador monofásico", CatPower, Behavior.Transformer1, "T",
            [new("1U1", 0, 0, "1U1"), new("1U2", 2, 0, "1U2"), new("2U1", 0, 6, "2U1"), new("2U2", 2, 6, "2U2")],
            [P("S", "Potencia asignada", "VA", 250, 5, 1e6), P("V1", "Tensión primaria", "V", 400, 12, 36000),
             P("V2", "Tensión secundaria", "V", 230, 6, 36000), P("ucc", "Tensión de cortocircuito", "%", 6, 0.5, 30),
             P("i0", "Corriente de vacío", "%", 3, 0, 30)],
            "Transformador de mando o de separación (p. ej. 400/230 V o 230/24 V). Primario arriba (1U1-1U2) y secundario abajo (2U1-2U2).");
        yield return new("trafo_3f", "Transformador trifásico Dyn", CatPower, Behavior.Transformer3, "T",
            [new("1U", 0, 0, "1U"), new("1V", 2, 0, "1V"), new("1W", 4, 0, "1W"),
             new("2U", 0, 6, "2U"), new("2V", 2, 6, "2V"), new("2W", 4, 6, "2W"), new("2N", 6, 6, "2N")],
            [P("S", "Potencia asignada", "kVA", 10, 0.1, 5000), P("V1", "Tensión primaria (entre fases)", "V", 400, 100, 36000),
             P("V2", "Tensión secundaria (entre fases)", "V", 400, 100, 36000), P("ucc", "Tensión de cortocircuito", "%", 4, 0.5, 20),
             P("i0", "Corriente de vacío", "%", 2, 0, 20)],
            "Primario en triángulo y secundario en estrella con neutro accesible (2N). Crea una red nueva con su propio neutro.");
        yield return new("variador", "Variador de frecuencia", CatPower, Behavior.Vfd, "U",
            [new("L1", 0, 0, "L1"), new("L2", 2, 0, "L2"), new("L3", 4, 0, "L3"),
             new("U", 0, 8, "U"), new("V", 2, 8, "V"), new("W", 4, 8, "W"),
             new("STF", 8, 2, "STF"), new("STR", 8, 4, "STR"), new("SD", 8, 6, "SD")],
            [P("Pn", "Potencia del variador", "kW", 4, 0.1, 500), P("In", "Corriente de salida asignada", "A", 9, 0.5, 1000),
             P("Vin", "Tensión de red", "V", 400, 100, 690), P("fn", "Frecuencia base (motor)", "Hz", 50, 10, 400),
             P("fref", "Consigna de frecuencia", "Hz", 50, 0, 400), P("tacc", "Rampa de aceleración", "s", 5, 0.1, 600),
             P("tdec", "Rampa de deceleración", "s", 5, 0.1, 600)],
            "Arranca el motor con rampa y regula su velocidad (ley V/f). Une STF con SD para girar a derechas y STR con SD a izquierdas, con un contacto o una salida de PLC. Clic para rearmar un fallo.");

        // ---------------- Automatización ----------------
        {
            var plcTerms = new List<TerminalDef> { new("L+", 0, 0, "L+"), new("M", 2, 0, "M") };
            for (var i = 0; i < PlcInputs; i++) plcTerms.Add(new($"I0.{i}", 4 + 2 * i, 0, $"I0.{i}"));
            plcTerms.Add(new("1L", 0, 6, "1L"));
            for (var i = 0; i < PlcOutputs; i++) plcTerms.Add(new($"Q0.{i}", 2 + 2 * i, 6, $"Q0.{i}"));
            yield return new("plc_compacto", "PLC compacto 8E/8S relé", CatAutomation, Behavior.Plc, "KF", plcTerms,
                [P("ciclo", "Tiempo de ciclo", "ms", 10, 1, 200)],
                "Autómata de 24 V DC con 8 entradas (I0.0-I0.7, se activan con 24 V respecto a M) y 8 salidas a relé (1L común, Q0.0-Q0.7). Escribe su programa en texto estructurado en Propiedades o carga un Ladder del módulo PLC.");

            // Microcontrolador sobre una placa de E/S de 24 V: eléctricamente igual que el PLC compacto.
            var mcuTerms = new List<TerminalDef> { new("L+", 0, 0, "L+"), new("M", 2, 0, "M") };
            for (var i = 0; i < PlcInputs; i++) mcuTerms.Add(new($"I0.{i}", 4 + 2 * i, 0, $"IN{i + 1}"));
            mcuTerms.Add(new("1L", 0, 6, "COM"));
            for (var i = 0; i < PlcOutputs; i++) mcuTerms.Add(new($"Q0.{i}", 2 + 2 * i, 6, $"K{i + 1}"));
            yield return new(McuTypeId, "Arduino / ESP32 / Pico con placa de relés", CatAutomation, Behavior.Plc, "KF", mcuTerms,
                [O("placa", "Placa", 0, McuBoards), P("ciclo", "Paso de simulación", "ms", 5, 1, 50)],
                "Microcontrolador sobre una placa de E/S de 24 V DC: 8 entradas optoacopladas (IN1-IN8, activas con 24 V respecto a M) y 8 relés (COM común, K1-K8). Se programa en Propiedades: en C++ (Arduino) o en MicroPython (ESP32 y Pico).");
        }

        // ---------------- Medida y conexión ----------------
        yield return new("voltimetro", "Voltímetro", CatInstruments, Behavior.Voltmeter, "P", TwoTerm(), [],
            "Muestra la tensión eficaz entre sus bornes.");
        yield return new("amperimetro", "Amperímetro", CatInstruments, Behavior.Ammeter, "P", TwoTerm(), [],
            "Se intercala en el conductor y muestra la corriente eficaz.");
        yield return new("borne", "Borne / punto de conexión", CatInstruments, Behavior.Junction, "X", [new("1", 0, 0, "")], [],
            "Punto de unión para repartir conductores (regleta X1).");
    }
}
