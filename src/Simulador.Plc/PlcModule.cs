using Simulador.Core.Modules;

namespace Simulador.Plc;

/// <summary>Pestaña "PLC y microcontroladores".</summary>
public sealed class PlcModule : IModule
{
    public string Id => "plc";
    public string Name => "PLC y microcontroladores";
    public string Icon => "🎛";
    public int Order => 30;

    public object CreateRootView(IModuleHost host) => new PlcView(host);
}
