using System.Globalization;
using System.IO.Compression;
using System.Text;
using Simulador.Tableros.Import;
using Simulador.Tableros.Import.Step;
using Simulador.Tableros.Model;

namespace Simulador.Tableros.Tests;

/// <summary>Lector de STEP con archivos hechos a mano (no se incluyen modelos de fabricantes en el repositorio).</summary>
public class StepTests
{
    /// <summary>Escribe un STEP AP214 mínimo con cajas como sólidos B-rep.</summary>
    private sealed class StepWriter
    {
        private readonly StringBuilder _data = new();
        private int _id;
        private static string N(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);

        public int Add(string entity)
        {
            _data.Append('#').Append(++_id).Append('=').Append(entity).Append(";\n");
            return _id;
        }

        public int Point(double x, double y, double z) => Add($"CARTESIAN_POINT('',({N(x)},{N(y)},{N(z)}))");
        public int Dir(double x, double y, double z) => Add($"DIRECTION('',({N(x)},{N(y)},{N(z)}))");
        public int Axis(double x, double y, double z) => Add($"AXIS2_PLACEMENT_3D('',#{Point(x, y, z)},#{Dir(0, 0, 1)},#{Dir(1, 0, 0)})");

        /// <summary>Caja de x0..x1 × y0..y1 × z0..z1 como MANIFOLD_SOLID_BREP; devuelve su id.</summary>
        public int Box(double x0, double y0, double z0, double x1, double y1, double z1, (double R, double G, double B)? color = null)
        {
            var c = new (double, double, double)[] { (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1) };
            var v = c.Select(p => Add($"VERTEX_POINT('',#{Point(p.Item1, p.Item2, p.Item3)})")).ToArray();
            var edges = new Dictionary<(int, int), int>();
            int Edge(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (edges.TryGetValue(key, out var e)) return e;
                var (pa, pb) = (c[key.Item1], c[key.Item2]);
                var line = Add($"LINE('',#{Point(pa.Item1, pa.Item2, pa.Item3)},#{Add($"VECTOR('',#{Dir(pb.Item1 - pa.Item1, pb.Item2 - pa.Item2, pb.Item3 - pa.Item3)},1.0)")})");
                return edges[key] = Add($"EDGE_CURVE('',#{v[key.Item1]},#{v[key.Item2]},#{line},.T.)");
            }
            int Face(int[] loop, (double, double, double) normal)
            {
                var oriented = new List<int>();
                for (var i = 0; i < loop.Length; i++)
                {
                    int a = loop[i], b = loop[(i + 1) % loop.Length];
                    oriented.Add(Add($"ORIENTED_EDGE('',*,*,#{Edge(a, b)},{(a < b ? ".T." : ".F.")})"));
                }
                var el = Add($"EDGE_LOOP('',({string.Join(",", oriented.Select(o => "#" + o))}))");
                var bound = Add($"FACE_OUTER_BOUND('',#{el},.T.)");
                var p = c[loop[0]];
                var n = normal;
                var refDir = Math.Abs(n.Item1) > 0.5 ? Dir(0, 1, 0) : Dir(1, 0, 0);
                var plane = Add($"PLANE('',#{Add($"AXIS2_PLACEMENT_3D('',#{Point(p.Item1, p.Item2, p.Item3)},#{Dir(n.Item1, n.Item2, n.Item3)},#{refDir})")})");
                return Add($"ADVANCED_FACE('',(#{bound}),#{plane},.T.)");
            }
            var faces = new[]
            {
                Face([0, 3, 2, 1], (0, 0, -1)), Face([4, 5, 6, 7], (0, 0, 1)), Face([0, 1, 5, 4], (0, -1, 0)),
                Face([1, 2, 6, 5], (1, 0, 0)), Face([2, 3, 7, 6], (0, 1, 0)), Face([3, 0, 4, 7], (-1, 0, 0)),
            };
            var shell = Add($"CLOSED_SHELL('',({string.Join(",", faces.Select(f => "#" + f))}))");
            var solid = Add($"MANIFOLD_SOLID_BREP('',#{shell})");
            if (color is { } col)
            {
                var rgb = Add($"COLOUR_RGB('',{N(col.R)},{N(col.G)},{N(col.B)})");
                var fill = Add($"FILL_AREA_STYLE('',(#{Add($"FILL_AREA_STYLE_COLOUR('',#{rgb})")}))");
                var ssa = Add($"SURFACE_STYLE_USAGE(.BOTH.,#{Add($"SURFACE_SIDE_STYLE('',(#{Add($"SURFACE_STYLE_FILL_AREA(#{fill})")}))")})");
                Add($"STYLED_ITEM('',(#{Add($"PRESENTATION_STYLE_ASSIGNMENT((#{ssa}))")}),#{solid})");
            }
            return solid;
        }

        public int Context(int? lengthUnit = null)
        {
            var len = lengthUnit ?? Add("(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.))");
            var ang = Add("(NAMED_UNIT(*)PLANE_ANGLE_UNIT()SI_UNIT($,.RADIAN.))");
            var sol = Add("(NAMED_UNIT(*)SI_UNIT($,.STERADIAN.)SOLID_ANGLE_UNIT())");
            return Add($"(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNIT_ASSIGNED_CONTEXT((#{len},#{ang},#{sol}))REPRESENTATION_CONTEXT('',''))");
        }

        /// <summary>Producto con su forma; devuelve (definición de producto, representación).</summary>
        public (int Pd, int Rep) Product(string name, IEnumerable<int> items, int ctx, string repType = "ADVANCED_BREP_SHAPE_REPRESENTATION")
        {
            var appCtx = Add("APPLICATION_CONTEXT('automotive design')");
            var prod = Add($"PRODUCT('{name}','{name}','',(#{Add($"PRODUCT_CONTEXT('',#{appCtx},'mechanical')")}))");
            var form = Add($"PRODUCT_DEFINITION_FORMATION('','',#{prod})");
            var pd = Add($"PRODUCT_DEFINITION('design','',#{form},#{Add($"PRODUCT_DEFINITION_CONTEXT('part definition',#{appCtx},'design')")})");
            var pds = Add($"PRODUCT_DEFINITION_SHAPE('','',#{pd})");
            var rep = Add($"{repType}('',({string.Join(",", items.Select(i => "#" + i))}),#{ctx})");
            Add($"SHAPE_DEFINITION_REPRESENTATION(#{pds},#{rep})");
            return (pd, rep);
        }

        public string Text(string name = "prueba") =>
            "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION((''),'2;1');\nFILE_NAME('" + name + "','',(''),(''),'','','');\n"
            + "FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));\nENDSEC;\nDATA;\n" + _data + "ENDSEC;\nEND-ISO-10303-21;\n";
    }

    [Fact]
    public void LeeUnaCajaConSuMedida()
    {
        var w = new StepWriter();
        var ctx = w.Context();
        var box = w.Box(0, 0, 0, 18, 70, 90);
        w.Product("S201-C6", [w.Axis(0, 0, 0), box], ctx);
        var r = StepReader.Read(w.Text(), "caja.stp");
        Assert.Equal(12, r.Mesh.Triangles.Count);
        Assert.Equal(18, r.Mesh.Size.X, 2);
        Assert.Equal(70, r.Mesh.Size.Y, 2);
        Assert.Equal(90, r.Mesh.Size.Z, 2);
        Assert.Equal("S201-C6", r.ProductName);
    }

    [Fact]
    public void PasaPulgadasAMilimetros()
    {
        var w = new StepWriter();
        var mm = w.Add("(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.))");
        var measure = w.Add($"LENGTH_MEASURE_WITH_UNIT(LENGTH_MEASURE(25.4),#{mm})");
        var inch = w.Add($"(CONVERSION_BASED_UNIT('INCH',#{measure})LENGTH_UNIT()NAMED_UNIT(*))");
        var ctx = w.Context(inch);
        w.Product("pulgadas", [w.Box(0, 0, 0, 1, 2, 3)], ctx);
        var r = StepReader.Read(w.Text(), "pulgadas.stp");
        Assert.Equal(25.4, r.Mesh.Size.X, 1);
        Assert.Equal(76.2, r.Mesh.Size.Z, 1);
    }

    [Fact]
    public void ColocaLasPiezasDeUnConjuntoYNoLasRepite()
    {
        var w = new StepWriter();
        var ctx = w.Context();
        var box = w.Box(0, 0, 0, 10, 10, 10);
        var partAxis = w.Axis(0, 0, 0);
        var part = w.Product("pieza", [partAxis, box], ctx);
        // Misma pieza repetida en una representación suelta sin colocación (como hacen algunos programas).
        w.Add($"SHAPE_REPRESENTATION('',(#{box}),#{ctx})");
        var asmAxis = w.Axis(100, 0, 0);
        var asm = w.Product("conjunto", [w.Axis(0, 0, 0), asmAxis], ctx, "SHAPE_REPRESENTATION");
        var nauo = w.Add($"NEXT_ASSEMBLY_USAGE_OCCURRENCE('1','','',#{asm.Pd},#{part.Pd},$)");
        var idt = w.Add($"ITEM_DEFINED_TRANSFORMATION('','',#{partAxis},#{asmAxis})");
        var rel = w.Add($"(REPRESENTATION_RELATIONSHIP('','',#{part.Rep},#{asm.Rep})REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION(#{idt})SHAPE_REPRESENTATION_RELATIONSHIP())");
        w.Add($"CONTEXT_DEPENDENT_SHAPE_REPRESENTATION(#{rel},#{w.Add($"PRODUCT_DEFINITION_SHAPE('','',#{nauo})")})");
        var r = StepReader.Read(w.Text(), "conjunto.stp");
        Assert.Equal(12, r.Mesh.Triangles.Count);
        Assert.Equal(100, r.Mesh.Min.X, 2);
        Assert.Equal(110, r.Mesh.Max.X, 2);
    }

    [Fact]
    public void IgnoraCapasInvisiblesYPiezasSueltasDiminutas()
    {
        var w = new StepWriter();
        var ctx = w.Context();
        var body = w.Box(0, 0, 0, 45, 80, 90);
        var hidden = w.Box(0, 0, 200, 45, 80, 290);
        var pin = w.Box(-0.5, -30, -0.5, 0.5, -29, 0.5);
        var layer = w.Add($"PRESENTATION_LAYER_ASSIGNMENT('oculta','',(#{hidden}))");
        w.Add($"INVISIBILITY((#{layer}))");
        w.Product("guardamotor", [body, hidden, pin], ctx);
        var r = StepReader.Read(w.Text(), "capas.stp");
        Assert.Equal(90, r.Mesh.Size.Z, 2);
        Assert.Equal(80, r.Mesh.Size.Y, 2);
        Assert.Contains(r.Notes, n => n.Contains("diminuta"));
    }

    /// <summary>Aparato de carril: laterales planos en X, espalda plana en Y=0 y una maneta roja al frente.</summary>
    private static string Breaker(out double w, out double h, out double d)
    {
        (w, h, d) = (18, 90, 70);
        var s = new StepWriter();
        var ctx = s.Context();
        var body = s.Box(0, 0, 0, 18, 60, 90, (0.9, 0.9, 0.9));
        var front = s.Box(2, 60, 30, 16, 70, 60, (0.9, 0.9, 0.9));
        var handle = s.Box(6, 62, 40, 12, 72, 50, (0.8, 0.1, 0.1));
        s.Product("A9F74116", [s.Axis(0, 0, 0), body, front, handle], ctx);
        return s.Text("A9F74116");
    }

    [Fact]
    public void GiraElModeloConElFrenteHaciaFuera()
    {
        var c = StepImporter.Prepare(Encoding.ASCII.GetBytes(Breaker(out _, out _, out _)), "A9F74116_3D.stp");
        Assert.Equal("Schneider Electric", c.Brand);
        Assert.Equal("A9F74116", c.Reference);
        Assert.Equal("magnetotermico", c.Kind);
        Assert.Equal("+Y", c.Front);
        Assert.Equal("+Z", c.Up);
        var item = StepImporter.ToItem(c);
        Assert.Equal(18, item.Width, 1);
        Assert.Equal(90, item.Height, 1);
        Assert.Equal(72, item.Depth, 1);
        Assert.Equal(0, item.Mesh!.Min.Y, 3);
        // La maneta (roja) queda delante: Y grande.
        var red = item.Mesh.Triangles.Where(t => t.Color == 0xFFCC1A1A).ToList();
        Assert.NotEmpty(red);
        Assert.True(red.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))) > 60);
    }

    [Fact]
    public void ImportaUnZipDeLaWebYLoGuardaComoEdz()
    {
        var dir = Path.Combine(Path.GetTempPath(), "step-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zipPath = Path.Combine(dir, "descarga.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            using (var e = new StreamWriter(zip.CreateEntry("modelos/A9F74116_3D.step").Open()))
                e.Write(Breaker(out _, out _, out _));
            var lib = Path.Combine(dir, "lib");
            var r = LibraryStore.Import([zipPath], lib);
            var item = Assert.Single(r.Items);
            Assert.Equal("A9F74116", item.Reference);
            Assert.NotNull(item.Mesh);
            var edz = Assert.Single(Directory.GetFiles(lib, "*.edz"));

            // Al volver a abrir el programa, el .edz guardado trae lo mismo.
            var again = EdzImporter.Read(edz);
            var back = Assert.Single(again.Items);
            Assert.Equal("Schneider Electric", back.Brand);
            Assert.Equal("magnetotermico", back.Kind);
            Assert.Equal(item.Width, back.Width, 1);
            Assert.Equal(item.Depth, back.Depth, 1);
            Assert.Equal(item.Mesh!.Triangles.Count, back.Mesh!.Triangles.Count);
            Assert.Contains(back.Mesh.Triangles, t => t.Color == 0xFFCC1A1A);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LeeElStepDentroDeUnEdzSinObj()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stepedz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "solo-step.edz");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var x = new StreamWriter(zip.CreateEntry("items/p.xml").Open()))
                    x.Write("<partsmanagement><part P_ARTICLE_PARTNR=\"SE.A9F74116\" P_ARTICLE_MANUFACTURER=\"Schneider Electric\" P_ARTICLE_TYPENR=\"A9F74116\" P_ARTICLE_DESCR1=\"Interruptor automático 1P\" /></partsmanagement>");
                using var s = new StreamWriter(zip.CreateEntry("3D/A9F74116.stp").Open());
                s.Write(Breaker(out _, out _, out _));
            }
            var r = EdzImporter.Read(path);
            var item = Assert.Single(r.Items);
            Assert.NotNull(item.Mesh);
            Assert.Equal(90, item.Height, 1);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GiraNoventaGradosAlrededorDelFrente()
    {
        // Cuatro giros de 90° vuelven al principio.
        var up = "+Z";
        var seen = new List<string>();
        for (var i = 0; i < 4; i++) { up = Ui.StepImportDialog.RotateUp("+Y", up); seen.Add(up); }
        Assert.Equal(["+X", "-Z", "-X", "+Z"], seen);
    }

    [Fact]
    public void SimplificaLosModelosMuyDetallados()
    {
        var m = new DeviceMesh();
        for (var i = 0; i < 200; i++)
            for (var j = 0; j < 200; j++)
            {
                var a = new System.Numerics.Vector3(i * 0.5f, j * 0.5f, 0);
                m.Add(a, a + new System.Numerics.Vector3(0.5f, 0, 0), a + new System.Numerics.Vector3(0, 0.5f, 1), 0xFF808080);
            }
        m.Seal();
        var s = m.Simplified(10_000);
        Assert.InRange(s.Triangles.Count, 1, 10_000);
        Assert.InRange(s.Size.X, m.Size.X - 3, m.Size.X + 0.01f);
    }
}
