using Simulador.Electrico.Engine.Model;

namespace Simulador.Tableros.Model;

/// <summary>Crea o actualiza un tablero a partir de un esquema eléctrico.</summary>
public static class PanelBuilder
{
    /// <summary>Tablero nuevo: aparatos del esquema, barra de tierra, distribución automática y cableado.</summary>
    public static PanelDocument FromSchematic(Schematic schematic)
    {
        var analysis = SchematicImport.Analyze(schematic);
        var doc = new PanelDocument
        {
            Name = $"Tablero · {schematic.Name}",
            Description = $"Generado desde el esquema «{schematic.Name}».",
            Schematic = schematic.Clone(),
        };
        doc.Devices.AddRange(analysis.Devices);
        doc.Cables.AddRange(analysis.Cables);
        if (analysis.Cables.Any(c => c.WithPe)) doc.Devices.Add(DeviceLibrary.Create("barra_pe", "PE"));
        AutoLayout.Arrange(doc);
        DuctRouter.Rebuild(doc);
        SuggestFan(doc);
        return doc;
    }

    /// <summary>
    /// Actualiza el tablero con una versión nueva del esquema: conserva la posición, medidas y referencia de los
    /// aparatos que siguen, coloca los nuevos en el primer hueco libre y quita los que ya no están.
    /// Devuelve un resumen de los cambios.
    /// </summary>
    public static string UpdateFromSchematic(PanelDocument doc, Schematic schematic)
    {
        var analysis = SchematicImport.Analyze(schematic);
        var added = new List<string>();
        var removed = new List<string>();
        var kept = new List<PanelDevice>();
        foreach (var fresh in analysis.Devices)
        {
            var old = doc.FindTag(fresh.Tag);
            if (old is not null && old.Kind == fresh.Kind)
            {
                fresh.Id = old.Id;
                fresh.X = old.X;
                fresh.Y = old.Y;
                fresh.Reference = old.Reference;
                fresh.Location = old.Location;
                if (fresh.Kind != "regleta")
                {
                    fresh.Width = old.Width;
                    fresh.Height = old.Height;
                    fresh.Depth = old.Depth;
                }
                kept.Add(fresh);
            }
            else
            {
                added.Add(fresh.Tag);
                kept.Add(fresh);
            }
        }
        // Lo que se añadió a mano (sin estar en el esquema) se queda.
        var manual = doc.Devices.Where(d => analysis.Devices.All(a => !a.Tag.Equals(d.Tag, StringComparison.OrdinalIgnoreCase))
                                            && (doc.Schematic is null || SchematicImport.Analyze(doc.Schematic).Devices.All(a => !a.Tag.Equals(d.Tag, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        foreach (var d in doc.Devices)
            if (analysis.Devices.All(a => !a.Tag.Equals(d.Tag, StringComparison.OrdinalIgnoreCase)) && !manual.Contains(d)) removed.Add(d.Tag);

        doc.Devices = [.. kept, .. manual];
        if (analysis.Cables.Any(c => c.WithPe) && doc.Devices.All(d => d.Kind != "barra_pe")) doc.Devices.Add(DeviceLibrary.Create("barra_pe", "PE"));
        foreach (var d in doc.Devices.Where(d => added.Contains(d.Tag))) AutoLayout.PlaceInFreeSpot(doc, d);
        doc.Cables = analysis.Cables;
        doc.Schematic = schematic.Clone();
        DuctRouter.Rebuild(doc);

        var parts = new List<string>();
        if (added.Count > 0) parts.Add($"añadidos {string.Join(", ", added)}");
        if (removed.Count > 0) parts.Add($"quitados {string.Join(", ", removed)}");
        return parts.Count == 0 ? "El tablero ya coincidía con el esquema; cableado regenerado." : "Tablero actualizado: " + string.Join("; ", parts) + ".";
    }

    /// <summary>Caudales habituales de ventiladores con filtro (m³/h).</summary>
    public static readonly double[] FanSizes = [20, 55, 100, 160, 230, 300, 550, 700];

    /// <summary>Si dentro se pasaría de la temperatura máxima, pone el ventilador normalizado que hace falta.</summary>
    public static void SuggestFan(PanelDocument doc)
    {
        doc.Enclosure.FanFlow = 0;
        var th = PanelChecks.Thermal(doc);
        if (th.InsideC <= doc.Enclosure.MaxInsideC || !double.IsFinite(th.FanNeededM3h)) return;
        doc.Enclosure.FanFlow = FanSizes.FirstOrDefault(f => f >= th.FanNeededM3h, FanSizes[^1]);
    }

    /// <summary>Ejemplos: los esquemas de Instalaciones eléctricas convertidos en tableros.</summary>
    public static IReadOnlyList<(string Name, Func<PanelDocument> Create)> Examples { get; } =
    [
        ("Arranque directo con autorretención", () => FromExample("Arranque directo con autorretención")),
        ("Arranque estrella-triángulo", () => FromExample("Arranque estrella-triángulo")),
        ("Inversión de giro con enclavamiento", () => FromExample("Inversión de giro con enclavamiento")),
        ("Cuadro de vivienda con diferencial", () => FromExample("Cuadro de vivienda con diferencial y fuga a tierra")),
        ("Marcha-paro de motor con PLC", () => FromExample("Marcha-paro de motor con PLC")),
        ("Variador de frecuencia", () => FromExample("Variador de frecuencia: rampa y dos sentidos de giro")),
        ("Cintas transportadoras en cascada", () => FromExample("Cintas transportadoras en cascada")),
    ];

    public static PanelDocument FromExample(string schematicName)
    {
        var ex = Simulador.Electrico.Engine.Model.Examples.All.First(e => e.Name == schematicName);
        return FromSchematic(ex.Create());
    }
}
