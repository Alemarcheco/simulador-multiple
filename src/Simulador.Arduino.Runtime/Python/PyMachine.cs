namespace Simulador.Arduino.Runtime.Python;

/// <summary>Clase nativa (Pin, PWM, ADC, Timer): se llama para crear objetos y tiene constantes como Pin.OUT.</summary>
internal sealed class PyType(string name, Func<PyArgs, object?> ctor, Dictionary<string, object?> attrs) : PyNative
{
    public string Name { get; } = name;
    public override string TypeName => "type";
    public override object? GetAttr(string n) => attrs.GetValueOrDefault(n);
    public override bool HasAttr(string n) => attrs.ContainsKey(n);
    public override object? Call(PyArgs args) => ctor(args);
    public override string Repr() => $"<class '{Name}'>";
}

/// <summary>Módulos de MicroPython: machine, time, math, random, sys… y las interrupciones de pines y temporizadores.</summary>
internal sealed partial class PyInterpreter
{
    // Valores de MicroPython (los mismos en ESP32 y RP2040 para IN/OUT/PULL_UP).
    internal const long PinIn = 1, PinOut = 3, PinOpenDrain = 7, PullUp = 1, PullDown = 2, IrqFalling = 2, IrqRising = 1;

    private sealed record IrqEntry(PinObj Pin, long Trigger, object? Handler)
    {
        public bool Last { get; set; }
    }

    private sealed class TimerEntry(TimerObj timer, long period, bool periodic, object? callback, long next)
    {
        public TimerObj Timer { get; } = timer;
        public long Period { get; } = period;
        public bool Periodic { get; } = periodic;
        public object? Callback { get; } = callback;
        public long Next { get; set; } = next;
    }

    private readonly Dictionary<int, IrqEntry> _irqs = [];
    private readonly List<TimerEntry> _timers = [];
    private bool _inEvent;

    private bool HasEvents => _irqs.Count > 0 || _timers.Count > 0;

    private bool IsEsp32 => Board.Chip.StartsWith("ESP32", StringComparison.Ordinal);

    /// <summary>Atiende los flancos de los pines con irq() y los temporizadores vencidos.</summary>
    private void PollEvents()
    {
        if (_inEvent || !HasEvents) return;
        _inEvent = true;
        int line = CurrentLine;
        try
        {
            foreach (var irq in _irqs.Values.ToArray())
            {
                bool level = Host.DigitalRead(irq.Pin.Number, line);
                bool rising = level && !irq.Last, falling = !level && irq.Last;
                irq.Last = level;
                if ((rising && (irq.Trigger & IrqRising) != 0) || (falling && (irq.Trigger & IrqFalling) != 0))
                    if (irq.Handler is not null) Call(irq.Handler, new PyArgs([irq.Pin], PyArgs.NoKw));
            }
            foreach (var t in _timers.ToArray())
            {
                if (Host.Now < t.Next) continue;
                if (t.Periodic) t.Next += t.Period;
                else _timers.Remove(t);
                if (t.Callback is not null) Call(t.Callback, new PyArgs([t.Timer], PyArgs.NoKw));
            }
        }
        finally
        {
            _inEvent = false;
            CurrentLine = line;
        }
    }

    private PyModuleObj Module(string name, params (string Key, object? Value)[] attrs)
    {
        var m = new PyModuleObj(name);
        foreach (var (k, v) in attrs) m.Attrs[k] = v;
        return m;
    }

    private static PyBuiltin F(string name, Func<PyArgs, object?> fn) => new(name, fn);

    private void InitModules()
    {
        _moduleFactories["machine"] = MachineModule;
        _moduleFactories["time"] = () => TimeModule("time");
        _moduleFactories["utime"] = () => TimeModule("utime");
        _moduleFactories["math"] = MathModule;
        _moduleFactories["random"] = () => RandomModule("random");
        _moduleFactories["urandom"] = () => RandomModule("urandom");
        _moduleFactories["sys"] = () => Module("sys",
            ("platform", IsEsp32 ? "esp32" : "rp2"),
            ("version", "3.4.0; MicroPython v1.24 (simulador)"),
            ("exit", F("exit", _ => throw new PyError("SystemExit", ""))),
            ("maxsize", (long)int.MaxValue));
        _moduleFactories["micropython"] = () => Module("micropython",
            ("const", F("const", a => Arg(a, 0, "const"))),
            ("mem_info", F("mem_info", _ => { Host.SerialWrite("stack: 736 out of 15360\nGC: total: 111168, used: 9536, free: 101632\n"); return null; })));
        _moduleFactories["gc"] = () => Module("gc",
            ("collect", F("collect", _ => { Spend(20_000); return null; })),
            ("mem_free", F("mem_free", _ => 101_632L)),
            ("mem_alloc", F("mem_alloc", _ => 9_536L)),
            ("enable", F("enable", _ => null)),
            ("disable", F("disable", _ => null)));
    }

    // ---- time ----

    private PyModuleObj TimeModule(string name) => Module(name,
        ("sleep", F("sleep", a => { Sleep((long)(PyOps.ToDouble(Arg(a, 0, "sleep")) * 10_000_000)); return null; })),
        ("sleep_ms", F("sleep_ms", a => { Sleep(PyOps.ToLong(Arg(a, 0, "sleep_ms")) * 10_000); return null; })),
        ("sleep_us", F("sleep_us", a => { Sleep(PyOps.ToLong(Arg(a, 0, "sleep_us")) * 10); return null; })),
        ("ticks_ms", F("ticks_ms", _ => Host.Now / 10_000)),
        ("ticks_us", F("ticks_us", _ => Host.Now / 10)),
        ("ticks_cpu", F("ticks_cpu", _ => Host.Now)),
        ("ticks_diff", F("ticks_diff", a => PyOps.ToLong(Arg(a, 0, "ticks_diff")) - PyOps.ToLong(Arg(a, 1, "ticks_diff")))),
        ("ticks_add", F("ticks_add", a => PyOps.ToLong(Arg(a, 0, "ticks_add")) + PyOps.ToLong(Arg(a, 1, "ticks_add")))),
        ("time", F("time", _ => Host.Now / 10_000_000)),
        ("time_ns", F("time_ns", _ => Host.Now * 100)));

    // ---- math ----

    private static PyModuleObj MathModule()
    {
        var m = new PyModuleObj("math");
        void D(string n, Func<double, double> f) => m.Attrs[n] = new PyBuiltin(n, a => f(PyOps.ToDouble(Arg(a, 0, n))));
        m.Attrs["pi"] = Math.PI;
        m.Attrs["e"] = Math.E;
        m.Attrs["tau"] = Math.Tau;
        m.Attrs["inf"] = double.PositiveInfinity;
        m.Attrs["nan"] = double.NaN;
        D("sqrt", x => x < 0 ? throw new PyError("ValueError", "raíz cuadrada de un número negativo") : Math.Sqrt(x));
        D("sin", Math.Sin);
        D("cos", Math.Cos);
        D("tan", Math.Tan);
        D("asin", Math.Asin);
        D("acos", Math.Acos);
        D("atan", Math.Atan);
        D("exp", Math.Exp);
        D("log10", Math.Log10);
        D("log2", Math.Log2);
        D("fabs", Math.Abs);
        D("degrees", x => x * 180 / Math.PI);
        D("radians", x => x * Math.PI / 180);
        m.Attrs["floor"] = new PyBuiltin("floor", a => Arg(a, 0, "floor") is long l ? l : (long)Math.Floor(PyOps.ToDouble(a.Pos[0])));
        m.Attrs["ceil"] = new PyBuiltin("ceil", a => Arg(a, 0, "ceil") is long l ? l : (long)Math.Ceiling(PyOps.ToDouble(a.Pos[0])));
        m.Attrs["trunc"] = new PyBuiltin("trunc", a => (long)Math.Truncate(PyOps.ToDouble(Arg(a, 0, "trunc"))));
        m.Attrs["isnan"] = new PyBuiltin("isnan", a => double.IsNaN(PyOps.ToDouble(Arg(a, 0, "isnan"))));
        m.Attrs["isinf"] = new PyBuiltin("isinf", a => double.IsInfinity(PyOps.ToDouble(Arg(a, 0, "isinf"))));
        m.Attrs["atan2"] = new PyBuiltin("atan2", a => Math.Atan2(PyOps.ToDouble(Arg(a, 0, "atan2")), PyOps.ToDouble(Arg(a, 1, "atan2"))));
        m.Attrs["pow"] = new PyBuiltin("pow", a => Math.Pow(PyOps.ToDouble(Arg(a, 0, "pow")), PyOps.ToDouble(Arg(a, 1, "pow"))));
        m.Attrs["fmod"] = new PyBuiltin("fmod", a => PyOps.ToDouble(Arg(a, 0, "fmod")) % PyOps.ToDouble(Arg(a, 1, "fmod")));
        m.Attrs["hypot"] = new PyBuiltin("hypot", a => Math.Sqrt(a.Pos.Sum(x => PyOps.ToDouble(x) * PyOps.ToDouble(x))));
        m.Attrs["copysign"] = new PyBuiltin("copysign", a => Math.CopySign(PyOps.ToDouble(Arg(a, 0, "copysign")), PyOps.ToDouble(Arg(a, 1, "copysign"))));
        m.Attrs["log"] = new PyBuiltin("log", a =>
        {
            double x = PyOps.ToDouble(Arg(a, 0, "log"));
            if (x <= 0) throw new PyError("ValueError", "logaritmo de un número que no es positivo");
            return a.Pos.Count > 1 ? Math.Log(x) / Math.Log(PyOps.ToDouble(a.Pos[1])) : Math.Log(x);
        });
        return m;
    }

    // ---- random ----

    private PyModuleObj RandomModule(string name)
    {
        var rnd = Host.Random;
        return Module(name,
            ("random", F("random", _ => rnd.NextDouble())),
            ("randint", F("randint", a => rnd.NextInt64(PyOps.ToLong(Arg(a, 0, "randint")), PyOps.ToLong(Arg(a, 1, "randint")) + 1))),
            ("randrange", F("randrange", a =>
            {
                long start = a.Pos.Count > 1 ? PyOps.ToLong(a.Pos[0]) : 0;
                long stop = PyOps.ToLong(a.Pos.Count > 1 ? a.Pos[1] : Arg(a, 0, "randrange"));
                long step = a.Pos.Count > 2 ? PyOps.ToLong(a.Pos[2]) : 1;
                long n = (stop - start + step - 1) / step;
                if (n <= 0) throw new PyError("ValueError", "randrange() con un rango vacío");
                return start + step * rnd.NextInt64(n);
            })),
            ("uniform", F("uniform", a =>
            {
                double lo = PyOps.ToDouble(Arg(a, 0, "uniform")), hi = PyOps.ToDouble(Arg(a, 1, "uniform"));
                return lo + (hi - lo) * rnd.NextDouble();
            })),
            ("choice", F("choice", a =>
            {
                var items = Iterate(Arg(a, 0, "choice")).ToList();
                if (items.Count == 0) throw new PyError("IndexError", "choice() de una secuencia vacía");
                return items[rnd.Next(items.Count)];
            })),
            ("getrandbits", F("getrandbits", a => rnd.NextInt64(1L << (int)Math.Clamp(PyOps.ToLong(Arg(a, 0, "getrandbits")), 1, 62)))),
            ("seed", F("seed", _ => null)),
            ("shuffle", F("shuffle", a =>
            {
                if (Arg(a, 0, "shuffle") is not PyList l) throw new PyError("TypeError", "shuffle() necesita una lista");
                for (int i = l.Items.Count - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    (l.Items[i], l.Items[j]) = (l.Items[j], l.Items[i]);
                }
                return null;
            })));
    }

    // ---- machine ----

    private PyModuleObj MachineModule()
    {
        var pinType = new PyType("Pin", NewPin, new Dictionary<string, object?>
        {
            ["IN"] = PinIn, ["OUT"] = PinOut, ["OPEN_DRAIN"] = PinOpenDrain,
            ["PULL_UP"] = PullUp, ["PULL_DOWN"] = PullDown,
            ["IRQ_FALLING"] = IrqFalling, ["IRQ_RISING"] = IrqRising,
        });
        var pwmType = new PyType("PWM", NewPwm, []);
        var adcType = new PyType("ADC", NewAdc, new Dictionary<string, object?>
        {
            ["ATTN_0DB"] = 0L, ["ATTN_2_5DB"] = 1L, ["ATTN_6DB"] = 2L, ["ATTN_11DB"] = 3L,
            ["WIDTH_9BIT"] = 9L, ["WIDTH_10BIT"] = 10L, ["WIDTH_11BIT"] = 11L, ["WIDTH_12BIT"] = 12L,
            ["CORE_TEMP"] = 4L,
        });
        var timerType = new PyType("Timer", NewTimer, new Dictionary<string, object?>
        {
            ["ONE_SHOT"] = 0L, ["PERIODIC"] = 1L,
        });
        long freq = IsEsp32 ? 240_000_000 : 125_000_000;
        return Module("machine",
            ("Pin", pinType), ("PWM", pwmType), ("ADC", adcType), ("Timer", timerType),
            ("freq", F("freq", a => { if (a.Pos.Count > 0) freq = PyOps.ToLong(a.Pos[0]); return freq; })),
            ("unique_id", F("unique_id", _ => IsEsp32 ? "¤Ï\u00124Vx" : "æaA\u0004\u0003P*(")),
            ("idle", F("idle", _ => { Sleep(10_000); return null; })),
            ("lightsleep", F("lightsleep", a => { Sleep(a.Pos.Count > 0 ? PyOps.ToLong(a.Pos[0]) * 10_000 : 10_000_000); return null; })),
            ("deepsleep", F("deepsleep", a => { Sleep(a.Pos.Count > 0 ? PyOps.ToLong(a.Pos[0]) * 10_000 : 10_000_000); throw new PyError("SystemExit", ""); })),
            ("reset", F("reset", _ => throw new PyError("SystemExit", ""))),
            ("disable_irq", F("disable_irq", _ => 0L)),
            ("enable_irq", F("enable_irq", _ => null)));
    }

    /// <summary>Número de GPIO a partir de un entero, un Pin o "LED" (Pico W).</summary>
    private int PinNumber(object? id)
    {
        switch (id)
        {
            case PinObj p: return p.Number;
            case long or bool:
            {
                long n = PyOps.ToLong(id);
                if (n < 0 || n >= Board.PinCount)
                    throw new PyError("ValueError", $"el pin {n} no existe en {Board.Name} (de 0 a {Board.PinCount - 1})");
                return (int)n;
            }
            case string s when s.Equals("LED", StringComparison.OrdinalIgnoreCase): return Board.LedBuiltin;
            case string s when Board.PinPrefix is { } pre && s.StartsWith(pre, StringComparison.OrdinalIgnoreCase) && int.TryParse(s[pre.Length..], out int k):
                return PinNumber((long)k);
        }
        throw new PyError("TypeError", $"un pin se indica con su número, no con {PyOps.TypeName(id)}");
    }

    private object? NewPin(PyArgs a)
    {
        var pin = new PinObj(this, PinNumber(Arg(a, 0, "Pin")));
        pin.Init(a, 1);
        return pin;
    }

    private object? NewPwm(PyArgs a)
    {
        int n = PinNumber(Arg(a, 0, "PWM"));
        var pwm = new PwmObj(this, n, IsEsp32 ? 5000 : 1000, IsEsp32 && !a.Kw.ContainsKey("duty_u16") && !a.Kw.ContainsKey("duty_ns") ? 0.5 : 0);
        if (a.Has(1, "freq")) pwm.Freq = PyOps.ToDouble(a.Get(1, "freq"));
        if (a.Has(2, "duty")) pwm.Duty = PyOps.ToDouble(a.Get(2, "duty")) / 1023;
        if (a.Kw.TryGetValue("duty_u16", out var u)) pwm.Duty = PyOps.ToDouble(u) / 65535;
        if (a.Kw.TryGetValue("duty_ns", out var ns)) pwm.Duty = PyOps.ToDouble(ns) * 1e-9 * pwm.Freq;
        pwm.Apply();
        return pwm;
    }

    private object? NewAdc(PyArgs a)
    {
        var id = Arg(a, 0, "ADC");
        if (!IsEsp32 && id is long ch && ch is >= 0 and <= 4 && Board.PinCount < 30)
        {
            if (ch == 4) return new AdcObj(this, -1);
            return new AdcObj(this, Board.FirstAnalog + (int)ch);
        }
        int n = PinNumber(id);
        if (!Board.IsAnalog(n))
        {
            string list = string.Join(", ", Enumerable.Range(Board.FirstAnalog, Board.AnalogCount).Select(Board.PinName));
            throw new PyError("ValueError", $"{Board.PinName(n)} no tiene conversor analógico (ADC). Pines con ADC: {list}");
        }
        return new AdcObj(this, n);
    }

    private object? NewTimer(PyArgs a)
    {
        var t = new TimerObj(this);
        if (a.Kw.Count > 0) t.Init(new PyArgs([], a.Kw));
        return t;
    }

    // ---- Objetos de machine ----

    internal sealed class PinObj(PyInterpreter py, int number) : PyNative
    {
        public int Number { get; } = number;
        public long Mode { get; private set; } = PinIn;
        public override string TypeName => "Pin";
        public override string Repr() => $"Pin({Number}, mode={(Mode == PinOut ? "OUT" : Mode == PinOpenDrain ? "OPEN_DRAIN" : "IN")})";

        private int Line => py.CurrentLine;

        public void Init(PyArgs a, int first)
        {
            object? mode = a.Get(first, "mode"), pull = a.Get(first + 1, "pull");
            if (a.Has(first, "mode") && mode is not null) Mode = PyOps.ToLong(mode);
            long pullV = pull is null ? 0 : PyOps.ToLong(pull);
            if (Mode is PinOut or PinOpenDrain)
            {
                if (py.Board.InputOnly.Contains(Number))
                    throw new PyError("ValueError", $"{py.Board.PinName(Number)} de {py.Board.Name} solo puede ser entrada");
                py.Host.PinMode(Number, 1, Line);
                if (a.Kw.TryGetValue("value", out var v) && v is not null) py.Host.DigitalWrite(Number, PyOps.Truthy(v), Line);
            }
            else if (a.Has(first, "mode") || a.Has(first + 1, "pull"))
                py.Host.PinMode(Number, pullV == PullUp ? 2 : 0, Line);
        }

        private object? Value(PyArgs a)
        {
            if (a.Pos.Count == 0) return py.Host.DigitalRead(Number, Line) ? 1L : 0L;
            Write(PyOps.Truthy(a.Pos[0]));
            return null;
        }

        private void Write(bool high)
        {
            if (Mode == PinOpenDrain && high) { py.Host.PinMode(Number, 0, Line); return; }
            if (Mode == PinOpenDrain) py.Host.PinMode(Number, 1, Line);
            py.Host.DigitalWrite(Number, high, Line);
        }

        public override object? Call(PyArgs args) => Value(args);

        public override bool HasAttr(string name) => name is "value" or "on" or "off" or "high" or "low" or "toggle" or "init" or "irq";

        public override object? GetAttr(string name) => name switch
        {
            "value" => F(name, Value),
            "on" or "high" => F(name, _ => { Write(true); return null; }),
            "off" or "low" => F(name, _ => { Write(false); return null; }),
            "toggle" => F(name, _ => { Write(!py.Host.DigitalRead(Number, Line)); return null; }),
            "init" => F(name, a => { Init(a, 0); return null; }),
            "irq" => F(name, a =>
            {
                var handler = a.Get(0, "handler");
                long trigger = a.Has(1, "trigger") ? PyOps.ToLong(a.Get(1, "trigger")) : IrqFalling | IrqRising;
                if (handler is null) py._irqs.Remove(Number);
                else py._irqs[Number] = new IrqEntry(this, trigger, handler) { Last = py.Host.DigitalRead(Number, Line) };
                return null;
            }),
            _ => null,
        };
    }

    internal sealed class PwmObj(PyInterpreter py, int number, double freq, double duty) : PyNative
    {
        public double Freq { get; set; } = freq;
        public double Duty { get; set; } = duty;
        private bool _on = true;
        public override string TypeName => "PWM";
        public override string Repr() => $"PWM(Pin({number}), freq={Freq:0}, duty_u16={Math.Round(Duty * 65535):0})";

        public void Apply()
        {
            if (Freq <= 0) throw new PyError("ValueError", "la frecuencia del PWM tiene que ser mayor que 0");
            Duty = Math.Clamp(Duty, 0, 1);
            if (_on) py.Host.Pwm(number, Freq, Duty, py.CurrentLine);
        }

        public override bool HasAttr(string name) => name is "freq" or "duty" or "duty_u16" or "duty_ns" or "deinit" or "init";

        public override object? GetAttr(string name) => name switch
        {
            "freq" => F(name, a =>
            {
                if (a.Pos.Count == 0) return (long)Math.Round(Freq);
                Freq = PyOps.ToDouble(a.Pos[0]);
                Apply();
                return null;
            }),
            "duty" => F(name, a =>
            {
                if (a.Pos.Count == 0) return (long)Math.Round(Duty * 1023);
                Duty = PyOps.ToDouble(a.Pos[0]) / 1023;
                Apply();
                return null;
            }),
            "duty_u16" => F(name, a =>
            {
                if (a.Pos.Count == 0) return (long)Math.Round(Duty * 65535);
                Duty = PyOps.ToDouble(a.Pos[0]) / 65535;
                Apply();
                return null;
            }),
            "duty_ns" => F(name, a =>
            {
                if (a.Pos.Count == 0) return (long)Math.Round(Duty / Freq * 1e9);
                Duty = PyOps.ToDouble(a.Pos[0]) * 1e-9 * Freq;
                Apply();
                return null;
            }),
            "init" => F(name, a =>
            {
                _on = true;
                if (a.Kw.TryGetValue("freq", out var f)) Freq = PyOps.ToDouble(f);
                if (a.Kw.TryGetValue("duty_u16", out var u)) Duty = PyOps.ToDouble(u) / 65535;
                Apply();
                return null;
            }),
            "deinit" => F(name, _ =>
            {
                _on = false;
                py.Host.Pwm(number, Freq, null, py.CurrentLine);
                return null;
            }),
            _ => null,
        };
    }

    internal sealed class AdcObj(PyInterpreter py, int number) : PyNative
    {
        public override string TypeName => "ADC";
        public override string Repr() => number < 0 ? "ADC(CORE_TEMP)" : $"ADC(Pin({number}))";

        /// <summary>Tensión en el pin; el canal 4 del RP2040 es el sensor de temperatura interno (≈25 °C).</summary>
        private double Volts()
        {
            py.Spend(IsEsp(py) ? 400 : 20); // una conversión: ~40 µs en el ESP32, ~2 µs en el RP2040
            if (number < 0) return 0.706 - (25 - 27) * 0.001721;
            return Math.Clamp(py.Host.PinVolts(number, py.CurrentLine), 0, py.Board.Vcc);
        }

        private static bool IsEsp(PyInterpreter p) => p.IsEsp32;

        public override bool HasAttr(string name) => name is "read" or "read_u16" or "read_uv" or "atten" or "width";

        public override object? GetAttr(string name) => name switch
        {
            "read_u16" => F(name, _ => (long)Math.Round(Volts() / py.Board.Vcc * 65535)),
            "read" => F(name, _ => (long)Math.Round(Volts() / py.Board.Vcc * 4095)),
            "read_uv" => F(name, _ => (long)Math.Round(Volts() * 1e6)),
            "atten" or "width" => F(name, _ => null),
            _ => null,
        };
    }

    internal sealed class TimerObj(PyInterpreter py) : PyNative
    {
        public override string TypeName => "Timer";

        public void Init(PyArgs a)
        {
            Deinit();
            double periodMs = a.Kw.TryGetValue("period", out var p) ? PyOps.ToDouble(p)
                : a.Kw.TryGetValue("freq", out var f) ? 1000 / PyOps.ToDouble(f)
                : throw new PyError("TypeError", "Timer.init() necesita period=milisegundos o freq=hercios");
            if (periodMs <= 0) throw new PyError("ValueError", "el periodo del temporizador tiene que ser mayor que 0");
            bool periodic = !a.Kw.TryGetValue("mode", out var m) || PyOps.ToLong(m) != 0;
            long period = (long)(periodMs * 10_000);
            py._timers.Add(new TimerEntry(this, period, periodic, a.Kw.GetValueOrDefault("callback"), py.Host.Now + period));
        }

        private void Deinit() => py._timers.RemoveAll(t => t.Timer == this);

        public override bool HasAttr(string name) => name is "init" or "deinit";

        public override object? GetAttr(string name) => name switch
        {
            "init" => F(name, a => { Init(a); return null; }),
            "deinit" => F(name, _ => { Deinit(); return null; }),
            _ => null,
        };
    }
}
