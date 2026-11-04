using System.Globalization;

namespace Simulador.Arduino.Runtime.Sketch;

internal enum VK { Void, Int, Float, Str, Arr, Obj }

/// <summary>Valor del intérprete: entero, real, texto (String), array (por referencia) u objeto (Servo…).</summary>
internal readonly struct Val
{
    public readonly VK K;
    public readonly long I;
    public readonly double F;
    public readonly object? O;

    private Val(VK k, long i, double f, object? o)
    {
        K = k;
        I = i;
        F = f;
        O = o;
    }

    public static readonly Val Void = new(VK.Void, 0, 0, null);
    public static Val Int(long v) => new(VK.Int, v, 0, null);
    public static Val Float(double v) => new(VK.Float, 0, v, null);
    public static Val Str(string v) => new(VK.Str, 0, 0, v);
    public static Val Arr(ArrayObj a) => new(VK.Arr, 0, 0, a);
    public static Val Obj(object o) => new(VK.Obj, 0, 0, o);
    public static Val Bool(bool b) => Int(b ? 1 : 0);

    public bool IsNumber => K is VK.Int or VK.Float;
    public double D => K == VK.Float ? F : I;
    public long L => K == VK.Float ? (long)Math.Truncate(F) : I;
    public string S => O as string ?? "";
    public ArrayObj A => (ArrayObj)O!;

    public bool Truthy => K switch
    {
        VK.Int => I != 0,
        VK.Float => F != 0,
        VK.Str => S.Length > 0,
        _ => O is not null,
    };

    public override string ToString() => K switch
    {
        VK.Int => I.ToString(CultureInfo.InvariantCulture),
        VK.Float => F.ToString("0.00", CultureInfo.InvariantCulture),
        VK.Str => S,
        VK.Arr => A.AsText(),
        VK.Void => "",
        _ => O?.ToString() ?? "",
    };

    /// <summary>Convierte al tipo de una variable, con el desbordamiento real (int de 16 bits: 32767 + 1 = -32768).</summary>
    public static Val Coerce(Val v, CType t, int line)
    {
        if (t.IsString) return v.K == VK.Str ? v : Str(v.K == VK.Arr ? v.A.AsText() : v.ToString());
        if (t.IsObject || t.IsVoid) return v;
        if (v.K == VK.Arr) return v;
        if (!v.IsNumber) throw new SketchRuntimeException($"No se puede guardar un texto en una variable {t.Name}.", line);
        if (t.IsBool) return Bool(v.Truthy);
        if (t.IsFloat) return Float(t.Bits == 32 ? (float)v.D : v.D);
        return Int(Wrap(v.K == VK.Float ? (long)Math.Truncate(v.F) : v.I, t.Bits, t.Unsigned));
    }

    public static long Wrap(long v, int bits, bool unsigned)
    {
        if (bits >= 64) return v;
        long mask = (1L << bits) - 1;
        v &= mask;
        if (!unsigned && (v & (1L << (bits - 1))) != 0) v -= 1L << bits;
        return v;
    }
}

/// <summary>Variable con su tipo: el valor se convierte al guardarlo.</summary>
internal sealed class Slot(CType type, Val value)
{
    public CType Type { get; } = type;
    public Val Value { get; set; } = value;
    public bool IsConst { get; set; }
}

internal sealed class ArrayObj(CType element, Val[] items)
{
    public CType Element { get; } = element;
    public Val[] Items { get; } = items;

    /// <summary>Un array de char se imprime como texto hasta el primer '\0'.</summary>
    public string AsText()
    {
        if (Element.Bits != 8 || Element.IsBool) return "[array]";
        var chars = Items.TakeWhile(v => v.L != 0).Select(v => (char)(v.L & 0xFF)).ToArray();
        return new string(chars);
    }
}

/// <summary>Objeto <c>Servo</c> de la librería Servo.h.</summary>
internal sealed class ServoObj
{
    public int Pin { get; set; } = -1;
    public double Angle { get; set; } = 90;
}
