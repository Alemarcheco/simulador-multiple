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
    int IntBits = 16,
    string? PinPrefix = null,
    BoardLanguage Language = BoardLanguage.Cpp,
    IReadOnlySet<int>? InputOnlyPins = null)
{
    public bool IsAnalog(int pin) => pin >= FirstAnalog && pin < FirstAnalog + AnalogCount;

    /// <summary>Pines que solo pueden ser entradas (GPIO34-39 del ESP32).</summary>
    public IReadOnlySet<int> InputOnly => InputOnlyPins ?? NoPins;

    public bool IsPython => Language == BoardLanguage.MicroPython;

    /// <summary>"D13" o "A0" en Arduino; "GPIO2" o "GP25" en las placas de MicroPython.</summary>
    public string PinName(int pin) => PinPrefix is not null ? PinPrefix + pin : IsAnalog(pin) ? $"A{pin - FirstAnalog}" : $"D{pin}";

    private static readonly HashSet<int> NoPins = [];

    public override string ToString() => Name;

    public static ArduinoBoard Uno { get; } = new("arduino-uno", "Arduino Uno R3", "ATmega328P", 20, 14, 6,
        new HashSet<int> { 3, 5, 6, 9, 10, 11 }, new HashSet<int>());

    public static ArduinoBoard Nano { get; } = new("arduino-nano", "Arduino Nano", "ATmega328P", 22, 14, 8,
        new HashSet<int> { 3, 5, 6, 9, 10, 11 }, new HashSet<int> { 20, 21 });

    public static ArduinoBoard Mega { get; } = new("arduino-mega", "Arduino Mega 2560", "ATmega2560", 70, 54, 16,
        new HashSet<int> { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 44, 45, 46 }, new HashSet<int>());

    /// <summary>ESP32 DevKit V1 (ESP-WROOM-32): 3,3 V, ADC1 en GPIO32-39, GPIO34-39 solo entrada, LED en GPIO2.</summary>
    public static ArduinoBoard Esp32 { get; } = new("esp32-devkit", "ESP32 DevKit V1", "ESP32-WROOM-32", 40, 32, 8,
        new HashSet<int>(Enumerable.Range(0, 34).Except([6, 7, 8, 9, 10, 11])), new HashSet<int>(),
        LedBuiltin: 2, Vcc: 3.3, IntBits: 32, PinPrefix: "GPIO", Language: BoardLanguage.MicroPython,
        InputOnlyPins: new HashSet<int> { 34, 35, 36, 37, 38, 39 });

    /// <summary>Raspberry Pi Pico (RP2040): 3,3 V, GP0-28, ADC en GP26-28, LED en GP25.</summary>
    public static ArduinoBoard Pico { get; } = new("rpi-pico", "Raspberry Pi Pico", "RP2040", 29, 26, 3,
        new HashSet<int>(Enumerable.Range(0, 29)), new HashSet<int>(),
        LedBuiltin: 25, Vcc: 3.3, IntBits: 32, PinPrefix: "GP", Language: BoardLanguage.MicroPython);

    /// <summary>Placas que se programan en C++ (Arduino).</summary>
    public static IReadOnlyList<ArduinoBoard> All { get; } = [Uno, Nano, Mega];

    /// <summary>Placas que se programan en MicroPython.</summary>
    public static IReadOnlyList<ArduinoBoard> Python { get; } = [Esp32, Pico];

    public static IReadOnlyList<ArduinoBoard> Everything { get; } = [Uno, Nano, Mega, Esp32, Pico];
}

public enum BoardLanguage { Cpp, MicroPython }
