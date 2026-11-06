using Simulador.Core.Modules;

namespace Simulador.Banco;

/// <summary>
/// Pestaña "Banco de pruebas": junta piezas de todos los módulos (PLC, eléctrico, mecánico, hidráulico) en una
/// misma simulación con un reloj común y la muestra animada.
/// </summary>
public sealed class BancoModule : IModule
{
    public const string ModuleId = "banco";

    public string Id => ModuleId;
    public string Name => "Banco de pruebas";
    public string Icon => "🔗";
    public int Order => 90;

    public object CreateRootView(IModuleHost host) => new Ui.BancoView(host);
}
