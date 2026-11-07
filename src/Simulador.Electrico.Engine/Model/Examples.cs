namespace Simulador.Electrico.Engine.Model;

/// <summary>Esquemas de ejemplo listos para simular. También sirven de pruebas del motor.</summary>
public static class Examples
{
    public static IReadOnlyList<(string Name, Func<Schematic> Create)> All { get; } =
    [
        ("Arranque directo con autorretención", DirectOnLine),
        ("Inversión de giro con enclavamiento", Reversing),
        ("Arranque estrella-triángulo", StarDelta),
        ("Luz conmutada desde tres puntos", ThreeWayLighting),
        ("Telerruptor y minutero de escalera", ImpulseAndStairTimer),
        ("Bomba con flotador y selector Manual-0-Automático", FloatPump),
        ("Cintas transportadoras en cascada", CascadeConveyors),
        ("Puerta de garaje con finales de carrera", GarageDoor),
        ("Cuadro de vivienda con diferencial y fuga a tierra", HouseholdPanel),
        ("Bomba monofásica con transformador de mando 230/24 V", SinglePhasePump),
        ("Variador de frecuencia: rampa y dos sentidos de giro", VfdDrive),
        ("Marcha-paro de motor con PLC", PlcStartStop),
        ("Marcha-paro de motor con Arduino y placa de relés", McuStartStop),
        ("Depósito con sensor de nivel 0-10 V y Arduino", McuLevelControl),
    ];

    /// <summary>Sketch del Arduino Uno del ejemplo <see cref="McuStartStop"/>.</summary>
    public const string McuStartStopSketch = """
        // Marcha-paro con autorretención en un Arduino Uno con placa de relés.
        // IN1 (D2) = S1 marcha, IN3 (D4) = S2 paro (NC), IN5 (D6) = Q1 cerrado
        // K1 (D10) = bobina de KM1, K3 (D12) = piloto H1
        const int MARCHA = 2;
        const int PARO = 4;
        const int GUARDAMOTOR = 6;
        const int CONTACTOR = 10;
        const int PILOTO = 12;

        bool motor = false;

        void setup() {
          pinMode(MARCHA, INPUT);
          pinMode(PARO, INPUT);
          pinMode(GUARDAMOTOR, INPUT);
          pinMode(CONTACTOR, OUTPUT);
          pinMode(PILOTO, OUTPUT);
          Serial.begin(9600);
        }

        void loop() {
          bool permiso = digitalRead(PARO) == HIGH && digitalRead(GUARDAMOTOR) == HIGH;
          if (digitalRead(MARCHA) == HIGH && permiso && !motor) {
            motor = true;
            Serial.println("Motor en marcha");
          }
          if (!permiso && motor) {
            motor = false;
            Serial.println("Motor parado");
          }
          digitalWrite(CONTACTOR, motor ? HIGH : LOW);
          digitalWrite(PILOTO, motor ? HIGH : LOW);
          delay(10);
        }
        """;

    /// <summary>Sketch del ejemplo <see cref="McuLevelControl"/>: bomba con histéresis según el nivel (0-10 V en AI1).</summary>
    public const string McuLevelSketch = """
        // Control de nivel de un depósito con un Arduino Uno y su placa de E/S.
        // AI1 (A4): transmisor de nivel 0-10 V -> analogRead 0-1023
        // K1 (D10): bomba de llenado (aquí, su piloto H1)
        const int NIVEL = A4;
        const int BOMBA = 10;
        const float ARRANCA = 30;   // % : por debajo se llena
        const float PARA = 80;      // % : por encima se para

        bool bomba = false;

        void setup() {
          pinMode(BOMBA, OUTPUT);
          Serial.begin(9600);
        }

        void loop() {
          int lectura = analogRead(NIVEL);
          float nivel = lectura * 100.0 / 1023;
          if (nivel < ARRANCA && !bomba) {
            bomba = true;
            Serial.print("Nivel bajo (");
            Serial.print(nivel, 0);
            Serial.println(" %): bomba en marcha");
          }
          if (nivel > PARA && bomba) {
            bomba = false;
            Serial.print("Depósito lleno (");
            Serial.print(nivel, 0);
            Serial.println(" %): bomba parada");
          }
          digitalWrite(BOMBA, bomba ? HIGH : LOW);
          delay(100);
        }
        """;

    /// <summary>Programa del PLC del ejemplo <see cref="PlcStartStop"/> (texto estructurado IEC 61131-3).</summary>
    public const string PlcStartStopProgram = """
        PROGRAM MarchaParo
        VAR
            Marcha AT %IX0.0 : BOOL;   (* S1, pulsador NA *)
            Paro   AT %IX0.2 : BOOL;   (* S2, pulsador NC: en reposo la entrada está a 1 *)
            Q1_ok  AT %IX0.4 : BOOL;   (* Q1 13-14: guardamotor cerrado *)
            KM1    AT %QX0.0 : BOOL;   (* bobina del contactor *)
            H1     AT %QX0.2 : BOOL;   (* piloto de marcha *)
        END_VAR

        KM1 := (Marcha OR KM1) AND Paro AND Q1_ok;
        H1 := KM1;
        END_PROGRAM
        """;

    /// <summary>Potencia común. Devuelve también el borne XN del que sale el neutro del mando.</summary>
    private static (PlacedElement xn, PlacedElement q1, PlacedElement fuse) PowerHead(SchematicBuilder b, int fuseX)
    {
        var red = b.Add("red_3f", "XS1", 0, 0);
        var xn = b.Add("borne", "XN", fuseX - 2, 3);
        b.Wire(red, "N", xn, "1");
        var q1 = b.Add("guardamotor", "Q1", 0, 5, ("Ir", 8.5));
        b.Wire(red, "L1", q1, "1").Wire(red, "L2", q1, "3").Wire(red, "L3", q1, "5");
        var f3 = b.Add("fusible", "F3", fuseX, 5, ("In", 2), ("tipo", 0));
        b.Wire(red, "L1", f3, "1"); // el mando se toma de L1 antes del guardamotor, para poder señalizar su disparo
        return (xn, q1, f3);
    }

    private static PlacedElement Aux(SchematicBuilder b, string type, string tag, int x, int y, string terminals)
    {
        var e = b.Add(type, tag, x, y);
        b.Text(e, "Bornes", terminals);
        return e;
    }

    public static Schematic DirectOnLine()
    {
        var b = new SchematicBuilder("Arranque directo con autorretención",
            "Pulsa S2 (marcha) y el contactor KM1 se queda enclavado por su contacto 13-14. S1 para y S0 es la seta de emergencia. " +
            "Sube la carga del motor por encima del 120 % y el guardamotor Q1 disparará al cabo de un rato.");
        var (xn, q1, f3) = PowerHead(b, 16);
        var km = b.Add("contactos_principales", "KM1", 0, 12, ("In", 9));
        var m = b.Add("motor_3f", "M1", 0, 20);
        b.Wire(q1, "2", km, "1").Wire(q1, "4", km, "3").Wire(q1, "6", km, "5");
        b.Wire(km, "2", m, "U").Wire(km, "4", m, "V").Wire(km, "6", m, "W");

        var s0 = b.Add("seta_emergencia", "S0", 16, 10);
        var s1 = b.Add("pulsador_nc", "S1", 16, 15);
        var s2 = b.Add("pulsador_na", "S2", 16, 20);
        var ka = Aux(b, "contacto_na", "KM1", 20, 20, "13-14");
        var coil = b.Add("bobina_contactor", "KM1", 16, 26);
        var h1 = b.Add("piloto", "H1", 22, 26, ("color", 1));
        var h2 = b.Add("piloto", "H2", 26, 26, ("color", 0));
        var qnc = Aux(b, "contacto_nc", "Q1", 26, 20, "21-22");
        b.Chain(f3, s0, s1, s2, coil);
        b.Wire(s1, "2", ka, "1").Wire(ka, "2", coil, "A1");
        b.Wire(coil, "A1", h1, "1").Wire(h1, "2", coil, "A2").Wire(coil, "A2", xn, "1");
        // H2 (rojo): avisa de que el guardamotor está abierto o disparado.
        b.Wire(f3, "2", qnc, "1").Wire(qnc, "2", h2, "1").Wire(h2, "2", coil, "A2");
        return b.Schematic;
    }

    public static Schematic Reversing() => BuildReversing(limits: false);

    public static Schematic GarageDoor() => BuildReversing(limits: true);

    private static Schematic BuildReversing(bool limits)
    {
        var b = limits
            ? new SchematicBuilder("Puerta de garaje con finales de carrera",
                "S2 sube la puerta (KM1) y S3 la baja (KM2). Cuando la puerta llega arriba acciona SQ1 y, abajo, SQ2: haz clic en el " +
                "final de carrera para simularlo y verás que el motor se para solo. H1 y H2 avisan de que la puerta se mueve.")
            : new SchematicBuilder("Inversión de giro con enclavamiento",
                "S2 gira a derechas (KM1) y S3 a izquierdas (KM2), que cruza L1 y L3. Cada contactor bloquea al otro con su contacto NC 21-22. " +
                "Para cambiar de sentido hay que parar antes con S1.");
        var coilY = limits ? 35 : 30;
        var (xn, q1, f3) = PowerHead(b, 22);
        var km1 = b.Add("contactos_principales", "KM1", 0, 12);
        var km2 = b.Add("contactos_principales", "KM2", 8, 12);
        var f2 = b.Add("rele_termico", "F2", 0, 19, ("Ir", 8.5));
        var m = b.Add("motor_3f", "M1", 0, 26);
        b.Wire(q1, "2", km1, "1").Wire(q1, "4", km1, "3").Wire(q1, "6", km1, "5");
        b.Wire(q1, "2", km2, "1").Wire(q1, "4", km2, "3").Wire(q1, "6", km2, "5");
        b.Wire(km1, "2", f2, "1").Wire(km1, "4", f2, "3").Wire(km1, "6", f2, "5");
        // KM2 cruza las fases: L1 → borne 5 del térmico, L3 → borne 1.
        b.Wire(km2, "2", f2, "5").Wire(km2, "4", f2, "3").Wire(km2, "6", f2, "1");
        b.Wire(f2, "2", m, "U").Wire(f2, "4", m, "V").Wire(f2, "6", m, "W");

        var f2c = Aux(b, "contacto_nc", "F2", 22, 10, "95-96");
        var s1 = b.Add("pulsador_nc", "S1", 22, 15);
        var s2 = b.Add("pulsador_na", "S2", 22, 20);
        var k1h = Aux(b, "contacto_na", "KM1", 26, 20, "13-14");
        var k2i = Aux(b, "contacto_nc", "KM2", 22, 25, "21-22");
        var c1 = b.Add("bobina_contactor", "KM1", 22, coilY);
        var s3 = b.Add("pulsador_na", "S3", 32, 20);
        var k2h = Aux(b, "contacto_na", "KM2", 36, 20, "13-14");
        var k1i = Aux(b, "contacto_nc", "KM1", 32, 25, "21-22");
        var c2 = b.Add("bobina_contactor", "KM2", 32, coilY);
        b.Chain(f3, f2c, s1, s2, k2i);
        b.Wire(s1, "2", k1h, "1").Wire(k1h, "2", k2i, "1");
        b.Wire(s1, "2", s3, "1");
        b.Chain(s3, k1i);
        b.Wire(s3, "1", k2h, "1").Wire(k2h, "2", k1i, "1");
        if (limits)
        {
            var sq1 = b.Text(b.Add("final_carrera_nc", "SQ1", 22, 30), "Rótulo", "Arriba");
            var sq2 = b.Text(b.Add("final_carrera_nc", "SQ2", 32, 30), "Rótulo", "Abajo");
            b.Chain(k2i, sq1, c1).Chain(k1i, sq2, c2);
            var h1 = b.Add("piloto", "H1", 27, coilY, ("color", 2));
            var h2 = b.Add("piloto", "H2", 37, coilY, ("color", 2));
            b.Wire(c1, "A1", h1, "1").Wire(h1, "2", c1, "A2");
            b.Wire(c2, "A1", h2, "1").Wire(h2, "2", c2, "A2");
            m.Params["carga"] = 60;
        }
        else
        {
            b.Chain(k2i, c1).Chain(k1i, c2);
        }
        b.Wire(c1, "A2", c2, "A2").Wire(c1, "A2", xn, "1");
        return b.Schematic;
    }

    public static Schematic StarDelta()
    {
        var b = new SchematicBuilder("Arranque estrella-triángulo",
            "S2 conecta KM1 (línea) y KM2 (estrella). Pasados los segundos de KT1, sale KM2 y entra KM3 (triángulo). " +
            "Fíjate en la corriente de arranque: es un tercio de la de un arranque directo.");
        var (xn, q1, f3) = PowerHead(b, 26);
        q1.Params["Ir"] = 16;
        var km1 = b.Add("contactos_principales", "KM1", 0, 12, ("In", 18));
        var f2 = b.Add("rele_termico", "F2", 0, 19, ("Ir", 15));
        var m = b.Add("motor_3f_6b", "M1", 0, 26, ("Uw", 400), ("Pn", 7.5), ("rpm", 1450), ("kI", 7), ("J", 0.4), ("tipoCarga", 1), ("carga", 90));
        var km3 = b.Add("contactos_principales", "KM3", 10, 26, ("In", 12));
        var km2 = b.Add("contactos_principales", "KM2", 18, 26, ("In", 12));
        b.Wire(q1, "2", km1, "1").Wire(q1, "4", km1, "3").Wire(q1, "6", km1, "5");
        b.Wire(km1, "2", f2, "1").Wire(km1, "4", f2, "3").Wire(km1, "6", f2, "5");
        b.Wire(f2, "2", m, "U1").Wire(f2, "4", m, "V1").Wire(f2, "6", m, "W1");
        // Triángulo: U1-W2, V1-U2, W1-V2.
        b.Wire(f2, "2", km3, "1").Wire(f2, "4", km3, "3").Wire(f2, "6", km3, "5");
        b.Wire(km3, "2", m, "W2").Wire(km3, "4", m, "U2").Wire(km3, "6", m, "V2");
        // Estrella: une U2, V2 y W2.
        b.Wire(m, "U2", km2, "1").Wire(m, "V2", km2, "3").Wire(m, "W2", km2, "5");
        b.Wire(km2, "2", km2, "4").Wire(km2, "4", km2, "6");

        var f2c = Aux(b, "contacto_nc", "F2", 26, 10, "95-96");
        var s1 = b.Add("pulsador_nc", "S1", 26, 15);
        var s2 = b.Add("pulsador_na", "S2", 26, 20);
        var k1h = Aux(b, "contacto_na", "KM1", 30, 20, "13-14");
        var c1 = b.Add("bobina_contactor", "KM1", 26, 30);
        var kt = b.Add("bobina_ton", "KT1", 34, 30, ("t", 4));
        var ktNc = Aux(b, "contacto_temp_nc", "KT1", 40, 20, "15-16");
        var k3i = Aux(b, "contacto_nc", "KM3", 40, 25, "21-22");
        var c2 = b.Add("bobina_contactor", "KM2", 40, 30);
        var ktNa = Aux(b, "contacto_temp_na", "KT1", 46, 20, "17-18");
        var k2i = Aux(b, "contacto_nc", "KM2", 46, 25, "21-22");
        var c3 = b.Add("bobina_contactor", "KM3", 46, 30);
        b.Chain(f3, f2c, s1, s2, c1);
        b.Wire(s1, "2", k1h, "1").Wire(k1h, "2", c1, "A1");
        b.Wire(c1, "A1", kt, "A1");
        b.Wire(c1, "A1", ktNc, "1");
        b.Chain(ktNc, k3i, c2);
        b.Wire(c1, "A1", ktNa, "1");
        b.Chain(ktNa, k2i, c3);
        b.Wire(c1, "A2", kt, "A2").Wire(c1, "A2", c2, "A2").Wire(c1, "A2", c3, "A2").Wire(c1, "A2", xn, "1");
        return b.Schematic;
    }

    public static Schematic ThreeWayLighting()
    {
        var b = new SchematicBuilder("Luz conmutada desde tres puntos",
            "Dos conmutadores en los extremos y un cruzamiento en medio: cualquiera de los tres cambia el estado de la luz. " +
            "Con Mayús+clic en el diferencial pruebas su botón de test.");
        var src = b.Add("fuente_ac1", "XS1", 0, 0);
        var q1 = b.Add("magnetotermico_2p", "Q1", 0, 4, ("In", 10), ("curva", 1));
        var q2 = b.Add("diferencial_2p", "Q2", 0, 10, ("Idn", 30));
        b.Wire(src, "L", q1, "1").Wire(src, "N", q1, "3");
        b.Wire(q1, "2", q2, "1").Wire(q1, "4", q2, "3");
        // Conmutadores y cruzamiento en horizontal: los dos hilos de puente (viajeros) van en paralelo.
        var s1 = b.Add("conmutador", "S1", 12, 20);
        s1.Rotation = 1; // C a la izquierda, 1 y 2 a la derecha
        var s2 = b.Add("cruzamiento", "S2", 20, 20);
        s2.Rotation = 1;
        var s3 = b.Add("conmutador", "S3", 24, 22);
        s3.Rotation = 3; // 1 y 2 a la izquierda, C a la derecha
        var e1 = b.Add("lampara", "E1", 32, 22);
        b.Wire(q2, "2", s1, "C");
        b.Wire(s1, "1", s2, "3").Wire(s1, "2", s2, "4");
        b.Wire(s2, "1", s3, "2").Wire(s2, "2", s3, "1");
        b.Wire(s3, "C", e1, "1").Wire(e1, "2", q2, "4");
        return b.Schematic;
    }

    public static Schematic ImpulseAndStairTimer()
    {
        var b = new SchematicBuilder("Telerruptor y minutero de escalera",
            "Izquierda: cada pulsador (S1, S2, S3) enciende o apaga E1 a través del telerruptor KL1. " +
            "Derecha: S4 enciende E2 y el minutero KT1 la apaga sola a los 10 s de soltarlo.");
        var src = b.Add("fuente_ac1", "XS1", 0, 0);
        var q1 = b.Add("magnetotermico_2p", "Q1", 0, 4, ("In", 10));
        b.Wire(src, "L", q1, "1").Wire(src, "N", q1, "3");
        var s1 = b.Add("pulsador_na", "S1", 8, 10);
        var s2 = b.Add("pulsador_na", "S2", 12, 10);
        var s3 = b.Add("pulsador_na", "S3", 16, 10);
        var kl = b.Add("bobina_telerruptor", "KL1", 8, 16);
        var klc = Aux(b, "contacto_na", "KL1", 22, 10, "1-2");
        var e1 = b.Add("lampara", "E1", 22, 16);
        foreach (var s in new[] { s1, s2, s3 }) b.Wire(q1, "2", s, "1").Wire(s, "2", kl, "A1");
        b.Wire(q1, "2", klc, "1").Wire(klc, "2", e1, "1");
        b.Wire(kl, "A2", q1, "4").Wire(e1, "2", q1, "4");

        var s4 = b.Add("pulsador_na", "S4", 30, 10);
        var kt = b.Add("bobina_tof", "KT1", 30, 16, ("t", 10));
        var ktc = Aux(b, "contacto_temp_na", "KT1", 36, 10, "17-18");
        var e2 = b.Add("lampara", "E2", 36, 16);
        b.Wire(q1, "2", s4, "1").Wire(s4, "2", kt, "A1").Wire(kt, "A2", q1, "4");
        b.Wire(q1, "2", ktc, "1").Wire(ktc, "2", e2, "1").Wire(e2, "2", q1, "4");
        return b.Schematic;
    }

    public static Schematic FloatPump()
    {
        var b = new SchematicBuilder("Bomba con flotador y selector Manual-0-Automático",
            "Haz clic en SA1 para elegir posición (0, I manual, II automático). En manual, S2 arranca la bomba con autorretención y S1 la para. " +
            "En automático la manda el flotador B1: clic en B1 para simular que sube el nivel (arranca) y otro clic cuando baja (para).");
        var (xn, q1, f3) = PowerHead(b, 16);
        q1.Params["Ir"] = 3.5;
        var km = b.Add("contactos_principales", "KM1", 0, 12, ("In", 9));
        var m = b.Add("motor_3f", "M1", 0, 20, ("Pn", 1.5), ("rpm", 1440), ("cos", 0.8), ("eta", 0.85), ("tipoCarga", 1), ("carga", 90));
        b.Wire(q1, "2", km, "1").Wire(q1, "4", km, "3").Wire(q1, "6", km, "5");
        b.Wire(km, "2", m, "U").Wire(km, "4", m, "V").Wire(km, "6", m, "W");

        var s0 = b.Add("seta_emergencia", "S0", 16, 10);
        var s1 = b.Add("pulsador_nc", "S1", 16, 15);
        var sa = b.Add("selector_3p", "SA1", 16, 20);
        var s2 = b.Add("pulsador_na", "S2", 16, 26);
        var hold = Aux(b, "contacto_na", "KM1", 10, 26, "13-14");
        var b1 = b.Text(b.Add("sensor_na", "B1", 22, 26), "Rótulo", "Flotador");
        var coil = b.Add("bobina_contactor", "KM1", 16, 33);
        var h1 = b.Add("piloto", "H1", 26, 33, ("color", 1));
        b.Chain(f3, s0, s1);
        b.Wire(s1, "2", sa, "C");
        b.Wire(sa, "1", s2, "1").Wire(sa, "1", hold, "1");
        b.Wire(s2, "2", coil, "A1").Wire(hold, "2", coil, "A1");
        b.Wire(sa, "2", b1, "1").Wire(b1, "2", coil, "A1");
        b.Wire(coil, "A1", h1, "1").Wire(h1, "2", coil, "A2").Wire(coil, "A2", xn, "1");
        return b.Schematic;
    }

    public static Schematic CascadeConveyors()
    {
        var b = new SchematicBuilder("Cintas transportadoras en cascada",
            "S2 arranca la cinta 1 (KM1) y, pasados los segundos de KT1, la cinta 2 (KM2), para que el material no se amontone. " +
            "S1 las para las dos. Si salta el térmico de cualquiera de ellas (F1 o F2), se para todo.");
        var red = b.Add("red_3f", "XS1", 0, 0);
        var xn = b.Add("borne", "XN", 22, 3);
        b.Wire(red, "N", xn, "1");
        var q1 = b.Add("magnetotermico_3p", "Q1", 0, 5, ("In", 10), ("curva", 1));
        b.Wire(red, "L1", q1, "1").Wire(red, "L2", q1, "3").Wire(red, "L3", q1, "5");
        var f3 = b.Add("fusible", "F3", 24, 5, ("In", 2));
        b.Wire(red, "L1", f3, "1");
        var parts = new List<(PlacedElement km, PlacedElement f, PlacedElement m)>();
        for (var i = 0; i < 2; i++)
        {
            var x = 10 * i;
            var km = b.Add("contactos_principales", $"KM{i + 1}", x, 12, ("In", 9));
            var f = b.Add("rele_termico", $"F{i + 1}", x, 19, ("Ir", 3.5));
            var m = b.Add("motor_3f", $"M{i + 1}", x, 26, ("Pn", 1.5), ("rpm", 1440), ("cos", 0.8), ("eta", 0.85), ("carga", 70));
            b.Wire(q1, "2", km, "1").Wire(q1, "4", km, "3").Wire(q1, "6", km, "5");
            b.Wire(km, "2", f, "1").Wire(km, "4", f, "3").Wire(km, "6", f, "5");
            b.Wire(f, "2", m, "U").Wire(f, "4", m, "V").Wire(f, "6", m, "W");
            parts.Add((km, f, m));
        }

        var f1c = Aux(b, "contacto_nc", "F1", 24, 10, "95-96");
        var f2c = Aux(b, "contacto_nc", "F2", 24, 15, "95-96");
        var s1 = b.Add("pulsador_nc", "S1", 24, 20);
        var s2 = b.Add("pulsador_na", "S2", 24, 25);
        var hold = Aux(b, "contacto_na", "KM1", 28, 25, "13-14");
        var c1 = b.Add("bobina_contactor", "KM1", 24, 32);
        var kt = b.Add("bobina_ton", "KT1", 32, 32, ("t", 5));
        var ktc = Aux(b, "contacto_temp_na", "KT1", 38, 25, "17-18");
        var c2 = b.Add("bobina_contactor", "KM2", 38, 32);
        b.Chain(f3, f1c, f2c, s1, s2, c1);
        b.Wire(s1, "2", hold, "1").Wire(hold, "2", c1, "A1");
        b.Wire(c1, "A1", kt, "A1").Wire(c1, "A1", ktc, "1").Wire(ktc, "2", c2, "A1");
        b.Wire(c1, "A2", kt, "A2").Wire(kt, "A2", c2, "A2").Wire(c1, "A2", xn, "1");
        return b.Schematic;
    }

    public static Schematic HouseholdPanel()
    {
        var b = new SchematicBuilder("Cuadro de vivienda con diferencial y fuga a tierra",
            "Interruptor general Q0, diferencial Q1 de 30 mA y dos circuitos: alumbrado (Q2, 10 A) y tomas de corriente (Q3, 16 A) con un " +
            "calefactor de 2 kW. Cierra S3 para provocar una fuga a tierra de unos 115 mA a través de RF: el diferencial dispara. " +
            "Mayús+clic en Q1 prueba su botón de test.");
        var src = b.Add("fuente_ac1", "XS1", 0, 0);
        var q0 = b.Add("magnetotermico_2p", "Q0", 0, 4, ("In", 25), ("curva", 1));
        var q1 = b.Add("diferencial_2p", "Q1", 0, 10, ("In", 40), ("Idn", 30));
        var q2 = b.Add("magnetotermico_2p", "Q2", 8, 18, ("In", 10), ("curva", 1));
        var q3 = b.Add("magnetotermico_2p", "Q3", 20, 18, ("In", 16), ("curva", 1));
        b.Wire(src, "L", q0, "1").Wire(src, "N", q0, "3");
        b.Wire(q0, "2", q1, "1").Wire(q0, "4", q1, "3");
        // Embarrado de reparto: fase y neutro por alturas distintas para que no se confundan.
        var xl = b.Add("borne", "XL", 0, 16);
        var xnb = b.Add("borne", "XN", 2, 15);
        b.Wire(q1, "2", xl, "1").Wire(q1, "4", xnb, "1");
        foreach (var q in new[] { q2, q3 }) b.Wire(xl, "1", q, "1").Wire(xnb, "1", q, "3");

        var s1 = b.Add("interruptor", "S1", 6, 25);
        var e1 = b.Add("lampara", "E1", 6, 31, ("P", 60));
        b.Wire(q2, "2", s1, "1").Wire(s1, "2", e1, "1").Wire(e1, "2", q2, "4");

        var s2 = b.Add("interruptor", "S2", 18, 25);
        var r1 = b.Add("resistencia", "R1", 18, 31, ("R", 230.0 * 230 / 2000));
        b.Wire(q3, "2", s2, "1").Wire(s2, "2", r1, "1").Wire(r1, "2", q3, "4");

        // Defecto de aislamiento: la corriente vuelve por tierra (XT) al neutro de origen sin pasar por el diferencial.
        var s3 = b.Text(b.Add("interruptor", "S3", 28, 25), "Referencia", "Provoca defecto");
        var rf = b.Add("resistencia", "RF", 28, 31, ("R", 2000));
        var xt = b.Add("borne", "XT", 32, 9);
        b.Wire(q3, "2", s3, "1").Wire(s3, "2", rf, "1").Wire(rf, "2", xt, "1").Wire(xt, "1", q0, "4");
        return b.Schematic;
    }

    public static Schematic SinglePhasePump()
    {
        var b = new SchematicBuilder("Bomba monofásica con transformador de mando 230/24 V",
            "La bomba M1 es un motor monofásico de 230 V entre fase y neutro. El mando trabaja a 24 V AC desde el transformador T1, " +
            "más seguro para los pulsadores: S2 arranca con autorretención por KM1 13-14 y S1 para. Q2 protege el primario y F3 el " +
            "secundario, que va a tierra por X0 como pide la norma de máquinas.");
        var red = b.Add("red_3f", "XS1", 0, 0);
        var q1 = b.Add("magnetotermico_2p", "Q1", 0, 5, ("In", 10), ("curva", 1));
        b.Wire(red, "L1", q1, "1").Wire(red, "N", q1, "3");
        var km = b.Add("contactos_principales", "KM1", 0, 13, ("In", 9));
        b.Wire(q1, "2", km, "1").Wire(q1, "4", km, "3");
        var m = b.Add("motor_1f", "M1", 0, 21, ("Pn", 0.75));
        b.Wire(km, "2", m, "U1").Wire(km, "4", m, "U2");

        var q2 = b.Add("magnetotermico_2p", "Q2", 12, 5, ("In", 2), ("curva", 1));
        b.Wire(red, "L1", q2, "1").Wire(red, "N", q2, "3");
        var t1 = b.Add("trafo_1f", "T1", 12, 11, ("S", 63), ("V1", 230), ("V2", 24), ("ucc", 8));
        b.Wire(q2, "2", t1, "1U1").Wire(q2, "4", t1, "1U2");
        var f3 = b.Add("fusible", "F3", 12, 19, ("In", 2), ("tipo", 0));
        b.Wire(t1, "2U1", f3, "1");
        var s1 = b.Add("pulsador_nc", "S1", 12, 25);
        var s2 = b.Add("pulsador_na", "S2", 12, 31);
        var hold = Aux(b, "contacto_na", "KM1", 17, 31, "13-14");
        var coil = b.Add("bobina_contactor", "KM1", 12, 37, ("V", 24), ("S", 4));
        var h1 = b.Add("piloto", "H1", 20, 37, ("V", 24), ("P", 1), ("color", 1));
        b.Chain(f3, s1, s2, coil);
        b.Wire(s1, "2", hold, "1").Wire(hold, "2", coil, "A1");
        b.Wire(coil, "A1", h1, "1").Wire(h1, "2", coil, "A2");
        var x0 = b.Add("borne", "X0", 23, 17);
        b.Wire(t1, "2U2", x0, "1").Wire(x0, "1", h1, "2").Wire(x0, "1", red, "PE");
        return b.Schematic;
    }

    public static Schematic VfdDrive()
    {
        var b = new SchematicBuilder("Variador de frecuencia: rampa y dos sentidos de giro",
            "Gira el selector SA1: en I une STF con SD y el variador U1 acelera el motor con una rampa de 5 s hasta la consigna; " +
            "en II gira a izquierdas (primero frena hasta 0 Hz). En 0 decelera y para. Fíjate en que la corriente de arranque es mucho " +
            "menor que en el arranque directo. Cambia la consigna en Propiedades mientras simulas.");
        var red = b.Add("red_3f", "XS1", 0, 0);
        var q1 = b.Add("magnetotermico_3p", "Q1", 0, 5, ("In", 16), ("curva", 1));
        b.Wire(red, "L1", q1, "1").Wire(red, "L2", q1, "3").Wire(red, "L3", q1, "5");
        var u1 = b.Add("variador", "U1", 0, 13, ("Pn", 4), ("In", 9), ("fref", 50), ("tacc", 5), ("tdec", 5));
        b.Wire(q1, "2", u1, "L1").Wire(q1, "4", u1, "L2").Wire(q1, "6", u1, "L3");
        var m = b.Add("motor_3f", "M1", 0, 26);
        b.Wire(u1, "U", m, "U").Wire(u1, "V", m, "V").Wire(u1, "W", m, "W");
        // Selector girado 90°: C a la derecha, posiciones I y II a la izquierda, frente a STF y STR.
        var sa = b.Add("selector_3p", "SA1", 14, 15);
        sa.Rotation = 1;
        b.Wire(u1, "SD", sa, "C").Wire(sa, "1", u1, "STF").Wire(sa, "2", u1, "STR");
        return b.Schematic;
    }

    public static Schematic PlcStartStop() => ControllerStartStop(mcu: false);

    /// <summary>El mismo marcha-paro, con un Arduino Uno sobre una placa de 8 entradas de 24 V y 8 relés.</summary>
    public static Schematic McuStartStop() => ControllerStartStop(mcu: true);

    private static Schematic ControllerStartStop(bool mcu)
    {
        var b = mcu
            ? new SchematicBuilder("Marcha-paro de motor con Arduino y placa de relés",
                "Un Arduino Uno montado en una placa de E/S industrial (KF1) hace de autómata: lee los pulsadores con sus entradas " +
                "optoacopladas de 24 V (IN1, IN3, IN5) y su relé K1 alimenta la bobina de KM1. El sketch está en Propiedades de KF1 y " +
                "se puede cambiar por MicroPython eligiendo ESP32 o Pico. Pulsa S1 para arrancar y S2 para parar; lo que imprime " +
                "Serial.println aparece en el registro.")
            : new SchematicBuilder("Marcha-paro de motor con PLC",
            "El autómata KF1 lee los pulsadores en sus entradas de 24 V DC y su salida a relé Q0.0 alimenta la bobina de KM1. " +
            "El programa (en Propiedades del PLC) hace la autorretención: pulsa S1 para arrancar y S2 para parar. Los LED verdes del PLC " +
            "son las entradas y los ámbar las salidas. Si abres Q1, el PLC lo ve en I0.4 y para el motor.");
        var (xn, q1, f3) = PowerHead(b, 22);
        var km = b.Add("contactos_principales", "KM1", 0, 12, ("In", 9));
        var m = b.Add("motor_3f", "M1", 0, 20);
        b.Wire(q1, "2", km, "1").Wire(q1, "4", km, "3").Wire(q1, "6", km, "5");
        b.Wire(km, "2", m, "U").Wire(km, "4", m, "V").Wire(km, "6", m, "W");

        var g1 = b.Add("fuente_dc", "G1", 44, 0, ("V", 24));
        var xp = b.Add("borne", "X+", 44, 5);
        b.Wire(g1, "+", xp, "1");
        var plc = b.Add(mcu ? Catalog.ElementCatalog.McuTypeId : "plc_compacto", "KF1", 28, 18);
        if (mcu) b.Text(plc, "codigo", McuStartStopSketch);
        else b.Text(plc, "programa", PlcStartStopProgram);
        b.Wire(xp, "1", plc, "L+").Wire(g1, "-", plc, "M");
        var s1 = b.Add("pulsador_na", "S1", 32, 8);
        var s2 = b.Add("pulsador_nc", "S2", 36, 8);
        var qaux = Aux(b, "contacto_na", "Q1", 40, 8, "13-14");
        foreach (var x in new[] { s1, s2, qaux }) b.Wire(xp, "1", x, "1");
        b.Wire(s1, "2", plc, "I0.0").Wire(s2, "2", plc, "I0.2").Wire(qaux, "2", plc, "I0.4");

        b.Wire(f3, "2", plc, "1L");
        var coil = b.Add("bobina_contactor", "KM1", 30, 30);
        var h1 = b.Add("piloto", "H1", 34, 30, ("color", 1));
        b.Wire(plc, "Q0.0", coil, "A1").Wire(plc, "Q0.2", h1, "1");
        var xn2 = b.Add("borne", "XN2", 22, 37);
        b.Wire(coil, "A2", xn2, "1").Wire(h1, "2", xn2, "1").Wire(xn2, "1", xn, "1");
        return b.Schematic;
    }

    /// <summary>Transmisor de nivel de 0-10 V en la entrada AI1 de un Arduino; su relé K1 enciende la bomba (piloto de 24 V).</summary>
    public static Schematic McuLevelControl()
    {
        var b = new SchematicBuilder("Depósito con sensor de nivel 0-10 V y Arduino",
            "El transmisor de nivel B1 da de 0 a 10 V y llega a la entrada analógica AI1 de la placa del Arduino (A4). El sketch " +
            "arranca la bomba (piloto H1, por el relé K1) por debajo del 30 % y la para por encima del 80 %. Simula, selecciona B1 " +
            "y mueve su valor en Propiedades; los mensajes del Arduino salen en el registro.");
        var g1 = b.Add("fuente_dc", "G1", 0, 0, ("V", 24));
        var xp = b.Add("borne", "X+", 6, 4);
        var xm = b.Add("borne", "X-", 8, 22);
        b.Wire(g1, "+", xp, "1").Wire(g1, "-", xm, "1");
        var kf = b.Add(Catalog.ElementCatalog.McuTypeId, "KF1", 4, 10);
        b.Text(kf, "codigo", McuLevelSketch);
        b.Wire(xp, "1", kf, "L+").Wire(xm, "1", kf, "M");
        var b1 = b.Add("transmisor_010", "B1", 28, 2, ("magnitud", 2), ("valor", 50));
        b.Wire(xp, "1", b1, "L+").Wire(b1, "OUT", kf, "AI1").Wire(b1, "M", xm, "1");
        var h1 = b.Add("piloto", "H1", 12, 20, ("V", 24), ("P", 1.5), ("color", 1));
        b.Wire(xp, "1", kf, "1L").Wire(kf, "Q0.0", h1, "1").Wire(h1, "2", xm, "1");
        return b.Schematic;
    }
}
