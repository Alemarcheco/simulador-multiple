namespace Simulador.Core.Pcb;

// Modelo neutro de una placa terminada, para que otros módulos la usen sin depender del editor de PCB:
// el módulo 3D genera cajas a medida y el banco de pruebas la usa como subsistema.
// Todas las medidas en milímetros; origen abajo a la izquierda, Y hacia arriba; ángulos en grados antihorarios.

public readonly record struct PointMm(double X, double Y);

public enum BoardSide { Top, Bottom }

/// <summary>Taladro (pasante o de fijación).</summary>
public sealed record BoardHole(PointMm Position, double DiameterMm, bool Plated, string? ComponentRef = null);

/// <summary>Componente colocado en la placa.</summary>
public sealed record BoardComponent(
    string Reference,
    string Value,
    string Footprint,
    PointMm Position,
    double RotationDeg,
    BoardSide Side,
    // Altura aproximada sobre la placa, para cajas. 0 si se desconoce.
    double HeightMm,
    // Ruta al modelo STEP/WRL (normalmente de las librerías KiCad), si existe.
    string? Model3DPath,
    // Rectángulo que ocupa la huella en la placa (mismas coordenadas, Y hacia arriba), si se conoce.
    // El módulo 3D lo usa para que los huecos de la caja tengan el ancho real del conector.
    PointMm? BoundsMin = null,
    PointMm? BoundsMax = null);

/// <summary>Pin de conector que sale de la placa (se expone como ISignalPort en co-simulación).</summary>
public sealed record BoardConnectorPin(string ComponentRef, string PinNumber, string NetName, PointMm Position);

/// <summary>Placa exportada.</summary>
public sealed record BoardExport(
    string Name,
    // Contorno exterior, polilínea cerrada (el último punto conecta con el primero).
    IReadOnlyList<PointMm> Outline,
    // Recortes interiores (ventanas), cada uno polilínea cerrada.
    IReadOnlyList<IReadOnlyList<PointMm>> Cutouts,
    double ThicknessMm,
    IReadOnlyList<BoardHole> Holes,
    IReadOnlyList<BoardComponent> Components,
    IReadOnlyList<BoardConnectorPin> ConnectorPins)
{
    public (PointMm Min, PointMm Max) Bounds()
    {
        if (Outline.Count == 0) return (default, default);
        return (new PointMm(Outline.Min(p => p.X), Outline.Min(p => p.Y)),
                new PointMm(Outline.Max(p => p.X), Outline.Max(p => p.Y)));
    }
}
