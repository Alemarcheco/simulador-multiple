using System.Globalization;
using System.Text.RegularExpressions;
using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Schematic;

namespace Simulador.Circuitos.Simulation;

/// <summary>Simula el esquema con sondas de corriente y prepara la animación.</summary>
public static partial class LiveRun
{
    public sealed record Outcome(FlowAnimation? Animation, SimulationResult? Result, SpiceNetlist Netlist, AnalysisSettings Settings, string Message);

    /// <summary>
    /// Análisis que mejor muestra el circuito: el transitorio recomendado del esquema; si hay un generador senoidal,
    /// tres periodos de la frecuencia más baja; si no, el punto de trabajo.
    /// </summary>
    public static AnalysisSettings Suggest(SchematicDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.TranStop))
            return new AnalysisSettings { Kind = AnalysisKind.Transient, StopTime = doc.TranStop!, Step = doc.TranStep ?? Fmt((EngineeringFormat.ParseSpice(doc.TranStop!) ?? 1) / 1000) };
        double? f = doc.Symbols
            .Select(s => SinFrequency().Match(s.Value))
            .Where(m => m.Success)
            .Select(m => EngineeringFormat.ParseSpice(m.Groups[1].Value))
            .Where(v => v > 0)
            .DefaultIfEmpty(null)
            .Min();
        if (f is { } freq)
        {
            double stop = 3 / freq;
            return new AnalysisSettings { Kind = AnalysisKind.Transient, StopTime = Fmt(stop), Step = Fmt(stop / 600) };
        }
        return new AnalysisSettings { Kind = AnalysisKind.OperatingPoint };
    }

    public static Outcome Run(SchematicDocument doc, ComponentCatalog catalog, AnalysisSettings settings)
    {
        var netlist = SpiceNetlister.Build(doc, catalog, settings.ToSpice(), currentProbes: true);
        var result = NgSpice.Run(netlist.Text);
        if (!result.Ok)
            return new Outcome(null, result, netlist, settings, "La simulación falló: " + (result.Errors.FirstOrDefault() ?? "sin datos") +
                (netlist.Warnings.Count > 0 ? " · " + netlist.Warnings[0] : ""));
        if (settings.Kind == AnalysisKind.Ac)
            return new Outcome(null, result, netlist, settings, "La respuesta en frecuencia no se anima.");
        var anim = FlowAnimation.From(doc, netlist.Connectivity, result);
        return new Outcome(anim, result, netlist, settings, anim is null ? "No hay corrientes que mostrar." :
            settings.Kind == AnalysisKind.Transient ? "Corriente animada: transitorio de " + settings.StopTime + "s." : "Corriente animada en el punto de trabajo.");
    }

    private static string Fmt(double v) => v.ToString("0.###e0", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"SIN\s*\(\s*\S+\s+\S+\s+([^\s\)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SinFrequency();
}
