namespace Simulador.Hidraulica.Model;

/// <summary>Un diseño de ejemplo que se puede abrir desde el menú "Ejemplos".</summary>
public sealed record SampleInfo(string Name, string Summary, Func<HydraulicNetwork> Create);

/// <summary>Diseños de ejemplo ya prediseñados para empezar o para aprender.</summary>
public static class Samples
{
    public static IReadOnlyList<SampleInfo> All { get; } =
    [
        new("Bombeo a tanque elevado", "Bomba desde un pozo a un tanque a 12 m, con salida regulada por válvula de control.", PumpingToTank),
        new("Trasvase por gravedad", "Un tanque alto vacía a otro bajo por gravedad; abre o cierra la válvula para ver el efecto.", GravityTransfer),
        new("Dos bombas en paralelo", "Dos bombas con antirretorno impulsan al mismo tanque; enciende la segunda y compara el caudal.", ParallelPumps),
        new("Red de distribución en malla", "Tanque elevado que alimenta una malla de cuatro tramos con consumos, como una urbanización o un riego.", DistributionLoop),
        new("Control de nivel con electroválvula", "Llenado desde la red pública con electroválvula y transmisor de nivel, listo para mandarlo desde un PLC.", LevelControl),
        new("Grupo de presión con calderín", "Bomba con presostato y tanque hidroneumático que mantiene la presión de una vivienda: la bomba arranca y para sola.", PressureGroup),
        new("Riego por aspersión", "Bomba que alimenta cuatro aspersores a distinta altura; cada uno echa más o menos agua según la presión que le llega.", SprinklerIrrigation),
        new("Golpe de ariete", "Conducción de acero de 600 m que termina en una válvula de cierre rápido; analiza la sobrepresión al cerrarla.", WaterHammerLine),
    ];

    /// <summary>Bombeo desde un pozo a un tanque elevado, con válvula de control a la salida hacia el consumo.</summary>
    public static HydraulicNetwork PumpingToTank()
    {
        var net = new HydraulicNetwork
        {
            Name = "Bombeo a tanque elevado",
            Description = "La bomba B1 sube agua del pozo R1 al tanque T1, a 12 m de altura. El tanque descarga hacia R2 a través de " +
                "la válvula de control V1. Durante la simulación, mueve V1 o cambia la velocidad de B1 para equilibrar el nivel.",
        };
        net.Add(new Reservoir { Id = "R1", Elevation = 0, Level = 1, X = 80, Y = 360 });
        net.Add(new Junction { Id = "N1", Elevation = 0, X = 260, Y = 360 });
        net.Add(new Junction { Id = "N2", Elevation = 0, X = 420, Y = 360 });
        net.Add(new Tank { Id = "T1", Elevation = 12, Diameter = 1.5, MaxLevel = 2.5, InitialLevel = 0.6, X = 600, Y = 160 });
        net.Add(new Junction { Id = "N3", Elevation = 2, X = 780, Y = 300 });
        net.Add(new Junction { Id = "N4", Elevation = 2, Demand = 0, X = 940, Y = 300 });
        net.Add(new Reservoir { Id = "R2", Elevation = 0, Level = 0, X = 1080, Y = 420 });

        net.Connect(new Pipe { Id = "P1", Length = 6, Diameter = 0.065, MinorLoss = 0.5 }, "R1", "N1");
        net.Connect(new Pump { Id = "B1", ShutoffHead = 32, RatedFlow = 0.005, RatedHead = 24 }, "N1", "N2");
        net.Connect(new Pipe { Id = "P2", Length = 40, Diameter = 0.05, MinorLoss = 2 }, "N2", "T1",
            [new CanvasPoint(600, 360)]);
        net.Connect(new Pipe { Id = "P3", Length = 25, Diameter = 0.05, MinorLoss = 1 }, "T1", "N3",
            [new CanvasPoint(780, 160)]);
        net.Connect(new Valve { Id = "V1", Kind = ValveKind.Control, Diameter = 0.05, InitialOpening = 0.15, StrokeTime = 8 }, "N3", "N4");
        net.Connect(new Pipe { Id = "P4", Length = 15, Diameter = 0.05 }, "N4", "R2", [new CanvasPoint(1080, 300)]);

        net.Sensors.Add(new Sensor { Id = "LT1", Kind = SensorKind.Level, Target = "T1", X = 680, Y = 110 });
        net.Sensors.Add(new Sensor { Id = "PT1", Kind = SensorKind.Pressure, Target = "N2", X = 420, Y = 300 });
        net.Sensors.Add(new Sensor { Id = "FT1", Kind = SensorKind.Flow, Target = "V1", X = 860, Y = 250 });
        return net;
    }

    /// <summary>Dos tanques a distinta altura unidos por una tubería con válvula manual.</summary>
    public static HydraulicNetwork GravityTransfer()
    {
        var net = new HydraulicNetwork
        {
            Name = "Trasvase por gravedad",
            Description = "El tanque T1 está 8 m por encima de T2 y se vacía hacia él por gravedad, sin bombas. El caudal baja a " +
                "medida que baja el nivel de T1. Cierra V1 para parar el trasvase, o ábrela a medias para hacerlo más lento.",
        };
        net.Add(new Tank { Id = "T1", Elevation = 8, Diameter = 2, MaxLevel = 3, InitialLevel = 2.8, X = 160, Y = 160 });
        net.Add(new Junction { Id = "N1", Elevation = 0, X = 160, Y = 420 });
        net.Add(new Junction { Id = "N2", Elevation = 0, X = 460, Y = 420 });
        net.Add(new Junction { Id = "N3", Elevation = 0, X = 620, Y = 420 });
        net.Add(new Tank { Id = "T2", Elevation = 0, Diameter = 2.5, MaxLevel = 3, InitialLevel = 0.2, X = 860, Y = 340 });

        net.Connect(new Pipe { Id = "P1", Length = 9, Diameter = 0.05, MinorLoss = 0.5 }, "T1", "N1");
        net.Connect(new Pipe { Id = "P2", Length = 15, Diameter = 0.05, MinorLoss = 0.9 }, "N1", "N2");
        net.Connect(new Valve { Id = "V1", Kind = ValveKind.Manual, Diameter = 0.05, InitialOpening = 1 }, "N2", "N3");
        net.Connect(new Pipe { Id = "P3", Length = 12, Diameter = 0.05, MinorLoss = 1.0 }, "N3", "T2", [new CanvasPoint(860, 420)]);

        net.Sensors.Add(new Sensor { Id = "LT1", Kind = SensorKind.Level, Target = "T1", X = 260, Y = 100 });
        net.Sensors.Add(new Sensor { Id = "LT2", Kind = SensorKind.Level, Target = "T2", X = 960, Y = 280 });
        net.Sensors.Add(new Sensor { Id = "FT1", Kind = SensorKind.Flow, Target = "V1", X = 540, Y = 360 });
        return net;
    }

    /// <summary>Dos bombas iguales en paralelo con sus antirretornos, impulsando a un tanque elevado.</summary>
    public static HydraulicNetwork ParallelPumps()
    {
        var net = new HydraulicNetwork
        {
            Name = "Dos bombas en paralelo",
            Description = "B1 y B2 son iguales y aspiran del mismo depósito. Empieza solo con B1. Selecciona B2 y ponla en marcha: el " +
                "caudal sube, pero no se duplica, porque la tubería común pierde más carga. Los antirretornos impiden que " +
                "el agua vuelva por la bomba parada.",
        };
        net.Add(new Reservoir { Id = "R1", Elevation = 0, Level = 1.5, X = 80, Y = 340 });
        net.Add(new Junction { Id = "N1", Elevation = 0, X = 240, Y = 340 });
        net.Add(new Junction { Id = "N2", Elevation = 0, X = 240, Y = 240 });
        net.Add(new Junction { Id = "N3", Elevation = 0, X = 240, Y = 440 });
        net.Add(new Junction { Id = "N4", Elevation = 0, X = 420, Y = 240 });
        net.Add(new Junction { Id = "N5", Elevation = 0, X = 420, Y = 440 });
        net.Add(new Junction { Id = "N6", Elevation = 0, X = 560, Y = 240 });
        net.Add(new Junction { Id = "N7", Elevation = 0, X = 560, Y = 440 });
        net.Add(new Junction { Id = "N8", Elevation = 0, X = 680, Y = 340 });
        net.Add(new Tank { Id = "T1", Elevation = 15, Diameter = 2.5, MaxLevel = 3, InitialLevel = 0.5, X = 960, Y = 160 });
        net.Add(new Junction { Id = "N9", Elevation = 0, Demand = 0.004, X = 1120, Y = 340 });

        net.Connect(new Pipe { Id = "P1", Length = 4, Diameter = 0.08, MinorLoss = 0.5 }, "R1", "N1");
        net.Connect(new Pipe { Id = "P2", Length = 2, Diameter = 0.065, MinorLoss = 0.9 }, "N1", "N2");
        net.Connect(new Pipe { Id = "P3", Length = 2, Diameter = 0.065, MinorLoss = 0.9 }, "N1", "N3");
        net.Connect(new Pump { Id = "B1", ShutoffHead = 34, RatedFlow = 0.006, RatedHead = 26 }, "N2", "N4");
        net.Connect(new Pump { Id = "B2", ShutoffHead = 34, RatedFlow = 0.006, RatedHead = 26, InitiallyOn = false }, "N3", "N5");
        net.Connect(new Valve { Id = "VR1", Kind = ValveKind.Check, Diameter = 0.05, OpenLoss = 2 }, "N4", "N6");
        net.Connect(new Valve { Id = "VR2", Kind = ValveKind.Check, Diameter = 0.05, OpenLoss = 2 }, "N5", "N7");
        net.Connect(new Pipe { Id = "P4", Length = 2, Diameter = 0.065, MinorLoss = 0.9 }, "N6", "N8", [new CanvasPoint(680, 240)]);
        net.Connect(new Pipe { Id = "P5", Length = 2, Diameter = 0.065, MinorLoss = 0.9 }, "N7", "N8", [new CanvasPoint(680, 440)]);
        net.Connect(new Pipe { Id = "P6", Length = 60, Diameter = 0.065, MinorLoss = 3 }, "N8", "T1", [new CanvasPoint(960, 340)]);
        net.Connect(new Pipe { Id = "P7", Length = 30, Diameter = 0.065, MinorLoss = 1 }, "T1", "N9", [new CanvasPoint(1120, 160)]);

        net.Sensors.Add(new Sensor { Id = "FT1", Kind = SensorKind.Flow, Target = "P6", X = 820, Y = 290 });
        net.Sensors.Add(new Sensor { Id = "PT1", Kind = SensorKind.Pressure, Target = "N8", X = 700, Y = 290 });
        net.Sensors.Add(new Sensor { Id = "LT1", Kind = SensorKind.Level, Target = "T1", X = 1060, Y = 100 });
        return net;
    }

    /// <summary>Tanque elevado que alimenta una malla de cuatro nudos con consumos.</summary>
    public static HydraulicNetwork DistributionLoop()
    {
        var net = new HydraulicNetwork
        {
            Name = "Red de distribución en malla",
            Description = "El tanque T1, a 25 m, alimenta una malla cerrada que da servicio a cuatro puntos de consumo (N2 a N5). " +
                "El agua llega a cada punto por dos caminos, y el cálculo reparte el caudal según las pérdidas de cada tramo. " +
                "Cambia los consumos o los diámetros y mira cómo cambian las presiones: la más baja está en N4, el punto más alto y lejano.",
        };
        net.Add(new Tank { Id = "T1", Elevation = 25, Diameter = 5, MaxLevel = 4, InitialLevel = 3.5, X = 120, Y = 140 });
        net.Add(new Junction { Id = "N1", Elevation = 2, X = 120, Y = 360 });
        net.Add(new Junction { Id = "N2", Elevation = 3, Demand = 0.0025, X = 400, Y = 360 });
        net.Add(new Junction { Id = "N3", Elevation = 4, Demand = 0.003, X = 760, Y = 360 });
        net.Add(new Junction { Id = "N4", Elevation = 5, Demand = 0.002, X = 760, Y = 620 });
        net.Add(new Junction { Id = "N5", Elevation = 3, Demand = 0.0035, X = 400, Y = 620 });

        net.Connect(new Pipe { Id = "P1", Length = 120, Diameter = 0.1, Roughness = 0.0015, MinorLoss = 1.5 }, "T1", "N1");
        net.Connect(new Pipe { Id = "P2", Length = 150, Diameter = 0.08 }, "N1", "N2");
        net.Connect(new Pipe { Id = "P3", Length = 200, Diameter = 0.063 }, "N2", "N3");
        net.Connect(new Pipe { Id = "P4", Length = 150, Diameter = 0.05 }, "N3", "N4");
        net.Connect(new Pipe { Id = "P5", Length = 200, Diameter = 0.063 }, "N4", "N5");
        net.Connect(new Pipe { Id = "P6", Length = 150, Diameter = 0.063 }, "N5", "N2");
        net.Connect(new Pipe { Id = "P7", Length = 300, Diameter = 0.063 }, "N1", "N5", [new CanvasPoint(120, 620)]);

        net.Sensors.Add(new Sensor { Id = "PT1", Kind = SensorKind.Pressure, Target = "N4", X = 840, Y = 680 });
        net.Sensors.Add(new Sensor { Id = "LT1", Kind = SensorKind.Level, Target = "T1", X = 220, Y = 80 });
        net.Sensors.Add(new Sensor { Id = "FT1", Kind = SensorKind.Flow, Target = "P1", X = 180, Y = 260 });
        return net;
    }

    /// <summary>Llenado de un depósito desde la red pública con electroválvula, pensado para el control con PLC.</summary>
    public static HydraulicNetwork LevelControl()
    {
        var net = new HydraulicNetwork
        {
            Name = "Control de nivel con electroválvula",
            Description = "La red pública (R1, a unos 3 bar) llena el depósito T1 a través de la electroválvula EV1, mientras el " +
                "consumo de N2 lo vacía. Sin control, T1 acaba rebosando. Ábrela y ciérrala a mano viendo LT1, o en el banco " +
                "combinado conecta LT1.Medida a una entrada analógica del PLC y una salida a EV1.Bobina (por ejemplo, abrir " +
                "por debajo de 0,8 m y cerrar por encima de 1,6 m).",
        };
        net.Add(new Reservoir { Id = "R1", Elevation = 0, Level = 30, X = 80, Y = 260 });
        net.Add(new Junction { Id = "N1", Elevation = 0, X = 300, Y = 260 });
        net.Add(new Junction { Id = "N3", Elevation = 0, X = 440, Y = 260 });
        net.Add(new Tank { Id = "T1", Elevation = 4, Diameter = 1.2, MaxLevel = 2, InitialLevel = 0.6, X = 620, Y = 180 });
        net.Add(new Junction { Id = "N2", Elevation = 0, Demand = 0.0015, X = 880, Y = 360 });

        net.Connect(new Pipe { Id = "P1", Length = 25, Diameter = 0.032, MinorLoss = 1 }, "R1", "N1");
        net.Connect(new Valve { Id = "EV1", Kind = ValveKind.Solenoid, Diameter = 0.025, OpenLoss = 3, InitialOpening = 1, StrokeTime = 0.5 }, "N1", "N3");
        net.Connect(new Pipe { Id = "P2", Length = 8, Diameter = 0.032, MinorLoss = 1.5 }, "N3", "T1", [new CanvasPoint(620, 260)]);
        net.Connect(new Pipe { Id = "P3", Length = 20, Diameter = 0.04, MinorLoss = 1 }, "T1", "N2", [new CanvasPoint(880, 180)]);

        net.Sensors.Add(new Sensor { Id = "LT1", Kind = SensorKind.Level, Target = "T1", X = 720, Y = 120 });
        net.Sensors.Add(new Sensor { Id = "FT1", Kind = SensorKind.Flow, Target = "EV1", X = 370, Y = 200 });
        net.Sensors.Add(new Sensor { Id = "PT1", Kind = SensorKind.Pressure, Target = "N2", X = 960, Y = 300 });
        return net;
    }

    /// <summary>Grupo de presión doméstico: bomba con presostato, calderín y consumos.</summary>
    public static HydraulicNetwork PressureGroup()
    {
        var net = new HydraulicNetwork
        {
            Name = "Grupo de presión con calderín",
            Description = "La bomba B1 aspira del aljibe R1 y llena el calderín T1, un tanque cerrado con aire. Al entrar agua, el aire " +
                "se comprime y la presión sube. El presostato para la bomba a 4 bar y la vuelve a arrancar a 2,5 bar, mientras la " +
                "vivienda (N3) consume. Mira en la gráfica cómo la bomba arranca y para sola, y prueba a cambiar el consumo de N3.",
        };
        net.Add(new Reservoir { Id = "R1", Elevation = 0, Level = 1, X = 80, Y = 360 });
        net.Add(new Junction { Id = "N1", Elevation = 0, X = 240, Y = 360 });
        net.Add(new Junction { Id = "N2", Elevation = 0, X = 400, Y = 360 });
        net.Add(new Junction { Id = "N4", Elevation = 0, X = 540, Y = 360 });
        net.Add(new Tank { Id = "T1", Elevation = 0, Diameter = 0.45, MaxLevel = 1.0, InitialLevel = 0.3, Closed = true, InitialGasPressure = 2.4e5, X = 540, Y = 220 });
        net.Add(new Junction { Id = "N3", Elevation = 6, Demand = 0.0006, X = 900, Y = 260 });

        net.Connect(new Pipe { Id = "P1", Length = 3, Diameter = 0.04, MinorLoss = 1 }, "R1", "N1");
        net.Connect(new Pump { Id = "B1", ShutoffHead = 55, RatedFlow = 0.0012, RatedHead = 40, PressureSwitchNode = "N4",
            SwitchOnPressure = 2.5e5, SwitchOffPressure = 4.0e5, InitiallyOn = false }, "N1", "N2");
        net.Connect(new Valve { Id = "VR1", Kind = ValveKind.Check, Diameter = 0.032, OpenLoss = 2 }, "N2", "N4");
        net.Connect(new Pipe { Id = "P2", Length = 1, Diameter = 0.032 }, "N4", "T1");
        net.Connect(new Pipe { Id = "P3", Length = 25, Diameter = 0.025, MinorLoss = 4, Material = "PE" }, "N4", "N3", [new CanvasPoint(900, 360)]);

        net.Sensors.Add(new Sensor { Id = "PT1", Kind = SensorKind.Pressure, Target = "N4", X = 620, Y = 420 });
        net.Sensors.Add(new Sensor { Id = "LT1", Kind = SensorKind.Level, Target = "T1", X = 640, Y = 160 });
        return net;
    }

    /// <summary>Cuatro aspersores a distinta cota alimentados por una bomba.</summary>
    public static HydraulicNetwork SprinklerIrrigation()
    {
        var net = new HydraulicNetwork
        {
            Name = "Riego por aspersión",
            Description = "B1 bombea desde la balsa R1 a un ramal con cuatro aspersores (A1 a A4) que dan 0,4 L/s a 1 bar. Están " +
                "cada vez más altos y más lejos, así que les llega menos presión y echan menos agua. Para igualarlos, prueba a " +
                "aumentar el diámetro del ramal o a cerrar un poco V1.",
        };
        net.Add(new Reservoir { Id = "R1", Elevation = 0, Level = 1, X = 80, Y = 380 });
        net.Add(new Junction { Id = "N1", Elevation = 0, X = 220, Y = 380 });
        net.Add(new Junction { Id = "N2", Elevation = 0, X = 360, Y = 380 });
        net.Add(new Junction { Id = "N3", Elevation = 0, X = 460, Y = 380 });
        net.Add(new Junction { Id = "A1", Elevation = 1, DischargeAt1Bar = 0.0004, X = 560, Y = 300 });
        net.Add(new Junction { Id = "A2", Elevation = 3, DischargeAt1Bar = 0.0004, X = 700, Y = 300 });
        net.Add(new Junction { Id = "A3", Elevation = 6, DischargeAt1Bar = 0.0004, X = 840, Y = 300 });
        net.Add(new Junction { Id = "A4", Elevation = 9, DischargeAt1Bar = 0.0004, X = 980, Y = 300 });

        net.Connect(new Pipe { Id = "P1", Length = 4, Diameter = 0.05, MinorLoss = 1 }, "R1", "N1");
        net.Connect(new Pump { Id = "B1", ShutoffHead = 45, RatedFlow = 0.0018, RatedHead = 32 }, "N1", "N2");
        net.Connect(new Valve { Id = "V1", Kind = ValveKind.Manual, Diameter = 0.04, InitialOpening = 1 }, "N2", "N3");
        net.Connect(new Pipe { Id = "P2", Length = 40, Diameter = 0.032, Material = "PE" }, "N3", "A1", [new CanvasPoint(560, 380)]);
        net.Connect(new Pipe { Id = "P3", Length = 40, Diameter = 0.025, Material = "PE" }, "A1", "A2");
        net.Connect(new Pipe { Id = "P4", Length = 40, Diameter = 0.025, Material = "PE" }, "A2", "A3");
        net.Connect(new Pipe { Id = "P5", Length = 40, Diameter = 0.02, Material = "PE" }, "A3", "A4");

        net.Sensors.Add(new Sensor { Id = "FT1", Kind = SensorKind.Flow, Target = "B1", X = 290, Y = 320 });
        net.Sensors.Add(new Sensor { Id = "PT1", Kind = SensorKind.Pressure, Target = "A4", X = 1040, Y = 230 });
        return net;
    }

    /// <summary>Conducción por gravedad con válvula de cierre rápido para estudiar el golpe de ariete.</summary>
    public static HydraulicNetwork WaterHammerLine()
    {
        var net = new HydraulicNetwork
        {
            Name = "Golpe de ariete",
            Description = "El depósito R1, a 40 m, alimenta una conducción de acero de 600 m que termina en la válvula V1, que " +
                "descarga a R2. Selecciona V1 y pulsa «Analizar golpe de ariete». Cerrándola en 0,5 s, la sobrepresión llega casi a la de " +
                "Joukowsky. Prueba con 10 s o más: es más lento que el tiempo crítico 2L/a y la sobrepresión baja mucho.",
        };
        net.Add(new Reservoir { Id = "R1", Elevation = 40, Level = 2, X = 100, Y = 160 });
        net.Add(new Junction { Id = "N1", Elevation = 20, X = 500, Y = 260 });
        net.Add(new Junction { Id = "N2", Elevation = 0, X = 860, Y = 380 });
        net.Add(new Junction { Id = "N3", Elevation = 0, X = 980, Y = 380 });
        net.Add(new Reservoir { Id = "R2", Elevation = 0, Level = 0.5, X = 1120, Y = 380 });

        net.Connect(new Pipe { Id = "P1", Length = 300, Diameter = 0.15, Roughness = 0.045, WallThickness = 0.005, Material = "Acero", MinorLoss = 0.5 }, "R1", "N1");
        net.Connect(new Pipe { Id = "P2", Length = 300, Diameter = 0.15, Roughness = 0.045, WallThickness = 0.005, Material = "Acero" }, "N1", "N2");
        net.Connect(new Valve { Id = "V1", Kind = ValveKind.Control, Diameter = 0.15, OpenLoss = 0.2, InitialOpening = 0.04, StrokeTime = 0.5 }, "N2", "N3");
        net.Connect(new Pipe { Id = "P3", Length = 5, Diameter = 0.15, Material = "Acero" }, "N3", "R2");

        net.Sensors.Add(new Sensor { Id = "PT1", Kind = SensorKind.Pressure, Target = "N2", X = 860, Y = 300 });
        net.Sensors.Add(new Sensor { Id = "FT1", Kind = SensorKind.Flow, Target = "V1", X = 920, Y = 320 });
        return net;
    }

    private static T Connect<T>(this HydraulicNetwork net, T link, string from, string to, List<CanvasPoint> vertices)
        where T : HydraulicLink
    {
        link.Vertices = vertices;
        return net.Connect(link, from, to);
    }
}
