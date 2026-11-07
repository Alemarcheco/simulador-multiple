using Simulador.Core.Modules;

namespace Simulador.Plc;

/// <summary>Pestaña "PLC y microcontroladores".</summary>
public sealed class PlcModule : IModule
{
    public string Id => "plc";
    public string Name => "PLC y microcontroladores";
    public string Icon => "🎛";
    public int Order => 30;

    /// <summary>Dos equipos en subpestañas: el PLC (con sus lenguajes IEC) y el Arduino.</summary>
    public object CreateRootView(IModuleHost host)
    {
        var tabs = new Avalonia.Controls.TabControl();
        tabs.Items.Add(new Avalonia.Controls.TabItem { Header = "PLC", Content = new PlcView(host) });
        tabs.Items.Add(new Avalonia.Controls.TabItem { Header = "Arduino", Content = new ArduinoView(host) });
        return tabs;
    }
}
