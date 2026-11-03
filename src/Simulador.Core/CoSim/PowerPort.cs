// Contrato: /mnt/project-files/contratos/co-simulacion.md
using Simulador.Core.Signals;

namespace Simulador.Core.CoSim;

/// <summary>Implementación sencilla de <see cref="IPowerPort"/> para módulos y pruebas.</summary>
public sealed class PowerPort(string id, PowerDomain domain, Causality causality) : IPowerPort
{
    public string Id { get; } = id;
    public PowerDomain Domain { get; } = domain;
    public Causality Causality { get; } = causality;
    /// <summary>V, N·m, N, Pa, K.</summary>
    public double Effort { get; set; }
    /// <summary>A, rad/s, m/s, m³/s, W.</summary>
    public double Flow { get; set; }

    public override string ToString() => $"{Id} [{Domain}] e={Effort} f={Flow}";
}
