using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Core.Modules;
using Simulador.App.Tema;

namespace Simulador.App;

/// <summary>Pantalla de inicio: presentación de la aplicación y una tarjeta por simulador.</summary>
public sealed class HomeView : UserControl
{
    /// <summary>Descripción corta de cada simulador conocido (por Id); los demás se muestran sin ella.</summary>
    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["circuitos"] = "Esquemas electrónicos, simulación con osciloscopio, placa PCB y archivos para fabricarla.",
        ["edificacion"] = "Planos de vivienda en 2D, vista 3D, recorrido, soleamiento y exportación DXF, PDF e IFC.",
        ["electrico"] = "Esquemas de mando y potencia con contactores, protecciones y motores simulados en tiempo real.",
        ["plc"] = "Programa el PLC en Ladder, FBD, ST, IL o GRAFCET, y Arduino, ESP32 o Pico con bloques o código.",
        ["hidraulica"] = "Redes de tuberías con bombas, depósitos y válvulas: presiones, caudales y golpe de ariete.",
        ["neumatica"] = "Cilindros, válvulas y compresores, con el movimiento simulado y vista en 3D.",
        ["cad3d"] = "Piezas para impresión 3D y cajas a medida para tus placas; exporta STL, 3MF y STEP.",
        ["banco"] = "Une varios simuladores: PLC, contactores, motores, bombas, tanques y turbinas trabajando juntos.",
    };

    public HomeView(IReadOnlyList<IModule> modules, Action<IModule> open)
    {
        var muted = (IBrush)Application.Current!.FindResource("SimMutedBrush")!;

        var hero = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            Margin = new Thickness(0, 0, 0, 28),
            Children =
            {
                Logo.Create(72),
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "Simulador múltiple", FontSize = 30, FontWeight = FontWeight.SemiBold },
                        new TextBlock
                        {
                            Text = "Diseña, simula y prueba circuitos, instalaciones, automatismos, fluidos y edificios en un solo programa.",
                            FontSize = 15,
                            Foreground = muted,
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },
            },
        };

        var cards = new WrapPanel();
        foreach (var module in modules)
            cards.Children.Add(Card(module, open, muted));

        var body = new StackPanel
        {
            MaxWidth = 1000,
            Margin = new Thickness(40, 36, 40, 36),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                hero,
                new TextBlock
                {
                    Text = "SIMULADORES",
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 1.2,
                    Foreground = muted,
                    Margin = new Thickness(0, 0, 0, 12),
                },
                cards,
                new TextBlock
                {
                    Text = "Elige un simulador para empezar. También puedes cambiar de simulador en cualquier momento con las pestañas de arriba; cada uno conserva tu trabajo mientras la aplicación siga abierta.",
                    FontSize = 13,
                    Foreground = muted,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                },
            },
        };

        Content = new ScrollViewer { Content = body };
    }

    private static Button Card(IModule module, Action<IModule> open, IBrush muted)
    {
        var accentSoft = (IBrush)Application.Current!.FindResource("SimAccentSoftBrush")!;
        var icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = accentSoft,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = module.Icon,
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var text = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = module.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    Text = Descriptions.GetValueOrDefault(module.Id, ""),
                    FontSize = 12.5,
                    Foreground = muted,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 72,
                },
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*") };
        grid.Children.Add(icon);
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var card = new Button { Content = grid };
        card.Classes.Add("card");
        ToolTip.SetTip(card, $"Abrir {module.Name}");
        card.Click += (_, _) => open(module);
        return card;
    }
}
