using System.Text.Json.Serialization;
using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>Forma de la escalera en planta.</summary>
public enum StairShape
{
    /// <summary>Un solo tramo recto.</summary>
    Straight,
    /// <summary>Dos tramos a 90° con un descanso cuadrado en la esquina.</summary>
    L,
    /// <summary>Dos tramos paralelos en sentidos opuestos (ida y vuelta) con un descanso de lado a lado.</summary>
    U,
}

/// <summary>Huella (o descanso) en planta con la cota de su cara superior, relativa al nivel.</summary>
public readonly record struct StairStep(Point2[] Outline, double Top, bool IsLanding);

/// <summary>
/// Escalera recta, en L o en U. <paramref name="Start"/> es el centro del borde del primer peldaño (arranque),
/// <paramref name="Angle"/> la dirección de subida del primer tramo en planta (rad), <paramref name="Rise"/> la altura
/// total a salvar. En L y U, <paramref name="TurnLeft"/> indica si el segundo tramo gira a la izquierda (visto al
/// subir) o a la derecha.
/// </summary>
public sealed record Stair(string Id, string LevelId, Point2 Start, double Angle, double Width, double Rise,
    StairShape Shape = StairShape.Straight, bool TurnLeft = true)
{
    /// <summary>Separación entre los dos tramos de una escalera en U (hueco o muro central).</summary>
    public const double UGap = 0.10;

    [JsonIgnore]
    public StairDesign Design => StairCalculator.Design(Rise, width: Width, flights: Shape == StairShape.Straight ? 1 : 2);
    [JsonIgnore]
    public Point2 Direction => new(Math.Cos(Angle), Math.Sin(Angle));

    /// <summary>Contrahuellas del primer tramo (hasta el descanso incluido). En recta, todas.</summary>
    [JsonIgnore]
    public int FirstFlightRisers => Shape == StairShape.Straight ? Design.Risers : (Design.Risers + 1) / 2;

    /// <summary>Huellas y descanso en orden de subida. La última contrahuella llega al piso de arriba.</summary>
    [JsonIgnore]
    public IReadOnlyList<StairStep> Steps
    {
        get
        {
            var d = Design;
            var steps = new List<StairStep>();
            if (d.Risers == 0) return steps;
            var dir = Direction;
            var side = TurnLeft ? dir.Perp() : dir.Perp() * -1;
            int n1 = FirstFlightRisers;
            AddFlight(steps, Start, dir, n1 - 1, 0, d);
            if (Shape == StairShape.Straight) return steps;

            double l1 = (n1 - 1) * d.Tread;
            var corner = Start + dir * l1; // centro del borde del descanso
            var half = dir.Perp() * (Width / 2);
            double landing = n1 * d.Riser;
            int n2 = d.Risers - n1;
            if (Shape == StairShape.L)
            {
                // Descanso cuadrado de ancho × ancho; el segundo tramo sale por el lado del giro.
                var far = corner + dir * Width;
                steps.Add(new([corner - half, far - half, far + half, corner + half], landing, true));
                var start2 = corner + dir * (Width / 2) + side * (Width / 2);
                AddFlight(steps, start2, side, n2 - 1, n1, d);
            }
            else
            {
                // Descanso de lado a lado: cubre los dos tramos y el hueco central; el segundo baja paralelo al primero.
                var far = corner + dir * Width;
                var outer = side * (Width * 1.5 + UGap);
                var inner = side * -(Width / 2);
                steps.Add(new([corner + inner, far + inner, far + outer, corner + outer], landing, true));
                var start2 = corner + side * (Width + UGap);
                AddFlight(steps, start2, dir * -1, n2 - 1, n1, d);
            }
            return steps;
        }
    }

    private void AddFlight(List<StairStep> steps, Point2 start, Point2 dir, int treads, int baseRisers, StairDesign d)
    {
        var half = dir.Perp() * (Width / 2);
        for (int k = 0; k < treads; k++)
        {
            var a = start + dir * (k * d.Tread);
            var b = start + dir * ((k + 1) * d.Tread);
            steps.Add(new([a - half, b - half, b + half, a + half], (baseRisers + k + 1) * d.Riser, false));
        }
    }

    /// <summary>Contorno en planta de cada parte (tramos y descanso), para dibujar y seleccionar.</summary>
    [JsonIgnore]
    public IReadOnlyList<IReadOnlyList<Point2>> Parts
    {
        get
        {
            if (Shape == StairShape.Straight) return [Outline];
            var parts = new List<IReadOnlyList<Point2>>();
            var steps = Steps;
            // Cada tramo se resume en el rectángulo de sus huellas; el descanso va tal cual.
            var flight = new List<StairStep>();
            void Flush()
            {
                if (flight.Count == 0) return;
                var f = flight[0].Outline;
                var l = flight[^1].Outline;
                parts.Add([f[0], l[1], l[2], f[3]]);
                flight.Clear();
            }
            foreach (var s in steps)
            {
                if (s.IsLanding) { Flush(); parts.Add(s.Outline); }
                else flight.Add(s);
            }
            Flush();
            return parts;
        }
    }

    /// <summary>
    /// Contorno exterior en planta. En recta es el rectángulo de las huellas; en L y U, el rectángulo que envuelve
    /// los tramos y el descanso (lo que ocupa la caja de escalera).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<Point2> Outline
    {
        get
        {
            var d = Direction;
            var n = d.Perp() * (Width / 2);
            if (Shape == StairShape.Straight)
            {
                var end = Start + d * Design.RunLength;
                return [Start - n, end - n, end + n, Start + n];
            }
            // Envolvente en ejes de la escalera (u a lo largo de la subida, v a la izquierda).
            var pts = Steps.SelectMany(s => s.Outline).ToList();
            if (pts.Count == 0) return [Start - n, Start + n, Start + n, Start - n];
            var perp = d.Perp();
            double U(Point2 p) => Point2.Dot(p - Start, d);
            double V(Point2 p) => Point2.Dot(p - Start, perp);
            double u0 = pts.Min(U), u1 = pts.Max(U), v0 = pts.Min(V), v1 = pts.Max(V);
            Point2 At(double u, double v) => Start + d * u + perp * v;
            return [At(u0, v0), At(u1, v0), At(u1, v1), At(u0, v1)];
        }
    }

    /// <summary>Tamaño de la caja de escalera: largo en la dirección de subida × ancho total.</summary>
    [JsonIgnore]
    public (double Length, double Breadth) Footprint
    {
        get
        {
            var o = Outline;
            return (o[0].DistanceTo(o[1]), o[1].DistanceTo(o[2]));
        }
    }

    /// <summary>Punto donde se llega arriba (centro del borde de la última contrahuella).</summary>
    [JsonIgnore]
    public Point2 Arrival
    {
        get
        {
            var steps = Steps;
            if (steps.Count == 0) return Start;
            var last = steps[^1].Outline;
            return (last[1] + last[2]) * 0.5;
        }
    }

    public static string ShapeName(StairShape shape) => shape switch
    {
        StairShape.L => "En L (con descanso)",
        StairShape.U => "En U (ida y vuelta)",
        _ => "Recta",
    };
}

/// <summary>Resultado del cálculo de una escalera.</summary>
/// <param name="Risers">Número de contrahuellas (escalones).</param>
/// <param name="Riser">Altura de cada contrahuella (m).</param>
/// <param name="Tread">Profundidad de cada huella (m).</param>
/// <param name="RunLength">Longitud en planta: (contrahuellas − 1) × huella (m).</param>
/// <param name="Blondel">2·contrahuella + huella (m); cómodo entre 0,60 y 0,65.</param>
public sealed record StairDesign(int Risers, double Riser, double Tread, double RunLength, double Blondel, IReadOnlyList<string> Warnings);

public static class StairCalculator
{
    public const double MaxRiser = 0.185;
    public const double MinTread = 0.25;
    public const double BlondelMin = 0.60;
    public const double BlondelMax = 0.65;
    public const double MinWidth = 0.80;

    /// <summary>
    /// Calcula los peldaños para salvar <paramref name="rise"/> metros buscando contrahuellas de unos
    /// <paramref name="targetRiser"/> m y la huella que da la regla de Blondel (2C + H ≈ 0,63 m).
    /// </summary>
    public static StairDesign Design(double rise, double targetRiser = 0.175, double? tread = null, double width = 1.0, int flights = 1)
    {
        if (rise <= 0) return new StairDesign(0, 0, 0, 0, 0, ["La altura a salvar debe ser mayor que cero."]);

        int n = Math.Max(1, (int)Math.Ceiling(rise / targetRiser - 1e-9));
        double r = rise / n;
        double t = tread ?? Math.Clamp(0.63 - 2 * r, MinTread, 0.32);
        double blondel = 2 * r + t;

        var warnings = new List<string>();
        if (r > MaxRiser) warnings.Add($"Contrahuella de {r * 100:0.#} cm: supera {MaxRiser * 100:0.#} cm, resulta incómoda.");
        if (t < MinTread) warnings.Add($"Huella de {t * 100:0.#} cm: menor que {MinTread * 100:0} cm.");
        if (blondel < BlondelMin - 1e-9 || blondel > BlondelMax + 1e-9)
            warnings.Add($"2C + H = {blondel * 100:0.#} cm: fuera del rango cómodo de {BlondelMin * 100:0}–{BlondelMax * 100:0} cm.");
        if (width < MinWidth) warnings.Add($"Ancho de {width:0.00} m: menor que {MinWidth:0.00} m.");
        int perFlight = (n + Math.Max(1, flights) - 1) / Math.Max(1, flights);
        if (perFlight > 18)
            warnings.Add(flights == 1
                ? $"{n} escalones seguidos: conviene un descanso intermedio (máx. ~18 por tramo); prueba la forma en L o en U."
                : $"{perFlight} escalones por tramo: supera ~18; conviene otro descanso.");

        return new StairDesign(n, r, t, (n - 1) * t, blondel, warnings);
    }
}
