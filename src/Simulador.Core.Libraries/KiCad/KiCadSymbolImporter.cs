using Simulador.Core.Libraries.Model;

namespace Simulador.Core.Libraries.KiCad;

/// <summary>Importa librerías de símbolos de KiCad 6, 7, 8 y 9 (<c>.kicad_sym</c>).</summary>
public sealed class KiCadSymbolImporter : ILibraryImporter
{
    public string Name => "Símbolos KiCad";
    public IReadOnlyList<string> Extensions { get; } = [".kicad_sym"];

    public LibraryImportResult Import(string content, string libraryName)
    {
        var result = new LibraryImportResult();
        var root = SExpr.Parse(content);
        if (root.Head != "kicad_symbol_lib")
            throw new FormatException($"Se esperaba 'kicad_symbol_lib' y se encontró '{root.Head}'.");

        var byName = new Dictionary<string, SymbolDefinition>(StringComparer.Ordinal);
        foreach (var node in root.Children("symbol"))
        {
            var name = node.Arg(1);
            if (string.IsNullOrEmpty(name)) continue;
            try
            {
                var symbol = ReadSymbol(node, name, libraryName);
                byName[name] = symbol;
                result.Symbols.Add(symbol);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                result.Warnings.Add($"{libraryName}:{name}: {ex.Message}");
            }
        }

        // Resolver "extends": el derivado hereda gráficos y pines del padre y sobrescribe propiedades.
        for (int i = 0; i < result.Symbols.Count; i++)
        {
            var s = result.Symbols[i];
            if (s.Extends is null) continue;
            if (!byName.TryGetValue(s.Extends, out var parent))
            {
                result.Warnings.Add($"{libraryName}:{s.Name}: hereda de '{s.Extends}', que no está en la librería.");
                continue;
            }
            var merged = new SymbolDefinition
            {
                Name = s.Name,
                Library = s.Library,
                Extends = s.Extends,
                Pins = [.. parent.Pins],
                Graphics = [.. parent.Graphics],
                UnitCount = parent.UnitCount,
                IsPower = parent.IsPower || s.IsPower,
            };
            foreach (var kv in parent.Properties) merged.Properties[kv.Key] = kv.Value;
            foreach (var kv in s.Properties) merged.Properties[kv.Key] = kv.Value;
            result.Symbols[i] = merged;
            byName[s.Name] = merged;
        }

        return result;
    }

    private static SymbolDefinition ReadSymbol(SExpr node, string name, string library)
    {
        var symbol = new SymbolDefinition
        {
            Name = name,
            Library = library,
            Extends = node.Child("extends")?.Arg(1),
            IsPower = node.Child("power") is not null || node.HasFlag("power"),
        };

        foreach (var prop in node.Children("property"))
            if (prop.Arg(1) is { } key)
                symbol.Properties[key] = prop.Arg(2) ?? "";

        int maxUnit = 1;
        foreach (var unitNode in node.Children("symbol"))
        {
            // Nombre "R_1_1": unidad 1, estilo 1. Unidad 0 = común a todas. Estilo 2 = De Morgan (se ignora por ahora).
            var (unit, style) = ParseUnitName(unitNode.Arg(1) ?? "");
            if (style > 1) continue;
            maxUnit = Math.Max(maxUnit, unit);
            ReadUnitContents(unitNode, unit, symbol);
        }
        // KiCad también permite gráficos directamente en el símbolo (raro, pero válido).
        ReadUnitContents(node, 0, symbol);
        symbol.UnitCount = maxUnit;
        return symbol;
    }

    private static (int Unit, int Style) ParseUnitName(string unitName)
    {
        var parts = unitName.Split('_');
        if (parts.Length >= 3 &&
            int.TryParse(parts[^2], out var unit) &&
            int.TryParse(parts[^1], out var style))
            return (unit, style);
        return (0, 1);
    }

    private static void ReadUnitContents(SExpr unitNode, int unit, SymbolDefinition symbol)
    {
        foreach (var item in unitNode.Items)
        {
            switch (item.Head)
            {
                case "pin": symbol.Pins.Add(ReadPin(item, unit)); break;
                case "rectangle":
                    symbol.Graphics.Add(new RectangleGraphic(Pt(item.Child("start")), Pt(item.Child("end")), unit, Stroke(item), Filled(item)));
                    break;
                case "circle":
                    symbol.Graphics.Add(new CircleGraphic(Pt(item.Child("center")), item.Child("radius")?.Num(1) ?? 0, unit, Stroke(item), Filled(item)));
                    break;
                case "arc":
                {
                    var start = Pt(item.Child("start"));
                    var end = Pt(item.Child("end"));
                    var mid = item.Child("mid") is { } m ? Pt(m) : new Vec2((start.X + end.X) / 2, (start.Y + end.Y) / 2);
                    symbol.Graphics.Add(new ArcGraphic(start, mid, end, unit, Stroke(item), Filled(item)));
                    break;
                }
                case "polyline":
                case "bezier":
                    symbol.Graphics.Add(new PolylineGraphic(Points(item), unit, Stroke(item), Filled(item)));
                    break;
                case "text":
                {
                    var at = item.Child("at");
                    symbol.Graphics.Add(new TextGraphic(item.Arg(1) ?? "", Pt(at), at?.Num(3) ?? 0, FontSize(item), unit));
                    break;
                }
            }
        }
    }

    private static SymbolPin ReadPin(SExpr pin, int unit)
    {
        var at = pin.Child("at");
        return new SymbolPin(
            Number: pin.Child("number")?.Arg(1) ?? "",
            Name: pin.Child("name")?.Arg(1) ?? "",
            ElectricalType: ParsePinType(pin.Arg(1)),
            Position: Pt(at),
            Length: pin.Child("length")?.Num(1) ?? 2.54,
            OrientationDeg: at?.Num(3) ?? 0,
            Unit: unit,
            Hidden: pin.HasFlag("hide") || pin.Child("hide")?.Arg(1) == "yes");
    }

    internal static PinElectricalType ParsePinType(string? t) => t switch
    {
        "input" => PinElectricalType.Input,
        "output" => PinElectricalType.Output,
        "bidirectional" => PinElectricalType.Bidirectional,
        "tri_state" => PinElectricalType.TriState,
        "passive" => PinElectricalType.Passive,
        "free" => PinElectricalType.Free,
        "power_in" => PinElectricalType.PowerIn,
        "power_out" => PinElectricalType.PowerOut,
        "open_collector" => PinElectricalType.OpenCollector,
        "open_emitter" => PinElectricalType.OpenEmitter,
        "no_connect" => PinElectricalType.NoConnect,
        _ => PinElectricalType.Unspecified,
    };

    internal static Vec2 Pt(SExpr? n) => n is null ? default : new Vec2(n.Num(1), n.Num(2));

    internal static IReadOnlyList<Vec2> Points(SExpr item) =>
        item.Child("pts")?.Children("xy").Select(Pt).ToList() ?? [];

    internal static double Stroke(SExpr item) =>
        item.Child("stroke")?.Child("width")?.Num(1) ?? item.Child("width")?.Num(1) ?? 0;

    internal static bool Filled(SExpr item) =>
        item.Child("fill")?.Child("type")?.Arg(1) is { } t && t != "none";

    internal static double FontSize(SExpr item) =>
        item.Child("effects")?.Child("font")?.Child("size")?.Num(1) ?? 1.27;
}
