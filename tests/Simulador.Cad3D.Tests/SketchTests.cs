using Simulador.Cad3D.Geometry;
using Xunit;

namespace Simulador.Cad3D.Tests;

public class SketchTests
{
    [Fact]
    public void SnapRoundsToGrid()
    {
        Assert.Equal(new Vec2(3, -2), Sketch2D.Snap(new Vec2(3.4, -1.6), 1));
        Assert.Equal(new Vec2(2.5, 0), Sketch2D.Snap(new Vec2(2.4, 0.2), 0.5));
        Assert.Equal(new Vec2(2.4, 0.2), Sketch2D.Snap(new Vec2(2.4, 0.2), 0));
    }

    [Fact]
    public void SetSegmentLengthMovesNextPointAlongDirection()
    {
        var pts = Sketch2D.Rectangle(40, 20);
        Assert.True(Sketch2D.SetSegmentLength(pts, 0, 55));
        Assert.Equal(55, pts[1].X, 9);
        Assert.Equal(0, pts[1].Y, 9);
        Assert.Equal(55, Sketch2D.SegmentLength(pts, 0), 9);
        // El último tramo cierra con el primer punto.
        Assert.True(Sketch2D.SetSegmentLength(pts, 3, 10));
        Assert.Equal(new Vec2(0, 10), pts[0]);
    }

    [Fact]
    public void AddPointInsertsIntoNearestSegment()
    {
        var pts = Sketch2D.Rectangle(40, 20);
        int i = Sketch2D.AddPoint(pts, new Vec2(20, -1));
        Assert.Equal(1, i);
        Assert.Equal(5, pts.Count);
        Assert.Null(Sketch2D.Validate(pts, revolve: false));
    }

    [Fact]
    public void ValidateDetectsProblems()
    {
        var bowtie = new List<Vec2> { new(0, 0), new(10, 10), new(10, 0), new(0, 10) };
        Assert.True(Sketch2D.SelfIntersects(bowtie));
        Assert.NotNull(Sketch2D.Validate(bowtie, false));
        Assert.NotNull(Sketch2D.Validate(new List<Vec2> { new(0, 0), new(5, 0), new(10, 0) }, false));
        Assert.NotNull(Sketch2D.Validate(Sketch2D.RegularPolygon(new Vec2(0, 0), 5, 6), revolve: true));
        Assert.Null(Sketch2D.Validate(Sketch2D.RegularPolygon(new Vec2(10, 5), 5, 6), revolve: true));
        Assert.Null(Sketch2D.Validate(Sketch2D.Rectangle(4, 4), false));
    }

    [Fact]
    public void ExamplesHaveValidProfiles()
    {
        foreach (var ex in Simulador.Cad3D.Geometry.Templates.Examples.All)
            foreach (var part in ex.Create(false).Parts)
                foreach (var f in part.Features)
                {
                    if (f is Simulador.Cad3D.Geometry.Model.ExtrudeFeature e) Assert.Null(Sketch2D.Validate(e.Profile, e.Bulges, false));
                    if (f is Simulador.Cad3D.Geometry.Model.RevolveFeature r) Assert.Null(Sketch2D.Validate(r.Profile, r.Bulges, true));
                }
    }
}

public class SketchArcTests
{
    private static double Area(List<Vec2> p, List<double> b) => Polygon2D.SignedArea(Sketch2D.Tessellate(p, b, 0.001));

    [Fact]
    public void CircleFromTwoSemicirclesHasCircleArea()
    {
        var (p, b) = Sketch2D.CircleArcs(new Vec2(5, 5), 10);
        Assert.Equal(Math.PI * 100, Area(p, b), 0);
        Assert.Null(Sketch2D.Validate(p, b, revolve: false));
        Assert.Equal(Math.PI * 10, Sketch2D.PathLength(p, b, 0), 6);
    }

    [Fact]
    public void ArcGeometryAndRadiusRoundTrip()
    {
        var a = new Vec2(0, 0); var c = new Vec2(10, 0);
        var bulge = Sketch2D.BulgeFromRadius(a, c, 8, +1)!.Value;
        var g = Sketch2D.ArcGeometry(a, c, bulge)!.Value;
        Assert.Equal(8, g.Radius, 9);
        Assert.Equal(8, Sketch2D.Length(g.Center, a), 9);
        Assert.Equal(8, Sketch2D.Length(g.Center, c), 9);
        // Abombamiento positivo: el arco queda a la derecha del tramo (abajo, al ir hacia +X).
        Assert.True(Sketch2D.ArcMidpoint(a, c, bulge).Y < 0);
        Assert.Equal(bulge, Sketch2D.BulgeThrough(a, c, Sketch2D.ArcMidpoint(a, c, bulge)), 9);
        Assert.Null(Sketch2D.BulgeFromRadius(a, c, 4, 1)); // radio menor que media cuerda
    }

    [Fact]
    public void FilletCornerKeepsTangentArcAndArea()
    {
        var pts = Sketch2D.Rectangle(40, 20);
        var bl = new List<double>();
        Assert.Null(Sketch2D.FilletCorner(pts, bl, 2, 5));
        Assert.Equal(5, pts.Count);
        Assert.Equal(new Vec2(40, 15), pts[2]);
        Assert.Equal(new Vec2(35, 20), pts[3]);
        // Se quita una esquina de 5×5 y se añade un cuarto de círculo.
        Assert.Equal(800 - 25 + Math.PI * 25 / 4, Area(pts, bl), 2);
        Assert.NotNull(Sketch2D.FilletCorner(pts, bl, 0, 30)); // radio demasiado grande
        Assert.NotNull(Sketch2D.FilletCorner(pts, bl, 2, 1));  // ya es un arco
    }

    [Fact]
    public void RoundedRectangleWithArcs()
    {
        var (p, b) = Sketch2D.RoundedRectangle(40, 20, 4);
        Assert.Equal(8, p.Count);
        Assert.InRange(Area(p, b), 800 - 4 * 16 + Math.PI * 16 - 0.05, 800 - 4 * 16 + Math.PI * 16);
        Assert.Null(Sketch2D.Validate(p, b, false));
        var (p2, b2) = Sketch2D.RoundedRectangle(40, 20, 10); // lados cortos convertidos en semicírculos
        Assert.InRange(Area(p2, b2), 800 - 4 * 100 + Math.PI * 100 - 0.1, 800 - 4 * 100 + Math.PI * 100);
        Assert.Equal(6, p2.Count);
    }

    [Fact]
    public void NormalizeReversesArcsWithProfile()
    {
        var (p, b) = Sketch2D.RoundedRectangle(40, 20, 4);
        var rp = Enumerable.Reverse(p).ToList();
        // Mismo contorno recorrido al revés: el tramo i del original es el n-2-i del invertido, con signo contrario.
        int n = p.Count;
        var rb = Enumerable.Range(0, n).Select(j => -b[((n - 2 - j) % n + n) % n]).ToList();
        Assert.True(Area(rp, rb) < 0);
        var (np, nb) = Sketch2D.Normalize(rp, rb);
        Assert.Equal(Area(p, b), Area(np, nb), 6);
    }

    [Fact]
    public void ExtrudeWithArcsGivesExactVolume()
    {
        var (p, b) = Sketch2D.CircleArcs(new Vec2(0, 0), 10);
        var f = new Simulador.Cad3D.Geometry.Model.ExtrudeFeature { Profile = p, Bulges = b, Height = 5 };
        var k = new Simulador.Cad3D.Geometry.Csg.MeshKernel();
        var mesh = k.ToMesh(f.Build(k));
        // Los arcos se dividen con 0.01 mm de error de cuerda: el volumen queda a menos del 0.2 %.
        Assert.InRange(mesh.Volume(), Math.PI * 100 * 5 * 0.998, Math.PI * 100 * 5);
        Assert.Equal(0, mesh.CountOpenEdges());
    }

    [Fact]
    public void ArcsSurviveSaveAndOpen()
    {
        var doc = Simulador.Cad3D.Geometry.Templates.Examples.PipeClamp(false);
        var again = Simulador.Cad3D.Geometry.Model.CadDocument.FromJson(doc.ToJson());
        var a = (Simulador.Cad3D.Geometry.Model.ExtrudeFeature)doc.Parts[0].Features[0];
        var b = (Simulador.Cad3D.Geometry.Model.ExtrudeFeature)again.Parts[0].Features[0];
        Assert.Equal(a.Bulges, b.Bulges);
        Assert.Contains(b.Bulges, x => Math.Abs(x - 1) < 1e-12);
        // Los archivos antiguos, sin arcos, siguen abriéndose con todos los tramos rectos.
        var old = Simulador.Cad3D.Geometry.Model.CadDocument.FromJson(doc.ToJson().Replace("\"bulges\"", "\"ignorado\""));
        Assert.Empty(((Simulador.Cad3D.Geometry.Model.ExtrudeFeature)old.Parts[0].Features[0]).Bulges);
    }
}
