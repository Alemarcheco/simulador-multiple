using Simulador.Hidraulica.Model;

namespace Simulador.Hidraulica.Solver;

/// <summary>Leyes de pérdida de carga de cada tipo de enlace: h = H(entrada) − H(salida) en función de Q.</summary>
public static class LinkLaws
{
    public const double Gravity = 9.80665;
    /// <summary>Resistencia lineal de un enlace cerrado (m por m³/s): deja pasar un caudal despreciable.</summary>
    public const double ClosedResistance = 1e8;

    /// <summary>Pérdida de carga (m) para el caudal <paramref name="q"/> (m³/s) con el estado actual del enlace.</summary>
    public static double HeadLoss(HydraulicLink link, double q, Fluid fluid) => link switch
    {
        Pipe p => PipeLoss(p, q, fluid),
        Valve v => ValveLoss(v, q),
        Pump b => PumpLoss(b, q),
        _ => throw new NotSupportedException(link.GetType().Name),
    };

    public static double PipeLoss(Pipe p, double q, Fluid fluid)
    {
        double area = Math.PI * p.Diameter * p.Diameter / 4;
        double v = q / area;
        double re = Math.Abs(v) * p.Diameter / fluid.KinematicViscosity;
        double velocityHead = v * Math.Abs(v) / (2 * Gravity);
        double minor = p.MinorLoss * velocityHead;

        if (re < 2000)
            return 32 * fluid.KinematicViscosity * p.Length * v / (Gravity * p.Diameter * p.Diameter) + minor;

        double f = re > 4000
            ? SwameeJain(re, p.Roughness / 1000, p.Diameter)
            : Lerp(64.0 / 2000, SwameeJain(4000, p.Roughness / 1000, p.Diameter), (re - 2000) / 2000);
        return f * p.Length / p.Diameter * velocityHead + minor;
    }

    /// <summary>Factor de fricción de Darcy en régimen turbulento (Swamee–Jain, explícito).</summary>
    public static double SwameeJain(double re, double roughnessMeters, double diameter)
    {
        double x = Math.Log10(roughnessMeters / (3.7 * diameter) + 5.74 / Math.Pow(re, 0.9));
        return 0.25 / (x * x);
    }

    public static double ValveLoss(Valve v, double q)
    {
        if (v.Kind == ValveKind.Check && q < 0) return ClosedResistance * q;
        double opening = Math.Clamp(v.Opening, 0, 1);
        if (opening < 1e-3) return ClosedResistance * q;
        double area = Math.PI * v.Diameter * v.Diameter / 4;
        double vel = q / area;
        // Característica cuadrática simplificada: K crece con 1/apertura².
        double k = Math.Max(v.OpenLoss, 0.01) / (opening * opening);
        return k * vel * Math.Abs(vel) / (2 * Gravity);
    }

    public static double PumpLoss(Pump b, double q)
    {
        double s = b.RelativeSpeed;
        if (s < 0.01) return ClosedResistance * q;            // parada: el antirretorno cierra
        if (q < 0) return -s * s * b.ShutoffHead + ClosedResistance * q;
        return -b.HeadAt(q, s);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);
}
