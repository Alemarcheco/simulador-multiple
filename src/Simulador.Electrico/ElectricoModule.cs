using Simulador.Core.Modules;

namespace Simulador.Electrico;

/// <summary>Pestaña "Instalaciones eléctricas": mando, potencia, motores, protecciones e iluminación.</summary>
public sealed class ElectricoModule : IModule
{
    public string Id => "electrico";
    public string Name => "Instalaciones eléctricas";
    public string Icon => "🔌";
    public int Order => 20;

    public object CreateRootView(IModuleHost host) => new ElectricoView(host);
}
