using System.Globalization;
using System.Text;
using Simulador.Civil.Import;
using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Tests;

/// <summary>Genera DXF de texto mínimos para las pruebas.</summary>
internal sealed class DxfWriter
{
    private readonly StringBuilder _entities = new();
    private readonly StringBuilder _blocks = new();
    public int? InsUnits { get; set; }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
    private static void Pair(StringBuilder sb, int code, string value) => sb.Append(code).Append('\n').Append(value).Append('\n');

    public DxfWriter Line(double x1, double y1, double x2, double y2, string layer = "MUROS", StringBuilder? into = null)
    {
        var sb = into ?? _entities;
        Pair(sb, 0, "LINE"); Pair(sb, 8, layer);
        Pair(sb, 10, F(x1)); Pair(sb, 20, F(y1)); Pair(sb, 30, "0");
        Pair(sb, 11, F(x2)); Pair(sb, 21, F(y2)); Pair(sb, 31, "0");
        return this;
    }

    public DxfWriter LwPolyline(bool closed, string layer, params (double X, double Y, double Bulge)[] pts)
    {
        Pair(_entities, 0, "LWPOLYLINE"); Pair(_entities, 8, layer);
        Pair(_entities, 90, pts.Length.ToString()); Pair(_entities, 70, closed ? "1" : "0");
        foreach (var (x, y, b) in pts)
        {
            Pair(_entities, 10, F(x)); Pair(_entities, 20, F(y));
            if (b != 0) Pair(_entities, 42, F(b));
        }
        return this;
    }

    public DxfWriter Circle(double x, double y, double r, string layer = "MUEBLES")
    {
        Pair(_entities, 0, "CIRCLE"); Pair(_entities, 8, layer);
        Pair(_entities, 10, F(x)); Pair(_entities, 20, F(y)); Pair(_entities, 40, F(r));
        return this;
    }

    public DxfWriter Text(string text)
    {
        Pair(_entities, 0, "TEXT"); Pair(_entities, 8, "TEXTOS"); Pair(_entities, 1, text);
        return this;
    }

    /// <summary>Bloque con una línea de (0,0) a (1,0), insertado en (x, y) con escala y giro.</summary>
    public DxfWriter UnitBlockInsert(double x, double y, double scale, double rotationDeg)
    {
        if (_blocks.Length == 0)
        {
            Pair(_blocks, 0, "BLOCK"); Pair(_blocks, 8, "0"); Pair(_blocks, 2, "UNO"); Pair(_blocks, 70, "0");
            Pair(_blocks, 10, "0"); Pair(_blocks, 20, "0");
            Line(0, 0, 1, 0, "0", _blocks);
            Pair(_blocks, 0, "ENDBLK");
        }
        Pair(_entities, 0, "INSERT"); Pair(_entities, 8, "PUERTAS"); Pair(_entities, 2, "UNO");
        Pair(_entities, 10, F(x)); Pair(_entities, 20, F(y));
        Pair(_entities, 41, F(scale)); Pair(_entities, 42, F(scale)); Pair(_entities, 50, F(rotationDeg));
        return this;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (InsUnits is { } u)
        {
            Pair(sb, 0, "SECTION"); Pair(sb, 2, "HEADER"); Pair(sb, 9, "$INSUNITS"); Pair(sb, 70, u.ToString()); Pair(sb, 0, "ENDSEC");
        }
        if (_blocks.Length > 0) { Pair(sb, 0, "SECTION"); Pair(sb, 2, "BLOCKS"); sb.Append(_blocks); Pair(sb, 0, "ENDSEC"); }
        Pair(sb, 0, "SECTION"); Pair(sb, 2, "ENTITIES"); sb.Append(_entities); Pair(sb, 0, "ENDSEC");
        Pair(sb, 0, "EOF");
        return sb.ToString();
    }

    /// <summary>Habitación rectangular dibujada como en un plano: dos líneas por muro (cara exterior e interior).</summary>
    public DxfWriter DoubleLineRoom(double x0, double y0, double x1, double y1, double t)
    {
        // Cara exterior.
        Line(x0, y0, x1, y0); Line(x1, y0, x1, y1); Line(x1, y1, x0, y1); Line(x0, y1, x0, y0);
        // Cara interior.
        Line(x0 + t, y0 + t, x1 - t, y0 + t); Line(x1 - t, y0 + t, x1 - t, y1 - t);
        Line(x1 - t, y1 - t, x0 + t, y1 - t); Line(x0 + t, y1 - t, x0 + t, y0 + t);
        return this;
    }
}

public class DxfReaderTests
{
    [Fact]
    public void Reads_lines_polylines_circles_and_blocks_in_millimetres()
    {
        var w = new DxfWriter { InsUnits = 4 }
            .Line(0, 0, 5000, 0)
            .LwPolyline(true, "MUROS", (0, 0, 0), (1000, 0, 0), (1000, 1000, 0))
            .Circle(2000, 2000, 500)
            .UnitBlockInsert(3000, 0, 900, 90)
            .Text("Cocina");
        var d = DxfReader.Parse(w.ToString());
        Assert.Equal(0.001, d.MetersPerUnit);
        Assert.Contains("$INSUNITS", d.UnitSource);
        Assert.Contains(d.Segments, s => s.Layer == "MUROS" && Math.Abs(s.A.DistanceTo(s.B) - 5) < 1e-9);
        Assert.Equal(3, d.Segments.Count(s => s.Layer == "MUROS" && Math.Abs(s.A.DistanceTo(s.B) - 5) > 1e-9)); // polilínea cerrada
        // Círculo de 0,5 m de radio aproximado por segmentos: perímetro ≈ 2πr.
        double perimeter = d.Segments.Where(s => s.Layer == "MUEBLES").Sum(s => s.A.DistanceTo(s.B));
        Assert.Equal(2 * Math.PI * 0.5, perimeter, 1);
        // Bloque: línea de 1 unidad × escala 900, girada 90°, en (3000, 0) → de (3, 0) a (3, 0.9) m, capa del INSERT.
        var door = Assert.Single(d.Segments, s => s.Layer == "PUERTAS");
        Assert.Equal(3, door.A.X, 6); Assert.Equal(0, door.A.Y, 6);
        Assert.Equal(3, door.B.X, 6); Assert.Equal(0.9, door.B.Y, 6);
        Assert.Equal(1, d.Skipped["TEXT"]);
    }

    [Theory]
    [InlineData(12000, 0.001)] // sin cabecera, 12 000 unidades de largo: milímetros
    [InlineData(1200, 0.01)]   // centímetros
    [InlineData(12, 1.0)]      // metros
    public void Guesses_units_from_size_when_header_is_missing(double length, double expected)
    {
        var d = DxfReader.Parse(new DxfWriter().Line(0, 0, length, 0).ToString());
        Assert.Equal(expected, d.MetersPerUnit);
        Assert.Contains("deducida", d.UnitSource);
        Assert.Equal(12, d.Segments[0].B.X, 6);
    }

    [Fact]
    public void Bulge_draws_a_half_circle()
    {
        // Semicírculo de (0,0) a (2,0) con bulge 1 (180°): pasa por (1, -1) o (1, 1) según el sentido.
        var d = DxfReader.Parse(new DxfWriter().LwPolyline(false, "A", (0, 0, 1), (2, 0, 0)).ToString());
        double len = d.Segments.Sum(s => s.A.DistanceTo(s.B));
        Assert.Equal(Math.PI, len, 1);
        var pts = d.Segments.SelectMany(s => new[] { s.A, s.B }).ToList();
        Assert.All(pts, p => Assert.Equal(1, p.DistanceTo(new Point2(1, 0)), 3));
        // bulge > 0 es antihorario de (0,0) a (2,0): pasa por debajo.
        Assert.True(pts.Min(p => p.Y) < -0.99);
    }

    [Fact]
    public void Binary_or_broken_files_give_a_clear_error()
    {
        var ex = Assert.Throws<InvalidDataException>(() => DxfReader.Parse("AutoCAD Binary DXF\r\n\u001a\0garbage"));
        Assert.Contains("ASCII", ex.Message);
    }
}

public class UnderlayTransformTests
{
    [Fact]
    public void Plan_and_local_coordinates_round_trip_for_dxf_and_images()
    {
        foreach (var kind in new[] { UnderlayKind.Dxf, UnderlayKind.Image })
        {
            var u = new Underlay("C1", "N0", kind, "x") { Origin = new(3, -2), Rotation = 30, Scale = 0.02 };
            var p = new Point2(7.5, 4.25);
            var back = u.ToPlan(u.ToLocal(p));
            Assert.Equal(p.X, back.X, 9);
            Assert.Equal(p.Y, back.Y, 9);
        }
        // En una imagen la Y crece hacia abajo: el píxel (0, 100) queda por debajo de la esquina.
        var img = Underlay.FromImage("C1", "N0", "plano", "/x.png", 1000, 500);
        Assert.True(img.ToPlan(new(0, 100)).Y < img.ToPlan(new(0, 0)).Y);
        Assert.Equal(0, img.ToPlan(new(0, 500)).Y, 9); // la imagen queda en Y ≥ 0
        Assert.Equal(20, img.ToPlan(new(1000, 0)).X, 9);
    }

    [Fact]
    public void Calibrate_makes_a_known_dimension_measure_right_and_keeps_the_first_point()
    {
        var img = Underlay.FromImage("C1", "N0", "plano", "/x.png", 1000, 500);
        // Una puerta que en el calco va de A a B (dos píxeles separados 45 px) mide 0,90 m de verdad.
        var a = img.ToPlan(new(100, 200));
        var b = img.ToPlan(new(145, 200));
        var cal = img.Calibrate(a, b, 0.90);
        Assert.Equal(0.90, cal.ToPlan(new(100, 200)).DistanceTo(cal.ToPlan(new(145, 200))), 9);
        Assert.Equal(a.X, cal.ToPlan(new(100, 200)).X, 9);
        Assert.Equal(a.Y, cal.ToPlan(new(100, 200)).Y, 9);
        Assert.Equal(0.02, cal.Scale, 9);
    }

    [Fact]
    public void Move_and_rotate_take_points_where_asked()
    {
        var d = DxfReader.Parse(new DxfWriter().Line(0, 0, 10, 0).ToString());
        var u = Underlay.FromDxf("C1", "N0", "plano", d);
        var moved = u.MoveBy(new(0, 0), new(2, 3));
        Assert.Equal(new Point2(2, 3), moved.PlanSegments().First().A);
        // Girar 90° alrededor de (2, 3): el extremo (12, 3) pasa a (2, 13).
        var rotated = moved.RotateTo(new(2, 3), new(12, 3), new(2, 50));
        var seg = rotated.PlanSegments().First();
        Assert.Equal(2, seg.A.X, 9); Assert.Equal(3, seg.A.Y, 9);
        Assert.Equal(2, seg.B.X, 9); Assert.Equal(13, seg.B.Y, 9);
    }

    [Fact]
    public void Underlays_are_saved_with_the_project_but_not_undone()
    {
        var doc = new BuildingDocument();
        var d = DxfReader.Parse(new DxfWriter().Line(0, 0, 4, 0).ToString());
        var u = doc.AddUnderlay(Underlay.FromDxf("", "N0", "plano", d) with { HiddenLayers = ["MUEBLES"] });
        doc.Edit(e => e.AddWall("N0", new(0, 0), new(4, 0), 0.15, 2.5));
        doc.ReplaceUnderlay(u with { Opacity = 0.3 });
        doc.Undo(); // deshace el muro, no toca el calco
        Assert.Empty(doc.Walls);
        Assert.Equal(0.3, Assert.Single(doc.Underlays).Opacity);

        var copy = BuildingDocument.FromJson(doc.ToJson());
        var cu = Assert.Single(copy.Underlays);
        Assert.Equal(u.Id, cu.Id);
        Assert.Single(cu.Segments);
        Assert.Equal(["MUEBLES"], cu.HiddenLayers);
        Assert.Equal(0.3, cu.Opacity);
    }
}

public class WallDetectorTests
{
    [Fact]
    public void Double_line_room_becomes_four_joined_walls_and_a_room()
    {
        // Habitación de 5 × 4 m exteriores con muros de 15 cm, en milímetros como un plano real.
        var d = DxfReader.Parse(new DxfWriter { InsUnits = 4 }.DoubleLineRoom(0, 0, 5000, 4000, 150).ToString());
        var walls = WallDetector.Detect(d.Segments);
        Assert.Equal(4, walls.Count);
        Assert.All(walls, w => Assert.Equal(0.15, w.Thickness, 3));

        var doc = new BuildingDocument();
        doc.Edit(e => { foreach (var w in walls) e.AddWall("N0", w.A, w.B, w.Thickness, 2.6); });
        var room = Assert.Single(doc.Spaces);
        // Área a ejes: (5 − 0,15) × (4 − 0,15).
        Assert.Equal(4.85 * 3.85, room.Area, 2);
    }

    [Fact]
    public void Interior_partition_joins_the_outer_walls_in_T()
    {
        var w = new DxfWriter { InsUnits = 4 };
        // Exterior con muros de 20 cm…
        w.Line(0, 0, 8000, 0).Line(8000, 0, 8000, 4000).Line(8000, 4000, 0, 4000).Line(0, 4000, 0, 0);
        w.Line(200, 200, 7800, 200).Line(7800, 4000 - 200, 200, 4000 - 200);
        // …caras interiores partidas por el tabique de 10 cm en x = 4000.
        w.Line(200, 200, 200, 3800).Line(7800, 200, 7800, 3800);
        w.Line(3950, 200, 3950, 3800).Line(4050, 200, 4050, 3800);
        var walls = WallDetector.Detect(DxfReader.Parse(w.ToString()).Segments);
        Assert.Contains(walls, x => Math.Abs(x.Thickness - 0.10) < 1e-3);
        var doc = new BuildingDocument();
        doc.Edit(e => { foreach (var x in walls) e.AddWall("N0", x.A, x.B, x.Thickness, 2.6); });
        Assert.Equal(2, doc.Spaces.Count);
    }

    [Fact]
    public void Furniture_next_to_a_wall_is_not_taken_for_a_wall()
    {
        // Cama pegada a 30 cm de la cara interior, en una capa sin nombre de muro (todo en la capa "0").
        var w = new DxfWriter { InsUnits = 4 };
        foreach (var (x1, y1, x2, y2) in new (double, double, double, double)[]
        {
            (0, 0, 5000, 0), (5000, 0, 5000, 4000), (5000, 4000, 0, 4000), (0, 4000, 0, 0),
            (150, 150, 4850, 150), (4850, 150, 4850, 3850), (4850, 3850, 150, 3850), (150, 3850, 150, 150),
            (1000, 3550, 2600, 3550), (2600, 3550, 2600, 1550), (2600, 1550, 1000, 1550), (1000, 1550, 1000, 3550),
        })
            w.Line(x1, y1, x2, y2, "0");
        var walls = WallDetector.Detect(DxfReader.Parse(w.ToString()).Segments);
        Assert.Equal(4, walls.Count);
        Assert.All(walls, x => Assert.Equal(0.15, x.Thickness, 3));
    }

    [Fact]
    public void Wall_layers_are_preferred_when_the_plan_has_them()
    {
        var w = new DxfWriter { InsUnits = 4 }.DoubleLineRoom(0, 0, 5000, 4000, 150);
        // Una estantería de 30 cm de fondo en la capa de muebles: no cuenta porque hay capa "MUROS".
        w.Line(1000, 1000, 3000, 1000, "MUEBLES").Line(1000, 1300, 3000, 1300, "MUEBLES");
        Assert.Equal(4, WallDetector.Detect(DxfReader.Parse(w.ToString()).Segments).Count);
        Assert.True(WallDetector.LooksLikeWallLayer("A-WALL"));
        Assert.False(WallDetector.LooksLikeWallLayer("MUEBLES"));
    }

    [Fact]
    public void Lines_that_are_not_walls_are_ignored()
    {
        // Una mesa (rectángulo de 1,6 × 0,9 m) y una línea suelta: no hay parejas a distancia de muro.
        var w = new DxfWriter().LwPolyline(true, "MUEBLES", (1, 1, 0), (2.6, 1, 0), (2.6, 1.9, 0), (1, 1.9, 0)).Line(5, 0, 9, 0);
        Assert.Empty(WallDetector.Detect(DxfReader.Parse(w.ToString()).Segments));
    }
}
