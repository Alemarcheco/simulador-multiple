using Avalonia;
using Avalonia.Controls;
using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Fabrication;
using Simulador.Circuitos.Libraries;
using Simulador.Circuitos.Pcb;
using Simulador.Circuitos.Schematic;
using Simulador.Circuitos.Simulation;
using Simulador.Core.Modules;

namespace Simulador.Circuitos;

/// <summary>Pestaña "Circuitos y PCB".</summary>
public sealed class CircuitosModule : IModule
{
    public string Id => "circuitos";
    public string Name => "Circuitos y PCB";
    public string Icon => "⚡";
    public int Order => 10;

    public object CreateRootView(IModuleHost host)
    {
        var catalog = new ComponentCatalog(host.GetDataDirectory(Id));
        var schematic = new SchematicEditorView(host, catalog);

        var tabs = new TabControl { TabStripPlacement = Dock.Left, Padding = new Thickness(8) };
        tabs.Items.Add(new TabItem { Header = "Esquemático", Content = schematic });
        tabs.Items.Add(new TabItem { Header = "Simulación", Content = new SimulationView(host, catalog, schematic.Canvas, () => tabs.SelectedIndex = 0) });
        var pcb = new PcbView(host, catalog, schematic.Canvas);
        tabs.Items.Add(new TabItem { Header = "PCB", Content = pcb });
        tabs.Items.Add(new TabItem { Header = "Fabricación", Content = new FabricationView(host, pcb.Canvas) });
        tabs.Items.Add(new TabItem { Header = "Librerías", Content = new LibraryView(host, catalog) });
        return tabs;
    }
}
