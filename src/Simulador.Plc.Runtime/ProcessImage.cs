namespace Simulador.Plc.Runtime;

/// <summary>Zona de memoria del PLC.</summary>
public enum IoArea
{
    /// <summary>Entradas.</summary>
    I,
    /// <summary>Salidas.</summary>
    Q,
    /// <summary>Marcas (memoria interna).</summary>
    M,
}

/// <summary>Tamaño del acceso: bit o palabra (16 bits, usada para analógicas y enteros).</summary>
public enum IoSize { Bit, Word }

/// <summary>
/// Dirección canónica, independiente de la marca. Para bits: byte y bit (0-7). Para palabras: número de byte inicial,
/// como en IEC/Siemens (%IW64, %MW10).
/// </summary>
public readonly record struct IoAddress(IoArea Area, IoSize Size, int Byte, int Bit = 0)
{
    public static IoAddress InputBit(int b, int bit) => new(IoArea.I, IoSize.Bit, b, bit);
    public static IoAddress OutputBit(int b, int bit) => new(IoArea.Q, IoSize.Bit, b, bit);
    public static IoAddress MemoryBit(int b, int bit) => new(IoArea.M, IoSize.Bit, b, bit);

    /// <summary>Formato IEC 61131-3: %IX0.0, %QW2, %MX1.3.</summary>
    public override string ToString() => Size == IoSize.Bit
        ? $"%{Area}X{Byte}.{Bit}"
        : $"%{Area}W{Byte}";
}

/// <summary>
/// Imagen de proceso: copia de entradas que el programa lee durante todo el ciclo, salidas que se publican al final y
/// marcas internas.
/// </summary>
public sealed class ProcessImage
{
    private readonly Dictionary<(IoArea, int, int), bool> _bits = [];
    private readonly Dictionary<(IoArea, int), long> _words = [];

    public bool GetBit(IoAddress a) => _bits.TryGetValue((a.Area, a.Byte, a.Bit), out var v) && v;

    public void SetBit(IoAddress a, bool value) => _bits[(a.Area, a.Byte, a.Bit)] = value;

    public long GetWord(IoAddress a) => _words.TryGetValue((a.Area, a.Byte), out var v) ? v : 0;

    public void SetWord(IoAddress a, long value) => _words[(a.Area, a.Byte)] = value;

    public PlcValue Read(IoAddress a) =>
        a.Size == IoSize.Bit ? PlcValue.FromBool(GetBit(a)) : PlcValue.FromInt(GetWord(a));

    public void Write(IoAddress a, PlcValue v)
    {
        if (a.Size == IoSize.Bit) SetBit(a, v.AsBool);
        else SetWord(a, v.AsInt);
    }

    public void Clear()
    {
        _bits.Clear();
        _words.Clear();
    }

    /// <summary>Pone a cero las salidas (al pasar a STOP).</summary>
    public void ClearOutputs()
    {
        foreach (var k in _bits.Keys.Where(k => k.Item1 == IoArea.Q).ToList()) _bits[k] = false;
        foreach (var k in _words.Keys.Where(k => k.Item1 == IoArea.Q).ToList()) _words[k] = 0;
    }
}
