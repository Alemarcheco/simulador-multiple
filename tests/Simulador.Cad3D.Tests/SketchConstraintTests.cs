using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Model;
using Xunit;
using C = Simulador.Cad3D.Geometry.ConstraintKind;

namespace Simulador.Cad3D.Tests;

public class SketchConstraintTests
{
    // Cuadrilátero torcido: ningún tramo es horizontal ni vertical.
    private static List<Vec2> Quad() => new() { new(0, 0), new(31, 2), new(29, 22), new(-1, 19) };

    private static double Angle(List<Vec2> p, int i)
    {
        var a = p[i]; var b = p[(i + 1) % p.Count];
        return Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
    }

    [Fact]
    public void Rectangulo_desde_reglas()
    {
        var p = Quad();
        var rules = new List<SketchConstraint>
        {
            new(C.Fijo, 0),
            new(C.Horizontal, 0), new(C.Vertical, 1), new(C.Horizontal, 2), new(C.Vertical, 3),
            new(C.Longitud, 0, Value: 30), new(C.Longitud, 1, Value: 20),
        };
        double err = SketchSolver.Solve(p, rules);
        Assert.True(err < 1e-5, $"error {err}");
        Assert.Equal(new Vec2(0, 0), p[0]);
        Assert.Equal(30, p[1].X, 4); Assert.Equal(0, p[1].Y, 4);
        Assert.Equal(30, p[2].X, 4); Assert.Equal(20, p[2].Y, 4);
        Assert.Equal(0, p[3].X, 4); Assert.Equal(20, p[3].Y, 4);
    }

    [Fact]
    public void Paralelo_perpendicular_e_igual()
    {
        var p = Quad();
        var rules = new List<SketchConstraint> { new(C.Paralelo, 0, 2), new(C.Perpendicular, 0, 1), new(C.IgualLongitud, 1, 3) };
        double err = SketchSolver.Solve(p, rules);
        Assert.True(err < 1e-5, $"error {err} {string.Join(" ", p)}");
        double d = Angle(p, 0) - Angle(p, 2);
        Assert.Equal(0, Math.Abs(Math.Sin(d * Math.PI / 180)), 4);
        double q = Angle(p, 0) - Angle(p, 1);
        Assert.Equal(0, Math.Abs(Math.Cos(q * Math.PI / 180)), 4);
        Assert.Equal(Sketch2D.SegmentLength(p, 1), Sketch2D.SegmentLength(p, 3), 4);
    }

    [Fact]
    public void El_punto_arrastrado_no_se_mueve_y_arrastra_a_los_demas()
    {
        var p = new List<Vec2> { new(0, 0), new(30, 0), new(30, 20), new(0, 20) };
        var rules = new List<SketchConstraint> { new(C.Horizontal, 0), new(C.Vertical, 1), new(C.Horizontal, 2), new(C.Vertical, 3), new(C.Fijo, 0) };
        p[2] = new Vec2(40, 25); // el usuario arrastra la esquina opuesta
        Assert.True(SketchSolver.Solve(p, rules, held: new[] { 2 }) < 1e-5);
        Assert.Equal(new Vec2(40, 25), p[2]);
        Assert.Equal(new Vec2(0, 0), p[0]);
        Assert.Equal(40, p[1].X, 4); Assert.Equal(0, p[1].Y, 4);
        Assert.Equal(0, p[3].X, 4); Assert.Equal(25, p[3].Y, 4);
    }

    [Fact]
    public void Reglas_imposibles_dejan_error()
    {
        var p = new List<Vec2> { new(0, 0), new(30, 0), new(15, 20) };
        var rules = new List<SketchConstraint> { new(C.Horizontal, 0), new(C.Vertical, 0) };
        Assert.True(SketchSolver.Solve(p, rules) >= 1);
        Assert.Equal(new Vec2(30, 0), p[1]); // no aplasta el tramo

        var fixedPts = new List<Vec2> { new(0, 0), new(30, 0), new(15, 20) };
        var tooLong = new List<SketchConstraint> { new(C.Fijo, 0), new(C.Fijo, 1), new(C.Longitud, 0, Value: 50) };
        Assert.Equal(20, SketchSolver.Solve(fixedPts, tooLong), 6);
    }

    [Fact]
    public void Indices_se_mantienen_al_insertar_borrar_e_invertir()
    {
        var rules = new List<SketchConstraint> { new(C.Horizontal, 0), new(C.Paralelo, 1, 3), new(C.Fijo, 2) };
        // Insertar en la posición 2 parte el tramo 1: su regla desaparece, el resto se renumera.
        var ins = SketchSolver.AfterInsert(rules, 2);
        Assert.Equal(new[] { new SketchConstraint(C.Horizontal, 0), new SketchConstraint(C.Fijo, 3) }, ins);
        // Borrar el punto 1 junta los tramos 0 y 1.
        var rem = SketchSolver.AfterRemove(new List<SketchConstraint> { new(C.Horizontal, 2), new(C.Vertical, 0), new(C.Fijo, 3) }, 1, 4);
        Assert.Equal(new[] { new SketchConstraint(C.Horizontal, 1), new SketchConstraint(C.Fijo, 2) }, rem);
        var rev = SketchSolver.AfterReverse(new List<SketchConstraint> { new(C.Horizontal, 0), new(C.Fijo, 0) }, 4);
        Assert.Equal(new[] { new SketchConstraint(C.Horizontal, 2), new SketchConstraint(C.Fijo, 3) }, rev);
    }

    [Fact]
    public void Las_reglas_se_guardan_en_el_archivo()
    {
        var doc = new CadDocument();
        var part = new Part();
        part.Features.Add(new ExtrudeFeature { Constraints = { new(C.Horizontal, 0), new(C.Longitud, 1, Value: 12.5) } });
        doc.Parts.Add(part);
        var json = doc.ToJson();
        Assert.Contains("\"Horizontal\"", json);
        var back = (ExtrudeFeature)CadDocument.FromJson(json).Parts[0].Features[0];
        Assert.Equal(new[] { new SketchConstraint(C.Horizontal, 0), new SketchConstraint(C.Longitud, 1, -1, 12.5) }, back.Constraints);
    }
}
