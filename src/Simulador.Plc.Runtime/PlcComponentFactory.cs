using Simulador.Core.CoSim;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Plc.Runtime;

/// <summary>Catálogo de PLC para el banco de pruebas combinado: un tipo por perfil de marca.</summary>
public sealed class PlcComponentFactory : ISimComponentFactory
{
    public string ModuleId => "plc";

    public IEnumerable<ComponentDescriptor> Catalog => PlcProfiles.All.Select(p =>
    {
        var cpu = new PlcCpu("PLC", p);
        var ports = cpu.Signals
            .Select(s => new PortDescriptor(s.Id["PLC.".Length..], s.Kind.ToString(), s.Unit.ToString()))
            .ToList();
        return new ComponentDescriptor(
            p.Id,
            p.DisplayName,
            "🎛",
            ports,
            [
                new ParameterDescriptor("Programa (ST)", "", ""),
                new ParameterDescriptor("Tiempo de ciclo", "ms", p.DefaultCycleTime.TotalMilliseconds),
            ]);
    });

    public ISimComponent Create(string typeId, IDictionary<string, object> parameters)
    {
        string id = parameters.TryGetValue("Id", out var i) && i is string s ? s : "PLC1";
        var cpu = new PlcCpu(id, PlcProfiles.Get(typeId));
        if (parameters.TryGetValue("Tiempo de ciclo", out var ct) && ct is IConvertible c)
            cpu.CycleTime = TimeSpan.FromMilliseconds(c.ToDouble(null));
        if (parameters.TryGetValue("Programa (ST)", out var prog) && prog is string src && !string.IsNullOrWhiteSpace(src))
        {
            cpu.LoadStructuredText(src);
            cpu.Start();
        }
        return cpu;
    }
}
