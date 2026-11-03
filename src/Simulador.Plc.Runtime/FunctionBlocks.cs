namespace Simulador.Plc.Runtime;

/// <summary>Instancia de un bloque de función estándar (temporizador, contador, flanco, biestable).</summary>
public abstract class FunctionBlock
{
    private readonly Dictionary<string, PlcValue> _values = new(StringComparer.OrdinalIgnoreCase);

    protected FunctionBlock(params (string Name, PlcType Type, bool IsInput)[] members)
    {
        foreach (var (name, type, isInput) in members)
        {
            _values[name] = PlcValue.Default(type);
            Types[name] = type;
            if (isInput) Inputs.Add(name);
        }
    }

    public Dictionary<string, PlcType> Types { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Has(string member) => _values.ContainsKey(member);

    public PlcValue this[string member]
    {
        get => _values.TryGetValue(member, out var v) ? v : throw new KeyNotFoundException(member);
        set => _values[member] = value.ConvertTo(Types[member]);
    }

    public IEnumerable<KeyValuePair<string, PlcValue>> Members => _values;

    protected bool B(string n) => _values[n].AsBool;
    protected void B(string n, bool v) => _values[n] = PlcValue.FromBool(v);

    /// <summary>Ejecuta el bloque con las entradas actuales. <paramref name="now"/> es el tiempo de simulación.</summary>
    public abstract void Execute(TimeSpan now);

    public static IReadOnlyCollection<string> TypeNames => Factories.Keys;

    private static readonly Dictionary<string, Func<FunctionBlock>> Factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TON"] = () => new Ton(),
        ["TOF"] = () => new Tof(),
        ["TP"] = () => new Tp(),
        ["CTU"] = () => new Ctu(),
        ["CTD"] = () => new Ctd(),
        ["CTUD"] = () => new Ctud(),
        ["R_TRIG"] = () => new RTrig(),
        ["F_TRIG"] = () => new FTrig(),
        ["SR"] = () => new Sr(),
        ["RS"] = () => new Rs(),
    };

    public static bool TryCreate(string typeName, out FunctionBlock fb)
    {
        if (Factories.TryGetValue(typeName, out var f))
        {
            fb = f();
            return true;
        }
        fb = null!;
        return false;
    }
}

/// <summary>Temporizador con retardo a la conexión.</summary>
public sealed class Ton() : FunctionBlock(("IN", PlcType.Bool, true), ("PT", PlcType.Time, true), ("Q", PlcType.Bool, false), ("ET", PlcType.Time, false))
{
    private TimeSpan? _start;

    public override void Execute(TimeSpan now)
    {
        var pt = this["PT"].AsTime;
        if (!B("IN"))
        {
            _start = null;
            B("Q", false);
            this["ET"] = PlcValue.FromTime(TimeSpan.Zero);
            return;
        }
        _start ??= now;
        var et = now - _start.Value;
        if (et >= pt) et = pt;
        this["ET"] = PlcValue.FromTime(et);
        B("Q", et >= pt);
    }
}

/// <summary>Temporizador con retardo a la desconexión.</summary>
public sealed class Tof() : FunctionBlock(("IN", PlcType.Bool, true), ("PT", PlcType.Time, true), ("Q", PlcType.Bool, false), ("ET", PlcType.Time, false))
{
    private TimeSpan? _start;

    public override void Execute(TimeSpan now)
    {
        var pt = this["PT"].AsTime;
        if (B("IN"))
        {
            _start = null;
            B("Q", true);
            this["ET"] = PlcValue.FromTime(TimeSpan.Zero);
            return;
        }
        if (!B("Q")) return;
        _start ??= now;
        var et = now - _start.Value;
        if (et >= pt) et = pt;
        this["ET"] = PlcValue.FromTime(et);
        B("Q", et < pt);
    }
}

/// <summary>Temporizador de impulso.</summary>
public sealed class Tp() : FunctionBlock(("IN", PlcType.Bool, true), ("PT", PlcType.Time, true), ("Q", PlcType.Bool, false), ("ET", PlcType.Time, false))
{
    private TimeSpan? _start;
    private bool _prevIn;

    public override void Execute(TimeSpan now)
    {
        bool @in = B("IN");
        var pt = this["PT"].AsTime;
        if (_start is null && @in && !_prevIn) _start = now;
        if (_start is { } s)
        {
            var et = now - s;
            if (et >= pt)
            {
                et = pt;
                B("Q", false);
                if (!@in) _start = null;
            }
            else B("Q", true);
            this["ET"] = PlcValue.FromTime(_start is null ? TimeSpan.Zero : et);
        }
        _prevIn = @in;
    }
}

/// <summary>Contador ascendente.</summary>
public sealed class Ctu() : FunctionBlock(("CU", PlcType.Bool, true), ("R", PlcType.Bool, true), ("PV", PlcType.Int, true), ("Q", PlcType.Bool, false), ("CV", PlcType.Int, false))
{
    private bool _prev;

    public override void Execute(TimeSpan now)
    {
        bool cu = B("CU");
        long cv = this["CV"].AsInt;
        if (B("R")) cv = 0;
        else if (cu && !_prev) cv++;
        _prev = cu;
        this["CV"] = PlcValue.FromInt(cv);
        B("Q", cv >= this["PV"].AsInt);
    }
}

/// <summary>Contador descendente.</summary>
public sealed class Ctd() : FunctionBlock(("CD", PlcType.Bool, true), ("LD", PlcType.Bool, true), ("PV", PlcType.Int, true), ("Q", PlcType.Bool, false), ("CV", PlcType.Int, false))
{
    private bool _prev;

    public override void Execute(TimeSpan now)
    {
        bool cd = B("CD");
        long cv = this["CV"].AsInt;
        if (B("LD")) cv = this["PV"].AsInt;
        else if (cd && !_prev) cv--;
        _prev = cd;
        this["CV"] = PlcValue.FromInt(cv);
        B("Q", cv <= 0);
    }
}

/// <summary>Contador ascendente/descendente.</summary>
public sealed class Ctud() : FunctionBlock(
    ("CU", PlcType.Bool, true), ("CD", PlcType.Bool, true), ("R", PlcType.Bool, true), ("LD", PlcType.Bool, true),
    ("PV", PlcType.Int, true), ("QU", PlcType.Bool, false), ("QD", PlcType.Bool, false), ("CV", PlcType.Int, false))
{
    private bool _prevU, _prevD;

    public override void Execute(TimeSpan now)
    {
        bool cu = B("CU"), cd = B("CD");
        long cv = this["CV"].AsInt;
        if (B("R")) cv = 0;
        else if (B("LD")) cv = this["PV"].AsInt;
        else
        {
            if (cu && !_prevU) cv++;
            if (cd && !_prevD) cv--;
        }
        _prevU = cu;
        _prevD = cd;
        this["CV"] = PlcValue.FromInt(cv);
        B("QU", cv >= this["PV"].AsInt);
        B("QD", cv <= 0);
    }
}

/// <summary>Detector de flanco ascendente.</summary>
public sealed class RTrig() : FunctionBlock(("CLK", PlcType.Bool, true), ("Q", PlcType.Bool, false))
{
    private bool _prev;

    public override void Execute(TimeSpan now)
    {
        bool clk = B("CLK");
        B("Q", clk && !_prev);
        _prev = clk;
    }
}

/// <summary>Detector de flanco descendente.</summary>
public sealed class FTrig() : FunctionBlock(("CLK", PlcType.Bool, true), ("Q", PlcType.Bool, false))
{
    private bool _prev;

    public override void Execute(TimeSpan now)
    {
        bool clk = B("CLK");
        B("Q", !clk && _prev);
        _prev = clk;
    }
}

/// <summary>Biestable con prioridad a la activación.</summary>
public sealed class Sr() : FunctionBlock(("S1", PlcType.Bool, true), ("R", PlcType.Bool, true), ("Q1", PlcType.Bool, false))
{
    public override void Execute(TimeSpan now) => B("Q1", B("S1") || (!B("R") && B("Q1")));
}

/// <summary>Biestable con prioridad a la desactivación.</summary>
public sealed class Rs() : FunctionBlock(("S", PlcType.Bool, true), ("R1", PlcType.Bool, true), ("Q1", PlcType.Bool, false))
{
    public override void Execute(TimeSpan now) => B("Q1", !B("R1") && (B("S") || B("Q1")));
}
