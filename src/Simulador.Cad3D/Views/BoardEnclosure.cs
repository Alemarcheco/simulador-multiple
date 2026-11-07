using Simulador.Cad3D.Geometry.Model;
using Simulador.Cad3D.Geometry.Templates;
using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Pcb;

namespace Simulador.Cad3D.Views;

/// <summary>Crea una caja a medida a partir de una placa guardada con el módulo de circuitos.</summary>
public static class BoardEnclosure
{
    /// <param name="circuitsDataDir">Carpeta de datos del módulo de circuitos, para cargar las librerías que importó el usuario.</param>
    /// <returns>El documento y las huellas que no se encontraron en las librerías.</returns>
    public static (CadDocument Document, IReadOnlyList<string> MissingFootprints) FromPlacaFile(string path, string? circuitsDataDir)
    {
        var pcb = PcbDocument.FromJson(File.ReadAllText(path));
        var catalog = new ComponentCatalog(circuitsDataDir);
        // Los taladros de fijación se reconocen por el nombre aunque su huella no esté en las librerías.
        var missing = pcb.ResolveFootprints(catalog.FindFootprint)
            .Where(m => !m.Contains("MountingHole", StringComparison.OrdinalIgnoreCase)).ToList();
        var board = PcbBoardExport.ToBoardExport(pcb);
        return (EnclosureTemplate.FromBoard(board), missing);
    }
}
