using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Csg;

namespace Simulador.Cad3D.Tests;

public class KernelTests
{
    private readonly MeshKernel _k = new();

    [Fact]
    public void Caja_tiene_volumen_exacto_y_es_estanca()
    {
        var m = _k.ToMesh(_k.Box(new Vec3(10, 20, 30)));
        Assert.Equal(6000, m.Volume(), 6);
        Assert.Equal(0, m.CountOpenEdges());
        Assert.Equal(12, m.TriangleCount);
    }

    [Fact]
    public void Cilindro_y_esfera_aproximan_su_volumen()
    {
        var cyl = _k.ToMesh(_k.Cylinder(5, 5, 10, 128));
        Assert.InRange(cyl.Volume(), Math.PI * 25 * 10 * 0.99, Math.PI * 25 * 10);
        Assert.Equal(0, cyl.CountOpenEdges());

        var sph = _k.ToMesh(_k.Sphere(10, 64));
        Assert.InRange(sph.Volume(), 4.0 / 3 * Math.PI * 1000 * 0.97, 4.0 / 3 * Math.PI * 1000);
        Assert.Equal(0, sph.CountOpenEdges());

        var cone = _k.ToMesh(_k.Cylinder(5, 0, 9, 128));
        Assert.InRange(cone.Volume(), Math.PI * 25 * 9 / 3 * 0.99, Math.PI * 25 * 9 / 3);
        Assert.Equal(0, cone.CountOpenEdges());
    }

    [Fact]
    public void Resta_de_caja_deja_un_vaso_estanco()
    {
        var outer = _k.Box(new Vec3(20, 20, 20));
        var inner = _k.Transform(_k.Box(new Vec3(16, 16, 20)), Transform3.Translate(new Vec3(2, 2, 2)));
        var m = _k.ToMesh(_k.Subtract(outer, inner));
        Assert.Equal(8000 - 16 * 16 * 18, m.Volume(), 3);
        Assert.Equal(0, m.CountOpenEdges());
    }

    [Fact]
    public void Union_e_interseccion_de_cajas_solapadas()
    {
        var a = _k.Box(new Vec3(10, 10, 10));
        var b = _k.Transform(_k.Box(new Vec3(10, 10, 10)), Transform3.Translate(new Vec3(5, 5, 5)));
        var u = _k.ToMesh(_k.Union(a, b));
        var i = _k.ToMesh(_k.Intersect(a, b));
        Assert.Equal(2000 - 125, u.Volume(), 3);
        Assert.Equal(125, i.Volume(), 3);
        Assert.Equal(0, u.CountOpenEdges());
        Assert.Equal(0, i.CountOpenEdges());
    }

    [Fact]
    public void Agujero_cilindrico_en_placa_es_estanco()
    {
        var plate = _k.Box(new Vec3(30, 30, 3));
        var hole = _k.Transform(_k.Cylinder(2, 2, 5, 32), Transform3.Translate(new Vec3(15, 15, -1)));
        var m = _k.ToMesh(_k.Subtract(plate, hole));
        Assert.True(m.Volume() < 2700 && m.Volume() > 2700 - Math.PI * 4 * 3);
        Assert.Equal(0, m.CountOpenEdges());
    }

    [Fact]
    public void Extrusion_de_perfil_concavo()
    {
        // Perfil en L de 2 cuadrados de 10x10 más otro: área 300.
        var profile = new List<Vec2> { new(0, 0), new(20, 0), new(20, 10), new(10, 10), new(10, 20), new(0, 20) };
        var m = _k.ToMesh(_k.Extrude(profile, 5));
        Assert.Equal(1500, m.Volume(), 6);
        Assert.Equal(0, m.CountOpenEdges());

        // El mismo perfil en sentido horario da el mismo sólido.
        profile.Reverse();
        Assert.Equal(1500, _k.ToMesh(_k.Extrude(profile, 5)).Volume(), 6);
    }

    [Fact]
    public void Rotacion_conserva_volumen_y_espejo_mantiene_orientacion()
    {
        var box = _k.Box(new Vec3(10, 20, 30));
        var rotated = _k.ToMesh(_k.Transform(box, Transform3.FromPose(new Vec3(5, 5, 5), new Vec3(30, 45, 60))));
        Assert.Equal(6000, rotated.Volume(), 4);
        var mirrored = _k.ToMesh(_k.Transform(box, Transform3.Scale(new Vec3(-1, 1, 1))));
        Assert.Equal(6000, mirrored.Volume(), 4);
    }
}
