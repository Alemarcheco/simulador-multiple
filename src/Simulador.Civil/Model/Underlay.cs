using System.Text.Json.Serialization;
using Simulador.Civil.Import;
using Simulador.Core.Building;

namespace Simulador.Civil.Model;

public enum UnderlayKind { Dxf, Image }

/// <summary>
/// Plano de referencia (calco) bajo la planta de un nivel: un DXF vectorial o una imagen escaneada. No forma parte
/// del edificio; sirve para dibujar encima. Coordenadas locales en metros (DXF) o en píxeles (imagen, Y hacia
/// abajo) que se llevan al plano con <see cref="Scale"/>, <see cref="Rotation"/> y <see cref="Origin"/>.
/// </summary>
public sealed record Underlay(string Id, string LevelId, UnderlayKind Kind, string Name)
{
    /// <summary>Segmentos del DXF en metros (se guardan dentro del proyecto para no depender del archivo).</summary>
    public List<DrawingSegment> Segments { get; init; } = [];
    /// <summary>Ruta de la imagen escaneada (las imágenes no se copian dentro del proyecto).</summary>
    public string? ImagePath { get; init; }
    /// <summary>Tamaño de la imagen en píxeles (para encuadrar sin tener que abrirla).</summary>
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }

    /// <summary>Dónde cae en la planta el origen local (DXF) o la esquina superior izquierda (imagen).</summary>
    public Point2 Origin { get; init; }
    /// <summary>Giro en grados, antihorario.</summary>
    public double Rotation { get; init; }
    /// <summary>Metros por unidad local: 1 en DXF (ya convertido) o metros por píxel en imágenes.</summary>
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 0.6;
    public bool Visible { get; init; } = true;
    /// <summary>Capas del DXF que no se muestran.</summary>
    public List<string> HiddenLayers { get; init; } = [];

    /// <summary>Punto local → planta.</summary>
    public Point2 ToPlan(Point2 local)
    {
        double a = Rotation * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        // En imágenes la Y local va hacia abajo.
        double x = local.X * Scale, y = (Kind == UnderlayKind.Image ? -local.Y : local.Y) * Scale;
        return new Point2(Origin.X + c * x - s * y, Origin.Y + s * x + c * y);
    }

    /// <summary>Punto de la planta → local.</summary>
    public Point2 ToLocal(Point2 plan)
    {
        double a = Rotation * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        double dx = plan.X - Origin.X, dy = plan.Y - Origin.Y;
        double x = (c * dx + s * dy) / Scale, y = (-s * dx + c * dy) / Scale;
        return new Point2(x, Kind == UnderlayKind.Image ? -y : y);
    }

    /// <summary>Segmentos visibles ya colocados en la planta.</summary>
    public IEnumerable<DrawingSegment> PlanSegments()
    {
        foreach (var s in Segments)
            if (!HiddenLayers.Contains(s.Layer)) yield return new(ToPlan(s.A), ToPlan(s.B), s.Layer);
    }

    /// <summary>Esquinas en la planta (para encuadrar).</summary>
    [JsonIgnore]
    public IReadOnlyList<Point2> Corners
    {
        get
        {
            if (Kind == UnderlayKind.Image)
                return [ToPlan(new(0, 0)), ToPlan(new(PixelWidth, 0)), ToPlan(new(PixelWidth, PixelHeight)), ToPlan(new(0, PixelHeight))];
            if (Segments.Count == 0) return [Origin];
            double x0 = Segments.Min(s => Math.Min(s.A.X, s.B.X)), x1 = Segments.Max(s => Math.Max(s.A.X, s.B.X));
            double y0 = Segments.Min(s => Math.Min(s.A.Y, s.B.Y)), y1 = Segments.Max(s => Math.Max(s.A.Y, s.B.Y));
            return [ToPlan(new(x0, y0)), ToPlan(new(x1, y0)), ToPlan(new(x1, y1)), ToPlan(new(x0, y1))];
        }
    }

    /// <summary>
    /// Escala el calco para que la distancia entre dos puntos de la planta pase a medir <paramref name="realMeters"/>,
    /// manteniendo fijo el primer punto.
    /// </summary>
    public Underlay Calibrate(Point2 a, Point2 b, double realMeters)
    {
        double measured = a.DistanceTo(b);
        if (measured < 1e-9 || realMeters <= 0) return this;
        double k = realMeters / measured;
        var localA = ToLocal(a);
        var scaled = this with { Scale = Scale * k };
        var drift = scaled.ToPlan(localA) - a;
        return scaled with { Origin = scaled.Origin - drift };
    }

    /// <summary>Desplaza el calco para que el punto <paramref name="from"/> quede en <paramref name="to"/>.</summary>
    public Underlay MoveBy(Point2 from, Point2 to) => this with { Origin = Origin + (to - from) };

    /// <summary>Gira el calco alrededor de <paramref name="pivot"/> para que la dirección pivot→from apunte a pivot→to.</summary>
    public Underlay RotateTo(Point2 pivot, Point2 from, Point2 to)
    {
        double delta = Math.Atan2(to.Y - pivot.Y, to.X - pivot.X) - Math.Atan2(from.Y - pivot.Y, from.X - pivot.X);
        var localPivot = ToLocal(pivot);
        var rotated = this with { Rotation = Rotation + delta * 180 / Math.PI };
        var drift = rotated.ToPlan(localPivot) - pivot;
        return rotated with { Origin = rotated.Origin - drift };
    }

    /// <summary>Crea el calco de un DXF centrado en el origen de la planta (la esquina inferior izquierda en 0,0).</summary>
    public static Underlay FromDxf(string id, string levelId, string name, DxfDrawing d)
    {
        var (min, _) = d.Extents;
        var segs = d.Segments.Select(s => new DrawingSegment(s.A - min, s.B - min, s.Layer)).ToList();
        return new Underlay(id, levelId, UnderlayKind.Dxf, name) { Segments = segs, Opacity = 0.7 };
    }

    /// <summary>
    /// Calco de una imagen. Sin escala conocida se supone que el ancho de la imagen son <paramref name="assumedWidthMeters"/>;
    /// luego se ajusta con <see cref="Calibrate"/> midiendo una cota conocida.
    /// </summary>
    public static Underlay FromImage(string id, string levelId, string name, string path, int w, int h, double assumedWidthMeters = 20) =>
        new(id, levelId, UnderlayKind.Image, name)
        {
            ImagePath = path, PixelWidth = w, PixelHeight = h,
            Scale = assumedWidthMeters / Math.Max(1, w),
            Origin = new Point2(0, assumedWidthMeters / Math.Max(1, w) * h), // esquina superior izquierda: la imagen queda en Y ≥ 0
            Opacity = 0.5,
        };
}
