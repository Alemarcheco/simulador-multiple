namespace Simulador.Core.Signals;

/// <summary>Resultado de conectar dos señales. Los avisos no bloquean (para poder simular errores de cableado).</summary>
public sealed record ConnectionResult(bool Connected, IReadOnlyList<string> Warnings)
{
    public static ConnectionResult Ok { get; } = new(true, []);
}

/// <summary>Registro de conexiones entre señales de distintos módulos.</summary>
public interface ISignalBus
{
    IReadOnlyCollection<ISignalPort> Ports { get; }
    void Register(ISignalPort port);
    /// <summary>Conecta <paramref name="source"/> (salida) con <paramref name="target"/> (entrada).</summary>
    ConnectionResult Connect(ISignalPort source, ISignalPort target);
    void Disconnect(ISignalPort source, ISignalPort target);
}

/// <summary>Reloj de simulación de paso fijo (1 ms por defecto). Nunca usa el reloj de pared.</summary>
public interface ISimulationClock
{
    TimeSpan Now { get; }
    TimeSpan Step { get; }
    event EventHandler<TimeSpan>? Tick;
    /// <summary>Pide que el reloj se detenga exactamente en <paramref name="time"/> (evento discreto).</summary>
    void RequestEventAt(TimeSpan time);
}
