namespace Simulador.Hidraulica.Model;

/// <summary>Diseños de ejemplo para empezar.</summary>
public static class Samples
{
    /// <summary>Bombeo desde un pozo a un tanque elevado, con válvula de control a la salida hacia el consumo.</summary>
    public static HydraulicNetwork PumpingToTank()
    {
        var net = new HydraulicNetwork { Name = "Bombeo a tanque elevado" };
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

    private static T Connect<T>(this HydraulicNetwork net, T link, string from, string to, List<CanvasPoint> vertices)
        where T : HydraulicLink
    {
        link.Vertices = vertices;
        return net.Connect(link, from, to);
    }
}
