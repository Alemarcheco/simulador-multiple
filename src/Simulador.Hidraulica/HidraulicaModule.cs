using Simulador.Core.Modules;

namespace Simulador.Hidraulica;

/// <summary>Pestaña "Hidráulica": diseño gráfico y simulación de procesos con tanques, tuberías, bombas y válvulas.</summary>
public sealed class HidraulicaModule : IModule
{
    public const string ModuleId = "hidraulica";

    public string Id => ModuleId;
    public string Name => "Hidráulica";
    public string Icon => "💧";
    public int Order => 40;

    public object CreateRootView(IModuleHost host) => new Ui.HydraulicView(host);
}
