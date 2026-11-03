using System.Text.Json.Serialization;

namespace Simulador.Hidraulica.Model;

/// <summary>Punto de la red donde se calcula la altura piezométrica (nudo, depósito, tanque).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(Junction), "nudo")]
[JsonDerivedType(typeof(Reservoir), "deposito")]
[JsonDerivedType(typeof(Tank), "tanque")]
public abstract class HydraulicNode
{
    /// <summary>Identificador visible, p. ej. "N1", "T1".</summary>
    public string Id { get; set; } = "";
    /// <summary>Cota del nudo o del fondo del tanque (m).</summary>
    public double Elevation { get; set; }
    /// <summary>Posición en el lienzo (píxeles lógicos).</summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Nombre del tipo para la interfaz y la lista de materiales.</summary>
    [JsonIgnore] public abstract string TypeName { get; }
}

/// <summary>Unión de tuberías (codo, te, cruz) con un consumo opcional.</summary>
public sealed class Junction : HydraulicNode
{
    /// <summary>Caudal que sale de la red en este punto (m³/s). Positivo = consumo.</summary>
    public double Demand { get; set; }
    public override string TypeName => "Nudo / unión";
}

/// <summary>Depósito de nivel constante (fuente infinita: río, red pública, pozo grande).</summary>
public sealed class Reservoir : HydraulicNode
{
    /// <summary>Nivel de agua sobre la cota (m). La altura piezométrica es Elevation + Level.</summary>
    public double Level { get; set; }
    public override string TypeName => "Depósito de nivel constante";
}

/// <summary>Tanque cilíndrico abierto a la atmósfera cuyo nivel cambia con el tiempo.</summary>
public sealed class Tank : HydraulicNode
{
    /// <summary>Diámetro interior (m).</summary>
    public double Diameter { get; set; } = 2;
    /// <summary>Altura útil máxima (m). Por encima rebosa.</summary>
    public double MaxLevel { get; set; } = 3;
    /// <summary>Nivel al empezar la simulación (m).</summary>
    public double InitialLevel { get; set; } = 1;

    /// <summary>Nivel actual durante la simulación (m). No se guarda.</summary>
    [JsonIgnore] public double Level { get; set; }

    [JsonIgnore] public double Area => Math.PI * Diameter * Diameter / 4;
    [JsonIgnore] public double Volume => Area * Level;
    [JsonIgnore] public double Capacity => Area * MaxLevel;
    public override string TypeName => "Tanque";
}

/// <summary>Elemento entre dos nudos por el que circula caudal (tubería, bomba, válvula).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(Pipe), "tuberia")]
[JsonDerivedType(typeof(Pump), "bomba")]
[JsonDerivedType(typeof(Valve), "valvula")]
public abstract class HydraulicLink
{
    public string Id { get; set; } = "";
    /// <summary>Nudo de entrada (sentido positivo del caudal).</summary>
    public string From { get; set; } = "";
    /// <summary>Nudo de salida.</summary>
    public string To { get; set; } = "";
    /// <summary>Puntos intermedios del trazado en el lienzo.</summary>
    public List<CanvasPoint> Vertices { get; set; } = [];

    [JsonIgnore] public abstract string TypeName { get; }
}

public readonly record struct CanvasPoint(double X, double Y);

public sealed class Pipe : HydraulicLink
{
    /// <summary>Longitud (m).</summary>
    public double Length { get; set; } = 10;
    /// <summary>Diámetro interior (m).</summary>
    public double Diameter { get; set; } = 0.05;
    /// <summary>Rugosidad absoluta (mm). PVC ≈ 0,0015; acero comercial ≈ 0,045; hierro fundido ≈ 0,26.</summary>
    public double Roughness { get; set; } = 0.0015;
    /// <summary>Suma de coeficientes de pérdidas locales (codos, entradas, salidas).</summary>
    public double MinorLoss { get; set; }
    /// <summary>Material para la lista de materiales.</summary>
    public string Material { get; set; } = "PVC";
    public override string TypeName => "Tubería";
}

/// <summary>
/// Bomba centrífuga. Curva H = H0 − r·Q² ajustada con dos puntos (altura a caudal cero y punto nominal),
/// escalada con las leyes de semejanza según la velocidad relativa. Lleva antirretorno incorporado.
/// </summary>
public sealed class Pump : HydraulicLink
{
    /// <summary>Altura a caudal cero (m).</summary>
    public double ShutoffHead { get; set; } = 30;
    /// <summary>Caudal nominal (m³/s).</summary>
    public double RatedFlow { get; set; } = 0.005;
    /// <summary>Altura en el punto nominal (m).</summary>
    public double RatedHead { get; set; } = 22;
    /// <summary>Velocidad nominal del eje (rad/s). 2900 rpm ≈ 303,7 rad/s.</summary>
    public double RatedSpeed { get; set; } = 303.7;
    /// <summary>Rendimiento hidráulico (0–1).</summary>
    public double Efficiency { get; set; } = 0.65;
    /// <summary>Inercia del rodete (kg·m²), la suma el motor que la acciona.</summary>
    public double Inertia { get; set; } = 0.01;
    /// <summary>Encendida al empezar (si no hay motor ni PLC conectados).</summary>
    public bool InitiallyOn { get; set; } = true;

    /// <summary>Velocidad relativa actual (0 = parada, 1 = nominal). No se guarda.</summary>
    [JsonIgnore] public double RelativeSpeed { get; set; }

    [JsonIgnore] public double CurveCoefficient =>
        RatedFlow > 0 ? Math.Max(ShutoffHead - RatedHead, 0) / (RatedFlow * RatedFlow) : 0;

    /// <summary>Altura de la bomba a un caudal y velocidad relativa dados (m).</summary>
    public double HeadAt(double flow, double relativeSpeed) =>
        relativeSpeed * relativeSpeed * ShutoffHead - CurveCoefficient * flow * Math.Abs(flow);

    public override string TypeName => "Bomba centrífuga";
}

public enum ValveKind
{
    /// <summary>Compuerta o bola manual: abierta/cerrada o apertura fija.</summary>
    Manual,
    /// <summary>Válvula de control con actuador (apertura 0–100 % desde PLC).</summary>
    Control,
    /// <summary>Electroválvula todo/nada (bobina desde PLC).</summary>
    Solenoid,
    /// <summary>Antirretorno: solo deja pasar en sentido From → To.</summary>
    Check,
}

public sealed class Valve : HydraulicLink
{
    public ValveKind Kind { get; set; } = ValveKind.Manual;
    /// <summary>Diámetro nominal (m).</summary>
    public double Diameter { get; set; } = 0.05;
    /// <summary>Coeficiente de pérdida totalmente abierta (adimensional).</summary>
    public double OpenLoss { get; set; } = 0.2;
    /// <summary>Apertura inicial (0–1).</summary>
    public double InitialOpening { get; set; } = 1;
    /// <summary>Tiempo de recorrido de cerrada a abierta (s). 0 = instantáneo.</summary>
    public double StrokeTime { get; set; }

    /// <summary>Apertura actual (0–1). No se guarda.</summary>
    [JsonIgnore] public double Opening { get; set; }
    /// <summary>Apertura pedida por el usuario o el PLC (0–1). No se guarda.</summary>
    [JsonIgnore] public double OpeningSetpoint { get; set; }

    public override string TypeName => Kind switch
    {
        ValveKind.Control => "Válvula de control",
        ValveKind.Solenoid => "Electroválvula",
        ValveKind.Check => "Válvula antirretorno",
        _ => "Válvula manual",
    };
}

public enum SensorKind { Pressure, Level, Flow }

/// <summary>Instrumento colocado en un nudo, tanque o enlace; publica su medida como señal analógica.</summary>
public sealed class Sensor
{
    public string Id { get; set; } = "";
    public SensorKind Kind { get; set; }
    /// <summary>Id del nudo (presión, nivel) o del enlace (caudal) medido.</summary>
    public string Target { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }

    [JsonIgnore] public string TypeName => Kind switch
    {
        SensorKind.Pressure => "Transmisor de presión",
        SensorKind.Level => "Transmisor de nivel",
        _ => "Caudalímetro",
    };
}

/// <summary>Propiedades del fluido. Por defecto agua a 20 °C.</summary>
public sealed record Fluid(string Name = "Agua 20 °C", double Density = 998.2, double KinematicViscosity = 1.004e-6,
    double BulkModulus = 2.2e9)
{
    public static Fluid Water { get; } = new();
}
