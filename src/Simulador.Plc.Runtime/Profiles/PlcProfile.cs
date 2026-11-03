using System.Globalization;
using System.Text.RegularExpressions;
using Simulador.Core.Signals;

namespace Simulador.Plc.Runtime.Profiles;

/// <summary>
/// Perfil de una marca/modelo de PLC: cuántas entradas y salidas trae la CPU y cómo se escriben sus direcciones.
/// Todas las marcas comparten el mismo motor; el perfil solo traduce la nomenclatura a direcciones canónicas.
/// </summary>
/// <remarks>Las vistas de bit y de palabra de una misma zona no se solapan (simplificación de la fase 1).</remarks>
public abstract class PlcProfile
{
    /// <summary>Identificador estable, p. ej. "siemens-s7-1200".</summary>
    public abstract string Id { get; }
    public abstract string Brand { get; }
    public abstract string Model { get; }
    public abstract int DigitalInputs { get; }
    public abstract int DigitalOutputs { get; }
    public virtual int AnalogInputs => 0;
    public virtual int AnalogOutputs => 0;

    /// <summary>Tensión nominal de las entradas digitales (V DC).</summary>
    public virtual double InputNominalVolts => 24;
    public virtual CommonType InputCommon => CommonType.PNP;
    /// <summary>Salidas a transistor (PNP) o a relé.</summary>
    public virtual CommonType OutputCommon => CommonType.PNP;

    /// <summary>Tiempo de ciclo por defecto.</summary>
    public virtual TimeSpan DefaultCycleTime => TimeSpan.FromMilliseconds(10);

    public string DisplayName => $"{Brand} {Model}";

    public virtual IoAddress InputAddress(int index) => new(IoArea.I, IoSize.Bit, index / 8, index % 8);
    public virtual IoAddress OutputAddress(int index) => new(IoArea.Q, IoSize.Bit, index / 8, index % 8);
    public virtual IoAddress AnalogInputAddress(int index) => new(IoArea.I, IoSize.Word, 64 + 2 * index);
    public virtual IoAddress AnalogOutputAddress(int index) => new(IoArea.Q, IoSize.Word, 64 + 2 * index);

    /// <summary>Escribe la dirección con la nomenclatura de la marca.</summary>
    public abstract string Format(IoAddress address);

    /// <summary>Lee una dirección escrita con la nomenclatura de la marca (también acepta el formato IEC con %).</summary>
    public bool TryParse(string text, out IoAddress address)
    {
        text = text.Trim();
        return TryParseBrand(text, out address) || IecAddress.TryParse(text, out address);
    }

    protected abstract bool TryParseBrand(string text, out IoAddress address);

    public override string ToString() => DisplayName;

    protected static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>Direcciones directas IEC 61131-3: %IX0.0, %I0.0, %QW2, %MD4.</summary>
public static partial class IecAddress
{
    [GeneratedRegex(@"^%([IQM])([XBWD]?)(\d+)(?:\.(\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static bool TryParse(string text, out IoAddress address)
    {
        address = default;
        var m = Pattern().Match(text.Trim());
        if (!m.Success) return false;
        var area = Enum.Parse<IoArea>(m.Groups[1].Value, ignoreCase: true);
        var size = m.Groups[2].Value.ToUpperInvariant();
        int n = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        bool hasBit = m.Groups[4].Success;
        if (size is "" or "X")
        {
            if (!hasBit) return false;
            int bit = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (bit > 7) return false;
            address = new IoAddress(area, IoSize.Bit, n, bit);
            return true;
        }
        if (hasBit) return false;
        address = new IoAddress(area, IoSize.Word, n);
        return true;
    }
}

/// <summary>Todos los perfiles disponibles.</summary>
public static class PlcProfiles
{
    public static IReadOnlyList<PlcProfile> All { get; } =
    [
        new GenericIecProfile(),
        new SiemensS71200Profile(),
        new AllenBradleyMicroLogixProfile(),
        new SchneiderM221Profile(),
        new MitsubishiFx5uProfile(),
        new OmronCp1eProfile(),
        new DeltaDvpProfile(),
    ];

    public static PlcProfile Get(string id) =>
        All.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"Perfil de PLC desconocido: {id}");
}
