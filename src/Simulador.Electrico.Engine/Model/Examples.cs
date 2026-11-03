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
    ];

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
}
