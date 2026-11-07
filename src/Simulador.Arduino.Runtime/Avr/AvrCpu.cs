// Núcleo de la CPU AVR traducido a C# de avr8js (src/cpu/cpu.ts e interrupt.ts), licencia MIT.
// Copyright (c) Uri Shaked and contributors. Ver THIRD-PARTY-NOTICES.md.
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Simulador.Arduino.Runtime.Avr;

/// <summary>Memoria de datos accesible con enteros: al escribir se queda con los 8 bits bajos, como un registro real.</summary>
internal sealed class ByteMemory(int size)
{
    public readonly byte[] Bytes = new byte[size];

    public int Length => Bytes.Length;

    public int this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Bytes[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Bytes[index] = unchecked((byte)value);
    }
}

/// <summary>Memoria de programa (palabras de 16 bits).</summary>
internal sealed class ProgramMemory(ushort[] words)
{
    public readonly ushort[] Words = words;

    public int this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Words[index];
    }
}

/// <summary>Una fuente de interrupción: dirección del vector y bits de bandera y de habilitación.</summary>
internal sealed record AvrInterrupt(int Address, int FlagRegister, int FlagMask, int EnableRegister, int EnableMask, bool Constant = false, bool InverseFlag = false);

/// <summary>Lee un registro de E/S: devuelve el valor que verá el programa.</summary>
internal delegate int ReadHook(int address);

/// <summary>Escribe un registro de E/S. Devuelve true si el periférico ya guardó el valor.</summary>
internal delegate bool WriteHook(int value, int oldValue, int address, int mask);

/// <summary>CPU AVR de 8 bits: registros, memoria, pila, interrupciones y eventos de reloj de los periféricos.</summary>
internal sealed class AvrCpu
{
    private const int RegisterSpace = 0x100;
    private const int MaxInterrupts = 128;

    private sealed class ClockEvent
    {
        public long Cycles;
        public Action Callback = null!;
        public ClockEvent? Next;
    }

    private readonly AvrInterrupt?[] _pending = new AvrInterrupt?[MaxInterrupts];
    private readonly Stack<ClockEvent> _pool = new();
    private ClockEvent? _nextClockEvent;
    private int _nextInterrupt = -1;
    private int _maxInterrupt;

    public AvrCpu(ushort[] program, int sramBytes = 2048)
    {
        Prog = new ProgramMemory(program);
        Data = new ByteMemory(sramBytes + RegisterSpace);
        ReadHooks = new ReadHook?[Data.Length];
        WriteHooks = new WriteHook?[Data.Length];
        Pc22Bits = program.Length * 2 > 0x20000;
        Reset();
    }

    public ByteMemory Data { get; }
    public ProgramMemory Prog { get; }
    public ReadHook?[] ReadHooks { get; }
    public WriteHook?[] WriteHooks { get; }
    public bool Pc22Bits { get; }

    /// <summary>Contador de programa (en palabras).</summary>
    public int PC;

    /// <summary>Ciclos de reloj desde el arranque.</summary>
    public long Cycles;

    public int ProgLength => Prog.Words.Length;
    public int ProgBytesLength => Prog.Words.Length * 2;

    /// <summary>Lo llama la instrucción WDR (reinicio del perro guardián).</summary>
    public Action? WatchdogReset { get; set; }

    public void OnWatchdogReset() => WatchdogReset?.Invoke();

    public void Reset()
    {
        SP = Data.Length - 1;
        PC = 0;
        Array.Fill(_pending, null);
        _nextInterrupt = -1;
        _nextClockEvent = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Get16(int address) => BinaryPrimitives.ReadUInt16LittleEndian(Data.Bytes.AsSpan(address));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set16(int address, int value) => BinaryPrimitives.WriteUInt16LittleEndian(Data.Bytes.AsSpan(address), unchecked((ushort)value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetInt8(int address) => unchecked((sbyte)Data.Bytes[address]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ProgByte(int address)
    {
        int w = Prog.Words[(address >> 1) % Prog.Words.Length];
        return (address & 1) == 0 ? w & 0xff : w >> 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadData(int address)
    {
        if (address >= 32 && address < ReadHooks.Length && ReadHooks[address] is { } hook) return hook(address);
        return address < Data.Length ? Data[address] : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteData(int address, int value, int mask = 0xff)
    {
        if (address >= Data.Length) return;
        if (WriteHooks[address] is { } hook && hook(value & 0xff, Data[address], address, mask)) return;
        Data[address] = value;
    }

    public int SP
    {
        get => Get16(93);
        set => Set16(93, value);
    }

    public int SREG => Data[95];

    public bool InterruptsEnabled => (Data[95] & 0x80) != 0;

    public void SetInterruptFlag(AvrInterrupt i)
    {
        if (i.InverseFlag) Data[i.FlagRegister] &= ~i.FlagMask;
        else Data[i.FlagRegister] |= i.FlagMask;
        if ((Data[i.EnableRegister] & i.EnableMask) != 0) QueueInterrupt(i);
    }

    public void UpdateInterruptEnable(AvrInterrupt i, int registerValue)
    {
        if ((registerValue & i.EnableMask) != 0)
        {
            bool bitSet = (Data[i.FlagRegister] & i.FlagMask) != 0;
            if (i.InverseFlag ? !bitSet : bitSet) QueueInterrupt(i);
        }
        else ClearInterrupt(i, false);
    }

    public void QueueInterrupt(AvrInterrupt i)
    {
        _pending[i.Address] = i;
        if (_nextInterrupt == -1 || _nextInterrupt > i.Address) _nextInterrupt = i.Address;
        if (i.Address > _maxInterrupt) _maxInterrupt = i.Address;
    }

    public void ClearInterrupt(AvrInterrupt i, bool clearFlag = true)
    {
        if (clearFlag) Data[i.FlagRegister] &= ~i.FlagMask;
        if (_pending[i.Address] is null) return;
        _pending[i.Address] = null;
        if (_nextInterrupt == i.Address)
        {
            _nextInterrupt = -1;
            for (int a = i.Address + 1; a <= _maxInterrupt; a++)
            {
                if (_pending[a] is not null)
                {
                    _nextInterrupt = a;
                    break;
                }
            }
        }
    }

    public void ClearInterruptByFlag(AvrInterrupt i, int registerValue)
    {
        if ((registerValue & i.FlagMask) != 0)
        {
            Data[i.FlagRegister] &= ~i.FlagMask;
            ClearInterrupt(i);
        }
    }

    /// <summary>Programa una llamada dentro de <paramref name="cycles"/> ciclos (al menos 1).</summary>
    public Action AddClockEvent(Action callback, long cycles)
    {
        long at = Cycles + Math.Max(1, cycles);
        var entry = _pool.Count > 0 ? _pool.Pop() : new ClockEvent();
        entry.Cycles = at;
        entry.Callback = callback;
        ClockEvent? ev = _nextClockEvent, last = null;
        while (ev is not null && ev.Cycles < at)
        {
            last = ev;
            ev = ev.Next;
        }
        if (last is not null) last.Next = entry;
        else _nextClockEvent = entry;
        entry.Next = ev;
        return callback;
    }

    public bool UpdateClockEvent(Action callback, long cycles)
    {
        if (!ClearClockEvent(callback)) return false;
        AddClockEvent(callback, cycles);
        return true;
    }

    public bool ClearClockEvent(Action callback)
    {
        ClockEvent? ev = _nextClockEvent, last = null;
        while (ev is not null)
        {
            if (ReferenceEquals(ev.Callback, callback))
            {
                if (last is not null) last.Next = ev.Next;
                else _nextClockEvent = ev.Next;
                if (_pool.Count < 10) _pool.Push(ev);
                return true;
            }
            last = ev;
            ev = ev.Next;
        }
        return false;
    }

    /// <summary>Atiende el evento de reloj que toque y, si hay interrupciones habilitadas y pendientes, salta a la primera.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick()
    {
        var ev = _nextClockEvent;
        if (ev is not null && ev.Cycles <= Cycles)
        {
            _nextClockEvent = ev.Next;
            ev.Callback();
            if (_pool.Count < 10) _pool.Push(ev);
        }
        if (_nextInterrupt >= 0 && InterruptsEnabled)
        {
            var i = _pending[_nextInterrupt]!;
            Interrupt(i.Address);
            if (!i.Constant) ClearInterrupt(i);
        }
    }

    private void Interrupt(int address)
    {
        int sp = SP;
        Data[sp] = PC & 0xff;
        Data[sp - 1] = (PC >> 8) & 0xff;
        if (Pc22Bits) Data[sp - 2] = (PC >> 16) & 0xff;
        SP = sp - (Pc22Bits ? 3 : 2);
        Data[95] &= 0x7f;
        Cycles += 2;
        PC = address;
    }
}
