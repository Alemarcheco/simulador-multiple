using Simulador.Electrico.Engine.Catalog;

namespace Simulador.Electrico.Engine.Model;

/// <summary>Geometría de los símbolos en unidades de rejilla (sin dependencias gráficas).</summary>
public static class Layout
{
    public readonly record struct GridPoint(double X, double Y);

    public readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public bool Contains(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
        public double CenterX => (X0 + X1) / 2;
    }

    /// <summary>Gira un punto local según el giro del elemento (pasos de 90° en sentido horario, con y hacia abajo).</summary>
    public static GridPoint Rotate(double x, double y, int rotation) => (((rotation % 4) + 4) % 4) switch
    {
        1 => new(-y, x),
        2 => new(-x, -y),
        3 => new(y, -x),
        _ => new(x, y),
    };

    public static GridPoint TerminalPosition(PlacedElement e, TerminalDef t)
    {
        var r = Rotate(t.X, t.Y, e.Rotation);
        return new(e.X + r.X, e.Y + r.Y);
    }

    /// <summary>Recuadro del símbolo en coordenadas locales (sin girar).</summary>
    public static Box LocalBounds(ElementType t)
    {
        var minX = t.Terminals.Min(p => p.X);
        var maxX = t.Terminals.Max(p => p.X);
        var minY = t.Terminals.Min(p => p.Y);
        var maxY = t.Terminals.Max(p => p.Y);
        return t.Behavior switch
        {
            Behavior.Source3Ph or Behavior.SourceAc1 or Behavior.SourceDc => new(minX - 1, 0, Math.Max(maxX + 1, minX + 6), 2),
            Behavior.Motor3 => new(-1, 0, 5, 5),
            Behavior.Motor6 => new(-1, 0, 5, 6),
            Behavior.Junction => new(-0.5, -0.5, 0.5, 0.5),
            Behavior.TwoWay or Behavior.Intermediate or Behavior.Selector3 => new(minX - 1, minY, maxX + 1, maxY),
            Behavior.Breaker or Behavior.MotorBreaker or Behavior.Rcd => new(minX - 1.2, minY, maxX + 1.8, maxY),
            _ => new(minX - 1.2, minY, maxX + 1.2, maxY),
        };
    }

    /// <summary>Recuadro del símbolo ya girado y colocado.</summary>
    public static Box WorldBounds(PlacedElement e, ElementType t)
    {
        var b = LocalBounds(t);
        var a = Rotate(b.X0, b.Y0, e.Rotation);
        var c = Rotate(b.X1, b.Y1, e.Rotation);
        return new(e.X + Math.Min(a.X, c.X), e.Y + Math.Min(a.Y, c.Y), e.X + Math.Max(a.X, c.X), e.Y + Math.Max(a.Y, c.Y));
    }

    /// <summary>Trazado ortogonal de un conductor entre dos bornes (en Z por la mitad).</summary>
    public static IReadOnlyList<GridPoint> Route(GridPoint a, GridPoint b) => Candidates(a, b)[0];

    /// <summary>
    /// Trazado ortogonal que evita pasar por encima de otros símbolos: prueba en Z (vertical u horizontal)
    /// y en L, y se queda con el que cruza menos recuadros; a igualdad, el más corto.
    /// </summary>
    public static IReadOnlyList<GridPoint> Route(GridPoint a, GridPoint b, IReadOnlyList<Box> obstacles,
        IReadOnlyList<(GridPoint p, GridPoint q)>? foreignSegments = null)
    {
        var best = Candidates(a, b)[0];
        var bestScore = double.MaxValue;
        foreach (var c in Candidates(a, b))
        {
            var hits = 0;
            var overlaps = 0;
            var length = 0.0;
            for (var i = 0; i + 1 < c.Count; i++)
            {
                length += Math.Abs(c[i].X - c[i + 1].X) + Math.Abs(c[i].Y - c[i + 1].Y);
                foreach (var o in obstacles) if (SegmentCrosses(c[i], c[i + 1], o)) hits++;
                if (foreignSegments is not null)
                    foreach (var (p, q) in foreignSegments) if (Overlaps(c[i], c[i + 1], p, q)) overlaps++;
            }
            // Pisar el cable de otra red engaña (parece que están unidos): se evita casi tanto como cruzar un símbolo.
            var score = hits * 1000 + overlaps * 800 + length + (c.Count - 2) * 0.5;
            if (score < bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    private static List<IReadOnlyList<GridPoint>> Candidates(GridPoint a, GridPoint b)
    {
        if (Math.Abs(a.X - b.X) < 1e-9 || Math.Abs(a.Y - b.Y) < 1e-9) return [new[] { a, b }];
        var midY = Math.Round((a.Y + b.Y) / 2 * 2) / 2;
        var midX = Math.Round((a.X + b.X) / 2 * 2) / 2;
        return
        [
            new[] { a, new GridPoint(a.X, midY), new GridPoint(b.X, midY), b },
            new[] { a, new GridPoint(a.X, b.Y), b },
            new[] { a, new GridPoint(b.X, a.Y), b },
            new[] { a, new GridPoint(midX, a.Y), new GridPoint(midX, b.Y), b },
        ];
    }

    /// <summary>¿El tramo horizontal o vertical p-q atraviesa el interior del recuadro?</summary>
    public static bool SegmentCrosses(GridPoint p, GridPoint q, Box box)
    {
        const double inset = 0.35;
        var x0 = box.X0 + inset; var x1 = box.X1 - inset; var y0 = box.Y0 + inset; var y1 = box.Y1 - inset;
        if (x0 >= x1 || y0 >= y1) return false;
        var sx0 = Math.Min(p.X, q.X); var sx1 = Math.Max(p.X, q.X);
        var sy0 = Math.Min(p.Y, q.Y); var sy1 = Math.Max(p.Y, q.Y);
        return sx1 > x0 && sx0 < x1 && sy1 > y0 && sy0 < y1;
    }

    /// <summary>¿Dos tramos ortogonales van uno encima del otro en más de un punto?</summary>
    public static bool Overlaps(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
    {
        const double eps = 1e-6;
        bool aH = Math.Abs(a.Y - b.Y) < eps, cH = Math.Abs(c.Y - d.Y) < eps;
        bool aV = Math.Abs(a.X - b.X) < eps, cV = Math.Abs(c.X - d.X) < eps;
        if (aH && cH && Math.Abs(a.Y - c.Y) < eps)
            return Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) > eps;
        if (aV && cV && Math.Abs(a.X - c.X) < eps)
            return Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) > eps;
        return false;
    }
}
