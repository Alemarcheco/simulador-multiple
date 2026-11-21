namespace Simulador.Banco.Mesa;

/// <summary>Montajes de ejemplo para empezar en la mesa libre: se abren, se miran y se cambian.</summary>
public static class BenchExamples
{
    public sealed record Example(string Name, string Level, Func<BenchProject> Create);

    public static IReadOnlyList<Example> All { get; } =
    [
        new("Marcha y paro de un motor con PLC", "Principiante", MotorStartStop),
        new("Marcha y paro con Arduino y placa de relés", "Principiante", ArduinoStartStop),
        new("Llenado de tanque: PLC, motor y bomba", "Intermedio", TankFill),
        new("Turbogenerador en isla con regulador PI", "Intermedio", TurboGenerator),
    ];

    private sealed class Builder(string name, string description)
    {
        public BenchProject Project { get; } = new() { Name = name, Description = description };

        public BenchPart Add(string type, string id, double x, double y, params (string Key, string Value)[] prms)
        {
            var t = BenchCatalog.Get(type) ?? throw new KeyNotFoundException(type);
            var p = t.NewPart(id, x, y);
            foreach (var (k, v) in prms) p.Params[k] = v;
            Project.Parts.Add(p);
            return p;
        }

        public void Wire(string a, string b)
        {
            var (pa, qa) = Split(a);
            var (pb, qb) = Split(b);
            Project.Wires.Add(new BenchWire(pa, qa, pb, qb));
        }

        private static (string, string) Split(string s)
        {
            int i = s.IndexOf('.');
            return (s[..i], s[(i + 1)..]);
        }
    }

    public static BenchProject MotorStartStop()
    {
        var b = new Builder("Marcha y paro de un motor con PLC",
            "Pulsa S1 (marcha) y el PLC cierra KM1: el motor M1 arranca con un pico de corriente que ves en el osciloscopio. " +
            "S2 (paro, NC) lo para. Prueba a bajar el ajuste del térmico de KM1 a 1 A en su ficha mientras simulas.");
        b.Add("pulsador", "S1", 40, 60);
        b.Add("pulsador", "S2", 40, 200, ("contacto", "NC"));
        b.Add("plc", "PLC1", 300, 40);
        b.Add("red", "RED", 640, 0);
        b.Add("contactor", "KM1", 640, 200, ("termico", "4.7"));
        b.Add("motor", "M1", 640, 440);
        b.Add("piloto", "H1", 980, 60);
        b.Add("osciloscopio", "OSC1", 980, 340);
        b.Add("analizador", "LA1", 40, 360);
        b.Add("multimetro", "MM1", 980, 190);
        b.Wire("S1.13-14", "PLC1.%IX0.0");
        b.Wire("S2.13-14", "PLC1.%IX0.1");
        b.Wire("PLC1.%QX0.0", "KM1.A1");
        b.Wire("PLC1.%QX0.1", "H1.X1");
        b.Wire("RED.L", "KM1.L");
        b.Wire("KM1.T", "M1.Bornes");
        b.Wire("OSC1.CH1", "M1.Bornes");
        b.Wire("OSC1.CH2", "M1.Eje");
        b.Wire("MM1.Punta", "M1.Bornes");
        b.Wire("LA1.D1", "S1.13-14");
        b.Wire("LA1.D2", "S2.13-14");
        b.Wire("LA1.D3", "PLC1.%QX0.0");
        b.Wire("LA1.D4", "KM1.13-14");
        return b.Project;
    }

    public static BenchProject ArduinoStartStop()
    {
        var b = new Builder("Marcha y paro con Arduino y placa de relés",
            "El Arduino lee la marcha (IN1) y el paro (IN2, NC) a 24 V y cierra el relé K1, que manda la bobina de KM1. " +
            "Cambia el programa en la ficha de ARD1 para probar otras maniobras.");
        b.Add("pulsador", "S1", 40, 60);
        b.Add("pulsador", "S2", 40, 200, ("contacto", "NC"));
        b.Add("arduino-io", "ARD1", 300, 40);
        b.Add("red", "RED", 640, 0);
        b.Add("contactor", "KM1", 640, 200, ("termico", "4.7"));
        b.Add("motor", "M1", 640, 440);
        b.Add("piloto", "H1", 980, 220);
        b.Add("analizador", "LA1", 980, 360);
        b.Wire("S1.13-14", "ARD1.IN1");
        b.Wire("S2.13-14", "ARD1.IN2");
        b.Wire("ARD1.K1", "KM1.A1");
        b.Wire("KM1.13-14", "H1.X1");
        b.Wire("RED.L", "KM1.L");
        b.Wire("KM1.T", "M1.Bornes");
        b.Wire("LA1.D1", "S1.13-14");
        b.Wire("LA1.D2", "S2.13-14");
        b.Wire("LA1.D3", "ARD1.K1");
        b.Wire("LA1.D4", "KM1.13-14");
        return b.Project;
    }

    public const string TankProgram = """
        PROGRAM Llenado
        VAR
            Nivel   AT %IW64  : INT;    (* convertidor TT1: 0-10 V = 0-3 m *)
            Termico AT %IX0.0 : BOOL;   (* 97-98 del térmico de KM1 *)
            Bomba   AT %QX0.0 : BOOL;   (* bobina de KM1 *)
            Alarma  AT %QX0.1 : BOOL;
            Porcentaje : REAL;
        END_VAR

        Porcentaje := INT_TO_REAL(Nivel) * 100.0 / 27648.0;
        IF Termico THEN
            Bomba := FALSE;
        ELSIF Porcentaje < 20.0 THEN
            Bomba := TRUE;
        ELSIF Porcentaje > 80.0 THEN
            Bomba := FALSE;
        END_IF;
        Alarma := Termico OR (Porcentaje > 95.0);
        END_PROGRAM
        """;

    public static BenchProject TankFill()
    {
        var b = new Builder("Llenado de tanque: PLC, motor y bomba",
            "El PLC arranca la bomba por debajo del 20 % de nivel y la para por encima del 80 %. Mueve el consumo con R1 " +
            "y mira en el osciloscopio el nivel y la corriente del motor.");
        b.Add("plc", "PLC1", 40, 40, ("programa", TankProgram));
        b.Add("red", "RED", 380, 20);
        b.Add("contactor", "KM1", 380, 150, ("termico", "5"));
        b.Add("motor", "M1", 380, 380, ("inerciaCarga", "0.01"));
        b.Add("h-deposito", "CIS", 420, 620, ("nivel", "1.5"));
        b.Add("h-tuberia", "TB1", 600, 560, ("longitud", "4"), ("diametro", "0.065"));
        b.Add("h-bomba", "P1", 640, 400);
        b.Add("h-tuberia", "TB2", 860, 400, ("longitud", "18"), ("diametro", "0.065"), ("perdidasLocales", "3"));
        b.Add("h-tanque", "TK1", 1080, 380, ("cota", "4"), ("diametro", "1.2"), ("alturaMax", "3"), ("nivelInicial", "0.9"));
        b.Add("h-tuberia", "TB3", 1080, 580, ("longitud", "12"), ("diametro", "0.05"));
        b.Add("h-valvula-control", "FV1", 1280, 580, ("diametro", "0.04"), ("tiempoRecorrido", "3"));
        b.Add("h-deposito", "DES", 1480, 620, ("nivel", "0"));
        b.Add("h-sensor-nivel", "LT1", 1300, 380);
        b.Add("transmisor", "TT1", 1300, 220);
        b.Add("consigna", "R1", 1060, 720, ("unidad", "%"), ("min", "0"), ("max", "100"), ("inicial", "20"));
        b.Add("osciloscopio", "OSC1", 40, 400, ("ventana", "120"), ("muestreo", "20"));
        b.Wire("PLC1.%QX0.0", "KM1.A1");
        b.Wire("KM1.97-98", "PLC1.%IX0.0");
        b.Wire("RED.L", "KM1.L");
        b.Wire("KM1.T", "M1.Bornes");
        b.Wire("M1.Eje", "P1.Eje");
        b.Wire("CIS.Boca", "TB1.Entrada");
        b.Wire("TB1.Salida", "P1.Entrada");
        b.Wire("P1.Salida", "TB2.Entrada");
        b.Wire("TB2.Salida", "TK1.Boca");
        b.Wire("TK1.Boca", "TB3.Entrada");
        b.Wire("TB3.Salida", "FV1.Entrada");
        b.Wire("FV1.Salida", "DES.Boca");
        b.Wire("LT1.Toma", "TK1.Boca");
        b.Wire("LT1.Medida", "TT1.Entrada");
        b.Wire("TT1.Salida", "PLC1.%IW64");
        b.Wire("R1.Salida", "FV1.Consigna");
        b.Wire("OSC1.CH1", "LT1.Medida");
        b.Wire("OSC1.CH2", "M1.Bornes");
        b.Wire("OSC1.CH3", "P1.Salida");
        return b.Project;
    }

    public static BenchProject TurboGenerator()
    {
        var b = new Builder("Turbogenerador en isla con regulador PI",
            "El regulador PIC1 mide la velocidad del generador y abre la admisión de la turbina para mantener 3000 rpm (50 Hz). " +
            "Sube o baja la carga C2 mientras simulas y mira cómo cae y se recupera la frecuencia.");
        b.Add("pi", "PIC1", 40, 40, ("unidad", "rpm"), ("consigna", "3000"), ("kp", "2500"), ("ti", "3"));
        b.Add("turbina", "T1", 320, 40, ("potencia", "550"), ("inercia", "9"), ("inerciaCarga", "5"), ("actuador", "0.3"));
        b.Add("generador", "G1", 620, 40, ("kva", "600"));
        b.Add("barras", "B1", 620, 280);
        b.Add("carga", "C1", 520, 480, ("potencia", "200"));
        b.Add("carga", "C2", 760, 480, ("potencia", "150"));
        b.Add("osciloscopio", "OSC1", 940, 40, ("ventana", "60"), ("muestreo", "10"));
        b.Add("multimetro", "MM1", 940, 300);
        b.Wire("G1.Velocidad", "PIC1.Medida");
        b.Wire("PIC1.Salida", "T1.Admision");
        b.Wire("T1.Eje", "G1.Eje");
        b.Wire("G1.Bornes", "B1.Entrada");
        b.Wire("B1.S1", "C1.Bornes");
        b.Wire("B1.S2", "C2.Bornes");
        b.Wire("OSC1.CH1", "T1.Eje");
        b.Wire("OSC1.CH2", "G1.Bornes");
        b.Wire("MM1.Punta", "B1.Entrada");
        return b.Project;
    }
}
