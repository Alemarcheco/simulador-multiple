using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Model;
using Simulador.Cad3D.Geometry.Occt;
using Simulador.Cad3D.Geometry.Templates;

namespace Simulador.Cad3D.Tests;

/// <summary>Prueba que se salta si la biblioteca nativa de OpenCASCADE no está compilada.</summary>
public sealed class OcctFactAttribute : FactAttribute
{
    public OcctFactAttribute()
    {
        // En la integración continua (CAD3D_REQUIRE_OCCT=1) no se saltan: deben fallar si falta la biblioteca.
        if (Environment.GetEnvironmentVariable("CAD3D_REQUIRE_OCCT") == "1") return;
        if (!OcctKernel.TryCreate(out _, out var why)) Skip = $"OpenCASCADE no disponible: {why}";
    }
}

public class OcctTests
{
    private static OcctKernel K()
    {
        Assert.True(OcctKernel.TryCreate(out var k, out var why), why);
        return k!;
    }

    [OcctFact]
    public void Caja_exacta_y_malla_cerrada()
    {
        var k = K();
        var box = k.Box(new Vec3(10, 20, 30));
        Assert.Equal(6000, k.Volume(box), 6);
        var m = k.ToMesh(box);
        Assert.Equal(6000, m.Volume(), 6);
        Assert.Equal(0, m.CountOpenEdges());
        Assert.StartsWith("OpenCASCADE 7.", k.Version);
    }

    [OcctFact]
    public void Cilindro_tiene_volumen_exacto()
    {
        var k = K();
        Assert.Equal(Math.PI * 25 * 10, k.Volume(k.Cylinder(5, 5, 10, 0)), 6);
        Assert.Equal(Math.PI * 10 / 3 * (25 + 10 + 4), k.Volume(k.Cylinder(5, 2, 10, 0)), 6);
        var m = k.ToMesh(k.Sphere(10, 0));
        Assert.Equal(0, m.CountOpenEdges());
        Assert.InRange(m.Volume(), 4.0 / 3 * Math.PI * 1000 * 0.99, 4.0 / 3 * Math.PI * 1000);
    }

    [OcctFact]
    public void Booleanas_y_transformaciones()
    {
        var k = K();
        var outer = k.Box(new Vec3(20, 20, 20));
        var inner = k.Transform(k.Box(new Vec3(16, 16, 20)), Transform3.Translate(new Vec3(2, 2, 2)));
        var cup = k.Subtract(outer, inner);
        Assert.Equal(8000 - 16 * 16 * 18, k.Volume(cup), 4);
        var rotated = k.Transform(cup, Transform3.FromPose(new Vec3(3, 4, 5), new Vec3(10, 20, 30)));
        Assert.Equal(k.Volume(cup), k.Volume(rotated), 4);
        var mirrored = k.Transform(cup, Transform3.Scale(new Vec3(-1, 1, 1)));
        Assert.Equal(k.Volume(cup), Math.Abs(k.Volume(mirrored)), 3);
        Assert.True(k.ToMesh(mirrored).Volume() > 0);
        Assert.Equal(0, k.ToMesh(cup).CountOpenEdges());
    }

    [OcctFact]
    public void Redondeo_chaflan_y_vaciado()
    {
        var k = K();
        var box = k.Box(new Vec3(40, 30, 20));
        var rounded = k.Fillet(box, 3, EdgeSelection.Verticales);
        // Cada arista vertical redondeada quita (1 - π/4)·r²·alto.
        Assert.Equal(24000 - 4 * (1 - Math.PI / 4) * 9 * 20, k.Volume(rounded), 3);
        var chamfered = k.Chamfer(box, 1, EdgeSelection.Superiores);
        Assert.True(k.Volume(chamfered) < 24000);
        var shell = k.ShellOpenTop(box, 2);
        Assert.Equal(24000 - 36 * 26 * 18, k.Volume(shell), 2);
        Assert.Equal(0, k.ToMesh(rounded).CountOpenEdges());
        Assert.Equal(0, k.ToMesh(shell).CountOpenEdges());
    }

    [OcctFact]
    public void Revolucion_y_ida_y_vuelta_por_step()
    {
        var k = K();
        var vase = k.Revolve(new List<Vec2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) }, 360);
        Assert.Equal(Math.PI * 100 * 10, k.Volume(vase), 3);

        var path = Path.Combine(Path.GetTempPath(), $"cad3d-{Guid.NewGuid():N}.step");
        try
        {
            k.ExportStep(path, new[] { ("Cilindro", vase) });
            Assert.Contains("ISO-10303-21", File.ReadAllText(path));
            var back = k.ImportStep(path);
            Assert.Equal(k.Volume(vase), k.Volume(back), 3);
        }
        finally { File.Delete(path); }
    }

    [OcctFact]
    public void Plantilla_de_caja_con_redondeo_en_el_motor_exacto()
    {
        var k = K();
        var eval = new DocumentEvaluator(k);
        var doc = EnclosureTemplate.Create(new EnclosureParameters
        {
            InnerX = 60, InnerY = 40, InnerZ = 20, VentSlots = 4,
            Standoffs = new[] { new Standoff(5, 5), new Standoff(55, 35) },
            Cutouts = new[] { new WallCutout("USB", Wall.Frontal, 20, 5, 12, 6) },
        });
        doc.Parts[0].Features.Insert(1, new FilletFeature { Radius = 1, Edges = EdgeSelection.Inferiores });
        foreach (var part in doc.Parts)
        {
            var r = eval.Evaluate(part);
            Assert.Empty(r.Errors);
            Assert.True(r.Mesh!.Volume() > 0);
            Assert.Equal(0, r.Mesh.CountOpenEdges());
        }
    }

    [Fact]
    public void Motor_de_mallas_avisa_si_falta_el_exacto()
    {
        var eval = new DocumentEvaluator(new Geometry.Csg.MeshKernel());
        var part = new Part();
        part.Features.Add(new BoxFeature());
        part.Features.Add(new FilletFeature());
        var r = eval.Evaluate(part);
        Assert.Contains(r.Errors, e => e.Contains("OpenCASCADE"));
        Assert.NotNull(r.Mesh); // la caja se sigue viendo
    }
}

public class OcctExamplesTests
{
    public static IEnumerable<object[]> Names() => Examples.All.Select(e => new object[] { e.Name });

    [OcctTheory]
    [MemberData(nameof(Names))]
    public void Ejemplo_con_redondeos_se_construye_en_OpenCASCADE(string name)
    {
        Assert.True(OcctKernel.TryCreate(out var k, out var why), why);
        var eval = new DocumentEvaluator(k!);
        foreach (var part in Examples.All.Single(e => e.Name == name).Create(true).Parts)
        {
            var r = eval.Evaluate(part);
            Assert.Empty(r.Errors);
            Assert.True(r.Mesh!.Volume() > 0);
            Assert.Equal(0, r.Mesh.CountOpenEdges());
        }
    }
}

/// <summary>Como <see cref="OcctFactAttribute"/> pero para pruebas con datos.</summary>
public sealed class OcctTheoryAttribute : TheoryAttribute
{
    public OcctTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("CAD3D_REQUIRE_OCCT") == "1") return;
        if (!OcctKernel.TryCreate(out _, out var why)) Skip = $"OpenCASCADE no disponible: {why}";
    }
}
