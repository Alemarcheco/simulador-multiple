namespace Simulador.Arduino.Runtime.Avr;

/// <summary>Cambio de nivel de una salida digital, en ciclos de reloj.</summary>
internal readonly record struct PinEdge(long Cycle, bool High);

/// <summary>
/// Microcontrolador ATmega328P (Arduino Uno y Nano) a 16 MHz: CPU, puertos B/C/D, temporizadores 0-2, USART0, ADC e
/// interrupciones externas INT0/INT1. Los pines se numeran como en Arduino: D0-D7 = PD0-7, D8-D13 = PB0-5,
/// A0-A5 = PC0-5 (14-19); en el Nano A6 y A7 (20, 21) solo van al ADC.
/// </summary>
internal sealed class Atmega328
{
    public const long Frequency = 16_000_000;
    private const int PinCount = 20;

    // Registros de los puertos: PINx, DDRx, PORTx.
    private static readonly int[] PinRegs = [0x23, 0x26, 0x29];
    private const int PortB = 0, PortC = 1, PortD = 2;
    private const int ADCL = 0x78, ADCH = 0x79, ADCSRA = 0x7a, ADMUX = 0x7c;
    private const int EICRA = 0x69, EIMSK = 0x3d, EIFR = 0x3c;

    private readonly Func<int, bool> _readInput;
    private readonly Func<int, double> _readAnalog;
    private readonly AvrInterrupt _adcInterrupt = new(0x2a, ADCSRA, 0x10, ADCSRA, 0x08);
    private readonly AvrInterrupt _int0 = new(0x02, EIFR, 1, EIMSK, 1);
    private readonly AvrInterrupt _int1 = new(0x04, EIFR, 2, EIMSK, 2);
    private readonly bool[] _lastInput = new bool[PinCount];

    /// <param name="program">Programa en palabras de 16 bits (32 KB = 16384 palabras).</param>
    /// <param name="readInput">Nivel lógico que llega a un pin de entrada (número Arduino).</param>
    /// <param name="readAnalog">Tensión en un canal del ADC (0-7).</param>
    public Atmega328(ushort[] program, Func<int, bool> readInput, Func<int, double> readAnalog)
    {
        var flash = new ushort[16384];
        Array.Copy(program, flash, Math.Min(program.Length, flash.Length));
        Cpu = new AvrCpu(flash, 2048);
        _readInput = readInput;
        _readAnalog = readAnalog;
        Timer0 = new AvrTimer(Cpu, TimerConfig.Timer0);
        Timer1 = new AvrTimer(Cpu, TimerConfig.Timer1);
        Timer2 = new AvrTimer(Cpu, TimerConfig.Timer2);
        Usart = new AvrUsart(Cpu, Frequency);
        Edges = new List<PinEdge>[PinCount];
        for (int i = 0; i < PinCount; i++) Edges[i] = [];

        for (int port = 0; port < 3; port++)
        {
            int p = port, pinReg = PinRegs[port];
            Cpu.ReadHooks[pinReg] = _ => ReadPins(p);
            // Escribir un 1 en PINx invierte ese bit de PORTx.
            Cpu.WriteHooks[pinReg] = (value, _, _, _) => { WritePort(p, Cpu.Data[pinReg + 2] ^ value, Cpu.Data[pinReg + 1]); return true; };
            Cpu.WriteHooks[pinReg + 1] = (value, _, _, _) => { WritePort(p, Cpu.Data[pinReg + 2], value); return true; };
            Cpu.WriteHooks[pinReg + 2] = (value, _, _, _) => { WritePort(p, value, Cpu.Data[pinReg + 1]); return true; };
        }

        Cpu.WriteHooks[ADCSRA] = (value, _, _, _) =>
        {
            // Escribir 1 en ADIF la borra.
            if ((value & 0x10) != 0) Cpu.ClearInterrupt(_adcInterrupt);
            Cpu.Data[ADCSRA] = (value & ~0x10) | (Cpu.Data[ADCSRA] & 0x10);
            Cpu.UpdateInterruptEnable(_adcInterrupt, value);
            if ((value & 0xc0) == 0xc0) StartConversion();
            return true;
        };
        Cpu.WriteHooks[EIMSK] = (value, _, _, _) =>
        {
            Cpu.Data[EIMSK] = value & 3;
            Cpu.UpdateInterruptEnable(_int0, value);
            Cpu.UpdateInterruptEnable(_int1, value);
            return true;
        };
        Cpu.WriteHooks[EIFR] = (value, _, _, _) =>
        {
            Cpu.ClearInterruptByFlag(_int0, value);
            Cpu.ClearInterruptByFlag(_int1, value);
            return true;
        };
    }

    public AvrCpu Cpu { get; }
    public AvrTimer Timer0 { get; }
    public AvrTimer Timer1 { get; }
    public AvrTimer Timer2 { get; }
    public AvrUsart Usart { get; }

    /// <summary>Cambios de las salidas digitales desde la última vez que se vaciaron.</summary>
    public List<PinEdge>[] Edges { get; }

    /// <summary>Se llama cuando un pin cambia de modo (DDR) o de pull-up/nivel (PORT).</summary>
    public event Action<int>? PinConfigured;

    public static (int Port, int Bit) PortOf(int pin) => pin switch
    {
        < 8 => (PortD, pin),
        < 14 => (PortB, pin - 8),
        _ => (PortC, pin - 14),
    };

    private static int PinOf(int port, int bit) => port switch
    {
        PortD => bit,
        PortB => bit < 6 ? 8 + bit : -1,
        _ => bit < 6 ? 14 + bit : -1,
    };

    public bool IsOutput(int pin)
    {
        if (pin >= PinCount) return false;
        var (port, bit) = PortOf(pin);
        return (Cpu.Data[PinRegs[port] + 1] & (1 << bit)) != 0;
    }

    public bool PortBit(int pin)
    {
        if (pin >= PinCount) return false;
        var (port, bit) = PortOf(pin);
        return (Cpu.Data[PinRegs[port] + 2] & (1 << bit)) != 0;
    }

    private int ReadPins(int port)
    {
        int reg = PinRegs[port], ddr = Cpu.Data[reg + 1], data = Cpu.Data[reg + 2], value = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            int mask = 1 << bit, pin = PinOf(port, bit);
            bool level = (ddr & mask) != 0 ? (data & mask) != 0 : pin >= 0 && _readInput(pin);
            if (level) value |= mask;
        }
        return value;
    }

    private void WritePort(int port, int data, int ddr)
    {
        int reg = PinRegs[port];
        int oldData = Cpu.Data[reg + 2], oldDdr = Cpu.Data[reg + 1];
        Cpu.Data[reg + 2] = data;
        Cpu.Data[reg + 1] = ddr;
        int changed = (oldData ^ data) | (oldDdr ^ ddr);
        if (changed == 0) return;
        for (int bit = 0; bit < 8; bit++)
        {
            int mask = 1 << bit;
            if ((changed & mask) == 0) continue;
            int pin = PinOf(port, bit);
            if (pin < 0) continue;
            bool wasHigh = (oldDdr & mask) != 0 && (oldData & mask) != 0;
            bool isHigh = (ddr & mask) != 0 && (data & mask) != 0;
            if ((ddr & mask) != 0 && wasHigh != isHigh)
            {
                var list = Edges[pin];
                if (list.Count < 8192) list.Add(new PinEdge(Cpu.Cycles, isHigh));
            }
            if (((oldDdr ^ ddr) & mask) != 0 || (ddr & mask) == 0) PinConfigured?.Invoke(pin);
        }
    }

    private void StartConversion()
    {
        int prescaler = 1 << Math.Max(1, Cpu.Data[ADCSRA] & 7);
        Cpu.AddClockEvent(() =>
        {
            int admux = Cpu.Data[ADMUX];
            int channel = admux & 0xf;
            double reference = (admux >> 6) == 3 ? 1.1 : 5.0;
            double volts = channel switch
            {
                < 8 => _readAnalog(channel),
                14 => 1.1,
                _ => 0,
            };
            int result = (int)Math.Clamp(Math.Round(volts / reference * 1024), 0, 1023);
            if ((admux & 0x20) != 0) result <<= 6; // ADLAR: resultado alineado a la izquierda
            Cpu.Data[ADCL] = result & 0xff;
            Cpu.Data[ADCH] = (result >> 8) & 0xff;
            Cpu.Data[ADCSRA] &= ~0x40; // fin de conversión: ADSC = 0
            Cpu.SetInterruptFlag(_adcInterrupt);
        }, 13L * prescaler);
    }

    /// <summary>Revisa las entradas INT0 (D2) e INT1 (D3) y dispara su interrupción según EICRA.</summary>
    public void CheckExternalInterrupts()
    {
        for (int n = 0; n < 2; n++)
        {
            int pin = 2 + n;
            if (IsOutput(pin)) continue;
            bool level = _readInput(pin), last = _lastInput[pin];
            _lastInput[pin] = level;
            int sense = (Cpu.Data[EICRA] >> (2 * n)) & 3;
            bool fire = sense switch
            {
                0 => !level,
                1 => level != last,
                2 => last && !level,
                _ => !last && level,
            };
            if (fire) Cpu.SetInterruptFlag(n == 0 ? _int0 : _int1);
        }
    }

    /// <summary>Ejecuta instrucciones hasta llegar al ciclo indicado.</summary>
    public void RunUntil(long cycle)
    {
        var cpu = Cpu;
        while (cpu.Cycles < cycle)
        {
            AvrInstruction.Execute(cpu);
            cpu.Tick();
        }
    }
}
