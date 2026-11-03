using System.Text.Json.Serialization;
using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>
/// Escalera recta. <paramref name="Start"/> es el centro del primer peldaño (arranque), <paramref name="Angle"/> la
/// dirección de subida en planta (rad), <paramref name="Rise"/> la altura total a salvar.
/// </summary>
public sealed record Stair(string Id, string LevelId, Point2 Start, double Angle, double Width, double Rise)
{
    [JsonIgnore]
    public StairDesign Design => StairCalculator.Design(Rise, width: Width);
    [JsonIgnore]
    public Point2 Direction => new(Math.Cos(Angle), Math.Sin(Angle));

    /// <summary>Contorno en planta (rectángulo de ancho × longitud de huellas).</summary>
    [JsonIgnore]
    public IReadOnlyList<Point2> Outline
    {
        get
        {
            var d = Direction;
            var n = d.Perp() * (Width / 2);
            var end = Start + d * Design.RunLength;
            return [Start - n, end - n, end + n, Start + n];
        }
    }
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
    public static StairDesign Design(double rise, double targetRiser = 0.175, double? tread = null, double width = 1.0)
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
        if (n > 18) warnings.Add($"{n} escalones seguidos: conviene un descanso intermedio (máx. ~18 por tramo).");

        return new StairDesign(n, r, t, (n - 1) * t, blondel, warnings);
    }
}
