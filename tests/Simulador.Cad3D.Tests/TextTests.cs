using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Csg;
using Simulador.Cad3D.Geometry.Model;
using Xunit;

namespace Simulador.Cad3D.Tests;

public class TextTests
{
    [Fact]
    public void LetterOHasOuterContourAndHole()
    {
        var c = TextOutline.Contours("O", "Arial", true, 10, TextAlign.Izquierda);
        Assert.Equal(2, c.Count);
        var outer = c.OrderByDescending(x => Math.Abs(Polygon2D.SignedArea(x))).First();
        var inner = c.First(x => !ReferenceEquals(x, outer));
        Assert.True(TextOutline.Contains(outer, inner));
        Assert.False(TextOutline.Contains(inner, outer));
        // Altura de las mayúsculas ≈ 10 mm (la «O» sobresale un poco de la línea base y de la altura).
        double h = outer.Max(p => p.Y) - outer.Min(p => p.Y);
        Assert.InRange(h, 9.5, 11);
    }

    [Fact]
    public void AlignmentShiftsText()
    {
        var left = TextOutline.Contours("HOLA", "Arial", true, 10, TextAlign.Izquierda).SelectMany(x => x).ToList();
        var center = TextOutline.Contours("HOLA", "Arial", true, 10, TextAlign.Centro).SelectMany(x => x).ToList();
        Assert.True(left.Min(p => p.X) >= -0.01);
        Assert.Equal(0, (center.Min(p => p.X) + center.Max(p => p.X)) / 2, 0);
    }

    [Fact]
    public void RaisedTextIsClosedSolid()
    {
        var part = new Part { Features = { new TextFeature { Text = "Ab8", Size = 8, Depth = 1 } } };
        var r = new DocumentEvaluator(new MeshKernel()).Evaluate(part);
        Assert.Empty(r.Errors);
        Assert.Equal(0, r.Mesh!.CountOpenEdges());
        Assert.InRange(r.Mesh.Volume(), 10, 200);
    }

    private static Part EngravedPlate(string text) => new()
    {
        Features =
        {
            new BoxFeature { SizeX = 60, SizeY = 20, SizeZ = 3 },
            new TextFeature { Text = text, Size = 8, Depth = 2, Mode = CombineMode.Hueco, Position = new Vec3(5, 6, 2) },
        },
    };

    [Fact]
    public void EngravedLetterWithMeshKernel()
    {
        var r = new DocumentEvaluator(new MeshKernel()).Evaluate(EngravedPlate("T"));
        Assert.Empty(r.Errors);
        Assert.Equal(0, r.Mesh!.CountOpenEdges());
        Assert.True(r.Mesh.Volume() < 60 * 20 * 3 - 5);
    }

    // Con varias letras grabadas el motor de mallas (BSP) puede dejar grietas; OpenCASCADE, que es el que usa la app, no.
    [OcctFact]
    public void EngravedWordWithOpenCascade()
    {
        var r = new DocumentEvaluator(KernelFactory.CreateBest()).Evaluate(EngravedPlate("TEST 8"));
        Assert.Empty(r.Errors);
        Assert.Equal(0, r.Mesh!.CountOpenEdges());
        Assert.True(r.Mesh.Volume() < 60 * 20 * 3 - 10);
    }

    [OcctFact]
    public void RaisedTextWithOpenCascade()
    {
        var r = new DocumentEvaluator(KernelFactory.CreateBest()).Evaluate(new Part { Features = { new TextFeature { Text = "Ñandú 9@", Size = 6 } } });
        Assert.Empty(r.Errors);
        Assert.Equal(0, r.Mesh!.CountOpenEdges());
    }

    [Fact]
    public void EmptyTextReportsError()
    {
        var r = new DocumentEvaluator(new MeshKernel()).Evaluate(new Part { Features = { new TextFeature { Text = "  " } } });
        Assert.NotEmpty(r.Errors);
    }
}
