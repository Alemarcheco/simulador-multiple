using System.Globalization;
using Simulador.Arduino.Runtime;
using Simulador.Banco.Generic;
using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Hidraulica.CoSim;
using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Banco.Mesa;

/// <summary>
/// Catálogo de piezas de la mesa libre: equipos genéricos del banco, PLC y placas Arduino con E/S de 24 V, piezas de
/// la red hidráulica (de la fábrica del módulo Hidráulica) e instrumentos de medida.
/// </summary>
public static class BenchCatalog
{
    public const string CatControl = "Mando y señales";
    public const string CatProgrammable = "PLC y Arduino";
    public const string CatElectric = "Eléctrico";
    public const string CatMechanical = "Máquinas y aire";
    public const string CatHydraulic = "Hidráulica";
    public const string CatInstruments = "Instrumentos";

    public static IReadOnlyList<string> Categories { get; } =
        [CatControl, CatProgrammable, CatElectric, CatMechanical, CatHydraulic, CatInstruments];

    private static readonly Dictionary<string, PartType> ById = [];

    public static IReadOnlyList<PartType> All { get; }

    static BenchCatalog()
    {
        All = [.. Control(), .. Programmable(), .. Electric(), .. Mechanical(), .. Hydraulic(), .. Instruments()];
        foreach (var t in All) ById[t.TypeId] = t;
    }

    public static PartType? Get(string typeId) => ById.GetValueOrDefault(typeId);

    // ------------------------------------------------------------------ utilidades

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static ParamDef Num(string key, string label, string unit, double def, string? help = null) =>
        new(key, label, unit, F(def), ParamKind.Number, null, help);

    private static BuiltPart Wrap(BenchPart p, PartType t, ISimComponent c, object? model = null) => new()
    {
        Part = p,
        Type = t,
        Component = c,
        SnapshotSource = c,
        Model = model ?? c,
        Signal = name => c.Signals.FirstOrDefault(s => s.Id == $"{c.Id}.{name}"),
        Power = name => c.PowerPorts.FirstOrDefault(s => s.Id == $"{c.Id}.{name}"),
    };

    private static readonly (string Label, SignalUnit Unit)[] Units =
    [
        ("V", SignalUnit.Volt), ("%", SignalUnit.Percent), ("mA", SignalUnit.MilliAmp), ("bar", SignalUnit.Pascal),
        ("m", SignalUnit.Meter), ("rpm", SignalUnit.RadPerSecond), ("L/s", SignalUnit.CubicMeterPerSecond),
    ];

    private static IReadOnlyList<string> UnitOptions => Units.Select(u => u.Label).ToList();

    /// <summary>Unidad elegida y factor de la unidad mostrada a SI (bar → Pa, rpm → rad/s, L/s → m³/s).</summary>
    private static (SignalUnit Unit, double ToSi) Unit(string label) => label switch
    {
        "bar" => (SignalUnit.Pascal, 1e5),
        "rpm" => (SignalUnit.RadPerSecond, 2 * Math.PI / 60),
        "L/s" => (SignalUnit.CubicMeterPerSecond, 1e-3),
        _ => (Units.Any(x => x.Label == label) ? Units.First(x => x.Label == label).Unit : SignalUnit.Volt, 1),
    };

    private static string Rpm(double radPerSecond) => $"{radPerSecond * 60 / (2 * Math.PI):0} rpm";

    // ------------------------------------------------------------------ mando y señales

    private static IEnumerable<PartType> Control()
    {
        PartType? button = null;
        button = new PartType
        {
            TypeId = "pulsador", Name = "Pulsador", Category = CatControl, Prefix = "S", Icon = "🔘",
            Description = "Pulsador de panel con un contacto a 24 V. NA (marcha) da 24 V mientras lo pulsas; NC (paro) los quita.",
            Params = [new("contacto", "Contacto", "", "NA", ParamKind.Choice, ["NA", "NC"])],
            FixedPorts = [new("13-14", PortKind.SignalOut, "13-14", IfMissing: "El pulsador no está unido a nada.", MissingLevel: IssueLevel.Info)],
            Create = p => Switch(p, button!, latching: false),
        };
        yield return button;

        PartType? selector = null;
        selector = new PartType
        {
            TypeId = "selector", Name = "Selector / interruptor", Category = CatControl, Prefix = "SA", Icon = "🎚",
            Description = "Selector de dos posiciones con contacto a 24 V. Se queda en la posición que lo dejas.",
            Params = [new("contacto", "Contacto", "", "NA", ParamKind.Choice, ["NA", "NC"])],
            FixedPorts = [new("13-14", PortKind.SignalOut, "13-14", IfMissing: "El selector no está unido a nada.", MissingLevel: IssueLevel.Info)],
            Create = p => Switch(p, selector!, latching: true),
        };
        yield return selector;

        PartType? lamp = null;
        lamp = new PartType
        {
            TypeId = "piloto", Name = "Piloto luminoso", Category = CatControl, Prefix = "H", Icon = "💡",
            Description = "Lámpara de señalización de 24 V. Se enciende con 24 V o con una salida a 1 (relé o transistor).",
            Params = [new("color", "Color", "", "verde", ParamKind.Choice, ["verde", "rojo", "ámbar", "azul", "blanco"]),
                Num("tension", "Tensión nominal", "V", 24)],
            FixedPorts = [new("X1", PortKind.SignalIn, "X1", IfMissing: "El piloto no tiene alimentación: nunca se encenderá.")],
            Create = p =>
            {
                var c = new PilotLamp(p.Id, p.Number("tension", 24));
                var b = Wrap(p, lamp!, c);
                string color = p.Text("color", "verde");
                b.Look = r => new([r.B(PanelKeys.Lit) ? "encendido" : "apagado"], r.B(PanelKeys.Lit) ? PartState.On : PartState.Idle, Color: color);
                return b;
            },
        };
        yield return lamp;

        PartType? setpoint = null;
        setpoint = new PartType
        {
            TypeId = "consigna", Name = "Potenciómetro / consigna", Category = CatControl, Prefix = "R", Icon = "🎛",
            Description = "Da una señal analógica que mueves con un deslizador mientras simulas (p. ej. 0-10 V a una entrada del PLC).",
            Params = [new("unidad", "Unidad", "", "V", ParamKind.Choice, UnitOptions), Num("min", "Mínimo", "", 0), Num("max", "Máximo", "", 10), Num("inicial", "Valor inicial", "", 5)],
            FixedPorts = [new("Salida", PortKind.SignalOut, "Salida", Analog: true)],
            Create = p =>
            {
                var (unit, k) = Unit(p.Text("unidad", "V"));
                double min = p.Number("min", 0), max = p.Number("max", 10);
                var c = new SetpointSource(p.Id, unit, min * k, max * k, p.Number("inicial", 5) * k);
                var b = Wrap(p, setpoint!, c);
                string u = p.Text("unidad", "V");
                b.Controls.Add(new("Valor", LiveKind.Slider, v => c.Value = v * k, () => c.Value / k, min, max, u));
                b.Look = r => new([$"{Reading.Format(r.D(GenericKeys.Output) / k)} {u}"], PartState.On);
                return b;
            },
        };
        yield return setpoint;

        PartType? transmitter = null;
        transmitter = new PartType
        {
            TypeId = "transmisor", Name = "Convertidor de señal", Category = CatControl, Prefix = "TT", Icon = "⇄",
            Description = "Convierte una magnitud en otra de forma lineal: p. ej. un nivel de 0-3 m en 0-10 V para el PLC, o 0-10 V en 0-100 %.",
            Params = [new("unidadEntrada", "Unidad de entrada", "", "m", ParamKind.Choice, UnitOptions), Num("inMin", "Entrada mínima", "", 0), Num("inMax", "Entrada máxima", "", 3),
                new("unidadSalida", "Unidad de salida", "", "V", ParamKind.Choice, UnitOptions), Num("outMin", "Salida mínima", "", 0), Num("outMax", "Salida máxima", "", 10)],
            FixedPorts = [new("Entrada", PortKind.SignalIn, "Entrada", Analog: true, IfMissing: "El convertidor no recibe ninguna medida."),
                new("Salida", PortKind.SignalOut, "Salida", Analog: true, IfMissing: "La salida del convertidor no va a ninguna parte.", MissingLevel: IssueLevel.Info)],
            Create = p =>
            {
                var (ui, ki) = Unit(p.Text("unidadEntrada", "m"));
                var (uo, ko) = Unit(p.Text("unidadSalida", "V"));
                var c = new Transmitter(p.Id, ui, p.Number("inMin", 0) * ki, p.Number("inMax", 3) * ki, uo, p.Number("outMin", 0) * ko, p.Number("outMax", 10) * ko);
                var b = Wrap(p, transmitter!, c);
                string su = p.Text("unidadSalida", "V"), iu = p.Text("unidadEntrada", "m");
                b.Look = r => new([$"{Reading.Format(r.D(GenericKeys.Input) / ki)} {iu} → {Reading.Format(r.D(GenericKeys.Output) / ko)} {su}"], PartState.On);
                return b;
            },
        };
        yield return transmitter;

        PartType? pi = null;
        pi = new PartType
        {
            TypeId = "pi", Name = "Regulador PI", Category = CatControl, Prefix = "PIC", Icon = "∫",
            Description = "Regulador proporcional-integral: compara la medida con la consigna y da una salida 0-100 % (p. ej. la admisión de una turbina).",
            Params = [new("unidad", "Unidad de la medida", "", "rpm", ParamKind.Choice, UnitOptions), Num("consigna", "Consigna", "", 3000),
                Num("kp", "Ganancia Kp", "%", 2500), Num("ti", "Tiempo integral Ti", "s", 3),
                new("inverso", "Acción inversa (sube si la medida baja)", "", "sí", ParamKind.Choice, ["sí", "no"])],
            FixedPorts = [new("Medida", PortKind.SignalIn, "Medida", Analog: true, IfMissing: "El regulador no recibe la medida: no sabe qué corregir.", MissingLevel: IssueLevel.Error),
                new("Salida", PortKind.SignalOut, "Salida", Analog: true, IfMissing: "La salida del regulador no mueve nada.")],
            Create = p =>
            {
                var (unit, k) = Unit(p.Text("unidad", "rpm"));
                var c = new PiController(p.Id, unit, p.Number("consigna", 3000) * k, p.Number("kp", 2500), Math.Max(0.01, p.Number("ti", 3)), p.Text("inverso", "sí") != "no");
                var b = Wrap(p, pi!, c);
                string u = p.Text("unidad", "rpm");
                double sp = p.Number("consigna", 3000);
                b.Controls.Add(new("Consigna", LiveKind.Slider, v => c.Setpoint = v * k, () => c.Setpoint / k, 0, Math.Max(1, sp * 1.5), u));
                b.Look = r => new([$"medida {Reading.Format(r.D(GenericKeys.Input) / k)} {u}", $"salida {r.D(GenericKeys.Output):0} %"], PartState.On);
                return b;
            },
        };
        yield return pi;
    }

    private static BuiltPart Switch(BenchPart p, PartType t, bool latching)
    {
        var c = new PanelSwitch(p.Id, p.Text("contacto", "NA") == "NC", latching);
        var b = Wrap(p, t, c);
        b.Controls.Add(latching
            ? new("Posición", LiveKind.Toggle, v => c.Actuated = v > 0.5, () => c.Actuated ? 1 : 0)
            : new("Pulsar", LiveKind.Momentary, v => c.Actuated = v > 0.5));
        b.Look = r => new([(r.B(PanelKeys.Actuated) ? (latching ? "posición 1" : "pulsado") : (latching ? "posición 0" : "suelto")) + (r.B(GenericKeys.Closed) ? " · 24 V" : " · 0 V")],
            r.B(GenericKeys.Closed) ? PartState.On : PartState.Idle);
        return b;
    }

    // ------------------------------------------------------------------ PLC y Arduino

    public const string DefaultPlcProgram = """
        PROGRAM Main
        VAR
            Marcha AT %IX0.0 : BOOL;   (* pulsador NA *)
            Paro   AT %IX0.1 : BOOL;   (* pulsador NC: con 24 V todo va bien *)
            Motor  AT %QX0.0 : BOOL;   (* bobina del contactor *)
            Piloto AT %QX0.1 : BOOL;
        END_VAR

        Motor := (Marcha OR Motor) AND Paro;
        Piloto := Motor;
        END_PROGRAM
        """;

    private static readonly string[] Boards = ["Arduino Uno", "Arduino Nano", "Arduino Mega", "ESP32", "Raspberry Pi Pico"];

    private static ArduinoBoard Board(string name) => name switch
    {
        "Arduino Nano" => ArduinoBoard.Nano,
        "Arduino Mega" => ArduinoBoard.Mega,
        "ESP32" => ArduinoBoard.Esp32,
        "Raspberry Pi Pico" => ArduinoBoard.Pico,
        _ => ArduinoBoard.Uno,
    };

    /// <summary>Programa que se usa si el de la placa está vacío (marcha/paro en IN1/IN2 → K1).</summary>
    public static string ArduinoTemplate(string board) => McuIoBoard.Template(Board(board));

    private static IReadOnlyList<PortDef>? _plcPorts;

    private static IReadOnlyList<PortDef> PlcPorts()
    {
        if (_plcPorts is not null) return _plcPorts;
        var cpu = new PlcCpu("X", PlcProfiles.Get("generico-iec"));
        string N(ISignalPort s) => s.Id[2..];
        var list = new List<PortDef>();
        list.AddRange(cpu.DigitalInputs.Take(8).Select(s => new PortDef(N(s), PortKind.SignalIn)));
        list.AddRange(cpu.AnalogInputs.Take(2).Select(s => new PortDef(N(s), PortKind.SignalIn, Analog: true)));
        list.AddRange(cpu.DigitalOutputs.Take(8).Select(s => new PortDef(N(s), PortKind.SignalOut)));
        list.AddRange(cpu.AnalogOutputs.Take(2).Select(s => new PortDef(N(s), PortKind.SignalOut, Analog: true)));
        return _plcPorts = list;
    }

    private static IEnumerable<PartType> Programmable()
    {
        PartType? plc = null;
        plc = new PartType
        {
            TypeId = "plc", Name = "PLC (IEC 61131-3)", Category = CatProgrammable, Prefix = "PLC", Icon = "▦", Module = "plc",
            Description = "Autómata genérico con 8 entradas y 8 salidas digitales a 24 V, 2 entradas y 2 salidas 0-10 V. Se programa en texto estructurado (ST).",
            Params = [new("programa", "Programa (texto estructurado)", "", DefaultPlcProgram, ParamKind.Program), Num("ciclo", "Tiempo de ciclo", "ms", 10)],
            DynamicPorts = _ => PlcPorts(),
            Create = p =>
            {
                var cpu = new PlcCpu(p.Id, PlcProfiles.Get("generico-iec")) { CycleTime = TimeSpan.FromMilliseconds(Math.Clamp(p.Number("ciclo", 10), 1, 1000)) };
                string src = p.Text("programa", "");
                cpu.LoadStructuredText(string.IsNullOrWhiteSpace(src) ? DefaultPlcProgram : src);
                var b = Wrap(p, plc!, cpu);
                b.Signal = name => cpu.Signals.FirstOrDefault(s => s.Id == $"{p.Id}.{name}");
                b.AfterReset = cpu.Start;
                b.Controls.Add(new("RUN / STOP", LiveKind.Toggle, v => { if (v > 0.5) cpu.Start(); else cpu.Stop(); }, () => cpu.State == PlcState.Run ? 1 : 0));
                var outs = cpu.DigitalOutputs.Take(8).Select(s => s.Id).ToArray();
                var ins = cpu.DigitalInputs.Take(8).Select(s => s.Id).ToArray();
                static string Bits(PartReader r, string[] ids, double on) => string.Concat(ids.Select(id => r.D(id) > on ? "■" : "□"));
                b.Look = r =>
                {
                    string st = r.S(PlcCpu.StateKeys.State, "Stop");
                    return new([st == "Run" ? "RUN" : st == "Fault" ? "FALLO" : "STOP", "E " + Bits(r, ins, 15), "S " + Bits(r, outs, 0.5)],
                        st == "Run" ? PartState.On : st == "Fault" ? PartState.Fault : PartState.Idle);
                };
                b.Watches = r =>
                [
                    new("plc-fault", r.S(PlcCpu.StateKeys.State) == "Fault", IssueLevel.Error,
                        $"El {p.Id} se ha parado por un fallo del programa: {r.S(PlcCpu.StateKeys.Fault, "error")}.",
                        "Revisa el programa en la ficha del PLC (división por cero, bucle sin fin, índice fuera de rango…)."),
                ];
                return b;
            },
        };
        yield return plc;

        PartType? ard = null;
        ard = new PartType
        {
            TypeId = "arduino-io", Name = "Arduino con placa de E/S 24 V", Category = CatProgrammable, Prefix = "ARD", Icon = "∞", Module = "plc",
            Description = "Arduino, ESP32 o Pico montado en una placa con 8 entradas optoacopladas de 24 V (IN1-IN8), 8 relés (K1-K8) y 2 entradas 0-10 V. Se programa en C++ de Arduino o en MicroPython.",
            Params = [new("placa", "Placa", "", "Arduino Uno", ParamKind.Choice, Boards),
                new("programa", "Programa", "", "", ParamKind.Program, Help: "Vacío = programa de ejemplo (marcha/paro con IN1, IN2 y K1).")],
            FixedPorts =
            [
                .. Enumerable.Range(1, McuIoBoard.Channels).Select(i => new PortDef($"IN{i}", PortKind.SignalIn)),
                .. Enumerable.Range(1, McuIoBoard.AnalogChannels).Select(i => new PortDef($"AI{i}", PortKind.SignalIn, Analog: true)),
                .. Enumerable.Range(1, McuIoBoard.Channels).Select(i => new PortDef($"K{i}", PortKind.SignalOut)),
            ],
            Create = p =>
            {
                var board = Board(p.Text("placa", "Arduino Uno"));
                var io = new McuIoBoard(p.Id, board);
                string src = p.Text("programa", "");
                if (string.IsNullOrWhiteSpace(src)) src = McuIoBoard.Template(board);
                io.Load(src);
                var b = Wrap(p, ard!, io);
                b.AfterReset = () => io.Load(src);
                b.Look = r =>
                {
                    string st = r.S(ArduinoMcu.StateKeys.State, "Stopped");
                    return new([board.Name, st == "Running" ? "en marcha" : st == "Fault" ? "FALLO" : "parado"],
                        st == "Running" ? PartState.On : st == "Fault" ? PartState.Fault : PartState.Idle);
                };
                b.Watches = r =>
                [
                    new("mcu-fault", r.S(ArduinoMcu.StateKeys.State) == "Fault", IssueLevel.Error,
                        $"El programa de {p.Id} se ha parado: {r.S(ArduinoMcu.StateKeys.Fault, "error")}.", "Revisa el programa en la ficha de la placa."),
                ];
                return b;
            },
        };
        yield return ard;
    }

    // ------------------------------------------------------------------ eléctrico

    private static IEnumerable<PartType> Electric()
    {
        PartType? grid = null;
        grid = new PartType
        {
            TypeId = "red", Name = "Red trifásica", Category = CatElectric, Prefix = "RED", Icon = "⚡",
            Description = "Acometida de la compañía: da la tensión de línea y recibe la corriente que piden las cargas. Puedes simular un corte.",
            Params = [Num("tension", "Tensión de línea", "V", 400), Num("frecuencia", "Frecuencia", "Hz", 50)],
            FixedPorts = [new("L", PortKind.Power, "L1-L2-L3", PowerDomain.Electrical3Ph, Causality.ImposesEffort, PortSide.Bottom, "La red no alimenta nada.", IssueLevel.Info)],
            Create = p =>
            {
                var c = new GridSource(p.Id, p.Number("tension", 400), p.Number("frecuencia", 50));
                var b = Wrap(p, grid!, c);
                b.Controls.Add(new("Corte de red", LiveKind.Toggle, v => c.Outage = v > 0.5, () => c.Outage ? 1 : 0));
                b.Look = r => new([$"{r.D(GenericKeys.Voltage):0} V · {r.D(GenericKeys.Current):0.0} A"], r.D(GenericKeys.Voltage) > 1 ? PartState.On : PartState.Warning);
                b.Watches = r => [new("outage", r.D(GenericKeys.Voltage) < 1, IssueLevel.Info, $"{p.Id}: no hay tensión (corte de red).", "Quita el corte de red en la ficha para reponer el suministro.")];
                return b;
            },
        };
        yield return grid;

        PartType? bus = null;
        bus = new PartType
        {
            TypeId = "barras", Name = "Embarrado (reparto)", Category = CatElectric, Prefix = "B", Icon = "☰",
            Description = "Juego de barras: reparte una alimentación entre hasta 4 salidas y suma sus corrientes. Úsalo para colgar varios motores o cargas de la misma red.",
            FixedPorts = [new("Entrada", PortKind.Power, "Entrada", PowerDomain.Electrical3Ph, Causality.ImposesFlow, PortSide.Top, "El embarrado no tiene alimentación.", IssueLevel.Error),
                .. Enumerable.Range(1, Busbar.Outputs).Select(i => new PortDef($"S{i}", PortKind.Power, $"S{i}", PowerDomain.Electrical3Ph, Causality.ImposesEffort, PortSide.Bottom))],
            Create = p =>
            {
                var c = new Busbar(p.Id);
                var b = Wrap(p, bus!, c);
                b.Look = r => new([$"{r.D(GenericKeys.Voltage):0} V · {r.D(GenericKeys.Current):0.0} A"], r.D(GenericKeys.Voltage) > 1 ? PartState.On : PartState.Idle);
                return b;
            },
        };
        yield return bus;

        PartType? contactor = null;
        contactor = new PartType
        {
            TypeId = "contactor", Name = "Contactor con relé térmico", Category = CatElectric, Prefix = "KM", Icon = "⊣⊢",
            Description = "Contactor tripolar con bobina de 24 V y relé térmico (clase 10). 13-14: contacto auxiliar; 97-98: aviso de disparo del térmico.",
            Params = [Num("termico", "Ajuste del térmico", "A", 5, "Pon la corriente nominal del motor (la de su placa)."), Num("retardo", "Retardo de cierre", "ms", 20)],
            FixedPorts =
            [
                new("A1", PortKind.SignalIn, "A1 bobina", IfMissing: "La bobina no tiene mando: el contactor nunca cerrará.", MissingLevel: IssueLevel.Warning),
                new("13-14", PortKind.SignalOut, "13-14"),
                new("97-98", PortKind.SignalOut, "97-98"),
                new("L", PortKind.Power, "L entrada", PowerDomain.Electrical3Ph, Causality.ImposesFlow, PortSide.Top, "El contactor no tiene alimentación (falta el cable desde la red).", IssueLevel.Error),
                new("T", PortKind.Power, "T salida", PowerDomain.Electrical3Ph, Causality.ImposesEffort, PortSide.Bottom, "El contactor no alimenta nada.", IssueLevel.Warning),
            ],
            Create = p =>
            {
                double setting = p.Number("termico", 5);
                var c = new Contactor(p.Id, setting, p.Number("retardo", 20));
                var b = Wrap(p, contactor!, c);
                b.Controls.Add(new("Rearmar térmico", LiveKind.Button, _ => c.ResetOverload()));
                b.Controls.Add(new("Ajuste del térmico", LiveKind.Slider, v => c.OverloadSetting = v, () => c.OverloadSetting, 0.1, Math.Max(10, setting * 3), "A"));
                b.Look = r => new([r.B(GenericKeys.Tripped) ? "TÉRMICO DISPARADO" : r.B(GenericKeys.Closed) ? "cerrado" : r.B(GenericKeys.CoilOn) ? "cerrando…" : "abierto",
                    $"{r.D(GenericKeys.Current):0.0} A"],
                    r.B(GenericKeys.Tripped) ? PartState.Fault : r.B(GenericKeys.Closed) ? PartState.On : PartState.Idle);
                b.Watches = r =>
                [
                    new("trip", r.B(GenericKeys.Tripped), IssueLevel.Error,
                        $"El relé térmico de {p.Id} ha disparado: la corriente superó durante demasiado tiempo el ajuste de {c.OverloadSetting:0.##} A.",
                        "Comprueba que el ajuste del térmico sea la corriente nominal del motor y que la carga no lo frene. Luego pulsa «Rearmar térmico»."),
                ];
                return b;
            },
        };
        yield return contactor;

        PartType? motor = null;
        motor = new PartType
        {
            TypeId = "motor", Name = "Motor asíncrono trifásico", Category = CatElectric, Prefix = "M", Icon = "Ⓜ",
            Description = "Motor de inducción con sus datos de placa. Pide mucha corriente al arrancar (unas 6,5 veces la nominal) y mueve el eje de lo que le acoples.",
            Params = [Num("potencia", "Potencia", "kW", 2.2), Num("tension", "Tensión", "V", 400), Num("rpm", "Velocidad nominal", "rpm", 2900),
                Num("corriente", "Corriente nominal", "A", 4.7), Num("inercia", "Inercia del rotor", "kg·m²", 0.006), Num("inerciaCarga", "Inercia de la carga", "kg·m²", 0.01)],
            FixedPorts =
            [
                new("Bornes", PortKind.Power, "U-V-W", PowerDomain.Electrical3Ph, Causality.ImposesFlow, PortSide.Top, "El motor no tiene alimentación.", IssueLevel.Error),
                new("Eje", PortKind.Power, "Eje", PowerDomain.MechanicalRotational, Causality.ImposesFlow, PortSide.Right, "El motor gira en vacío: no mueve nada.", IssueLevel.Info),
                new("Velocidad", PortKind.SignalOut, "Velocidad", Analog: true, Side: PortSide.Bottom),
            ],
            Create = p =>
            {
                double rated = p.Number("corriente", 4.7);
                var c = new InductionMotorGeneric(p.Id, p.Number("potencia", 2.2), p.Number("tension", 400), p.Number("rpm", 2900), rated,
                    inertia: Math.Max(1e-4, p.Number("inercia", 0.006)), loadInertia: Math.Max(0, p.Number("inerciaCarga", 0.01)));
                var b = Wrap(p, motor!, c);
                b.Look = r => new([Rpm(r.D(GenericKeys.Speed)), $"{r.D(GenericKeys.Current):0.0} A"],
                    r.D(GenericKeys.Speed) > 1 ? PartState.On : PartState.Idle, Spin: r.D(GenericKeys.Speed) / c.SyncSpeed);
                b.Watches = r =>
                {
                    double v = r.D(GenericKeys.Voltage), w = r.D(GenericKeys.Speed), i = r.D(GenericKeys.Current);
                    return
                    [
                        new("stall", v > 0.5 * c.RatedVoltage && w < 0.5 * c.SyncSpeed, IssueLevel.Error,
                            $"{p.Id} tiene tensión pero no consigue arrancar ({Rpm(w)}): la carga pide más par del que da el motor.",
                            "Usa un motor más potente, baja la carga o revisa la inercia y la relación de la máquina acoplada.", Hold: 4),
                        new("overload", w > 0.5 * c.SyncSpeed && i > 1.15 * rated, IssueLevel.Warning,
                            $"{p.Id} trabaja sobrecargado: pide {i:0.0} A y su corriente nominal es {rated:0.0} A.",
                            "Si sigue así el térmico disparará. Reduce la carga o elige un motor mayor.", Hold: 5),
                        new("lowv", v > 1 && v < 0.85 * c.RatedVoltage, IssueLevel.Warning,
                            $"{p.Id} recibe {v:0} V y es de {c.RatedVoltage:0} V: con tensión baja da mucho menos par.",
                            "Comprueba la tensión de la red o del generador que lo alimenta.", Hold: 2),
                    ];
                };
                return b;
            },
        };
        yield return motor;

        PartType? gen = null;
        gen = new PartType
        {
            TypeId = "generador", Name = "Generador síncrono", Category = CatElectric, Prefix = "G", Icon = "Ⓖ",
            Description = "Alternador en isla con regulador de tensión: la frecuencia sale de la velocidad del eje y el par que pide, de la potencia que consumen las cargas.",
            Params = [Num("kva", "Potencia aparente", "kVA", 500), Num("tension", "Tensión", "V", 400), Num("rpm", "Velocidad nominal", "rpm", 3000), Num("frecuencia", "Frecuencia", "Hz", 50)],
            FixedPorts =
            [
                new("Eje", PortKind.Power, "Eje", PowerDomain.MechanicalRotational, Causality.ImposesEffort, PortSide.Left, "Nadie mueve el generador: acopla una turbina o un motor a su eje.", IssueLevel.Error),
                new("Bornes", PortKind.Power, "Bornes", PowerDomain.Electrical3Ph, Causality.ImposesEffort, PortSide.Bottom, "El generador no alimenta nada.", IssueLevel.Info),
                new("Velocidad", PortKind.SignalOut, "Velocidad", Analog: true),
            ],
            Create = p =>
            {
                double kva = p.Number("kva", 500), hz = p.Number("frecuencia", 50);
                var c = new SynchronousGenerator(p.Id, kva, p.Number("tension", 400), p.Number("rpm", 3000), hz);
                var b = Wrap(p, gen!, c);
                b.Look = r => new([$"{r.D(GenericKeys.Frequency):0.00} Hz · {r.D(GenericKeys.Voltage):0} V", $"{r.D(GenericKeys.Power) / 1000:0} kW"],
                    r.D(GenericKeys.Voltage) > 1 ? PartState.On : PartState.Idle, Spin: r.D(GenericKeys.Speed) / c.RatedSpeed);
                b.Watches = r =>
                {
                    double f = r.D(GenericKeys.Frequency), kw = r.D(GenericKeys.Power) / 1000;
                    return
                    [
                        new("freq", f > 0.2 * hz && (f < 0.95 * hz || f > 1.05 * hz), IssueLevel.Warning,
                            $"La frecuencia de {p.Id} es {f:0.0} Hz (debería ser {hz:0} Hz).",
                            "La máquina que lo mueve no mantiene la velocidad: revisa el regulador o la carga conectada.", Hold: 5),
                        new("genover", kw > 1.1 * kva * c.PowerFactor, IssueLevel.Warning,
                            $"{p.Id} entrega {kw:0} kW y su potencia es {kva * c.PowerFactor:0} kW: está sobrecargado.", "Quita carga o usa un generador mayor.", Hold: 3),
                    ];
                };
                return b;
            },
        };
        yield return gen;

        PartType? load = null;
        load = new PartType
        {
            TypeId = "carga", Name = "Carga eléctrica", Category = CatElectric, Prefix = "C", Icon = "⏚",
            Description = "Consumo trifásico (alumbrado, resistencias…). La potencia baja con el cuadrado de la tensión. Se puede cambiar en marcha.",
            Params = [Num("potencia", "Potencia", "kW", 100), Num("tension", "Tensión nominal", "V", 400), Num("fp", "Factor de potencia", "", 0.9)],
            FixedPorts = [new("Bornes", PortKind.Power, "Bornes", PowerDomain.Electrical3Ph, Causality.ImposesFlow, PortSide.Top, "La carga no está alimentada.", IssueLevel.Warning)],
            Create = p =>
            {
                double kw = p.Number("potencia", 100);
                var c = new ElectricalLoad(p.Id, kw, p.Number("tension", 400), Math.Clamp(p.Number("fp", 0.9), 0.1, 1));
                var b = Wrap(p, load!, c);
                b.Controls.Add(new("Potencia", LiveKind.Slider, v => c.PowerKw = v, () => c.PowerKw, 0, Math.Max(1, kw * 2), "kW"));
                b.Look = r => new([$"{r.D(GenericKeys.Power) / 1000:0.0} kW · {r.D(GenericKeys.Current):0.0} A"], r.D(GenericKeys.Voltage) > 1 ? PartState.On : PartState.Idle);
                return b;
            },
        };
        yield return load;
    }

    // ------------------------------------------------------------------ máquinas y aire

    private static IEnumerable<PartType> Mechanical()
    {
        PartType? turbine = null;
        turbine = new PartType
        {
            TypeId = "turbina", Name = "Turbina", Category = CatMechanical, Prefix = "T", Icon = "✇",
            Description = "Turbina de vapor, gas o hidráulica: el par depende de la admisión (0-100 %). Tiene limitador de velocidad y dispara al 110 %.",
            Params = [new("tipo", "Tipo", "", "vapor", ParamKind.Choice, ["vapor", "gas", "hidráulica"]), Num("potencia", "Potencia", "kW", 500),
                Num("rpm", "Velocidad nominal", "rpm", 3000), Num("inercia", "Inercia", "kg·m²", 8), Num("inerciaCarga", "Inercia de la carga", "kg·m²", 4),
                Num("actuador", "Tiempo del actuador de admisión", "s", 0.8)],
            FixedPorts =
            [
                new("Admision", PortKind.SignalIn, "Admisión %", Analog: true, IfMissing: "Nadie manda la admisión: la turbina no arrancará.", MissingLevel: IssueLevel.Warning),
                new("Eje", PortKind.Power, "Eje", PowerDomain.MechanicalRotational, Causality.ImposesFlow, PortSide.Right, "La turbina gira en vacío: se embalará.", IssueLevel.Warning),
                new("Velocidad", PortKind.SignalOut, "Velocidad", Analog: true),
            ],
            Create = p =>
            {
                var kind = p.Text("tipo", "vapor") switch { "gas" => TurbineKind.Gas, "hidráulica" => TurbineKind.Hydro, _ => TurbineKind.Steam };
                var c = new Turbine(p.Id, kind, p.Number("potencia", 500), p.Number("rpm", 3000), Math.Max(0.01, p.Number("inercia", 8)), Math.Max(0, p.Number("inerciaCarga", 4)),
                    Math.Max(0.01, p.Number("actuador", 0.8)));
                var b = Wrap(p, turbine!, c);
                b.Controls.Add(new("Rearmar disparo", LiveKind.Button, _ => c.ResetTrip()));
                b.Look = r => new([Rpm(r.D(GenericKeys.Speed)), r.B(GenericKeys.Tripped) ? "DISPARADA" : $"admisión {r.D(GenericKeys.Admission) * 100:0} %"],
                    r.B(GenericKeys.Tripped) ? PartState.Fault : r.D(GenericKeys.Speed) > 1 ? PartState.On : PartState.Idle, Spin: r.D(GenericKeys.Speed) / c.RatedSpeed);
                b.Watches = r =>
                [
                    new("overspeed", r.B(GenericKeys.Tripped), IssueLevel.Error,
                        $"{p.Id} ha disparado por sobrevelocidad (pasó del 110 %): se quedó sin carga o la admisión era demasiado alta.",
                        "Pon un regulador de velocidad (Regulador PI) que mida la velocidad y mande la admisión, y pulsa «Rearmar disparo»."),
                ];
                return b;
            },
        };
        yield return turbine;

        PartType? comp = null;
        comp = new PartType
        {
            TypeId = "compresor", Name = "Compresor de aire", Category = CatMechanical, Prefix = "K", Icon = "◎",
            Description = "Compresor (centrífugo o de tornillo): da más aire cuanto más rápido gira y menos cuanto más presión hay a la salida.",
            Params = [Num("caudal", "Caudal nominal (aire libre)", "m³/s", 0.5), Num("presionMax", "Presión máxima", "bar", 9), Num("rpm", "Velocidad nominal", "rpm", 3000)],
            FixedPorts =
            [
                new("Eje", PortKind.Power, "Eje", PowerDomain.MechanicalRotational, Causality.ImposesEffort, PortSide.Left, "Nadie mueve el compresor: acóplale un motor o una turbina.", IssueLevel.Error),
                new("Salida", PortKind.Power, "Salida de aire", PowerDomain.Pneumatic, Causality.ImposesFlow, PortSide.Right, "El aire del compresor no va a ningún depósito.", IssueLevel.Warning),
            ],
            Create = p =>
            {
                var c = new Compressor(p.Id, p.Number("caudal", 0.5), p.Number("presionMax", 9) * 1e5, p.Number("rpm", 3000));
                var b = Wrap(p, comp!, c);
                b.Look = r => new([$"{r.D(GenericKeys.Flow):0.00} m³/s", $"{r.D(GenericKeys.Pressure) / 1e5:0.0} bar"], r.D(GenericKeys.Speed) > 1 ? PartState.On : PartState.Idle,
                    Spin: r.D(GenericKeys.Speed) / c.RatedSpeed);
                return b;
            },
        };
        yield return comp;

        PartType? recv = null;
        recv = new PartType
        {
            TypeId = "deposito-aire", Name = "Depósito de aire comprimido", Category = CatMechanical, Prefix = "D", Icon = "⬭",
            Description = "Calderín de aire con consumo ajustable, válvula de seguridad y transmisor de presión.",
            Params = [Num("volumen", "Volumen", "m³", 4), Num("seguridad", "Tarado de la válvula de seguridad", "bar", 9.5), Num("consumo", "Consumo (aire libre a 7 bar)", "m³/s", 0.15)],
            FixedPorts =
            [
                new("Entrada", PortKind.Power, "Entrada de aire", PowerDomain.Pneumatic, Causality.ImposesEffort, PortSide.Left, "El depósito no recibe aire de ningún compresor.", IssueLevel.Warning),
                new("Presion", PortKind.SignalOut, "Presión", Analog: true),
            ],
            Create = p =>
            {
                double relief = p.Number("seguridad", 9.5);
                var c = new AirReceiver(p.Id, Math.Max(0.01, p.Number("volumen", 4)), relief * 1e5) { Demand = p.Number("consumo", 0.15) };
                var b = Wrap(p, recv!, c);
                double d0 = c.Demand;
                b.Controls.Add(new("Consumo de aire", LiveKind.Slider, v => c.Demand = v, () => c.Demand, 0, Math.Max(0.05, d0 * 3), "m³/s"));
                b.Look = r => new([$"{r.D(GenericKeys.Pressure) / 1e5:0.00} bar", r.B(GenericKeys.Relief) ? "¡seguridad abierta!" : ""],
                    r.B(GenericKeys.Relief) ? PartState.Warning : PartState.On, Fill: Math.Clamp(r.D(GenericKeys.Pressure) / (relief * 1e5), 0, 1));
                b.Watches = r =>
                [
                    new("relief", r.B(GenericKeys.Relief), IssueLevel.Warning,
                        $"La válvula de seguridad de {p.Id} está soltando aire: la presión llegó al tarado de {relief:0.#} bar.",
                        "El compresor da más aire del que se consume. Regula la velocidad del compresor (PLC o regulador PI)."),
                ];
                return b;
            },
        };
        yield return recv;
    }

    // ------------------------------------------------------------------ hidráulica

    private static IEnumerable<PartType> Hydraulic()
    {
        var factory = new HydraulicComponentFactory();
        foreach (var d in factory.Catalog)
        {
            var ports = new List<PortDef>();
            foreach (var pd in d.Ports)
            {
                switch (pd.Name)
                {
                    case HydraulicPart.Inlet:
                        ports.Add(new(pd.Name, PortKind.Pipe, "Entrada", Side: PortSide.Left, IfMissing: "La entrada de agua está al aire (sin tubería).")); break;
                    case HydraulicPart.Outlet:
                        ports.Add(new(pd.Name, PortKind.Pipe, "Salida", Side: PortSide.Right, IfMissing: "La salida de agua está al aire (sin tubería).")); break;
                    case HydraulicPart.Mouth:
                        ports.Add(new(pd.Name, PortKind.Pipe, "Boca", Side: PortSide.Bottom, IfMissing: d.TypeId == "nudo" ? "La unión no conecta nada." : null, MissingLevel: IssueLevel.Info)); break;
                    case "Eje":
                        ports.Add(new("Eje", PortKind.Power, "Eje", PowerDomain.MechanicalRotational, Causality.ImposesEffort, PortSide.Bottom)); break;
                    case "Medida":
                        ports.Add(new("Toma", PortKind.Tap, "Toma", Side: PortSide.Left, IfMissing: "El transmisor no está puesto en ninguna pieza: no mide nada.", MissingLevel: IssueLevel.Error));
                        ports.Add(new("Medida", PortKind.SignalOut, "Medida", Analog: true)); break;
                    default:
                        ports.Add(new(pd.Name, PortKind.SignalIn, pd.Name, Side: PortSide.Top, Analog: pd.Unit != "Bool")); break;
                }
            }
            string prefix = d.TypeId switch
            {
                "deposito" => "DEP", "tanque" => "TK", "nudo" => "N", "tuberia" => "TB", "bomba" => "P", "valvula" => "V",
                "valvula-control" => "FV", "electrovalvula" => "YV", "antirretorno" => "VR", "sensor-presion" => "PT", "sensor-nivel" => "LT",
                "caudalimetro" => "FT", _ => "H",
            };
            string desc = d.TypeId switch
            {
                "deposito" => "Depósito o cisterna de nivel constante: fija la presión de la red (de aquí sale o aquí llega el agua).",
                "tanque" => "Tanque abierto: su nivel sube o baja con el agua que entra y sale. Rebosa si se llena.",
                "nudo" => "Unión de tuberías o punto de consumo con un caudal fijo.",
                "tuberia" => "Tubería con pérdidas por rozamiento según longitud, diámetro y rugosidad.",
                "bomba" => "Bomba centrífuga. Muévela con un motor por el eje, o arráncala con la señal Marcha si no tiene motor.",
                "valvula" => "Válvula manual con apertura fija.",
                "valvula-control" => "Válvula de control: la consigna 0-100 % (p. ej. de una salida analógica) fija su apertura.",
                "electrovalvula" => "Electroválvula todo/nada: abre con la bobina activada.",
                "antirretorno" => "Válvula antirretorno: solo deja pasar el agua de entrada a salida.",
                "sensor-presion" => "Transmisor de presión. Une su Toma a un tanque, depósito o unión.",
                "sensor-nivel" => "Transmisor de nivel (metros). Une su Toma a un tanque.",
                "caudalimetro" => "Caudalímetro. Une su Toma a una tubería, bomba o válvula.",
                _ => "",
            };
            var parms = d.Parameters.Where(pp => pp.Name != "objetivo").Select(pp => new ParamDef(pp.Name, Label(pp.Name), pp.Unit,
                pp.DefaultValue is double dv ? F(dv) : Convert.ToString(pp.DefaultValue, CultureInfo.InvariantCulture) ?? "")).ToList();
            yield return new PartType
            {
                TypeId = "h-" + d.TypeId, Name = d.Name, Category = CatHydraulic, Prefix = prefix, Icon = d.Icon, Module = "hidraulica",
                Description = desc, HydraulicType = d.TypeId, FixedPorts = ports, Params = parms,
            };
        }
    }

    private static string Label(string key) => key switch
    {
        "cota" => "Cota (altura del fondo)", "nivel" => "Nivel", "diametro" => "Diámetro", "alturaMax" => "Altura máxima",
        "nivelInicial" => "Nivel inicial", "consumo" => "Consumo", "longitud" => "Longitud", "rugosidad" => "Rugosidad",
        "perdidasLocales" => "Pérdidas locales (K)", "alturaCero" => "Altura a caudal cero", "caudalNominal" => "Caudal nominal",
        "alturaNominal" => "Altura nominal", "velocidadNominal" => "Velocidad nominal", "rendimiento" => "Rendimiento",
        "inercia" => "Inercia", "apertura" => "Apertura", "tiempoRecorrido" => "Tiempo de recorrido",
        _ => key,
    };

    // ------------------------------------------------------------------ instrumentos

    private static IEnumerable<PartType> Instruments()
    {
        yield return new PartType
        {
            TypeId = "multimetro", Name = "Multímetro", Category = CatInstruments, Prefix = "MM", Icon = "Ω", Instrument = InstrumentKind.Multimeter,
            Description = "Une su punta a cualquier borne: en un cable mide tensión, corriente y potencia; en un eje, velocidad y par; en una tubería, presión o caudal; en una señal, su valor.",
            FixedPorts = [new("Punta", PortKind.Probe, "Punta", IfMissing: "La punta del multímetro no toca nada.", MissingLevel: IssueLevel.Info)],
        };
        yield return new PartType
        {
            TypeId = "osciloscopio", Name = "Osciloscopio", Category = CatInstruments, Prefix = "OSC", Icon = "∿", Instrument = InstrumentKind.Oscilloscope,
            Description = "Cuatro canales que dibujan cómo cambia cada magnitud con el tiempo (p. ej. el pico de corriente al arrancar un motor).",
            Params = [Num("ventana", "Ancho de pantalla", "s", 5), Num("muestreo", "Una muestra cada", "ms", 2)],
            FixedPorts = [.. Enumerable.Range(1, 4).Select(i => new PortDef($"CH{i}", PortKind.Probe, $"CH{i}"))],
        };
        yield return new PartType
        {
            TypeId = "analizador", Name = "Línea de tiempo de señales", Category = CatInstruments, Prefix = "LA", Icon = "⊓⊔", Instrument = InstrumentKind.LogicAnalyzer,
            Description = "Analizador lógico de 8 canales: muestra cuándo se encienden y se apagan las señales digitales (pulsadores, salidas del PLC, contactores).",
            Params = [Num("ventana", "Ancho de pantalla", "s", 10)],
            FixedPorts = [.. Enumerable.Range(1, 8).Select(i => new PortDef($"D{i}", PortKind.Probe, $"D{i}"))],
        };
    }
}
