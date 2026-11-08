using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Simulador.App.Tema;

/// <summary>
/// Logotipo de la aplicación dibujado como vector (cuadrado azul con una señal blanca),
/// para que se vea nítido a cualquier tamaño. El mismo dibujo está en Assets/logo.svg.
/// </summary>
public static class Logo
{
    // Coordenadas en una caja de 64 × 64 (idénticas a Assets/logo.svg).
    private const string Trace = "M 11,38 L 21,38 L 27,19 L 35,47 L 41,31 L 53,31";

    public static Control Create(double size)
    {
        var canvas = new Canvas { Width = 64, Height = 64 };
        canvas.Children.Add(new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(15),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#4C8DFF"), 0),
                    new GradientStop(Color.Parse("#1F4FC2"), 1),
                },
            },
        });
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(Trace),
            Stroke = Brushes.White,
            StrokeThickness = 5.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        });
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(Color.Parse("#FFC940")) };
        Canvas.SetLeft(dot, 48.5);
        Canvas.SetTop(dot, 26.5);
        canvas.Children.Add(dot);

        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
