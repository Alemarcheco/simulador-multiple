using Simulador.Circuitos.Pcb;
using Simulador.Circuitos.Schematic;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Fabrication;

// Formas planas en milímetros (coordenadas de la placa, Y hacia abajo) que comparten Gerber, PDF y vista previa.
public abstract record Shape;
/// <summary>Círculo relleno.</summary>
public sealed record Disc(Vec2 Center, double Diameter) : Shape;
/// <summary>Trazo con extremos redondeados.</summary>
public sealed record Stroke(Vec2 A, Vec2 B, double Width) : Shape;
/// <summary>Polígono relleno.</summary>
public sealed record Polygon(IReadOnlyList<Vec2> Points) : Shape;

public enum ArtworkLayer { BottomCopper, TopCopper, TopSilk, BottomMask, TopMask, Edge }

public sealed record Drill(Vec2 Center, double Diameter, Vec2? SlotEnd = null, bool Plated = true);

/// <summary>Genera las formas de cada capa de una placa.</summary>
public static class Artwork
{
    public const double MaskMargin = 0.05;

    public static List<Shape> Layer(PcbDocument doc, ArtworkLayer layer)
    {
        var shapes = new List<Shape>();
        var pads = doc.Footprints.SelectMany(FootprintTransform.Pads).ToList();
        switch (layer)
        {
            case ArtworkLayer.BottomCopper:
            case ArtworkLayer.TopCopper:
            {
                var name = layer == ArtworkLayer.BottomCopper ? Layers.Bottom : Layers.Top;
                foreach (var t in doc.Tracks.Where(t => t.Layer == name)) shapes.Add(new Stroke(t.A, t.B, t.Width));
                foreach (var p in pads.Where(p => p.OnLayer(name) && p.Pad.Type != PadType.NpThroughHole)) shapes.AddRange(PadShapes(p, 0));
                break;
            }
            case ArtworkLayer.BottomMask:
            case ArtworkLayer.TopMask:
                foreach (var p in pads) shapes.AddRange(PadShapes(p, MaskMargin));
                break;
            case ArtworkLayer.TopSilk:
                foreach (var f in doc.Footprints) shapes.AddRange(SilkShapes(f));
                // Los puentes de cable se marcan en la serigrafía para saber dónde soldarlos.
                foreach (var t in doc.Tracks.Where(t => t.Layer == Layers.Jumper)) shapes.Add(new Stroke(t.A, t.B, 0.3));
                break;
            case ArtworkLayer.Edge:
                var c = new[] { new Vec2(0, 0), new Vec2(doc.BoardWidth, 0), new Vec2(doc.BoardWidth, doc.BoardHeight), new Vec2(0, doc.BoardHeight) };
                for (int i = 0; i < 4; i++) shapes.Add(new Stroke(c[i], c[(i + 1) % 4], 0.1));
                break;
        }
        return shapes;
    }

    public static List<Drill> Drills(PcbDocument doc)
    {
        var drills = new List<Drill>();
        foreach (var p in doc.Footprints.SelectMany(FootprintTransform.Pads))
        {
            if (p.Pad.Drill <= 0) continue;
            bool plated = p.Pad.Type != PadType.NpThroughHole;
            if (p.Pad.DrillSize is { } ds && Math.Abs(ds.X - ds.Y) > 1e-6)
            {
                var (a, b, w) = Capsule(p, ds.X, ds.Y, 0);
                drills.Add(new Drill(a, w, b, plated));
            }
            else drills.Add(new Drill(p.Center, p.Pad.Drill, null, plated));
        }
        return drills;
    }

    /// <summary>Formas de un pad, agrandado <paramref name="inflate"/> mm por cada lado (máscara).</summary>
    public static IEnumerable<Shape> PadShapes(PlacedPad p, double inflate)
    {
        double w = p.Pad.Size.X + 2 * inflate, h = p.Pad.Size.Y + 2 * inflate;
        switch (p.Pad.Shape)
        {
            case PadShape.Circle:
                yield return new Disc(p.Center, w);
                break;
            case PadShape.Oval when Math.Abs(w - h) < 1e-9:
                yield return new Disc(p.Center, w);
                break;
            case PadShape.Oval:
            {
                var (a, b, d) = Capsule(p, w, h, 0);
                yield return new Stroke(a, b, d);
                break;
            }
            default:
                yield return new Polygon([Rot(p, -w / 2, -h / 2), Rot(p, w / 2, -h / 2), Rot(p, w / 2, h / 2), Rot(p, -w / 2, h / 2)]);
                break;
        }
    }

    private static (Vec2 A, Vec2 B, double Width) Capsule(PlacedPad p, double w, double h, double _)
    {
        double r = Math.Min(w, h) / 2;
        return w > h
            ? (Rot(p, -(w / 2 - r), 0), Rot(p, w / 2 - r, 0), 2 * r)
            : (Rot(p, 0, -(h / 2 - r)), Rot(p, 0, h / 2 - r), 2 * r);
    }

    private static Vec2 Rot(PlacedPad p, double x, double y)
    {
        double a = p.RotationDeg * Math.PI / 180;
        return new Vec2(p.Center.X + x * Math.Cos(a) + y * Math.Sin(a), p.Center.Y - x * Math.Sin(a) + y * Math.Cos(a));
    }

    private static IEnumerable<Shape> SilkShapes(PcbFootprint f)
    {
        if (f.Definition is null) yield break;
        Vec2 W(Vec2 v) => FootprintTransform.ToBoard(f, v);
        foreach (var g in f.Definition.Graphics.Where(g => g.Layer == "F.SilkS"))
        {
            double sw = g.StrokeWidth > 0 ? g.StrokeWidth : 0.15;
            IReadOnlyList<Vec2>? pts = g switch
            {
                RectangleGraphic r => [W(r.Start), W(new Vec2(r.End.X, r.Start.Y)), W(r.End), W(new Vec2(r.Start.X, r.End.Y)), W(r.Start)],
                PolylineGraphic pl => pl.Points.Select(W).ToList(),
                ArcGraphic a => SchematicCanvas.ArcPoints(W(a.Start), W(a.Mid), W(a.End)),
                CircleGraphic c => Enumerable.Range(0, 49).Select(i => W(new Vec2(c.Center.X + c.Radius * Math.Cos(i * Math.PI / 24), c.Center.Y + c.Radius * Math.Sin(i * Math.PI / 24)))).ToList(),
                _ => null,
            };
            if (pts is null) continue;
            for (int i = 1; i < pts.Count; i++) yield return new Stroke(pts[i - 1], pts[i], sw);
        }
    }
}
