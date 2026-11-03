namespace Simulador.Core.Signals;

/// <summary>
/// Bus de señales básico: propaga el valor de cada salida a sus entradas conectadas.
/// Valida tipo, unidad y nivel nominal; las discrepancias se avisan pero no impiden conectar.
/// </summary>
public sealed class SignalBus : ISignalBus
{
    private readonly Dictionary<string, ISignalPort> _ports = new(StringComparer.Ordinal);
    private readonly Dictionary<ISignalPort, List<ISignalPort>> _links = [];

    public IReadOnlyCollection<ISignalPort> Ports => _ports.Values;

    public void Register(ISignalPort port)
    {
        if (!_ports.TryAdd(port.Id, port) && !ReferenceEquals(_ports[port.Id], port))
            throw new InvalidOperationException($"Ya existe una señal con Id '{port.Id}'.");
    }

    public ConnectionResult Connect(ISignalPort source, ISignalPort target)
    {
        Register(source);
        Register(target);
        var warnings = Validate(source, target);

        if (!_links.TryGetValue(source, out var targets))
        {
            targets = [];
            _links[source] = targets;
            source.Changed += Propagate;
        }
        if (!targets.Contains(target))
            targets.Add(target);

        target.Value = source.Value;
        return new ConnectionResult(true, warnings);
    }

    public void Disconnect(ISignalPort source, ISignalPort target)
    {
        if (!_links.TryGetValue(source, out var targets)) return;
        targets.Remove(target);
        if (targets.Count == 0)
        {
            _links.Remove(source);
            source.Changed -= Propagate;
        }
    }

    private void Propagate(object? sender, double value)
    {
        if (sender is ISignalPort source && _links.TryGetValue(source, out var targets))
            foreach (var t in targets)
                t.Value = value;
    }

    private static List<string> Validate(ISignalPort source, ISignalPort target)
    {
        var w = new List<string>();
        bool sourceIsOutput = source.Kind is SignalKind.DO or SignalKind.AO;
        bool targetIsInput = target.Kind is SignalKind.DI or SignalKind.AI;
        if (!sourceIsOutput) w.Add($"{source.Id} no es una salida.");
        if (!targetIsInput) w.Add($"{target.Id} no es una entrada.");
        bool digital = source.Kind == SignalKind.DO || target.Kind == SignalKind.DI;
        if (!digital && source.Unit != target.Unit)
            w.Add($"Unidades distintas: {source.Unit} → {target.Unit}.");
        if (source.NominalLevel > 0 && target.NominalLevel > 0 &&
            Math.Abs(source.NominalLevel - target.NominalLevel) > 1e-9)
            w.Add($"Nivel nominal distinto: {source.NominalLevel} → {target.NominalLevel}.");
        if (source.IsAc != target.IsAc)
            w.Add("Mezcla de señal alterna y continua.");
        return w;
    }
}
