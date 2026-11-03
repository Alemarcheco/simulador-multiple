using Simulador.Civil.Model;
using Simulador.Civil.Views;
using Simulador.Core.Building;
using Simulador.Core.Modules;

namespace Simulador.Civil;

/// <summary>
/// Pestaña "Edificación": diseño civil básico de casas, hoteles e industrias (niveles, muros, puertas, ventanas,
/// losas, escaleras y habitaciones) sobre el que se colocan las instalaciones de los demás módulos.
/// </summary>
public sealed class EdificacionModule : IModule
{
    public string Id => "edificacion";
    public string Name => "Edificación";
    public string Icon => "🏠";
    public int Order => 15;

    /// <summary>Documento abierto. Los demás módulos pueden leerlo como <see cref="IBuildingModel"/> desde <c>IModuleHost.Modules</c>.</summary>
    public BuildingDocument Document { get; } = BuildingTemplates.Casa();

    public IBuildingModel Building => Document;

    public object CreateRootView(IModuleHost host) => new EdificacionView(host, Document);
}
