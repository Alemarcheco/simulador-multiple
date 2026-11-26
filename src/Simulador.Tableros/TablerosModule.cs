using Simulador.Core.Modules;
using Simulador.Tableros.Ui;

namespace Simulador.Tableros;

/// <summary>Pestaña "Tableros eléctricos": diseño físico de cuadros y paneles de automatización.</summary>
public sealed class TablerosModule : IModule
{
    public string Id => "tableros";
    public string Name => "Tableros eléctricos";
    public string Icon => "🗄";
    public int Order => 22;

    public object CreateRootView(IModuleHost host) => new TablerosView(host);
}
