using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Tests;

public class StairTests
{
    [Fact]
    public void Standard_floor_height_gives_comfortable_steps()
    {
        var d = StairCalculator.Design(2.8);
        Assert.Equal(16, d.Risers);
        Assert.Equal(0.175, d.Riser, 3);
        Assert.Equal(0.28, d.Tread, 3);
        Assert.Equal(15 * 0.28, d.RunLength, 3);
        Assert.InRange(d.Blondel, 0.60, 0.65);
        Assert.Empty(d.Warnings);
    }

    [Fact]
    public void Warns_about_steep_or_long_stairs()
    {
        Assert.NotEmpty(StairCalculator.Design(3.0, targetRiser: 0.22).Warnings);
        Assert.Contains(StairCalculator.Design(3.5).Warnings, w => w.Contains("descanso"));
        Assert.Contains(StairCalculator.Design(2.8, width: 0.6).Warnings, w => w.Contains("Ancho"));
    }
}

public class SpaceDetectorTests
{
    private static Point2 P(double x, double y) => new(x, y);

    [Fact]
    public void Finds_single_rectangle()
    {
        var rooms = SpaceDetector.FindRooms([(P(0, 0), P(4, 0)), (P(4, 0), P(4, 3)), (P(4, 3), P(0, 3)), (P(0, 3), P(0, 0))]);
        var r = Assert.Single(rooms);
        Assert.Equal(12, Geo.SignedArea(r), 6);
    }

    [Fact]
    public void Splits_rooms_with_t_junction_and_crossing_walls()
    {
        // Rectángulo 6×4 con un tabique en T (x=3) y otro que cruza todo (y=2): 4 recintos de 3×2.
        var rooms = SpaceDetector.FindRooms(
        [
            (P(0, 0), P(6, 0)), (P(6, 0), P(6, 4)), (P(6, 4), P(0, 4)), (P(0, 4), P(0, 0)),
            (P(3, 0), P(3, 4)), (P(-0.5, 2), P(6.5, 2)),
        ]);
        Assert.Equal(4, rooms.Count);
        Assert.All(rooms, r => Assert.Equal(6, Geo.SignedArea(r), 6));
    }

    [Fact]
    public void Ignores_dangling_wall_inside_room()
    {
        var rooms = SpaceDetector.FindRooms(
        [
            (P(0, 0), P(4, 0)), (P(4, 0), P(4, 4)), (P(4, 4), P(0, 4)), (P(0, 4), P(0, 0)),
            (P(2, 0), P(2, 1.5)), // tabique que no cierra
        ]);
        var r = Assert.Single(rooms);
        Assert.Equal(16, Geo.SignedArea(r), 6);
    }

    [Fact]
    public void Open_walls_make_no_room()
    {
        Assert.Empty(SpaceDetector.FindRooms([(P(0, 0), P(4, 0)), (P(4, 0), P(4, 3)), (P(4, 3), P(0, 3))]));
    }
}

public class BuildingDocumentTests
{
    [Fact]
    public void House_template_detects_named_rooms()
    {
        var doc = BuildingTemplates.Casa();
        Assert.Equal(6, doc.Spaces.Count);
        var sala = doc.Spaces.Single(s => s.Name == "Sala-comedor");
        Assert.Equal(25, sala.Area, 6);
        Assert.Equal("estar", sala.Use);
        Assert.Contains(doc.Spaces, s => s.Name == "Baño" && Math.Abs(s.Area - 7.6) < 1e-6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Templates_have_no_errors(int index)
    {
        var doc = BuildingTemplates.All[index].Create();
        var errors = BuildingChecks.Run(doc).Where(c => c.Severity == CheckSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Message)));
        Assert.All(doc.Spaces, s => Assert.DoesNotContain("Espacio", s.Name));
    }

    [Fact]
    public void Hotel_has_typical_floors_with_rooms()
    {
        var doc = BuildingTemplates.Hotel();
        Assert.Equal(4, doc.Levels.Count);
        Assert.Equal(new[] { 0, 3.5, 6.5, 9.5 }, doc.Levels.Select(l => l.Elevation));
        Assert.Equal(8 * 3, doc.Spaces.Count(s => s.Use == "habitacion_hotel"));
    }

    [Fact]
    public void Json_round_trip_keeps_everything()
    {
        var doc = BuildingTemplates.CasaDosPlantas();
        AddOutletAndTap(doc);
        var copy = BuildingDocument.FromJson(doc.ToJson());
        Assert.Equal(doc.ToJson(), copy.ToJson());
        Assert.Equal(doc.Spaces.Count, copy.Spaces.Count);
        Assert.Equal(doc.Routes[0].Length, copy.Routes[0].Length, 9);
    }

    [Fact]
    public void Undo_and_redo_restore_state()
    {
        var doc = BuildingTemplates.Casa();
        int walls = doc.Walls.Count;
        doc.Edit(e => e.Delete(doc.Walls[0].Id));
        Assert.Equal(walls - 1, doc.Walls.Count);
        doc.Undo();
        Assert.Equal(walls, doc.Walls.Count);
        doc.Redo();
        Assert.Equal(walls - 1, doc.Walls.Count);
    }

    [Fact]
    public void Wall_point_is_on_requested_face_and_height()
    {
        var doc = new BuildingDocument();
        Wall w = null!;
        doc.Edit(e =>
        {
            var lvl = e.AddLevel("P1", 3.0, 3.0);
            w = e.AddWall(lvl.Id, new(0, 0), new(4, 0), 0.2, 2.8);
        });
        var a = doc.GetWallPoint(w.Id, WallFace.A, 1.0, 0.3);
        var b = doc.GetWallPoint(w.Id, WallFace.B, 1.0, 0.3);
        Assert.Equal(new Point3(1, 0.1, 3.3), a);
        Assert.Equal(new Point3(1, -0.1, 3.3), b);
    }

    [Fact]
    public void Route_via_ceiling_has_real_length_and_parameters()
    {
        var doc = new BuildingDocument();
        var (from, to) = AddOutletAndTap(doc);
        var route = doc.Routes.Single();
        // Sube de 0,30 a 2,90 (2,6), recorre 3 + 2 por el techo y baja de 2,90 a 1,10 (1,8).
        Assert.Equal(2.6 + 3 + 2 + 1.8, route.Length, 6);
        Assert.Equal(1.10 - 0.30, route.DeltaZ, 6);
        var p = route.ToComponentParameters();
        Assert.Equal(route.Length, (double)p["length"], 9);
        Assert.Equal("X1", from.ComponentId);
        Assert.Equal("S1", to.ComponentId);
    }

    [Fact]
    public void Fixture_inside_door_opening_is_reported()
    {
        var doc = new BuildingDocument();
        doc.Edit(e =>
        {
            var w = e.AddWall("N0", new(0, 0), new(4, 0));
            e.AddDoor(w, 2.0, 0.9);
            e.AddFixture(new PlacedFixture("F1", "electrico", "tomacorriente", "X1", "X1.L", "N0", w.Id, new Point3(2.0, 0.075, 0.3)));
        });
        Assert.Contains(BuildingChecks.Run(doc), c => c.ElementId == "F1" && c.Severity == CheckSeverity.Error);
    }

    [Fact]
    public void Duplicate_level_copies_walls_and_openings_on_top()
    {
        var doc = BuildingTemplates.Casa();
        doc.Edit(e => e.DuplicateLevel("N0", "Planta 1"));
        var l1 = doc.Levels.Single(l => l.Name == "Planta 1");
        Assert.Equal(3.0, l1.Elevation);
        Assert.Equal(doc.Walls.Count(w => w.LevelId == "N0"), doc.Walls.Count(w => w.LevelId == l1.Id));
        Assert.Equal(6, doc.Spaces.Count(s => s.LevelId == l1.Id));
        var l1Walls = doc.Walls.Where(w => w.LevelId == l1.Id).Select(w => w.Id).ToHashSet();
        Assert.Equal(doc.Openings.Count / 2, doc.Openings.Count(o => l1Walls.Contains(o.HostWallId)));
    }

    private static (PlacedFixture From, PlacedFixture To) AddOutletAndTap(BuildingDocument doc)
    {
        PlacedFixture from = null!, to = null!;
        doc.Edit(e =>
        {
            var lvl = doc.Levels[0];
            if (!doc.Walls.Any())
            {
                e.AddWall(lvl.Id, new(0, 0), new(3, 0), 0.0, 2.8);
                e.AddWall(lvl.Id, new(3, 0), new(3, 2), 0.0, 2.8);
            }
            var w1 = doc.Walls.First(w => w.LevelId == lvl.Id);
            var w2 = doc.Walls.First(w => w.LevelId == lvl.Id && w.Id != w1.Id);
            from = e.AddFixture(new PlacedFixture(e.NewId("F"), "electrico", "tomacorriente", e.NewComponentId("X"), null, lvl.Id, w1.Id, doc.GetWallPoint(w1.Id, WallFace.A, 0, 0.30)));
            to = e.AddFixture(new PlacedFixture(e.NewId("F"), "electrico", "interruptor", e.NewComponentId("S"), null, lvl.Id, w2.Id, doc.GetWallPoint(w2.Id, WallFace.A, w2.Length, 1.10)));
            e.AddRoute(new Route(e.NewId("R"), "electrico", "tubo", from.Id, to.Id, RouteBuilder.ViaCeiling(doc, from, to)));
        });
        return (from, to);
    }
}

public class ExampleTemplateTests
{
    [Fact]
    public void House_with_installations_has_wired_outlets_and_water()
    {
        var doc = BuildingTemplates.CasaConInstalaciones();
        Assert.Equal(8, doc.Fixtures.Count(f => f.Kind == "tomacorriente"));
        Assert.Equal(doc.Spaces.Count, doc.Fixtures.Count(f => f.Kind == "luminaria"));
        Assert.Single(doc.Fixtures, f => f.Kind == "tanque");
        Assert.Equal(3, doc.Routes.Count(r => r.ModuleId == "hidraulica"));
        Assert.All(doc.Routes, r => Assert.True(r.Length > 0));
        // Ningún elemento queda dentro de un hueco ni tapado por una puerta.
        Assert.DoesNotContain(BuildingChecks.Run(doc), c => doc.Fixtures.Any(f => f.Id == c.ElementId));
    }

    [Fact]
    public void Apartment_building_has_four_floors_and_entrance_only_on_ground()
    {
        var doc = BuildingTemplates.EdificioApartamentos();
        Assert.Equal(4, doc.Levels.Count);
        Assert.Equal(4 * 7, doc.Spaces.Count);
        Assert.Equal(3, doc.Stairs.Count);
        var ground = doc.Walls.Where(w => w.LevelId == "N0").Select(w => w.Id).ToHashSet();
        Assert.Single(doc.Openings, o => o.Width == 1.2 && o.Kind == OpeningKind.Door);
        Assert.Contains(doc.Openings, o => o.Width == 1.2 && o.Kind == OpeningKind.Door && ground.Contains(o.HostWallId));
        Assert.DoesNotContain(BuildingChecks.Run(doc), c => c.Severity != CheckSeverity.Info && c.ElementId.StartsWith('E'));
    }
}
