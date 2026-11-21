using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Banco.Mesa;

/// <summary>Qué clase de borne es: decide con qué se puede unir.</summary>
public enum PortKind
{
    /// <summary>Entrada de señal (bobina, entrada del PLC, consigna).</summary>
    SignalIn,
    /// <summary>Salida de señal (contacto, salida del PLC, transmisor).</summary>
    SignalOut,
    /// <summary>Acoplamiento de potencia esfuerzo–flujo: cable trifásico, eje, tubo de aire.</summary>
    Power,
    /// <summary>Boca de agua de la red hidráulica.</summary>
    Pipe,
    /// <summary>Toma de un transmisor hidráulico: se une a la pieza que mide.</summary>
    Tap,
    /// <summary>Punta de prueba de un instrumento: se une a cualquier borne.</summary>
    Probe,
}

public enum PortSide { Left, Right, Top, Bottom }

public enum IssueLevel { Error, Warning, Info }

/// <summary>Un aviso de la mesa: qué pasa, dónde, y cómo arreglarlo, en palabras sencillas.</summary>
public sealed record BenchIssue(IssueLevel Level, string? PartId, string Message, string? Hint = null)
{
    public string Icon => Level switch { IssueLevel.Error => "⛔", IssueLevel.Warning => "⚠", _ => "ℹ" };
}

/// <summary>Un borne de una pieza del catálogo.</summary>
/// <param name="IfMissing">Si no está conectado, qué se avisa (null = puede quedar libre).</param>
public sealed record PortDef(
    string Name,
    PortKind Kind,
    string? Label = null,
    PowerDomain Domain = PowerDomain.Electrical3Ph,
    Causality Causality = Causality.ImposesEffort,
    PortSide? Side = null,
    string? IfMissing = null,
    IssueLevel MissingLevel = IssueLevel.Warning,
    bool Analog = false)
{
    public PortSide EffectiveSide => Side ?? Kind switch
    {
        PortKind.SignalIn or PortKind.Probe or PortKind.Tap => PortSide.Left,
        PortKind.SignalOut => PortSide.Right,
        _ => PortSide.Bottom,
    };

    public string Text => Label ?? Name;
}

public enum ParamKind { Number, Text, Choice, Program, Bool }

/// <summary>Un parámetro editable de una pieza. El valor por defecto va como texto (números con punto).</summary>
public sealed record ParamDef(string Key, string Label, string Unit, string Default, ParamKind Kind = ParamKind.Number,
    IReadOnlyList<string>? Options = null, string? Help = null);

/// <summary>Estado visual de una pieza durante la simulación.</summary>
public enum PartState { Idle, On, Warning, Fault }

/// <summary>Lo que la mesa dibuja de una pieza en marcha.</summary>
/// <param name="Fill">0–1 para dibujar un nivel (tanque, depósito).</param>
/// <param name="Spin">Velocidad relativa (1 = nominal) para hacer girar el símbolo.</param>
public sealed record PartLook(IReadOnlyList<string> Lines, PartState State, double? Fill = null, double? Spin = null, string? Color = null);

public enum LiveKind { Momentary, Toggle, Slider, Button }

/// <summary>Un mando que el usuario toca durante la simulación (pulsador, deslizador, rearme…).</summary>
/// <param name="Set">Se ejecuta en el hilo de simulación.</param>
public sealed record LiveControl(string Name, LiveKind Kind, Action<double> Set, Func<double>? Get = null,
    double Min = 0, double Max = 1, string Unit = "");

/// <summary>Lee de una instantánea los valores de una pieza concreta.</summary>
public sealed class PartReader(ComponentState? state, string prefix)
{
    public static PartReader Empty { get; } = new(null, "");

    public bool Has => state is not null;

    public object? Raw(string key) =>
        state is not null && state.Values.TryGetValue(prefix + key, out var v) ? v : null;

    public double D(string key, double fallback = 0) => Raw(key) switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        IConvertible c and not string => c.ToDouble(null),
        _ => fallback,
    };

    public bool B(string key) => Raw(key) is true || (Raw(key) is double d && d > 0.5);

    public string S(string key, string fallback = "") => Raw(key)?.ToString() ?? fallback;
}

/// <summary>Una pieza ya creada para simular: su componente, sus bornes reales y lo que muestra.</summary>
public sealed class BuiltPart
{
    public required BenchPart Part { get; init; }
    public required PartType Type { get; init; }
    /// <summary>Lo que se añade al planificador (null en piezas hidráulicas: las resuelve su red).</summary>
    public ISimComponent? Component { get; set; }
    /// <summary>De dónde salen las instantáneas (la red, para las piezas hidráulicas).</summary>
    public ISimComponent? SnapshotSource { get; set; }
    /// <summary>Prefijo de sus claves en la instantánea de <see cref="SnapshotSource"/> ("P1." dentro de la red).</summary>
    public string SnapshotPrefix { get; set; } = "";
    /// <summary>Objeto del módulo (motor, PLC, pieza hidráulica…), para mandos y avisos.</summary>
    public object? Model { get; set; }
    public Func<string, ISignalPort?> Signal { get; set; } = _ => null;
    public Func<string, IPowerPort?> Power { get; set; } = _ => null;
    public Action? AfterReset { get; set; }
    public List<LiveControl> Controls { get; } = [];
    public Func<PartReader, PartLook>? Look { get; set; }
    /// <summary>Condiciones vigiladas en marcha (ver <see cref="BenchMonitor"/>).</summary>
    public Func<PartReader, IEnumerable<Watch>>? Watches { get; set; }
}

/// <summary>Una condición de aviso en marcha: si es cierta durante <see cref="Hold"/> segundos, se avisa.</summary>
public sealed record Watch(string Key, bool Active, IssueLevel Level, string Message, string? Hint = null, double Hold = 0);
