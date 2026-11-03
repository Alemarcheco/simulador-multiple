using Simulador.Cad3D.Views;
using Simulador.Core.Modules;

namespace Simulador.Cad3D;

/// <summary>Pestaña "Diseño 3D": piezas y encapsulados para imprimir en 3D.</summary>
public sealed class Cad3DModule : IModule
{
    public string Id => "cad3d";
    public string Name => "Diseño 3D";
    public string Icon => "🧊";
    public int Order => 60;

    public object CreateRootView(IModuleHost host) => new Cad3DView(host);
}
