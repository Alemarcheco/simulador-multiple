using System.Globalization;
using System.Text;
using Simulador.Arduino.Runtime.Sketch;
using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Arduino.Runtime;

public enum McuState { Stopped, Running, Fault }

/// <summary>Variable global del sketch tal como se ve en el inspector.</summary>
public sealed record SketchVariable(string Name, string Type, string Value);

/// <summary>
/// Placa Arduino simulada: interpreta el sketch (setup una vez, loop sin fin) en tiempo simulado. El sketch corre en
/// su propio hilo pero sincronizado paso a paso con <see cref="Advance"/>, así que el resultado es determinista:
/// <c>delay(1000)</c> dura exactamente un segundo de simulación y cada sentencia consume su tiempo de CPU.
/// </summary>
public sealed class ArduinoMcu : ISimComponent, IDisposable
{
    private const double HighThreshold = 0.6, LowThreshold = 0.3; // fracciones de Vcc (ATmega328P: VIH 0,6·Vcc, VIL 0,3·Vcc)

    private readonly PinPort[] _pins;
    private readonly bool[] _lastRead;
    private readonly StringBuilder _serialOut = new();
    private readonly Queue<char> _serialIn = new();
    private readonly object _serialLock = new();
    private readonly List<string> _diagnostics = [];
    private readonly Dictionary<int, long> _toneEnd = [];
    private SketchProgram? _program;
    private Interpreter? _interpreter;
    private Thread? _thread;
    private SemaphoreSlim _go = new(0);
    private SemaphoreSlim _yielded = new(0);
    private volatile bool _abort;
    private long _now;
    private long _target;
    private TimeSpan? _epoch;
    private TimeSpan _lastAdvance;
    private long _serialActivity = -1_000_000_000;

    public ArduinoMcu(string id, ArduinoBoard board)
    {
        Id = id;
        Board = board;
        _pins = new PinPort[board.PinCount];
        _lastRead = new bool[board.PinCount];
        for (int i = 0; i < board.PinCount; i++)
            _pins[i] = new PinPort($"{id}.{board.PinName(i)}", i, board.PinName(i), board.Vcc, board.Pwm.Contains(i), board.IsAnalog(i), board.AnalogOnly.Contains(i));
        Signals = _pins;
        var constants = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["INPUT"] = 0, ["OUTPUT"] = 1, ["INPUT_PULLUP"] = 2,
            ["LED_BUILTIN"] = board.LedBuiltin,
            ["DEC"] = 10, ["HEX"] = 16, ["OCT"] = 8, ["BIN"] = 2,
            ["LSBFIRST"] = 0, ["MSBFIRST"] = 1,
            ["CHANGE"] = 1, ["FALLING"] = 2, ["RISING"] = 3,
        };
        for (int a = 0; a < board.AnalogCount; a++) constants[$"A{a}"] = board.FirstAnalog + a;
        Host = new McuHost(this, constants);
    }

    public string Id { get; }
    public string ModuleId => "plc";
    public ArduinoBoard Board { get; }
    public McuState State { get; private set; } = McuState.Stopped;
    public string? FaultMessage { get; private set; }
    public int? FaultLine { get; private set; }
    public bool HasProgram => _program is not null;

    /// <summary>Tiempo transcurrido desde el arranque del sketch (lo que devuelve millis()).</summary>
    public TimeSpan Uptime => TimeSpan.FromTicks(_now);

    public IReadOnlyList<PinPort> Pins => _pins;
    public IReadOnlyList<ISignalPort> Signals { get; }
    public IReadOnlyList<IPowerPort> PowerPorts { get; } = [];
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>Línea del sketch que se estaba ejecutando al ceder el control.</summary>
    public int CurrentLine => _interpreter?.CurrentLine ?? 0;

    /// <summary>true si el puerto serie transmitió en los últimos 50 ms (LED TX).</summary>
    public bool SerialActive => _now - _serialActivity < 500_000;

    public long SerialBaud { get; private set; }

    internal McuHost Host { get; }

    public PinPort Pin(string name) =>
        _pins.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"{Board.Name} no tiene el pin {name}.");

    // ---- Programa ----

    /// <summary>Compila el sketch. Lanza <see cref="SketchCompileException"/> con la línea del error.</summary>
    public void Load(string source)
    {
        var program = Parser.Parse(source, Board.IntBits);
        Stop();
        _program = program;
    }

    public void Start()
    {
        if (_program is null) throw new InvalidOperationException("No hay ningún sketch cargado.");
        Stop();
        foreach (var p in _pins) p.ResetHardware();
        Array.Clear(_lastRead);
        _toneEnd.Clear();
        lock (_serialLock) _serialIn.Clear();
        _now = 0;
        _serialActivity = -1_000_000_000;
        _target = 0;
        _epoch = null;
        FaultMessage = null;
        FaultLine = null;
        _abort = false;
        _go = new SemaphoreSlim(0);
        _yielded = new SemaphoreSlim(0);
        _interpreter = new Interpreter(_program, Host);
        var interpreter = _interpreter;
        _thread = new Thread(() => Run(interpreter)) { IsBackground = true, Name = $"Sketch {Id}" };
        State = McuState.Running;
        _thread.Start();
        _yielded.Wait(); // el hilo arranca y espera la primera orden
    }

    /// <summary>Detiene el sketch (como mantener pulsado RESET): todos los pines vuelven a ser entradas.</summary>
    public void Stop()
    {
        if (_thread is { IsAlive: true })
        {
            _abort = true;
            _go.Release();
            _thread.Join();
        }
        _thread = null;
        if (State == McuState.Running) State = McuState.Stopped;
        foreach (var p in _pins) p.ResetHardware();
    }

    private sealed class AbortSketch : Exception;

    private void Run(Interpreter interpreter)
    {
        try
        {
            _yielded.Release();
            _go.Wait();
            if (_abort) return;
            interpreter.InitGlobals();
            interpreter.CallSetup();
            while (true) interpreter.CallLoop();
        }
        catch (AbortSketch) { }
        catch (SketchRuntimeException ex)
        {
            Fail(ex.Message, ex.Line);
        }
        catch (Exception ex) when (ex is InvalidCastException or IndexOutOfRangeException or OverflowException or ArgumentException or KeyNotFoundException)
        {
            Fail("Error interno del simulador: " + ex.Message, interpreter.CurrentLine);
        }
        finally
        {
            _yielded.Release();
        }
    }

    private void Fail(string message, int line)
    {
        FaultMessage = message;
        FaultLine = line;
        State = McuState.Fault;
        foreach (var p in _pins) p.ResetHardware();
    }

    /// <summary>Hace correr el sketch hasta el instante <paramref name="until"/> del tiempo de simulación.</summary>
    public void Advance(TimeSpan until)
    {
        _lastAdvance = until;
        if (State != McuState.Running || _thread is null) return;
        _epoch ??= until;
        _target = (until - _epoch.Value).Ticks;
        if (_now >= _target) return;
        _go.Release();
        _yielded.Wait();
    }

    /// <summary>Lo llama el hilo del sketch: consume tiempo y, si alcanza el objetivo, cede el control.</summary>
    private void Spend(long ticks)
    {
        _now += ticks;
        if (_toneEnd.Count > 0) ExpireTones();
        while (_now >= _target)
        {
            _yielded.Release();
            _go.Wait();
            if (_abort) throw new AbortSketch();
        }
    }

    private void ExpireTones()
    {
        foreach (var (pin, end) in _toneEnd.ToList())
        {
            if (_now < end) continue;
            _toneEnd.Remove(pin);
            SetOutput(_pins[pin], 0, null);
            _pins[pin].ToneHz = null;
            _pins[pin].Notify();
        }
    }

    // ---- Puerto serie ----

    public string SerialOutput
    {
        get { lock (_serialLock) return _serialOut.ToString(); }
    }

    public void ClearSerialOutput()
    {
        lock (_serialLock) _serialOut.Clear();
    }

    /// <summary>Envía texto a la placa (lo leen Serial.available() y Serial.read()).</summary>
    public void SerialSend(string text)
    {
        lock (_serialLock)
            foreach (char c in text) _serialIn.Enqueue(c);
    }

    // ---- Variables ----

    public IReadOnlyList<SketchVariable> Variables()
    {
        if (_interpreter is null) return [];
        return _interpreter.Globals
            .Where(kv => !kv.Value.IsConst || !Host.Constants.ContainsKey(kv.Key))
            .Select(kv => new SketchVariable(kv.Key, kv.Value.Type.Name + (kv.Value.Value.K == VK.Arr ? "[]" : ""), Format(kv.Value.Value)))
            .ToList();
    }

    private static string Format(Val v) => v.K switch
    {
        VK.Arr when v.A.Element.Bits == 8 && !v.A.Element.IsBool && !v.A.Element.Unsigned => "\"" + v.A.AsText() + "\"",
        VK.Arr => "{" + string.Join(", ", v.A.Items.Take(16).Select(Format)) + (v.A.Items.Length > 16 ? ", …" : "") + "}",
        VK.Str => "\"" + v.S + "\"",
        VK.Float => v.F.ToString("0.###", CultureInfo.InvariantCulture),
        VK.Obj when v.O is ServoObj s => $"Servo(pin {s.Pin}, {s.Angle:0}°)",
        _ => v.ToString(),
    };

    // ---- Pines ----

    private PinPort CheckPin(int pin, int line)
    {
        if (pin < 0 || pin >= _pins.Length)
            throw new SketchRuntimeException($"El pin {pin} no existe en {Board.Name} (pines 0 a {_pins.Length - 1}).", line);
        return _pins[pin];
    }

    private static void SetOutput(PinPort p, double volts, double? duty)
    {
        p.OutputVolts = volts;
        p.Duty = duty;
        p.Notify();
    }

    internal sealed class McuHost(ArduinoMcu mcu, IReadOnlyDictionary<string, long> constants) : IMcuHost
    {
        public long Now => mcu._now;
        public void Spend(long ticks) => mcu.Spend(ticks);
        public int IntBits => mcu.Board.IntBits;
        public IReadOnlyDictionary<string, long> Constants => constants;
        public Random Random { get; } = new(1);

        public void PinMode(int pin, long mode, int line)
        {
            var p = mcu.CheckPin(pin, line);
            if (p.IsAnalogOnly && mode == 1)
                throw new SketchRuntimeException($"{p.Name} de {mcu.Board.Name} solo es entrada analógica.", line);
            p.Configured = true;
            p.Mode = mode switch
            {
                1 => Runtime.PinMode.Output,
                2 => Runtime.PinMode.InputPullup,
                _ => Runtime.PinMode.Input,
            };
            if (p.Mode != Runtime.PinMode.Output) { p.OutputVolts = 0; p.Duty = null; }
            p.Notify();
        }

        public void DigitalWrite(int pin, bool high, int line)
        {
            var p = mcu.CheckPin(pin, line);
            if (p.Mode == Runtime.PinMode.Output) SetOutput(p, high ? mcu.Board.Vcc : 0, null);
            else
            {
                // En un pin de entrada, digitalWrite(HIGH) activa la resistencia de pull-up, como en el AVR.
                p.Mode = high ? Runtime.PinMode.InputPullup : Runtime.PinMode.Input;
                p.Notify();
            }
        }

        public bool DigitalRead(int pin, int line)
        {
            var p = mcu.CheckPin(pin, line);
            if (p.Mode == Runtime.PinMode.Output) return p.OutputHigh;
            double vcc = mcu.Board.Vcc, v = p.Value;
            if (v > vcc + 0.5 && !mcu._diagnostics.Any(d => d.StartsWith(p.Id, StringComparison.Ordinal)))
                mcu._diagnostics.Add($"{p.Id}: {v:0.#} V en un pin de {vcc:0.#} V. En la placa real se dañaría el microcontrolador.");
            bool state = v >= vcc * HighThreshold || (v > vcc * LowThreshold && mcu._lastRead[pin]);
            mcu._lastRead[pin] = state;
            return state;
        }

        public int AnalogRead(int pin, int line)
        {
            var board = mcu.Board;
            // analogRead(0) y analogRead(A0) leen el mismo canal.
            if (pin >= 0 && pin < board.AnalogCount) pin += board.FirstAnalog;
            var p = mcu.CheckPin(pin, line);
            if (!p.IsAnalog) throw new SketchRuntimeException($"analogRead() solo funciona con pines analógicos (A0…A{board.AnalogCount - 1}); {p.Name} no lo es.", line);
            double v = Math.Clamp(p.Value, 0, board.Vcc);
            return (int)Math.Round(v / board.Vcc * 1023);
        }

        public void AnalogWrite(int pin, int value, int line)
        {
            var p = mcu.CheckPin(pin, line);
            p.Mode = Runtime.PinMode.Output;
            if (!p.IsPwmCapable)
            {
                SetOutput(p, value >= 128 ? mcu.Board.Vcc : 0, null);
                if (!mcu._diagnostics.Any(d => d.Contains($"{p.Name} no tiene PWM", StringComparison.Ordinal)))
                    mcu._diagnostics.Add($"analogWrite(): {p.Name} no tiene PWM; solo se enciende (≥128) o se apaga. Pines PWM: {string.Join(", ", mcu.Board.Pwm.Order().Select(n => "D" + n))}.");
                return;
            }
            SetOutput(p, mcu.Board.Vcc * value / 255.0, value is 0 or 255 ? null : value / 255.0);
        }

        public void Tone(int pin, double hz, long? durationMs, int line)
        {
            var p = mcu.CheckPin(pin, line);
            p.Mode = Runtime.PinMode.Output;
            p.ToneHz = hz;
            SetOutput(p, mcu.Board.Vcc / 2, 0.5);
            if (durationMs is { } d) mcu._toneEnd[pin] = mcu._now + d * 10_000;
            else mcu._toneEnd.Remove(pin);
        }

        public void NoTone(int pin, int line)
        {
            var p = mcu.CheckPin(pin, line);
            p.ToneHz = null;
            mcu._toneEnd.Remove(pin);
            SetOutput(p, 0, null);
        }

        public void ServoWrite(ServoObj servo, int line)
        {
            if (servo.Pin < 0) return;
            var p = mcu.CheckPin(servo.Pin, line);
            p.Mode = Runtime.PinMode.Output;
            p.ServoAngle = servo.Angle;
            // Impulso de 0,5-2,4 ms cada 20 ms: tensión media pequeña.
            SetOutput(p, mcu.Board.Vcc * (0.544 + servo.Angle / 180 * 1.856) / 20, null);
        }

        public long PulseIn(int pin, bool level, long timeoutUs, int line)
        {
            long start = mcu._now, limit = start + timeoutUs * 10;
            while (DigitalRead(pin, line) == level) { if (mcu._now > limit) return 0; Spend(100); }
            while (DigitalRead(pin, line) != level) { if (mcu._now > limit) return 0; Spend(100); }
            long begin = mcu._now;
            while (DigitalRead(pin, line) == level) { if (mcu._now > limit) return 0; Spend(100); }
            return (mcu._now - begin) / 10;
        }

        public void SerialBegin(long baud) => mcu.SerialBaud = baud;

        public void SerialWrite(string text)
        {
            if (mcu.SerialBaud == 0 && !mcu._diagnostics.Any(d => d.StartsWith("Se usa Serial", StringComparison.Ordinal)))
                mcu._diagnostics.Add("Se usa Serial sin haber llamado a Serial.begin() en setup().");
            lock (mcu._serialLock)
            {
                mcu._serialOut.Append(text);
                if (mcu._serialOut.Length > 64_000) mcu._serialOut.Remove(0, mcu._serialOut.Length - 48_000);
            }
            mcu._serialActivity = mcu._now;
        }

        public int SerialAvailable()
        {
            lock (mcu._serialLock) return mcu._serialIn.Count;
        }

        public int SerialRead()
        {
            lock (mcu._serialLock) return mcu._serialIn.Count > 0 ? mcu._serialIn.Dequeue() : -1;
        }

        public int SerialPeek()
        {
            lock (mcu._serialLock) return mcu._serialIn.Count > 0 ? mcu._serialIn.Peek() : -1;
        }
    }

    // ---- ISimComponent ----

    public int StepMultiple => 1;
    public SimPhase Phase => SimPhase.Logic;

    public void Initialize(SimContext context)
    {
        foreach (var p in Signals) context.Bus.Register(p);
    }

    public void DoStep(TimeSpan now, TimeSpan dt) => Advance(now + dt);

    public ComponentState Snapshot()
    {
        var values = new Dictionary<string, object>
        {
            [StateKeys.State] = State.ToString(),
            [StateKeys.Line] = CurrentLine,
        };
        if (FaultMessage is not null) values[StateKeys.Fault] = FaultMessage;
        foreach (var p in _pins) values[p.Id] = p.Value;
        return new ComponentState(Id, _lastAdvance, values);
    }

    public void Reset()
    {
        Stop();
        State = McuState.Stopped;
        FaultMessage = null;
        FaultLine = null;
        _diagnostics.Clear();
        ClearSerialOutput();
    }

    public void Dispose() => Stop();

    public static class StateKeys
    {
        public const string State = "mcu.state";
        public const string Line = "mcu.line";
        public const string Fault = "mcu.fault";
    }
}
