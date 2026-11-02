using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Simulador.Circuitos.Catalog;
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
        tabs.Items.Add(new TabItem { Header = "Simulación", Content = new SimulationView(host, catalog, schematic.Canvas) });
        tabs.Items.Add(new TabItem { Header = "PCB", Content = new PcbView(host, catalog, schematic.Canvas) });
        tabs.Items.Add(new TabItem { Header = "Fabricación", Content = Pending("Salidas de fabricación", "Fase 3: Gerber y taladros para fabricar, e impresión 1:1 en espejo para transferencia térmica.") });
        tabs.Items.Add(new TabItem { Header = "Librerías", Content = new LibraryView(host, catalog) });
        return tabs;
    }

    private static Control Pending(string title, string text) => new StackPanel
    {
        Margin = new Thickness(24),
        Spacing = 8,
        Children =
        {
            new TextBlock { Text = title, FontSize = 22 },
            new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.8 },
            new TextBlock { Text = "En construcción.", Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Left },
        },
    };
}
