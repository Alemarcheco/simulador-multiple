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
                    if (f is Simulador.Cad3D.Geometry.Model.ExtrudeFeature e) Assert.Null(Sketch2D.Validate(e.Profile, false));
                    if (f is Simulador.Cad3D.Geometry.Model.RevolveFeature r) Assert.Null(Sketch2D.Validate(r.Profile, true));
                }
    }
}
