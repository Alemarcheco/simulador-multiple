using System.Text.RegularExpressions;
using Simulador.Core.Signals;

namespace Simulador.Plc.Runtime.Profiles;

/// <summary>PLC genérico IEC 61131-3 (16 DI, 16 DO, 2 AI, 2 AO). Direcciones %IX0.0, %QX0.0, %MX0.0, %IW64.</summary>
public sealed class GenericIecProfile : PlcProfile
{
    public override string Id => "generico-iec";
    public override string Brand => "Genérico";
    public override string Model => "IEC 61131-3";
    public override int DigitalInputs => 16;
    public override int DigitalOutputs => 16;
    public override int AnalogInputs => 2;
    public override int AnalogOutputs => 2;
    public override string Format(IoAddress a) => a.ToString();
    protected override bool TryParseBrand(string text, out IoAddress address)
    {
        address = default;
        return false;
    }
}

/// <summary>Siemens S7-1200, CPU 1214C DC/DC/DC: 14 DI, 10 DO, 2 AI. Direcciones I0.0, Q0.0, M0.0, IW64, MW10.</summary>
public sealed partial class SiemensS71200Profile : PlcProfile
{
    public override string Id => "siemens-s7-1200";
    public override string Brand => "Siemens";
    public override string Model => "S7-1200 CPU 1214C DC/DC/DC";
    public override int DigitalInputs => 14;
    public override int DigitalOutputs => 10;
    public override int AnalogInputs => 2;

    public override string Format(IoAddress a) => a.Size == IoSize.Bit ? $"{a.Area}{a.Byte}.{a.Bit}" : $"{a.Area}W{a.Byte}";

    [GeneratedRegex(@"^%?([IQM])(\d+)\.([0-7])$", RegexOptions.IgnoreCase)]
    private static partial Regex Bit();

    [GeneratedRegex(@"^%?([IQM])W(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Word();

    protected override bool TryParseBrand(string text, out IoAddress address)
    {
        address = default;
        if (Bit().Match(text) is { Success: true } b)
        {
            address = new(Enum.Parse<IoArea>(b.Groups[1].Value, true), IoSize.Bit, Int(b.Groups[2].Value), Int(b.Groups[3].Value));
            return true;
        }
        if (Word().Match(text) is { Success: true } w)
        {
            address = new(Enum.Parse<IoArea>(w.Groups[1].Value, true), IoSize.Word, Int(w.Groups[2].Value));
            return true;
        }
        return false;
    }
}

/// <summary>
/// Allen-Bradley MicroLogix 1100 (direccionamiento SLC 500): 10 DI, 6 DO, 2 AI.
/// Direcciones I:0/0, O:0/0, B3:0/0 (bits internos), N7:0 (enteros).
/// </summary>
public sealed partial class AllenBradleyMicroLogixProfile : PlcProfile
{
    public override string Id => "allen-bradley-micrologix-1100";
    public override string Brand => "Allen-Bradley";
    public override string Model => "MicroLogix 1100 (SLC 500)";
    public override int DigitalInputs => 10;
    public override int DigitalOutputs => 6;
    public override int AnalogInputs => 2;
    public override CommonType OutputCommon => CommonType.Relay;

    // Palabras de 16 bits: el bit n de la palabra w es el byte canónico 2w + n/8.
    public override string Format(IoAddress a)
    {
        int word = a.Byte / 2, bit = (a.Byte % 2) * 8 + a.Bit;
        return (a.Area, a.Size) switch
        {
            (IoArea.I, IoSize.Bit) => $"I:{word}/{bit}",
            (IoArea.Q, IoSize.Bit) => $"O:{word}/{bit}",
            (IoArea.M, IoSize.Bit) => $"B3:{word}/{bit}",
            (IoArea.I, IoSize.Word) => $"I:{a.Byte / 2}",
            (IoArea.Q, IoSize.Word) => $"O:{a.Byte / 2}",
            _ => $"N7:{a.Byte / 2}",
        };
    }

    // Las analógicas se colocan en la palabra I:4 y siguientes.
    public override IoAddress AnalogInputAddress(int index) => new(IoArea.I, IoSize.Word, 2 * (4 + index));

    [GeneratedRegex(@"^(I|O|B3):(\d+)/(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Bit();

    [GeneratedRegex(@"^(I|O|N7):(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Word();

    protected override bool TryParseBrand(string text, out IoAddress address)
    {
        address = default;
        if (Bit().Match(text) is { Success: true } b)
        {
            int bit = Int(b.Groups[3].Value);
            if (bit > 15) return false;
            var area = b.Groups[1].Value.ToUpperInvariant() switch { "I" => IoArea.I, "O" => IoArea.Q, _ => IoArea.M };
            address = new(area, IoSize.Bit, 2 * Int(b.Groups[2].Value) + bit / 8, bit % 8);
            return true;
        }
        if (Word().Match(text) is { Success: true } w)
        {
            var area = w.Groups[1].Value.ToUpperInvariant() switch { "I" => IoArea.I, "O" => IoArea.Q, _ => IoArea.M };
            address = new(area, IoSize.Word, 2 * Int(w.Groups[2].Value));
            return true;
        }
        return false;
    }
}

/// <summary>
/// Schneider Modicon M221 (TM221CE16T): 9 DI, 7 DO, 2 AI. Direcciones %I0.0, %Q0.0, %M0 (bits), %MW0, %IW0.0.
/// </summary>
public sealed partial class SchneiderM221Profile : PlcProfile
{
    public override string Id => "schneider-m221";
    public override string Brand => "Schneider Electric";
    public override string Model => "Modicon M221 TM221CE16T";
    public override int DigitalInputs => 9;
    public override int DigitalOutputs => 7;
    public override int AnalogInputs => 2;

    public override IoAddress AnalogInputAddress(int index) => new(IoArea.I, IoSize.Word, 2 * index);

    public override string Format(IoAddress a)
    {
        int linear = a.Byte * 8 + a.Bit;
        return (a.Area, a.Size) switch
        {
            (IoArea.M, IoSize.Bit) => $"%M{linear}",
            (IoArea.M, IoSize.Word) => $"%MW{a.Byte / 2}",
            (_, IoSize.Bit) => $"%{a.Area}0.{linear}",
            _ => $"%{a.Area}W0.{a.Byte / 2}",
        };
    }

    [GeneratedRegex(@"^%([IQ])0\.(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IoBit();

    [GeneratedRegex(@"^%M(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MBit();

    [GeneratedRegex(@"^%MW(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MWord();

    [GeneratedRegex(@"^%([IQ])W0\.(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IoWord();

    protected override bool TryParseBrand(string text, out IoAddress address)
    {
        address = default;
        if (IoBit().Match(text) is { Success: true } b)
        {
            int n = Int(b.Groups[2].Value);
            address = new(Enum.Parse<IoArea>(b.Groups[1].Value, true), IoSize.Bit, n / 8, n % 8);
            return true;
        }
        if (MBit().Match(text) is { Success: true } m)
        {
            int n = Int(m.Groups[1].Value);
            address = new(IoArea.M, IoSize.Bit, n / 8, n % 8);
            return true;
        }
        if (MWord().Match(text) is { Success: true } mw)
        {
            address = new(IoArea.M, IoSize.Word, 2 * Int(mw.Groups[1].Value));
            return true;
        }
        if (IoWord().Match(text) is { Success: true } w)
        {
            address = new(Enum.Parse<IoArea>(w.Groups[1].Value, true), IoSize.Word, 2 * Int(w.Groups[2].Value));
            return true;
        }
        return false;
    }
}

/// <summary>
/// Base para PLC de estilo Mitsubishi/Delta: X (entradas) e Y (salidas) en octal, M (relés internos) y D (registros) en
/// decimal.
/// </summary>
public abstract partial class OctalXyProfile : PlcProfile
{
    public override string Format(IoAddress a)
    {
        int linear = a.Byte * 8 + a.Bit;
        return (a.Area, a.Size) switch
        {
            (IoArea.I, IoSize.Bit) => "X" + Convert.ToString(linear, 8),
            (IoArea.Q, IoSize.Bit) => "Y" + Convert.ToString(linear, 8),
            (IoArea.M, IoSize.Bit) => $"M{linear}",
            (IoArea.M, IoSize.Word) => $"D{a.Byte / 2}",
            _ => a.ToString(),
        };
    }

    [GeneratedRegex(@"^([XY])([0-7]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex XY();

    [GeneratedRegex(@"^([MD])(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MD();

    protected override bool TryParseBrand(string text, out IoAddress address)
    {
        address = default;
        if (XY().Match(text) is { Success: true } xy)
        {
            int n = Convert.ToInt32(xy.Groups[2].Value, 8);
            var area = char.ToUpperInvariant(xy.Groups[1].Value[0]) == 'X' ? IoArea.I : IoArea.Q;
            address = new(area, IoSize.Bit, n / 8, n % 8);
            return true;
        }
        if (MD().Match(text) is { Success: true } md)
        {
            int n = Int(md.Groups[2].Value);
            address = char.ToUpperInvariant(md.Groups[1].Value[0]) == 'M'
                ? new(IoArea.M, IoSize.Bit, n / 8, n % 8)
                : new(IoArea.M, IoSize.Word, 2 * n);
            return true;
        }
        return false;
    }
}

/// <summary>Mitsubishi MELSEC iQ-F FX5U-32MT/ES: 16 DI (X0-X17), 16 DO (Y0-Y17).</summary>
public sealed class MitsubishiFx5uProfile : OctalXyProfile
{
    public override string Id => "mitsubishi-fx5u";
    public override string Brand => "Mitsubishi";
    public override string Model => "MELSEC iQ-F FX5U-32MT";
    public override int DigitalInputs => 16;
    public override int DigitalOutputs => 16;
    public override CommonType InputCommon => CommonType.NPN;
}

/// <summary>Delta DVP-14SS2: 8 DI (X0-X7), 6 DO (Y0-Y5).</summary>
public sealed class DeltaDvpProfile : OctalXyProfile
{
    public override string Id => "delta-dvp-14ss2";
    public override string Brand => "Delta";
    public override string Model => "DVP-14SS2";
    public override int DigitalInputs => 8;
    public override int DigitalOutputs => 6;
}

/// <summary>
/// Omron CP1E-N30: 18 DI (0.00-0.11, 1.00-1.05), 12 DO a relé (100.00-100.07, 101.00-101.03).
/// Bits internos W0.00, registros D0.
/// </summary>
public sealed partial class OmronCp1eProfile : PlcProfile
{
    public override string Id => "omron-cp1e";
    public override string Brand => "Omron";
    public override string Model => "CP1E-N30";
    public override int DigitalInputs => 18;
    public override int DigitalOutputs => 12;
    public override CommonType OutputCommon => CommonType.Relay;

    // Canales de 16 bits: el bit b del canal c es el byte canónico 2c + b/8.
    private static IoAddress Channel(IoArea area, int ch, int bit) => new(area, IoSize.Bit, 2 * ch + bit / 8, bit % 8);

    public override IoAddress InputAddress(int index) => Channel(IoArea.I, index / 12, index % 12);
    public override IoAddress OutputAddress(int index) => Channel(IoArea.Q, index / 8, index % 8);

    public override string Format(IoAddress a)
    {
        int ch = a.Byte / 2, bit = (a.Byte % 2) * 8 + a.Bit;
        return (a.Area, a.Size) switch
        {
            (IoArea.I, IoSize.Bit) => $"{ch}.{bit:00}",
            (IoArea.Q, IoSize.Bit) => $"{ch + 100}.{bit:00}",
            (IoArea.M, IoSize.Bit) => $"W{ch}.{bit:00}",
            (IoArea.M, IoSize.Word) => $"D{ch}",
            _ => a.ToString(),
        };
    }

    [GeneratedRegex(@"^(W)?(\d+)\.(\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex Bit();

    [GeneratedRegex(@"^D(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex D();

    protected override bool TryParseBrand(string text, out IoAddress address)
    {
        address = default;
        if (Bit().Match(text) is { Success: true } b)
        {
            int ch = Int(b.Groups[2].Value), bit = Int(b.Groups[3].Value);
            if (bit > 15) return false;
            if (b.Groups[1].Success) address = Channel(IoArea.M, ch, bit);
            else if (ch >= 100) address = Channel(IoArea.Q, ch - 100, bit);
            else address = Channel(IoArea.I, ch, bit);
            return true;
        }
        if (D().Match(text) is { Success: true } d)
        {
            address = new(IoArea.M, IoSize.Word, 2 * Int(d.Groups[1].Value));
            return true;
        }
        return false;
    }
}
