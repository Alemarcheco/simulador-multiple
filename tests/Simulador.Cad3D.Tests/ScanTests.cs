using System.Globalization;
using System.IO.Compression;
using System.Text;
using Simulador.Cad3D.Geometry;
using Simulador.Cad3D.Geometry.Csg;
using Simulador.Cad3D.Geometry.Export;
using Simulador.Cad3D.Geometry.Model;
using Simulador.Cad3D.Geometry.Scan;

namespace Simulador.Cad3D.Tests;

public class ScanTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Caja en metros con Y arriba (como ARKit), sin la cara de abajo (la que tocaba la mesa), y una mota suelta.</summary>
    private static string OpenBoxObj(double w, double h, double d, bool withDust = true)
    {
        var sb = new StringBuilder("# caja de prueba\n");
        (double, double, double)[] v =
        [
            (0, 0, 0), (w, 0, 0), (w, 0, d), (0, 0, d),
            (0, h, 0), (w, h, 0), (w, h, d), (0, h, d),
        ];
        foreach (var (x, y, z) in v) sb.Append(string.Create(Inv, $"v {x} {y} {z}\n"));
        // Caras con Y arriba: abajo (y = 0) falta a propósito.
        int[][] faces = [[5, 6, 7, 8], [1, 2, 6, 5], [2, 3, 7, 6], [3, 4, 8, 7], [4, 1, 5, 8]];
        foreach (var f in faces) sb.Append("f ").Append(string.Join(' ', f)).Append('\n');
        if (withDust)
        {
            sb.Append(string.Create(Inv, $"v {w * 3} {h * 3} {d * 3}\nv {w * 3 + 0.001} {h * 3} {d * 3}\nv {w * 3} {h * 3 + 0.001} {d * 3}\n"));
            sb.Append("f 9 10 11\n");
        }
        return sb.ToString();
    }

    private static string Temp(string ext, byte[] data)
    {
        var dir = Path.Combine(Path.GetTempPath(), "scan-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "pieza" + ext);
        File.WriteAllBytes(p, data);
        return p;
    }

    [Fact]
    public void Obj_en_metros_se_pasa_a_mm_se_limpia_y_queda_cerrado()
    {
        var path = Temp(".obj", Encoding.UTF8.GetBytes(OpenBoxObj(0.045, 0.08, 0.06)));
        var f = new ScanFeature { FilePath = path };
        var m = f.Process();
        var s = m.Size;
        // X ancho 45, Y (fondo) = Z del archivo = 60, Z (alto) = Y del archivo = 80.
        Assert.Equal(45, s.X, 1);
        Assert.Equal(60, s.Y, 1);
        Assert.Equal(80, s.Z, 1);
        Assert.Equal(0, m.CountBorderEdges());
        Assert.True(m.Volume() > 0);
        Assert.Equal(45 * 60 * 80, m.Volume(), 0);
        Assert.Contains("trozo", f.Report);
        Assert.Equal(0, m.Bounds().Min.Z, 6);
    }

    [Fact]
    public void Medida_real_escala_sin_deformar()
    {
        var path = Temp(".obj", Encoding.UTF8.GetBytes(OpenBoxObj(0.045, 0.08, 0.06, withDust: false)));
        var m = new ScanFeature { FilePath = path, TargetSizeZ = 100 }.Process();
        Assert.Equal(100, m.Size.Z, 1);
        Assert.Equal(45 * 100 / 80.0, m.Size.X, 1);
    }

    [Fact]
    public void Cortar_la_base_deja_tapa_plana_y_cerrada()
    {
        var path = Temp(".obj", Encoding.UTF8.GetBytes(OpenBoxObj(0.04, 0.05, 0.03, withDust: false)));
        var m = new ScanFeature { FilePath = path, CutBottom = 10 }.Process();
        Assert.Equal(40, m.Size.Z, 1);
        Assert.Equal(0, m.CountBorderEdges());
        Assert.Equal(40 * 30 * 40, m.Volume(), 0);
    }

    [Fact]
    public void Simplificar_reduce_triangulos_y_mantiene_medida()
    {
        // Esfera densa en mm (Z arriba, STL de texto).
        var tris = new List<(Vec3, Vec3, Vec3)>();
        int n = 80;
        Vec3 P(int i, int j)
        {
            double th = Math.PI * i / n, ph = 2 * Math.PI * j / n;
            return new Vec3(25 * Math.Sin(th) * Math.Cos(ph), 25 * Math.Sin(th) * Math.Sin(ph), 25 * Math.Cos(th));
        }
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                tris.Add((P(i, j), P(i + 1, j), P(i + 1, j + 1)));
                tris.Add((P(i, j), P(i + 1, j + 1), P(i, j + 1)));
            }
        var sb = new StringBuilder("solid esfera\n");
        foreach (var (a, b, c) in tris)
            sb.Append(string.Create(Inv, $"facet normal 0 0 0\nouter loop\nvertex {a.X} {a.Y} {a.Z}\nvertex {b.X} {b.Y} {b.Z}\nvertex {c.X} {c.Y} {c.Z}\nendloop\nendfacet\n"));
        sb.Append("endsolid\n");
        var path = Temp(".stl", Encoding.ASCII.GetBytes(sb.ToString()));
        var m = new ScanFeature { FilePath = path, MaxTriangles = 2000, Units = ScanUnits.Milimetros }.Process();
        Assert.InRange(m.TriangleCount, 500, 2000);
        Assert.Equal(50, m.Size.Z, 0);
        Assert.Equal(50, m.Size.X, 0);
    }

    [Fact]
    public void Ply_binario_con_colores()
    {
        // Tetraedro con un color por vértice.
        var head = "ply\nformat binary_little_endian 1.0\nelement vertex 4\nproperty float x\nproperty float y\nproperty float z\n" +
                   "property uchar red\nproperty uchar green\nproperty uchar blue\nelement face 4\nproperty list uchar int vertex_indices\nend_header\n";
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(head));
        var bw = new BinaryWriter(ms);
        float[][] v = [[0, 0, 0], [0.03f, 0, 0], [0, 0.03f, 0], [0, 0, 0.03f]];
        foreach (var p in v) { bw.Write(p[0]); bw.Write(p[1]); bw.Write(p[2]); bw.Write((byte)200); bw.Write((byte)10); bw.Write((byte)10); }
        int[][] f = [[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]];
        foreach (var t in f) { bw.Write((byte)3); foreach (var i in t) bw.Write(i); }
        bw.Flush();
        var mesh = ScanReader.Read(ms.ToArray(), ".ply");
        Assert.Equal(4, mesh.TriangleCount);
        Assert.True(mesh.HasColors);
        Assert.Equal(0xFFC80A0Au, mesh.Colors![0]);
    }

    [Fact]
    public void Ply_sin_caras_avisa_que_es_nube_de_puntos()
    {
        var ply = "ply\nformat ascii 1.0\nelement vertex 2\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n1 1 1\n";
        var ex = Assert.Throws<InvalidDataException>(() => ScanReader.Read(Encoding.ASCII.GetBytes(ply), ".ply"));
        Assert.Contains("nube de puntos", ex.Message);
    }

    [Fact]
    public void Glb_con_color_base()
    {
        // Un triángulo, Y arriba, en metros.
        float[] pos = [0, 0, 0, 0.02f, 0, 0, 0, 0.05f, 0];
        ushort[] idx = [0, 1, 2, 0];
        var bin = new MemoryStream();
        var bw = new BinaryWriter(bin);
        foreach (var x in pos) bw.Write(x);
        foreach (var i in idx) bw.Write(i);
        bw.Flush();
        var json = """
        {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0,"translation":[1,0,0]}],
         "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"material":0}]}],
         "materials":[{"pbrMetallicRoughness":{"baseColorFactor":[0,1,0,1]}}],
         "buffers":[{"byteLength":44}],
         "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":6}],
         "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}]}
        """;
        var jb = Encoding.UTF8.GetBytes(json);
        var jpad = (4 - jb.Length % 4) % 4;
        var glb = new MemoryStream();
        var w = new BinaryWriter(glb);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + jb.Length + jpad + 8 + 44));
        w.Write((uint)(jb.Length + jpad)); w.Write(0x4E4F534Au); w.Write(jb); for (int i = 0; i < jpad; i++) w.Write((byte)' ');
        w.Write(44u); w.Write(0x004E4942u); w.Write(bin.ToArray());
        w.Flush();
        var m = ScanReader.Read(glb.ToArray(), ".glb");
        Assert.Equal(1, m.TriangleCount);
        Assert.Equal(0xFF00FF00u, m.Colors![0]);
        // Y arriba pasa a Z arriba; la traslación del nodo se aplica.
        Assert.Equal(1, m.Bounds().Min.X, 5);
        Assert.Equal(0.05, m.Size.Z, 5);
    }

    [Fact]
    public void Usdz_del_iphone_con_textura_y_unidades()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Datos", "pieza.usdz");
        var raw = ScanReader.Read(path);
        Assert.Equal(1.0, raw.MetersPerUnit);
        Assert.Equal(6 * 6 * 6 * 2, raw.TriangleCount);
        // La traslación de 0,1 m del Xform padre está aplicada.
        Assert.Equal(0.1, raw.Bounds().Min.X, 5);
        Assert.True(raw.HasColors);
        // Textura: mitad izquierda roja, derecha azul (no la de normales).
        var left = raw.Vertices.Select((v, i) => (v, i)).Where(x => x.v.X < 0.105).Select(x => raw.Colors![x.i]).ToList();
        var right = raw.Vertices.Select((v, i) => (v, i)).Where(x => x.v.X > 0.135).Select(x => raw.Colors![x.i]).ToList();
        Assert.All(left, c => Assert.Equal(0xFFFF0000u, c));
        Assert.All(right, c => Assert.Equal(0xFF0000FFu, c));

        var m = new ScanFeature { FilePath = path }.Process();
        Assert.Equal(40, m.Size.X, 1);
        Assert.Equal(30, m.Size.Y, 1);
        Assert.Equal(20, m.Size.Z, 1);
        Assert.Equal(0, m.CountBorderEdges());
        Assert.Equal(40 * 30 * 20, m.Volume(), 0);
    }

    [Fact]
    public void Lz4_y_enteros_comprimidos_de_usd()
    {
        // Bloque LZ4 a mano: 4 literales «abcd» y una copia de 8 desde 4 atrás → «abcdabcdabcd».
        byte[] block = [0x44, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 4, 0];
        var outp = new byte[12];
        Assert.Equal(12, UsdReader.Lz4Block(block, 0, block.Length, outp, 0));
        Assert.Equal("abcdabcdabcd", Encoding.ASCII.GetString(outp));
    }

    [Fact]
    public void Pieza_solo_escaneada_y_escaneo_con_taladro()
    {
        var path = Temp(".obj", Encoding.UTF8.GetBytes(OpenBoxObj(0.04, 0.02, 0.04, withDust: false)));
        var part = new Part { Features = { new ScanFeature { FilePath = path } } };
        var ev = new DocumentEvaluator(new MeshKernel());
        var r = ev.Evaluate(part);
        Assert.Empty(r.Errors);
        Assert.Equal(40 * 40 * 20, r.Mesh!.Volume(), 0);

        part.Features.Add(new CylinderFeature { Mode = CombineMode.Hueco, Diameter = 10, Height = 40, Position = new Vec3(0, 0, -5), Segments = 64 });
        r = ev.Evaluate(part);
        Assert.Empty(r.Errors);
        Assert.InRange(r.Mesh!.Volume(), 40 * 40 * 20 - Math.PI * 25 * 20 - 50, 40 * 40 * 20 - Math.PI * 25 * 20 + 50);
    }

    [Fact]
    public void Se_guarda_en_el_cad3d_y_se_vuelve_a_leer()
    {
        var doc = new CadDocument { Parts = { new Part { Features = { new ScanFeature { FilePath = "x.obj", CutBottom = 2, Holes = ScanHoles.Pequenos } } } } };
        var back = CadDocument.FromJson(doc.ToJson());
        var s = Assert.IsType<ScanFeature>(back.Parts[0].Features[0]);
        Assert.Equal(2, s.CutBottom);
        Assert.Equal(ScanHoles.Pequenos, s.Holes);
    }

    [Fact]
    public void Edz_con_la_pieza_para_tableros()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Datos", "pieza.usdz");
        var mesh = new ScanFeature { FilePath = path }.BuildPlacedMesh();
        var edz = Path.Combine(Path.GetTempPath(), "scan-tests-" + Guid.NewGuid().ToString("N")[..8], EdzWriter.FileName("Mis piezas", "Relé ñ 1"));
        var r = EdzWriter.Write(edz, mesh, new EdzWriter.Options("Mis piezas", "Relé ñ 1", "Relé escaneado", "rele", false, true, MountFace.Abajo));
        // Tumbada en la mesa: ancho X 40, alto (Y del módulo 3D) 30, fondo (Z) 20.
        Assert.Equal(40, r.Width, 1);
        Assert.Equal(30, r.Height, 1);
        Assert.Equal(20, r.Depth, 1);
        Assert.Equal("Mis_piezas_Rele_n_1.edz", Path.GetFileName(edz));
        using var zip = ZipFile.OpenRead(edz);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("items/Mis_piezas_Rele_n_1.xml", names);
        Assert.Contains("3D/Mis_piezas_Rele_n_1.obj", names);
        using var mtl = new StreamReader(zip.GetEntry("3D/Mis_piezas_Rele_n_1.mtl")!.Open());
        var text = mtl.ReadToEnd();
        Assert.Contains("c_FF0000", text);
        Assert.Contains("c_0000FF", text);
    }
}
