using Simulador.Core.Modules;

namespace Simulador.Hidraulica.Neumatica;

/// <summary>Pestaña "Neumática": circuitos de aire comprimido con válvulas, cilindros y su simulación.</summary>
public sealed class NeumaticaModule : IModule
{
    public const string ModuleId = "neumatica";

    public string Id => ModuleId;
    public string Name => "Neumática";
    public string Icon => "💨";
    public int Order => 41;

    public object CreateRootView(IModuleHost host) => new Ui.PneumaticView(host);
}
