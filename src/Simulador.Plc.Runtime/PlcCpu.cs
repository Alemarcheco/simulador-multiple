using Simulador.Core.CoSim;
using Simulador.Core.Signals;
using Simulador.Plc.Runtime.Ladder;
using Simulador.Plc.Runtime.Profiles;
using Simulador.Plc.Runtime.St;

namespace Simulador.Plc.Runtime;

public enum PlcState { Stop, Run, Fault }

/// <summary>
/// CPU de PLC simulada: ciclo de scan (leer entradas → ejecutar programa → escribir salidas), bornes de E/S como
/// <see cref="ISignalPort"/> y componente del banco de pruebas combinado.
/// </summary>
public sealed class PlcCpu : ISimComponent
{
    /// <summary>Valor bruto de una analógica a fondo de escala (0-10 V → 0-27648, como Siemens).</summary>
    public const long AnalogFullScale = 27648;

    private readonly ProcessImage _image = new();
    private readonly List<(SignalPort Port, IoAddress Address)> _di = [];
    private readonly List<(SignalPort Port, IoAddress Address)> _do = [];
    private readonly List<(SignalPort Port, IoAddress Address)> _ai = [];
    private readonly List<(SignalPort Port, IoAddress Address)> _ao = [];
    private readonly Dictionary<IoAddress, bool> _inputLatch = [];
    private readonly HashSet<IoAddress> _damaged = [];
    private readonly Dictionary<IoAddress, bool> _forced = [];
    private readonly List<string> _diagnostics = [];
    private Interpreter? _interpreter;
    private TimeSpan _lastScan;

    public PlcCpu(string id, PlcProfile profile)
    {
        Id = id;
        Profile = profile;
        CycleTime = profile.DefaultCycleTime;
        for (int i = 0; i < profile.DigitalInputs; i++)
        {
            var a = profile.InputAddress(i);
            _di.Add((new SignalPort($"{id}.{profile.Format(a)}", SignalKind.DI, SignalUnit.Volt, profile.InputNominalVolts, false, profile.InputCommon), a));
        }
        for (int i = 0; i < profile.DigitalOutputs; i++)
        {
            var a = profile.OutputAddress(i);
            _do.Add((new SignalPort($"{id}.{profile.Format(a)}", SignalKind.DO, SignalUnit.Bool, 24, false, profile.OutputCommon), a));
        }
        for (int i = 0; i < profile.AnalogInputs; i++)
        {
            var a = profile.AnalogInputAddress(i);
            _ai.Add((new SignalPort($"{id}.{profile.Format(a)}", SignalKind.AI, SignalUnit.Volt, 10), a));
        }
        for (int i = 0; i < profile.AnalogOutputs; i++)
        {
            var a = profile.AnalogOutputAddress(i);
            _ao.Add((new SignalPort($"{id}.{profile.Format(a)}", SignalKind.AO, SignalUnit.Volt, 10), a));
        }
        Signals = [.. _di.Select(x => x.Port), .. _do.Select(x => x.Port), .. _ai.Select(x => x.Port), .. _ao.Select(x => x.Port)];
    }

    public string Id { get; }
    public string ModuleId => "plc";
    public PlcProfile Profile { get; }
    public PlcState State { get; private set; } = PlcState.Stop;
    public string? FaultMessage { get; private set; }
    public TimeSpan CycleTime { get; set; }
    public long ScanCount { get; private set; }
    public ProcessImage Image => _image;
    public ProgramUnit? Program { get; private set; }

    public IReadOnlyList<ISignalPort> Signals { get; }
    public IReadOnlyList<ISignalPort> DigitalInputs => _di.Select(x => (ISignalPort)x.Port).ToList();
    public IReadOnlyList<ISignalPort> DigitalOutputs => _do.Select(x => (ISignalPort)x.Port).ToList();
    public IReadOnlyList<ISignalPort> AnalogInputs => _ai.Select(x => (ISignalPort)x.Port).ToList();
    public IReadOnlyList<ISignalPort> AnalogOutputs => _ao.Select(x => (ISignalPort)x.Port).ToList();
    public IReadOnlyList<IPowerPort> PowerPorts { get; } = [];

    /// <summary>Avisos de hardware (p. ej. entrada alimentada a una tensión excesiva).</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public IReadOnlyCollection<Variable> Variables => _interpreter?.Variables ?? [];

    // ---- Programa ----

    /// <summary>Compila y carga un programa en texto estructurado. Lanza <see cref="PlcCompileException"/> si hay errores.</summary>
    public void LoadStructuredText(string source) => Load(Parser.ParseProgram(source));

    public void LoadLadder(LadderProgram program) => Load(LadderCompiler.Compile(program));

    /// <summary>Compila y carga un programa en lista de instrucciones (IL / AWL).</summary>
    public void LoadInstructionList(string source) => Load(Il.IlCompiler.Compile(source, Profile));

    /// <summary>Compila y carga un SFC / GRAFCET escrito en la forma textual de IEC 61131-3.</summary>
    public void LoadSfc(string source) => Load(Sfc.SfcCompiler.Compile(Parser.ParseSfc(source)));

    public void Load(ProgramUnit program)
    {
        var interpreter = new Interpreter(program, Profile, _image);
        Stop();
        Program = program;
        _interpreter = interpreter;
        _image.Clear();
    }

    public void Start()
    {
        if (_interpreter is null) throw new InvalidOperationException("No hay ningún programa cargado.");
        if (State == PlcState.Run) return;
        if (State == PlcState.Fault) _interpreter.ColdStart();
        FaultMessage = null;
        State = PlcState.Run;
    }

    public void Stop()
    {
        if (State == PlcState.Run) State = PlcState.Stop;
        _image.ClearOutputs();
        WriteOutputs();
    }

    /// <summary>Lee una variable, salida de bloque o dirección (tabla de observación).</summary>
    public PlcValue Read(string operand)
    {
        if (_interpreter is null) throw new InvalidOperationException("No hay ningún programa cargado.");
        return _interpreter.Read(operand);
    }

    /// <summary>Fuerza una entrada o salida digital a un valor fijo, o lo libera con <c>null</c>.</summary>
    public void Force(string address, bool? value)
    {
        if (!Profile.TryParse(address, out var a) || a.Size != IoSize.Bit)
            throw new ArgumentException($"'{address}' no es una dirección de bit de {Profile.DisplayName}.");
        if (value is null) _forced.Remove(a);
        else _forced[a] = value.Value;
    }

    public IReadOnlyDictionary<IoAddress, bool> Forced => _forced;

    // ---- Ciclo de scan ----

    /// <summary>Ejecuta un ciclo completo si está en RUN. <paramref name="now"/> es el tiempo de simulación.</summary>
    public void Scan(TimeSpan now)
    {
        ReadInputs();
        if (State == PlcState.Run && _interpreter is not null)
        {
            try
            {
                _interpreter.RunCycle(now);
                ScanCount++;
            }
            catch (PlcRuntimeException ex)
            {
                State = PlcState.Fault;
                FaultMessage = ex.Message;
                _image.ClearOutputs();
            }
        }
        else
        {
            _image.ClearOutputs();
        }
        foreach (var (a, v) in _forced)
            if (a.Area == IoArea.Q) _image.SetBit(a, v);
        WriteOutputs();
        _lastScan = now;
    }

    private void ReadInputs()
    {
        double nominal = Profile.InputNominalVolts;
        // Umbrales IEC 61131-2 tipo 1 escalados a la tensión nominal (24 V: ON ≥ 15 V, OFF ≤ 5 V).
        double on = nominal * 15 / 24, off = nominal * 5 / 24;
        foreach (var (port, a) in _di)
        {
            double v = Math.Abs(port.Value);
            if (v > nominal * 1.5 && _damaged.Add(a))
                _diagnostics.Add($"{port.Id}: {v:0.#} V en una entrada de {nominal:0.#} V. Entrada dañada (queda siempre a 0).");
            bool state;
            if (_damaged.Contains(a)) state = false;
            else if (v >= on) state = true;
            else if (v <= off) state = false;
            else state = _inputLatch.GetValueOrDefault(a);
            _inputLatch[a] = state;
            if (_forced.TryGetValue(a, out var f)) state = f;
            _image.SetBit(a, state);
        }
        foreach (var (port, a) in _ai)
        {
            double v = Math.Clamp(port.Value, 0, 11.76);
            _image.SetWord(a, (long)Math.Round(v / 10 * AnalogFullScale));
        }
    }

    private void WriteOutputs()
    {
        foreach (var (port, a) in _do) port.Value = _image.GetBit(a) ? 1 : 0;
        foreach (var (port, a) in _ao) port.Value = Math.Clamp(_image.GetWord(a), 0, AnalogFullScale) * 10.0 / AnalogFullScale;
    }

    // ---- ISimComponent ----

    public int StepMultiple => Math.Max(1, (int)Math.Round(CycleTime.TotalMilliseconds));
    public SimPhase Phase => SimPhase.Logic;

    public void Initialize(SimContext context)
    {
        foreach (var p in Signals) context.Bus.Register(p);
    }

    public void DoStep(TimeSpan now, TimeSpan dt) => Scan(now + dt);

    public ComponentState Snapshot()
    {
        var values = new Dictionary<string, object>
        {
            [StateKeys.State] = State.ToString(),
            [StateKeys.ScanCount] = ScanCount,
        };
        if (FaultMessage is not null) values[StateKeys.Fault] = FaultMessage;
        foreach (var p in Signals) values[p.Id] = p.Value;
        return new ComponentState(Id, _lastScan, values);
    }

    public void Reset()
    {
        Stop();
        State = PlcState.Stop;
        FaultMessage = null;
        ScanCount = 0;
        _damaged.Clear();
        _inputLatch.Clear();
        _diagnostics.Clear();
        _image.Clear();
        _interpreter?.ColdStart();
    }

    /// <summary>Claves de <see cref="Snapshot"/> además de los Id de las señales.</summary>
    public static class StateKeys
    {
        public const string State = "plc.state";
        public const string ScanCount = "plc.scanCount";
        public const string Fault = "plc.fault";
    }
}
