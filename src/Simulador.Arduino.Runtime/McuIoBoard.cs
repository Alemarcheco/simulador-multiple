using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Arduino.Runtime;

/// <summary>
/// Placa de E/S industrial para un microcontrolador: 8 entradas optoacopladas de 24 V y 8 relés. Así un Arduino, un
/// ESP32 o una Raspberry Pi Pico se puede montar en un cuadro eléctrico como si fuera un PLC pequeño.
/// <para>Entrada INn con 24 V → su pin lee HIGH (1). Pin de salida en HIGH → el relé Kn cierra su contacto.</para>
/// </summary>
public sealed class McuIoBoard : ISimComponent, IDisposable
{
    public const int Channels = 8;
    /// <summary>Entradas de 0-10 V (AI1, AI2): un divisor en la placa las reduce a 0-Vcc para el ADC.</summary>
    public const int AnalogChannels = 2;
    /// <summary>Tensión a partir de la cual el optoacoplador conduce (como una entrada de PLC de tipo 1).</summary>
    public const double InputOnVolts = 15;

    private readonly SignalPort[] _in;
    private readonly SignalPort[] _out;
    private readonly SignalPort[] _ain;

    public McuIoBoard(string id, ArduinoBoard board)
    {
        Id = id;
        Mcu = new ArduinoMcu(id + ".MCU", board);
        (InputPins, OutputPins) = Mapping(board);
        _in = Enumerable.Range(0, Channels).Select(i => new SignalPort($"{id}.IN{i + 1}", SignalKind.DI, SignalUnit.Volt, 24)).ToArray();
        _out = Enumerable.Range(0, Channels).Select(i => new SignalPort($"{id}.K{i + 1}", SignalKind.DO, SignalUnit.Bool, 24)).ToArray();
        _ain = Enumerable.Range(0, AnalogChannels).Select(i => new SignalPort($"{id}.AI{i + 1}", SignalKind.AI, SignalUnit.Volt, 10)).ToArray();
        AnalogPins = AnalogMapping(board);
        Signals = [.. _in, .. _out, .. _ain];
    }

    public string Id { get; }
    public string ModuleId => "plc";
    public ArduinoMcu Mcu { get; }
    public IReadOnlyList<int> InputPins { get; }
    public IReadOnlyList<int> OutputPins { get; }
    public IReadOnlyList<int> AnalogPins { get; }

    /// <summary>Pines del ADC unidos a AI1 y AI2.</summary>
    public static int[] AnalogMapping(ArduinoBoard board)
    {
        if (board == ArduinoBoard.Esp32) return [36, 39];
        if (board == ArduinoBoard.Pico) return [26, 27];
        if (board == ArduinoBoard.Mega) return [54, 55];
        if (board == ArduinoBoard.Nano) return [20, 21]; // A6 y A7, solo analógicas
        return [18, 19]; // Uno: A4 y A5
    }
    public IReadOnlyList<ISignalPort> Signals { get; }
    public IReadOnlyList<IPowerPort> PowerPorts { get; } = [];
    public int StepMultiple => 1;
    public SimPhase Phase => SimPhase.Logic;

    /// <summary>Pines de la placa unidos a IN1-IN8 y a los relés K1-K8.</summary>
    public static (int[] Inputs, int[] Outputs) Mapping(ArduinoBoard board)
    {
        if (board == ArduinoBoard.Esp32) return ([34, 35, 32, 33, 25, 26, 27, 14], [23, 22, 21, 19, 18, 5, 17, 16]);
        if (board == ArduinoBoard.Pico) return ([0, 1, 2, 3, 4, 5, 6, 7], [8, 9, 10, 11, 12, 13, 14, 15]);
        if (board == ArduinoBoard.Mega) return ([22, 23, 24, 25, 26, 27, 28, 29], [30, 31, 32, 33, 34, 35, 36, 37]);
        // Uno y Nano: D2-D9 entradas; D10-D13 y A0-A3 (usadas como digitales) salidas.
        return ([2, 3, 4, 5, 6, 7, 8, 9], [10, 11, 12, 13, 14, 15, 16, 17]);
    }

    /// <summary>Tabla legible "IN1 = D2 … K8 = A3" para las propiedades del esquema.</summary>
    public static string MappingText(ArduinoBoard board)
    {
        var (i, o) = Mapping(board);
        return "Entradas: " + string.Join(", ", i.Select((p, k) => $"IN{k + 1}={board.PinName(p)}"))
            + ". Relés: " + string.Join(", ", o.Select((p, k) => $"K{k + 1}={board.PinName(p)}"))
            + ". Analógicas 0-10 V: " + string.Join(", ", AnalogMapping(board).Select((p, k) => $"AI{k + 1}={board.PinName(p)}")) + " (10 V = " + board.Vcc.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " V en el pin).";
    }

    /// <summary>Programa de partida: marcha/paro de un motor con IN1 (marcha), IN2 (paro NC) y el relé K1.</summary>
    public static string Template(ArduinoBoard board)
    {
        var (i, o) = Mapping(board);
        if (board.IsPython)
            return $$"""
                # Placa de E/S: IN1-IN8 (24 V -> 1) y relés K1-K8 (1 -> contacto cerrado)
                # Marcha/paro con autorretención: IN1 = marcha (NA), IN2 = paro (NC), K1 = contactor del motor
                from machine import Pin
                import time

                marcha = Pin({{i[0]}}, Pin.IN)
                paro = Pin({{i[1]}}, Pin.IN)
                motor = Pin({{o[0]}}, Pin.OUT)

                while True:
                    funcionando = (marcha.value() or motor.value()) and paro.value()
                    motor.value(funcionando)
                    time.sleep_ms(10)
                """;
        return $$"""
            // Placa de E/S: IN1-IN8 (24 V -> HIGH) y relés K1-K8 (HIGH -> contacto cerrado)
            // Marcha/paro con autorretención: IN1 = marcha (NA), IN2 = paro (NC), K1 = contactor del motor
            const int MARCHA = {{board.PinName(i[0]).TrimStart('D')}};
            const int PARO = {{board.PinName(i[1]).TrimStart('D')}};
            const int MOTOR = {{(board.IsAnalog(o[0]) ? board.PinName(o[0]) : o[0].ToString())}};

            void setup() {
              pinMode(MARCHA, INPUT);
              pinMode(PARO, INPUT);
              pinMode(MOTOR, OUTPUT);
            }

            void loop() {
              bool funcionando = (digitalRead(MARCHA) == HIGH || digitalRead(MOTOR) == HIGH) && digitalRead(PARO) == HIGH;
              digitalWrite(MOTOR, funcionando ? HIGH : LOW);
            }
            """;
    }

    /// <summary>Carga el programa (C++ o MicroPython según la placa) y arranca. Lanza SketchCompileException.</summary>
    public void Load(string source)
    {
        Mcu.Load(source);
        Mcu.Start();
    }

    public void Initialize(SimContext context)
    {
        foreach (var p in Signals) context.Bus.Register(p);
    }

    public void DoStep(TimeSpan now, TimeSpan dt)
    {
        var pins = Mcu.Pins;
        double vcc = Mcu.Board.Vcc;
        // El optoacoplador da nivel alto o bajo con fuerza: el pin nunca queda al aire.
        for (int k = 0; k < Channels; k++) pins[InputPins[k]].Value = _in[k].Value >= InputOnVolts ? vcc : 0;
        for (int k = 0; k < AnalogChannels; k++) pins[AnalogPins[k]].Value = Math.Clamp(_ain[k].Value, 0, 10) / 10 * vcc;
        Mcu.Advance(now + dt);
        for (int k = 0; k < Channels; k++)
        {
            var p = pins[OutputPins[k]];
            bool on = (Mcu.State == McuState.Running || Mcu.ProgramEnded) && p.IsOutput && p.OutputHigh;
            _out[k].Value = on ? 1 : 0;
        }
    }

    public ComponentState Snapshot() => Mcu.Snapshot();

    public void Reset()
    {
        Mcu.Reset();
        foreach (var o in _out) o.Value = 0;
    }

    public void Dispose() => Mcu.Dispose();
}
