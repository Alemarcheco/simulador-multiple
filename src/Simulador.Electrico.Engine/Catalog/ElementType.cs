namespace Simulador.Electrico.Engine.Catalog;

/// <summary>Comportamiento eléctrico de un tipo de elemento. El simulador decide qué hace cada uno a partir de esto.</summary>
public enum Behavior
{
    // Fuentes
    SourceAc1, SourceDc, Source3Ph,
    // Mando manual y captadores
    PushNO, PushNC, EmergencyStop, Switch, TwoWay, Intermediate, Selector3, LimitNO, LimitNC, SensorNO, SensorNC,
    // Contactos que siguen a un aparato (misma designación)
    ContactNO, ContactNC, TimedContactNO, TimedContactNC, MainPoles,
    // Bobinas
    CoilContactor, CoilRelay, CoilTimerOn, CoilTimerOff, CoilFlasher, CoilImpulse,
    // Protecciones
    Fuse, Breaker, MotorBreaker, ThermalRelay, Rcd,
    // Receptores
    Lamp, Pilot, Bell, Heater, Solenoid, Motor3, Motor6,
    // Instrumentos y conexión
    Voltmeter, Ammeter, Junction,
}

public sealed record TerminalDef(string Id, int X, int Y, string Label);

/// <summary>Parámetro numérico. Si <see cref="Options"/> no es nulo, el valor es el índice de la opción.</summary>
public sealed record ParamDef(string Key, string Name, string Unit, double Default, double Min, double Max, string[]? Options = null);

public sealed record ElementType(
    string Id,
    string Name,
    string Category,
    Behavior Behavior,
    string TagPrefix,
    IReadOnlyList<TerminalDef> Terminals,
    IReadOnlyList<ParamDef> Params,
    string Description,
    string DefaultTerminalText = "")
{
    public double Default(string key) => Params.FirstOrDefault(p => p.Key == key)?.Default ?? 0;

    public TerminalDef? Terminal(string id) => Terminals.FirstOrDefault(t => t.Id == id);

    public bool IsManual => Behavior is Behavior.PushNO or Behavior.PushNC or Behavior.EmergencyStop or Behavior.Switch
        or Behavior.TwoWay or Behavior.Intermediate or Behavior.Selector3 or Behavior.LimitNO or Behavior.LimitNC
        or Behavior.SensorNO or Behavior.SensorNC;

    public bool IsMomentary => Behavior is Behavior.PushNO or Behavior.PushNC;

    public bool IsCoil => Behavior is Behavior.CoilContactor or Behavior.CoilRelay or Behavior.CoilTimerOn
        or Behavior.CoilTimerOff or Behavior.CoilFlasher or Behavior.CoilImpulse;

    public bool IsFollowerContact => Behavior is Behavior.ContactNO or Behavior.ContactNC or Behavior.TimedContactNO
        or Behavior.TimedContactNC or Behavior.MainPoles;

    public bool IsSource => Behavior is Behavior.SourceAc1 or Behavior.SourceDc or Behavior.Source3Ph;

    /// <summary>Número de polos de paso (pares de bornes 1-2, 3-4, 5-6…) para aparatos de potencia.</summary>
    public int PoleCount => Behavior is Behavior.Fuse or Behavior.Breaker or Behavior.MotorBreaker or Behavior.ThermalRelay
        or Behavior.Rcd or Behavior.MainPoles ? Terminals.Count / 2 : 0;
}
