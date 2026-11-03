namespace Simulador.Core.Libraries.Model;

/// <summary>Lo que se obtuvo al importar uno o varios archivos de librería.</summary>
public sealed class LibraryImportResult
{
    public List<SymbolDefinition> Symbols { get; } = [];
    public List<FootprintDefinition> Footprints { get; } = [];
    public List<SpiceModel> SpiceModels { get; } = [];
    public List<SpiceSubcircuit> SpiceSubcircuits { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool IsEmpty =>
        Symbols.Count == 0 && Footprints.Count == 0 && SpiceModels.Count == 0 && SpiceSubcircuits.Count == 0;

    public void Merge(LibraryImportResult other)
    {
        Symbols.AddRange(other.Symbols);
        Footprints.AddRange(other.Footprints);
        SpiceModels.AddRange(other.SpiceModels);
        SpiceSubcircuits.AddRange(other.SpiceSubcircuits);
        Warnings.AddRange(other.Warnings);
    }
}
