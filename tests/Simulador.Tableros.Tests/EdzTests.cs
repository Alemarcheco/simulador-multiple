using System.IO.Compression;
using Simulador.Tableros.Import;
using Simulador.Tableros.Model;
using Simulador.Tableros.Rendering;

namespace Simulador.Tableros.Tests;

public class EdzTests
{
    private static string Bundled => LibraryStore.BundledFolder;

    [Fact]
    public void LasLibreriasQueTraeElProgramaTienenModelo3DConLaMedidaDeLaFicha()
    {
        var files = Directory.GetFiles(Bundled, "*.edz");
        Assert.True(files.Length >= 40, $"Solo hay {files.Length} paquetes en {Bundled}");
        foreach (var f in files)
        {
            var r = EdzImporter.Read(f);
            var it = Assert.Single(r.Items);
            Assert.NotNull(it.Mesh);
            Assert.Equal(it.Kind, DeviceLibrary.Get(it.Kind).Kind);
            Assert.True(it.Width > 0 && it.Height > 0 && it.Depth > 0, it.Reference);
            Assert.False(string.IsNullOrWhiteSpace(it.Source));
            // El cuerpo del modelo ocupa el ancho y el alto de la ficha (puede sobresalir algo: orejas, conectores).
            var size = it.Mesh!.Size;
            Assert.True(size.X >= it.Width * 0.95 && size.X <= it.Width * 1.1 + 2, $"{it.Reference}: ancho {size.X}");
            Assert.True(size.Z >= it.Height * 0.9 && size.Z <= it.Height * 1.15 + 2, $"{it.Reference}: alto {size.Z}");
        }
    }

    [Fact]
    public void LeeLasMedidasYElTipoDeUnPaquete()
    {
        var r = EdzImporter.Read(Path.Combine(Bundled, "Pepperl+Fuchs_KCD2-SCD-Ex1.edz"));
        var it = Assert.Single(r.Items);
        Assert.Equal("Pepperl+Fuchs", it.Brand);
        Assert.Equal("KCD2-SCD-Ex1", it.Reference);
        Assert.Equal((12.5, 124.0, 114.0), (it.Width, it.Height, it.Depth));
        Assert.True(it.Verified);
        Assert.True(it.OnRail);
        Assert.Contains("70150440", it.Keys!);
    }

    [Fact]
    public void LeeUnPaqueteConLasPropiedadesDeEplanComoElementosYSinModelo3D()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prueba-{Guid.NewGuid():N}.edz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("items/pieza.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("""
                <?xml version="1.0" encoding="utf-8"?>
                <partsmanagement><part>
                  <P_ARTICLE_PARTNR>ACME.RL-24</P_ARTICLE_PARTNR>
                  <P_ARTICLE_DESCR1>en_US@Interface relay 24 V DC;es_ES@Relé de interfaz 24 V DC;</P_ARTICLE_DESCR1>
                  <property name="P_ARTICLE_WIDTH" value="6,2" />
                  <P_ARTICLE_HEIGHT>90</P_ARTICLE_HEIGHT><P_ARTICLE_DEPTH>80 mm</P_ARTICLE_DEPTH>
                </part></partsmanagement>
                """);
        }
        try
        {
            var it = Assert.Single(EdzImporter.Read(path).Items);
            Assert.Equal(("ACME", "RL-24", "rele"), (it.Brand, it.Reference, it.Kind));
            Assert.Equal("Relé de interfaz 24 V DC", it.Description);
            Assert.Equal((6.2, 90.0, 80.0), (it.Width, it.Height, it.Depth));
            Assert.Null(it.Mesh);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LaVista3DDibujaElModeloDelPaquete()
    {
        LibraryStore.LoadFolder(Bundled);
        var item = ManufacturerCatalog.Find("Allen-Bradley 1756-PA75R")!;
        Assert.NotNull(item.Mesh);
        var doc = new PanelDocument();
        var d = DeviceLibrary.Create("fuente", "G1");
        ManufacturerCatalog.Apply(d, item);
        d.X = 100;
        d.Y = 100;
        doc.Devices.Add(d);
        var options = new PanelSceneOptions(ShowEnclosure: false, ShowWires: false, ShowDoor: false);
        var withMesh = PanelScene.Build(doc, null, options).Triangles.Count;
        d.Reference = "";
        var generic = PanelScene.Build(doc, null, options).Triangles.Count;
        Assert.Equal(item.Mesh!.Triangles.Count, withMesh - (generic - 12 * 2));
    }
}
