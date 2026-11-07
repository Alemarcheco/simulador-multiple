namespace Simulador.Arduino.Runtime.Avr;

/// <summary>Direcciones y vectores de un temporizador del ATmega328P.</summary>
internal sealed record TimerConfig(
    int Bits, int TIFR, int TIMSK, int TCCRA, int TCCRB, int TCNT, int OCRA, int OCRB, int ICR,
    int OvfVector, int CompAVector, int CompBVector, int[] Dividers)
{
    private static readonly int[] Dividers01 = [0, 1, 8, 64, 256, 1024, 0, 0];

    public static readonly TimerConfig Timer0 = new(8, 0x35, 0x6e, 0x44, 0x45, 0x46, 0x47, 0x48, 0, 0x20, 0x1c, 0x1e, Dividers01);
    public static readonly TimerConfig Timer1 = new(16, 0x36, 0x6f, 0x80, 0x81, 0x84, 0x88, 0x8a, 0x86, 0x1a, 0x16, 0x18, Dividers01);
    public static readonly TimerConfig Timer2 = new(8, 0x37, 0x70, 0xb0, 0xb1, 0xb2, 0xb3, 0xb4, 0, 0x12, 0x0e, 0x10, [0, 1, 8, 32, 64, 128, 256, 1024]);
}

internal enum TimerMode { Normal, PhaseCorrect, Ctc, FastPwm }

/// <summary>
/// Temporizador/contador de 8 o 16 bits. Cuenta en bloque (sin ejecutar ciclo a ciclo): calcula cuándo llegará al
/// siguiente desbordamiento o coincidencia y programa un evento de reloj para ese instante. Modos: normal, CTC, PWM
/// rápido y PWM de fase correcta (la salida PWM en los pines la calcula la placa a partir de los registros).
/// </summary>
internal sealed class AvrTimer
{
    private const int TOV = 1, OCFA = 2, OCFB = 4;

    private readonly AvrCpu _cpu;
    private readonly AvrInterrupt _ovf, _compA, _compB;
    private readonly Action _event;
    private long _baseCycle;
    private int _basePos;   // posición en la secuencia de cuenta (en fase correcta: 0…2·TOP-1)
    private int _divider;
    private int _temp;      // registro temporal para los accesos de 16 bits
    private int _pendingFlags;

    public AvrTimer(AvrCpu cpu, TimerConfig config)
    {
        _cpu = cpu;
        Config = config;
        _ovf = new(config.OvfVector, config.TIFR, TOV, config.TIMSK, TOV);
        _compA = new(config.CompAVector, config.TIFR, OCFA, config.TIMSK, OCFA);
        _compB = new(config.CompBVector, config.TIFR, OCFB, config.TIMSK, OCFB);
        _event = OnEvent;
        Max = config.Bits == 16 ? 0xffff : 0xff;

        cpu.WriteHooks[config.TCCRA] = (value, _, _, _) => { Sync(); cpu.Data[config.TCCRA] = value; Reconfigure(); return true; };
        cpu.WriteHooks[config.TCCRB] = (value, _, _, _) => { Sync(); cpu.Data[config.TCCRB] = value & 0x1f; Reconfigure(); return true; };
        cpu.WriteHooks[config.TIMSK] = (value, _, _, _) =>
        {
            cpu.Data[config.TIMSK] = value;
            cpu.UpdateInterruptEnable(_ovf, value);
            cpu.UpdateInterruptEnable(_compA, value);
            cpu.UpdateInterruptEnable(_compB, value);
            Sync();
            Schedule();
            return true;
        };
        cpu.WriteHooks[config.TIFR] = (value, _, _, _) =>
        {
            cpu.ClearInterruptByFlag(_ovf, value);
            cpu.ClearInterruptByFlag(_compA, value);
            cpu.ClearInterruptByFlag(_compB, value);
            return true;
        };

        if (config.Bits == 8)
        {
            cpu.ReadHooks[config.TCNT] = _ => Count;
            cpu.WriteHooks[config.TCNT] = (value, _, _, _) => { SetCount(value); return true; };
            cpu.WriteHooks[config.OCRA] = (value, _, _, _) => { Sync(); cpu.Data[config.OCRA] = value; Schedule(); return true; };
            cpu.WriteHooks[config.OCRB] = (value, _, _, _) => { Sync(); cpu.Data[config.OCRB] = value; Schedule(); return true; };
        }
        else
        {
            // Registros de 16 bits: se lee primero el byte bajo (que guarda el alto en TEMP) y se escribe primero el alto.
            cpu.ReadHooks[config.TCNT] = _ => { int c = Count; _temp = c >> 8; return c & 0xff; };
            cpu.ReadHooks[config.TCNT + 1] = _ => _temp;
            cpu.WriteHooks[config.TCNT + 1] = (value, _, _, _) => { _temp = value; return true; };
            cpu.WriteHooks[config.TCNT] = (value, _, _, _) => { SetCount((_temp << 8) | value); return true; };
            foreach (int reg in new[] { config.OCRA, config.OCRB, config.ICR })
            {
                int r = reg;
                cpu.WriteHooks[r + 1] = (value, _, _, _) => { _temp = value; return true; };
                cpu.WriteHooks[r] = (value, _, _, _) =>
                {
                    Sync();
                    cpu.Data[r] = value;
                    cpu.Data[r + 1] = _temp;
                    Schedule();
                    return true;
                };
            }
        }
    }

    public TimerConfig Config { get; }
    public int Max { get; }

    public int WgmBits => Config.Bits == 16
        ? (_cpu.Data[Config.TCCRA] & 3) | ((_cpu.Data[Config.TCCRB] >> 1) & 0xc)
        : (_cpu.Data[Config.TCCRA] & 3) | ((_cpu.Data[Config.TCCRB] >> 1) & 4);

    public int OcrA => Config.Bits == 16 ? _cpu.Get16(Config.OCRA) : _cpu.Data[Config.OCRA];
    public int OcrB => Config.Bits == 16 ? _cpu.Get16(Config.OCRB) : _cpu.Data[Config.OCRB];
    private int Icr => Config.Bits == 16 ? _cpu.Get16(Config.ICR) : 0;

    public TimerMode Mode => Config.Bits == 16
        ? WgmBits switch
        {
            0 => TimerMode.Normal,
            1 or 2 or 3 or 8 or 9 or 10 or 11 => TimerMode.PhaseCorrect,
            4 or 12 => TimerMode.Ctc,
            5 or 6 or 7 or 14 or 15 => TimerMode.FastPwm,
            _ => TimerMode.Normal,
        }
        : WgmBits switch
        {
            1 or 5 => TimerMode.PhaseCorrect,
            2 => TimerMode.Ctc,
            3 or 7 => TimerMode.FastPwm,
            _ => TimerMode.Normal,
        };

    /// <summary>Valor máximo de la cuenta en el modo actual.</summary>
    public int Top => Config.Bits == 16
        ? WgmBits switch
        {
            1 or 5 => 0xff,
            2 or 6 => 0x1ff,
            3 or 7 => 0x3ff,
            4 or 9 or 11 or 15 => OcrA,
            8 or 10 or 12 or 14 => Icr,
            _ => 0xffff,
        }
        : WgmBits switch
        {
            2 or 5 or 7 => OcrA,
            _ => 0xff,
        };

    private int Period => Mode == TimerMode.PhaseCorrect ? Math.Max(2 * Top, 1) : Top + 1;

    private int ValueAt(int pos)
    {
        int top = Top;
        return Mode == TimerMode.PhaseCorrect && pos > top ? 2 * top - pos : pos;
    }

    /// <summary>Cuenta actual (lo que lee el programa en TCNT).</summary>
    public int Count
    {
        get
        {
            Sync();
            return ValueAt(_basePos);
        }
    }

    private void SetCount(int value)
    {
        Sync();
        _basePos = Math.Min(value, Math.Max(Period - 1, 0));
        Schedule();
    }

    /// <summary>Lleva la cuenta hasta el ciclo actual (sin marcar banderas: eso lo hacen los eventos).</summary>
    private void Sync()
    {
        if (_divider == 0)
        {
            _baseCycle = _cpu.Cycles;
            return;
        }
        long ticks = (_cpu.Cycles - _baseCycle) / _divider;
        if (ticks <= 0) return;
        _baseCycle += ticks * _divider;
        _basePos = (int)((_basePos + ticks) % Period);
    }

    private void Reconfigure()
    {
        _divider = Config.Dividers[_cpu.Data[Config.TCCRB] & 7];
        _baseCycle = _cpu.Cycles;
        if (_basePos >= Period) _basePos = 0;
        Schedule();
    }

    /// <summary>Busca el próximo desbordamiento o coincidencia que interese y programa un evento para ese momento.</summary>
    private void Schedule()
    {
        _cpu.ClearClockEvent(_event);
        _pendingFlags = 0;
        if (_divider == 0) return;
        int period = Period, top = Top, pos = _basePos;
        long best = long.MaxValue;
        int flags = 0;

        void Consider(int targetPos, int flag)
        {
            if (targetPos < 0 || targetPos >= period) return;
            long d = ((targetPos - pos) % period + period) % period;
            if (d == 0) d = period;
            if (d < best) { best = d; flags = flag; }
            else if (d == best) flags |= flag;
        }

        int enabled = _cpu.Data[Config.TIMSK];
        var mode = Mode;
        // Desbordamiento: en fase correcta al volver a 0; en normal y PWM rápido al pasar de TOP a 0; en CTC solo si TOP es el máximo.
        if (mode != TimerMode.Ctc || top == Max) Consider(0, TOV);
        foreach (var (flag, ocr) in new[] { (OCFA, OcrA), (OCFB, OcrB) })
        {
            if ((enabled & flag) == 0 || ocr > top) continue;
            Consider(ocr, flag);
            if (mode == TimerMode.PhaseCorrect && ocr > 0 && ocr < top) Consider(2 * top - ocr, flag);
        }
        if (flags == 0) return;
        _pendingFlags = flags;
        long at = _baseCycle + best * _divider;
        _cpu.AddClockEvent(_event, Math.Max(1, at - _cpu.Cycles));
    }

    private void OnEvent()
    {
        int flags = _pendingFlags;
        Sync();
        if ((flags & TOV) != 0) _cpu.SetInterruptFlag(_ovf);
        if ((flags & OCFA) != 0) _cpu.SetInterruptFlag(_compA);
        if ((flags & OCFB) != 0) _cpu.SetInterruptFlag(_compB);
        Schedule();
    }

    /// <summary>Fracción del tiempo en alto de la salida OCnA/OCnB, o null si el temporizador no controla ese pin.</summary>
    public double? PwmDuty(bool channelA)
    {
        int com = (_cpu.Data[Config.TCCRA] >> (channelA ? 6 : 4)) & 3;
        if (com < 2 || _divider == 0) return null;
        var mode = Mode;
        if (mode is not (TimerMode.FastPwm or TimerMode.PhaseCorrect)) return null;
        int top = Top, ocr = channelA ? OcrA : OcrB;
        if (top <= 0) return null;
        double duty = mode == TimerMode.FastPwm ? Math.Min(ocr + 1, top + 1) / (double)(top + 1) : Math.Min(ocr, top) / (double)top;
        return com == 3 ? 1 - duty : duty;
    }
}
