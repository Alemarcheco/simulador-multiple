using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;

namespace Simulador.Electrico.Editor;

/// <summary>Dibuja el símbolo de un tipo de la librería ajustado a su tamaño (miniaturas de la librería y vista previa).</summary>
internal sealed class SymbolPreview : Control
{
    private readonly ElementType _type;
    private readonly bool _showTerminals;

    public SymbolPreview(ElementType type, double size, bool showTerminals = false)
    {
        _type = type;
        _showTerminals = showTerminals;
        Width = size;
        Height = size;
    }

    public override void Render(DrawingContext dc)
    {
        var bounds = new Rect(Bounds.Size);
        dc.DrawRectangle(SymbolPainter.Paper, new Pen(new SolidColorBrush(Color.Parse("#D5DBE3")), 1), bounds, 4, 4);

        var b = Layout.LocalBounds(_type);
        // Margen extra a la izquierda para accionadores y a la derecha para cajas de disparo.
        var x0 = b.X0 - 0.9;
        var x1 = b.X1 + 0.4;
        var y0 = b.Y0 - 0.4;
        var y1 = b.Y1 + 0.4;
        var w = (x1 - x0) * SymbolPainter.G;
        var h = (y1 - y0) * SymbolPainter.G;
        var pad = Bounds.Width > 60 ? 10 : 3;
        var scale = Math.Min((Bounds.Width - 2 * pad) / w, (Bounds.Height - 2 * pad) / h);
        var ox = (Bounds.Width - w * scale) / 2 - x0 * SymbolPainter.G * scale;
        var oy = (Bounds.Height - h * scale) / 2 - y0 * SymbolPainter.G * scale;

        using (dc.PushClip(bounds))
        using (dc.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(ox, oy)))
        {
            SymbolPainter.Draw(dc, new PlacedElement { Type = _type.Id }, _type, null);
            foreach (var t in _type.Terminals)
            {
                var p = new Point(t.X * SymbolPainter.G, t.Y * SymbolPainter.G);
                dc.DrawEllipse(SymbolPainter.Ink, null, p, 2.5, 2.5);
                if (!_showTerminals || string.IsNullOrEmpty(t.Label)) continue;
                var label = SymbolPainter.Text(TerminalLabel(t), 9, SymbolPainter.Muted);
                var below = t.Y > 0 || _type.IsSource;
                dc.DrawText(label, new Point(p.X + 4, below ? p.Y - label.Height - 1 : p.Y + 1));
            }
        }
    }

    private string TerminalLabel(TerminalDef t)
    {
        var parts = _type.DefaultTerminalText.Split('-', 2);
        if (_type.Terminals.Count == 2 && parts.Length == 2) return t.Y == 0 ? parts[0] : parts[1];
        return t.Label;
    }
}
