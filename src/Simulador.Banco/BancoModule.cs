using Avalonia.Controls;
using Avalonia.Media;
using Simulador.Core.Modules;

namespace Simulador.Banco;

/// <summary>
/// Pestaña "Banco de pruebas": junta piezas de todos los módulos (PLC, Arduino, eléctrico, mecánico, hidráulico) en
/// una misma simulación con un reloj común. Tiene la mesa libre, donde el usuario arma sus propios montajes, y los
/// montajes de ejemplo ya preparados con su vista animada.
/// </summary>
public sealed class BancoModule : IModule
{
    public const string ModuleId = "banco";

    public string Id => ModuleId;
    public string Name => "Banco de pruebas";
    public string Icon => "🔗";
    public int Order => 90;

    public object CreateRootView(IModuleHost host) => new TabControl
    {
        Items =
        {
            new TabItem { Header = new TextBlock { Text = "🧩 Mesa libre", FontSize = 15 }, Content = new Mesa.Ui.MesaView(host) },
            new TabItem { Header = new TextBlock { Text = "🏭 Montajes animados", FontSize = 15 }, Content = new Ui.BancoView(host) },
        },
    };
}
