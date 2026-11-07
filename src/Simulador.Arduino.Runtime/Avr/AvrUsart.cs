// USART traducido a C# de avr8js (src/peripherals/usart.ts), licencia MIT.
// Copyright (c) Uri Shaked and contributors. Ver THIRD-PARTY-NOTICES.md.
namespace Simulador.Arduino.Runtime.Avr;

/// <summary>Puerto serie del ATmega (USART0): lo que escribe el programa sale por <see cref="ByteTransmitted"/>.</summary>
internal sealed class AvrUsart
{
    private const int UCSRA = 0xc0, UCSRB = 0xc1, UCSRC = 0xc2, UBRRL = 0xc4, UBRRH = 0xc5, UDR = 0xc6;
    private const int RXC = 0x80, TXC = 0x40, UDRE = 0x20, U2X = 0x2, MPCM = 0x1;
    private const int RXCIE = 0x80, TXCIE = 0x40, UDRIE = 0x20, RXEN = 0x10, TXEN = 0x8, UCSZ2 = 0x4;
    private const int UCSZ1 = 0x4, UCSZ0 = 0x2, USBS = 0x8, UPM1 = 0x20;

    private readonly AvrCpu _cpu;
    private readonly AvrInterrupt _rxc = new(0x24, UCSRA, RXC, UCSRB, RXCIE, Constant: true);
    private readonly AvrInterrupt _udre = new(0x26, UCSRA, UDRE, UCSRB, UDRIE);
    private readonly AvrInterrupt _txc = new(0x28, UCSRA, TXC, UCSRB, TXCIE);
    private int _rxByte;

    public AvrUsart(AvrCpu cpu, long frequency)
    {
        _cpu = cpu;
        Frequency = frequency;
        cpu.Data[UCSRA] = UDRE;
        cpu.Data[UCSRC] = UCSZ1 | UCSZ0;

        cpu.WriteHooks[UCSRA] = (value, _, _, _) =>
        {
            cpu.Data[UCSRA] = (cpu.Data[UCSRA] & ~(U2X | MPCM)) | (value & (MPCM | U2X));
            cpu.ClearInterruptByFlag(_txc, value);
            return true;
        };
        cpu.WriteHooks[UCSRB] = (value, old, _, _) =>
        {
            cpu.UpdateInterruptEnable(_rxc, value);
            cpu.UpdateInterruptEnable(_udre, value);
            cpu.UpdateInterruptEnable(_txc, value);
            if ((value & RXEN) != 0 && (old & RXEN) != 0) cpu.ClearInterrupt(_rxc);
            if ((value & TXEN) != 0 && (old & TXEN) == 0) cpu.SetInterruptFlag(_udre);
            cpu.Data[UCSRB] = value;
            return true;
        };
        cpu.ReadHooks[UDR] = _ =>
        {
            int result = _rxByte & 0xff;
            _rxByte = 0;
            cpu.ClearInterrupt(_rxc);
            return result;
        };
        cpu.WriteHooks[UDR] = (value, _, _, _) =>
        {
            ByteTransmitted?.Invoke((byte)value);
            cpu.AddClockEvent(() =>
            {
                cpu.SetInterruptFlag(_udre);
                cpu.SetInterruptFlag(_txc);
            }, CyclesPerChar);
            cpu.ClearInterrupt(_txc);
            cpu.ClearInterrupt(_udre);
            return false;
        };
    }

    public long Frequency { get; }

    public event Action<byte>? ByteTransmitted;

    public bool RxBusy { get; private set; }

    public bool RxEnabled => (_cpu.Data[UCSRB] & RXEN) != 0;

    public int Ubrr => (_cpu.Data[UBRRH] << 8) | _cpu.Data[UBRRL];

    public int Multiplier => (_cpu.Data[UCSRA] & U2X) != 0 ? 8 : 16;

    public long BaudRate => Frequency / (Multiplier * (1 + Ubrr));

    private int BitsPerChar => (((_cpu.Data[UCSRC] & (UCSZ1 | UCSZ0)) >> 1) | (_cpu.Data[UCSRB] & UCSZ2)) switch
    {
        0 => 5,
        1 => 6,
        2 => 7,
        3 => 8,
        _ => 9,
    };

    private long CyclesPerChar
    {
        get
        {
            int symbols = 1 + BitsPerChar + ((_cpu.Data[UCSRC] & USBS) != 0 ? 2 : 1) + ((_cpu.Data[UCSRC] & UPM1) != 0 ? 1 : 0);
            return (long)(Ubrr + 1) * Multiplier * symbols;
        }
    }

    /// <summary>Envía un byte a la placa (llega como si viniera del PC). Devuelve false si el receptor está ocupado o apagado.</summary>
    public bool WriteByte(byte value)
    {
        if (RxBusy || !RxEnabled) return false;
        RxBusy = true;
        _cpu.AddClockEvent(() =>
        {
            RxBusy = false;
            _rxByte = value;
            _cpu.SetInterruptFlag(_rxc);
        }, CyclesPerChar);
        return true;
    }
}
