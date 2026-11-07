namespace Simulador.Arduino.Runtime;

/// <summary>Características de una placa: número de pines, analógicas, PWM y tamaño de <c>int</c>.</summary>
public sealed record ArduinoBoard(
    string Id,
    string Name,
    string Chip,
    int PinCount,
    int FirstAnalog,
    int AnalogCount,
    IReadOnlySet<int> Pwm,
    IReadOnlySet<int> AnalogOnly,
    int LedBuiltin = 13,
    double Vcc = 5,
    int IntBits = 16)
{
    public bool IsAnalog(int pin) => pin >= FirstAnalog && pin < FirstAnalog + AnalogCount;

    /// <summary>"D13" o "A0".</summary>
    public string PinName(int pin) => IsAnalog(pin) ? $"A{pin - FirstAnalog}" : $"D{pin}";

    public override string ToString() => Name;

    public static ArduinoBoard Uno { get; } = new("arduino-uno", "Arduino Uno R3", "ATmega328P", 20, 14, 6,
        new HashSet<int> { 3, 5, 6, 9, 10, 11 }, new HashSet<int>());

    public static ArduinoBoard Nano { get; } = new("arduino-nano", "Arduino Nano", "ATmega328P", 22, 14, 8,
        new HashSet<int> { 3, 5, 6, 9, 10, 11 }, new HashSet<int> { 20, 21 });

    public static ArduinoBoard Mega { get; } = new("arduino-mega", "Arduino Mega 2560", "ATmega2560", 70, 54, 16,
        new HashSet<int> { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 44, 45, 46 }, new HashSet<int>());

    public static IReadOnlyList<ArduinoBoard> All { get; } = [Uno, Nano, Mega];
}
