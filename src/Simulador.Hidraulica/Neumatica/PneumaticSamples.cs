namespace Simulador.Hidraulica.Neumatica;

public sealed record PneumaticSample(string Name, string Summary, Func<PneumaticCircuit> Create);

/// <summary>Circuitos neumáticos de ejemplo, de los clásicos de la enseñanza de automatismos.</summary>
public static class PneumaticSamples
{
    public static IReadOnlyList<PneumaticSample> All { get; } =
    [
        new("Cilindro de simple efecto con pulsador", "Mando directo: mientras pulsas la válvula 3/2 el vástago sale; al soltarla, el muelle lo recoge.", SingleActing),
        new("Doble efecto con reguladores de caudal", "Válvula 5/2 de palanca y dos reguladores unidireccionales que frenan el aire de escape (regulación a la salida).", SpeedControl),
        new("Mando bimanual (función Y)", "El cilindro solo sale si se pulsan los dos pulsadores a la vez: válvula de simultaneidad y 5/2 pilotada.", TwoHand),
        new("Vaivén automático con finales de carrera", "Válvula 5/2 biestable pilotada por dos válvulas de rodillo: el cilindro va y vuelve solo mientras la palanca de marcha está puesta.", Oscillation),
        new("Electroneumática para PLC", "Electroválvula 5/2 de doble bobina (Y1, Y2) y detectores magnéticos (1B1, 1B2): lista para mandarla desde el PLC.", ElectroPneumatic),
        new("Compresor, depósito y consumo", "Un compresor pequeño llena un depósito de 20 L; al mover un cilindro grande la presión baja y el compresor arranca solo.", CompressorAndReceiver),
    ];

    private static (double, double) A(Cylinder c) => c.PortPosition("A");
    private static PortPoint P(double x, double y) => new(x, y);

    public static PneumaticCircuit SingleActing()
    {
        var c = new PneumaticCircuit
        {
            Name = "Cilindro de simple efecto con pulsador",
            Description = "Haz clic y mantén pulsado el pulsador de 1V1: el aire entra por A y el vástago sale. Al soltarlo, la " +
                "válvula vuelve a reposo, la cámara escapa por la toma 3 y el muelle recoge el vástago.",
        };
        c.Add(new Cylinder { Id = "1A1", Kind = CylinderKind.SingleActing, Bore = 0.025, RodDiameter = 0.01, Stroke = 0.1, Mass = 1, X = 400, Y = 140 });
        c.Add(new DirectionalValve { Id = "1V1", Ways = ValveWays.ThreeTwo, Actuation = ValveActuation.PushButton, X = 330, Y = 300 });
        c.Add(new AirSupply { Id = "0Z1", X = 342, Y = 420 });
        c.Add(new Gauge { Id = "PI1", X = 260, Y = 200 });
        c.Connect("1V1", "2", "1A1", "A", 0.8);
        c.Connect("0Z1", "1", "1V1", "1", 1);
        c.Connect("PI1", "1", "1A1", "A", 0.2, P(260, 240), P(350, 240));
        return c;
    }

    public static PneumaticCircuit SpeedControl()
    {
        var c = new PneumaticCircuit
        {
            Name = "Doble efecto con reguladores de caudal",
            Description = "Haz clic en la palanca de 1V1 para cambiarla de posición. Los reguladores 1V2 y 1V3 frenan el aire que " +
                "sale del cilindro (regulación a la salida), así el vástago va suave y con fuerza. Cambia su apertura y mira el tiempo de carrera.",
        };
        c.Add(new Cylinder { Id = "1A1", Bore = 0.032, RodDiameter = 0.012, Stroke = 0.2, Mass = 3, X = 420, Y = 120 });
        c.Add(new FlowControl { Id = "1V2", Rotation = 180, Opening = 0.25, X = 370, Y = 220 });
        c.Add(new FlowControl { Id = "1V3", Rotation = 180, Opening = 0.25, X = 470, Y = 220 });
        c.Add(new DirectionalValve { Id = "1V1", Ways = ValveWays.FiveTwo, Actuation = ValveActuation.Lever, X = 400, Y = 330 });
        c.Add(new AirSupply { Id = "0Z1", X = 420, Y = 440 });
        c.Connect("1V2", "1", "1A1", "A", 0.3);
        c.Connect("1V3", "1", "1A1", "B", 0.3);
        c.Connect("1V1", "4", "1V2", "2", 1, P(410, 280), P(370, 280));
        c.Connect("1V1", "2", "1V3", "2", 1, P(430, 280), P(470, 280));
        c.Connect("0Z1", "1", "1V1", "1", 1);
        return c;
    }

    public static PneumaticCircuit TwoHand()
    {
        var c = new PneumaticCircuit
        {
            Name = "Mando bimanual (función Y)",
            Description = "1S1 y 1S2 son dos pulsadores separados para que el operario tenga las dos manos ocupadas. La válvula de " +
                "simultaneidad 1V2 solo da aire a la 5/2 pilotada si llega por las dos entradas. Pulsa uno solo y no pasa nada; " +
                "mantén los dos (Ctrl+clic para dejar uno enclavado) y el cilindro sale.",
        };
        c.Add(new Cylinder { Id = "1A1", Bore = 0.04, RodDiameter = 0.016, Stroke = 0.15, Mass = 4, X = 500, Y = 100 });
        c.Add(new DirectionalValve { Id = "1V1", Ways = ValveWays.FiveTwo, Actuation = ValveActuation.Pilot, X = 500, Y = 240 });
        c.Add(new LogicValve { Id = "1V2", Kind = LogicKind.And, X = 360, Y = 300 });
        c.Add(new DirectionalValve { Id = "1S1", Ways = ValveWays.ThreeTwo, Actuation = ValveActuation.PushButton, X = 250, Y = 400 });
        c.Add(new DirectionalValve { Id = "1S2", Ways = ValveWays.ThreeTwo, Actuation = ValveActuation.PushButton, X = 430, Y = 400 });
        c.Add(new AirSupply { Id = "0Z1", X = 520, Y = 520 });
        c.Connect("1V1", "4", "1A1", "A", 0.8, P(510, 180), P(450, 180));
        c.Connect("1V1", "2", "1A1", "B", 0.8, P(530, 180), P(550, 180));
        c.Connect("1V2", "2", "1V1", "14", 0.6, P(360, 240));
        c.Connect("1S1", "2", "1V2", "1a", 0.8, P(270, 300));
        c.Connect("1S2", "2", "1V2", "1b", 0.8, P(450, 340), P(390, 340));
        c.Connect("0Z1", "1", "1V1", "1", 1);
        c.Connect("0Z1", "1", "1S1", "1", 1.5, P(520, 470), P(262, 470));
        c.Connect("0Z1", "1", "1S2", "1", 1, P(520, 470), P(442, 470));
        return c;
    }

    public static PneumaticCircuit Oscillation()
    {
        var c = new PneumaticCircuit
        {
            Name = "Vaivén automático con finales de carrera",
            Description = "Pon la palanca de marcha 1S3. Con el cilindro recogido, el rodillo 1S1 está pisado y pilota 1V1 por 14: " +
                "el vástago sale. Al llegar al final pisa 1S2, que pilota 1V1 por 12 y lo hace volver. Así sigue solo hasta que " +
                "quitas la marcha. Los reguladores 1V2 y 1V3 marcan la velocidad.",
        };
        var cyl = c.Add(new Cylinder { Id = "1A1", Bore = 0.032, RodDiameter = 0.012, Stroke = 0.2, Mass = 3, X = 500, Y = 100 });
        c.Add(new FlowControl { Id = "1V2", Rotation = 180, Opening = 0.3, X = 450, Y = 180 });
        c.Add(new FlowControl { Id = "1V3", Rotation = 180, Opening = 0.3, X = 550, Y = 180 });
        c.Add(new DirectionalValve { Id = "1V1", Ways = ValveWays.FiveTwo, Actuation = ValveActuation.DoublePilot, X = 500, Y = 280 });
        c.Add(new DirectionalValve { Id = "1S1", Ways = ValveWays.ThreeTwo, Actuation = ValveActuation.Roller, RollerCylinder = "1A1", RollerPosition = 0, X = 300, Y = 380 });
        c.Add(new DirectionalValve { Id = "1S2", Ways = ValveWays.ThreeTwo, Actuation = ValveActuation.Roller, RollerCylinder = "1A1", RollerPosition = 1, X = 680, Y = 380 });
        c.Add(new DirectionalValve { Id = "1S3", Ways = ValveWays.ThreeTwo, Actuation = ValveActuation.Lever, X = 300, Y = 480 });
        c.Add(new AirSupply { Id = "0Z1", X = 520, Y = 570 });
        c.Connect("1V2", "1", "1A1", "A", 0.2);
        c.Connect("1V3", "1", "1A1", "B", 0.2);
        c.Connect("1V1", "4", "1V2", "2", 0.6, P(510, 235), P(450, 235));
        c.Connect("1V1", "2", "1V3", "2", 0.6, P(530, 235), P(550, 235));
        c.Connect("1S1", "2", "1V1", "14", 0.8, P(320, 280));
        c.Connect("1S2", "2", "1V1", "12", 0.8, P(700, 280));
        c.Connect("1S3", "2", "1S1", "1", 0.5, P(320, 430), P(312, 430));
        c.Connect("0Z1", "1", "1V1", "1", 1);
        c.Connect("0Z1", "1", "1S3", "1", 1.5, P(520, 530), P(312, 530));
        c.Connect("0Z1", "1", "1S2", "1", 1.5, P(520, 530), P(692, 530));
        return c;
    }

    public static PneumaticCircuit ElectroPneumatic()
    {
        var c = new PneumaticCircuit
        {
            Name = "Electroneumática para PLC",
            Description = "Las bobinas Y1 (sacar) e Y2 (recoger) de 1V1 se pueden activar con un clic durante la simulación o desde un " +
                "PLC en el banco de pruebas combinado. Los detectores magnéticos 1B1 (recogido) y 1B2 (fuera) le dicen al PLC dónde " +
                "está el vástago. Al ser biestable, la válvula recuerda la última orden aunque se quite la bobina.",
        };
        c.Add(new Cylinder { Id = "1A1", Bore = 0.032, RodDiameter = 0.012, Stroke = 0.16, Mass = 2, X = 480, Y = 120, SensorRetracted = "1B1", SensorExtended = "1B2" });
        c.Add(new FlowControl { Id = "1V2", Rotation = 180, Opening = 0.35, X = 430, Y = 210 });
        c.Add(new FlowControl { Id = "1V3", Rotation = 180, Opening = 0.35, X = 530, Y = 210 });
        c.Add(new DirectionalValve { Id = "1V1", Ways = ValveWays.FiveTwo, Actuation = ValveActuation.DoubleSolenoid, Coil14 = "Y1", Coil12 = "Y2", X = 480, Y = 330 });
        c.Add(new AirSupply { Id = "0Z1", X = 500, Y = 450 });
        c.Add(new Gauge { Id = "PI1", X = 340, Y = 210 });
        c.Connect("1V2", "1", "1A1", "A", 0.2);
        c.Connect("1V3", "1", "1A1", "B", 0.2);
        c.Connect("1V1", "4", "1V2", "2", 0.8, P(490, 275), P(430, 275));
        c.Connect("1V1", "2", "1V3", "2", 0.8, P(510, 275), P(530, 275));
        c.Connect("0Z1", "1", "1V1", "1", 1);
        c.Connect("PI1", "1", "1V2", "2", 0.2, P(340, 260), P(430, 260));
        return c;
    }

    public static PneumaticCircuit CompressorAndReceiver()
    {
        var c = new PneumaticCircuit
        {
            Name = "Compresor, depósito y consumo",
            Description = "El compresor 0P1 (60 L/min) llena el depósito 0Z2 hasta 8 bar y para. Mueve la palanca de 1V1 varias " +
                "veces: el cilindro grande gasta aire en cada carrera, la presión del manómetro baja y, al llegar a 6 bar, el " +
                "compresor vuelve a arrancar. Así se dimensionan compresor y depósito.",
        };
        c.Add(new Cylinder { Id = "1A1", Bore = 0.063, RodDiameter = 0.02, Stroke = 0.3, Mass = 8, X = 560, Y = 120 });
        c.Add(new DirectionalValve { Id = "1V1", Ways = ValveWays.FiveTwo, Actuation = ValveActuation.Lever, X = 540, Y = 300 });
        c.Add(new Compressor { Id = "0P1", FreeAirDelivery = 60.0 / 60_000, X = 200, Y = 480 });
        c.Add(new AirReceiver { Id = "0Z2", Volume = 0.02, InitialPressure = 7e5, X = 360, Y = 480 });
        c.Add(new Gauge { Id = "PI1", X = 460, Y = 400 });
        c.Connect("1V1", "4", "1A1", "A", 1, P(550, 220), P(510, 220));
        c.Connect("1V1", "2", "1A1", "B", 1, P(570, 220), P(610, 220));
        c.Connect("0P1", "1", "0Z2", "1", 1, P(200, 420), P(360, 420));
        c.Connect("0Z2", "1", "1V1", "1", 2, P(360, 420), P(560, 420));
        c.Connect("PI1", "1", "0Z2", "1", 0.3, P(460, 420), P(360, 420));
        return c;
    }
}
