using System.IO.Compression;
using System.Xml.Linq;
using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Csg;
using Simulador.Cad3D.Geometry.Export;
using Simulador.Cad3D.Geometry.Model;
using Simulador.Cad3D.Geometry.Templates;
using Simulador.Core.Pcb;

namespace Simulador.Cad3D.Tests;

public class DocumentTests
{
    private readonly DocumentEvaluator _eval = new(new MeshKernel());

    [Fact]
    public void Historial_aplica_solidos_y_huecos_en_orden()
    {
        var part = new Part();
        part.Features.Add(new BoxFeature { SizeX = 20, SizeY = 20, SizeZ = 10 });
        part.Features.Add(new CylinderFeature { Mode = CombineMode.Hueco, Diameter = 4, Height = 12, Position = new Vec3(10, 10, -1) });
        part.Features.Add(new BoxFeature { SizeX = 5, SizeY = 5, SizeZ = 5, Position = new Vec3(0, 0, 10) });
        var r = _eval.Evaluate(part);
        Assert.Empty(r.Errors);
        Assert.NotNull(r.Mesh);
        Assert.InRange(r.Mesh!.Volume(), 4000 - Math.PI * 4 * 10 + 125 - 1, 4000 - Math.PI * 4 * 10 + 125 + 1);
        Assert.Equal(0, r.Mesh.CountOpenEdges());
    }

    [Fact]
    public void Operacion_desactivada_no_cuenta()
    {
        var part = new Part();
        part.Features.Add(new BoxFeature { SizeX = 10, SizeY = 10, SizeZ = 10 });
        part.Features.Add(new BoxFeature { Mode = CombineMode.Hueco, SizeX = 5, SizeY = 10, SizeZ = 10, Enabled = false });
        Assert.Equal(1000, _eval.Evaluate(part).Mesh!.Volume(), 6);
    }

    [Fact]
    public void Proyecto_se_guarda_y_se_abre_igual()
    {
        var doc = EnclosureTemplate.Create(new EnclosureParameters { VentSlots = 3, Standoffs = new[] { new Standoff(5, 5) } });
        doc.Parts[0].Features.Add(new ExtrudeFeature { Name = "Logo", Height = 1 });
        doc.Parts[0].Features.Add(new SphereFeature { Mode = CombineMode.Interseccion, Diameter = 500 });
        var json = doc.ToJson();
        var back = CadDocument.FromJson(json);
        Assert.Equal(json, back.ToJson());
        Assert.IsType<ExtrudeFeature>(back.Parts[0].Features[^2]);
        Assert.Contains("\"tipo\": \"caja_redondeada\"", json);
    }

    [Fact]
    public void Plantilla_de_caja_genera_caja_y_tapa_imprimibles()
    {
        var doc = EnclosureTemplate.Create(new EnclosureParameters
        {
            InnerX = 60, InnerY = 40, InnerZ = 20, VentSlots = 4,
            Standoffs = new[] { new Standoff(5, 5), new Standoff(55, 35) },
            Cutouts = new[] { new WallCutout("USB", Wall.Frontal, 20, 5, 12, 6) },
        });
        Assert.Equal(2, doc.Parts.Count);
        foreach (var part in doc.Parts)
        {
            var r = _eval.Evaluate(part);
            Assert.Empty(r.Errors);
            Assert.True(r.Mesh!.Volume() > 0);
            Assert.Equal(0, r.Mesh.CountOpenEdges());
        }
        // La tapa no se solapa con la caja en la bandeja.
        var box = _eval.Evaluate(doc.Parts[0]).Mesh!.Bounds();
        var lid = _eval.Evaluate(doc.Parts[1]).Mesh!.Bounds();
        Assert.True(lid.Min.X > box.Max.X);
    }

    [Fact]
    public void Caja_a_medida_de_una_placa()
    {
        var board = new BoardExport(
            "Placa",
            new[] { new PointMm(10, 10), new PointMm(60, 10), new PointMm(60, 40), new PointMm(10, 40) },
            Array.Empty<IReadOnlyList<PointMm>>(),
            1.6,
            new[] { new BoardHole(new PointMm(13, 13), 3.2, false), new BoardHole(new PointMm(57, 37), 3.2, false), new BoardHole(new PointMm(30, 20), 1.0, true, "R1") },
            new[]
            {
                new BoardComponent("J1", "USB_C", "USB_C", new PointMm(35, 11), 0, BoardSide.Top, 3.5, null),
                new BoardComponent("U1", "ATmega", "TQFP", new PointMm(35, 25), 0, BoardSide.Top, 1.2, null),
            },
            Array.Empty<BoardConnectorPin>());
        var doc = EnclosureTemplate.FromBoard(board);
        var box = doc.Parts[0];
        Assert.Equal(2, box.Features.Count(f => f.Name.StartsWith("Poste")));
        Assert.Contains(box.Features, f => f.Name == "Hueco J1" && f.Mode == CombineMode.Hueco);
        var r = _eval.Evaluate(box);
        Assert.Empty(r.Errors);
        Assert.Equal(0, r.Mesh!.CountOpenEdges());
    }

    [Fact]
    public void Exporta_stl_binario_y_3mf_validos()
    {
        var k = new MeshKernel();
        var mesh = k.ToMesh(k.Box(new Vec3(10, 10, 10)));

        using var stl = new MemoryStream();
        StlWriter.WriteBinary(stl, new[] { mesh });
        Assert.Equal(84 + 50 * 12, stl.Length);

        using var ms = new MemoryStream();
        ThreeMfWriter.Write(ms, new[] { new ThreeMfWriter.Item("Cubo", mesh, "#FF0000"), new ThreeMfWriter.Item("Otro", mesh) });
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("_rels/.rels"));
        using var s = zip.GetEntry("3D/3dmodel.model")!.Open();
        var xml = XDocument.Load(s);
        XNamespace ns = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
        Assert.Equal("millimeter", xml.Root!.Attribute("unit")!.Value);
        Assert.Equal(2, xml.Descendants(ns + "object").Count());
        Assert.Equal(24, xml.Descendants(ns + "triangle").Count());
        Assert.Equal("#FF0000FF", xml.Descendants(ns + "base").First().Attribute("displaycolor")!.Value);
    }
}
