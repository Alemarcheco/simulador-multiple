using System.Globalization;

namespace Simulador.Circuitos.Simulation;

public enum AnalysisKind { OperatingPoint, Transient, Ac }

/// <summary>Configuración del análisis. Los valores admiten sufijos SPICE (1m, 10u, 1k, 1meg).</summary>
public sealed record AnalysisSettings
{
    public AnalysisKind Kind { get; init; } = AnalysisKind.OperatingPoint;
    public string StopTime { get; init; } = "10m";
    public string Step { get; init; } = "10u";
    public string StartFrequency { get; init; } = "10";
    public string StopFrequency { get; init; } = "1meg";
    public int PointsPerDecade { get; init; } = 20;

    public IEnumerable<string> ToSpice() => Kind switch
    {
        AnalysisKind.Transient => [$".tran {Step} {StopTime}"],
        AnalysisKind.Ac => [$".ac dec {PointsPerDecade} {StartFrequency} {StopFrequency}"],
        _ => [".op"],
    };
}

/// <summary>Formato de ingeniería: 4.7 kΩ, 12.3 mV, 2.2 µs…</summary>
public static class EngineeringFormat
{
    private static readonly (double Factor, string Prefix)[] Prefixes =
    [
        (1e12, "T"), (1e9, "G"), (1e6, "M"), (1e3, "k"), (1, ""), (1e-3, "m"), (1e-6, "µ"), (1e-9, "n"), (1e-12, "p"), (1e-15, "f"),
    ];

    public static string Format(double value, string unit, int digits = 3)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "—";
        if (value == 0) return "0 " + unit;
        double abs = Math.Abs(value);
        foreach (var (factor, prefix) in Prefixes)
            if (abs >= factor * 0.9995)
                return (value / factor).ToString("G" + digits, CultureInfo.CurrentCulture) + " " + prefix + unit;
        return value.ToString("G" + digits, CultureInfo.CurrentCulture) + " " + unit;
    }

    /// <summary>Lee un número con sufijo SPICE ("4.7k", "100n", "1meg"). Devuelve null si no se entiende.</summary>
    public static double? ParseSpice(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.Length == 0) return null;
        int end = 0;
        while (end < t.Length && (char.IsDigit(t[end]) || t[end] is '.' or '-' or '+' or 'e') &&
               !(t[end] == 'e' && (end + 1 >= t.Length || !(char.IsDigit(t[end + 1]) || t[end + 1] is '-' or '+'))))
            end++;
        if (!double.TryParse(t[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
        var suffix = t[end..];
        double mult = suffix.StartsWith("meg") ? 1e6
            : suffix.StartsWith("mil") ? 25.4e-6
            : suffix.Length == 0 ? 1
            : suffix[0] switch { 't' => 1e12, 'g' => 1e9, 'k' => 1e3, 'm' => 1e-3, 'u' or 'µ' => 1e-6, 'n' => 1e-9, 'p' => 1e-12, 'f' => 1e-15, _ => 1 };
        return number * mult;
    }
}
