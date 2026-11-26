using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Export;
using Simulador.Cad3D.Geometry.Scan;
using Simulador.Core.Modules;
using Simulador.Tableros.Import;
using Simulador.Tableros.Model;
using Simulador.Tableros.Rendering;

namespace Simulador.Tableros.Tests;

/// <summary>Piezas escaneadas que Diseño 3D guarda como aparato (.edz) y que se usan en el tablero.</summary>
public class ScannedPartTests
{
    /// <summary>Caja de 30 × 50 × 70 mm con la mitad de abajo roja (Z del módulo 3D arriba).</summary>
    private static ScanMesh Box()
    {
        var m = new ScanMesh { Colors = [] };
        Vec3[] v =
        [
            new(0, 0, 0), new(30, 0, 0), new(30, 50, 0), new(0, 50, 0),
            new(0, 0, 70), new(30, 0, 70), new(30, 50, 70), new(0, 50, 70),
        ];
        foreach (var p in v) { m.Vertices.Add(p); m.Colors.Add(p.Z < 1 ? 0xFFFF0000 : 0xFFFFFFFF); }
        int[] f = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7];
        m.Indices.AddRange(f);
        return m;
    }

    [Fact]
    public void LaPiezaGuardadaEnDiseno3DSeLeeConSuMedidaYSuModelo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "escaneo-" + Guid.NewGuid().ToString("N")[..8]);
        var path = Path.Combine(dir, EdzWriter.FileName("Mis piezas", "CONT-ESC-1"));
        EdzWriter.Write(path, Box(), new EdzWriter.Options("Mis piezas", "CONT-ESC-1", "Contactor escaneado", "contactor", false, true, MountFace.Atras));
        var r = EdzImporter.Read(path);
        var it = Assert.Single(r.Items);
        Assert.Equal("Mis piezas", it.Brand);
        Assert.Equal("contactor", it.Kind);
        Assert.True(it.OnRail);
        Assert.Equal(DeviceLocation.Plate, it.Location);
        // Apoyada por detrás: ancho X 30, alto Z 70, fondo (lo que sobresale de la placa) 50.
        Assert.Equal((30.0, 70.0, 50.0), (it.Width, it.Height, it.Depth));
        Assert.NotNull(it.Mesh);
        Assert.Equal(12, it.Mesh!.Triangles.Count);
        Assert.Contains(it.Mesh.Triangles, t => t.Color == 0xFFFF0000 || t.Color == 0xFFFFAAAA);
        Assert.Equal(70, it.Mesh.Size.Z, 1);
        Assert.Equal(50, it.Mesh.Size.Y, 1);
    }

    [Fact]
    public void AlGuardarseApareceEnLaLibreriaYSeDibujaEnElTablero()
    {
        ManufacturerCatalog.ClearImported();
        var dir = Path.Combine(Path.GetTempPath(), "escaneo-" + Guid.NewGuid().ToString("N")[..8]);
        var path = Path.Combine(dir, EdzWriter.FileName("Mis piezas", "PUL-ESC"));
        EdzWriter.Write(path, Box(), new EdzWriter.Options("Mis piezas", "PUL-ESC", "Pulsador escaneado", "pulsador", true, false, MountFace.Abajo, DoorProtrusion: 20));
        var r = LibraryStore.LoadFile(path);
        var it = Assert.Single(r.Items);
        Assert.Equal(DeviceLocation.Door, it.Location);
        // En la puerta solo sobresalen 20 mm por fuera; el resto queda dentro (Y negativa).
        Assert.Equal(20, it.Mesh!.Max.Y, 1);
        Assert.Equal(-50, it.Mesh.Min.Y, 1);
        Assert.Same(it, ManufacturerCatalog.Find("Mis piezas PUL-ESC"));
        Assert.Contains("Mis piezas", ManufacturerCatalog.Brands);

        var doc = PanelBuilder.Examples[0].Create();
        var d = DeviceLibrary.Create(it.Kind, "S99");
        ManufacturerCatalog.Apply(d, it);
        doc.Devices.Add(d);
        AutoLayout.PlaceInFreeSpot(doc, d);
        Assert.Equal(30, d.Width, 1);
        ManufacturerCatalog.ClearImported();
    }

    [Fact]
    public void ElAvisoEntreModulosLlegaAQuienEscucha()
    {
        string? got = null;
        ModuleExchange.Subscribe("prueba.escaneo", v => got = v);
        Assert.Equal(1, ModuleExchange.Notify("prueba.escaneo", "x.edz"));
        Assert.Equal("x.edz", got);
    }
}
