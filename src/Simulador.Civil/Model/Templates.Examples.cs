using Simulador.Core.Building;

namespace Simulador.Civil.Model;

public static partial class BuildingTemplates
{
    /// <summary>
    /// La casa de una planta con su instalación de ejemplo: tablero, tomacorrientes, interruptores y luminarias,
    /// tanque elevado en la cubierta y puntos de agua en cocina y baño, con las rutas ya trazadas.
    /// </summary>
    public static BuildingDocument CasaConInstalaciones()
    {
        var doc = Casa();
        doc.Name = "Casa con instalaciones";
        doc.Edit(e =>
        {
            const string L = "N0";
            PlacedFixture OnWall(FixtureType t, Point2 near, double along, WallFace face, double? height = null)
            {
                var w = doc.NearestWall(L, near, 0.3)!.Value.Wall;
                var pos = doc.GetWallPoint(w.Id, face, along, height ?? t.DefaultHeight);
                return e.AddFixture(new PlacedFixture(e.NewId("F"), t.ModuleId, t.Kind, e.NewComponentId(t.ComponentPrefix), null, L, w.Id, pos));
            }
            PlacedFixture Free(FixtureType t, Point2 at, double z, double size = 0.3) =>
                e.AddFixture(new PlacedFixture(e.NewId("F"), t.ModuleId, t.Kind, e.NewComponentId(t.ComponentPrefix), null, L, null, new Point3(at, z), 0, size, size));
            void Wire(PlacedFixture a, PlacedFixture b, string kind) =>
                e.AddRoute(new Route(e.NewId("R"), a.ModuleId, kind, a.Id, b.Id, RouteBuilder.ViaCeiling(doc, a, b)));

            var fc = FixtureCatalog.Tomacorriente;
            // Tablero en el pasillo, junto a la entrada a las habitaciones.
            var q1 = OnWall(FixtureCatalog.Tablero, new(6.2, 0.5), 0.6, WallFace.A);

            // Tomacorrientes (0,30 m), uno o dos por habitación y en la cocina a altura de encimera.
            var outlets = new List<PlacedFixture>
            {
                OnWall(fc, new(4, 0), 4.0, WallFace.A),             // sala, muro sur
                OnWall(fc, new(0, 0.5), 7.5, WallFace.A),           // sala, muro oeste
                OnWall(fc, new(1.5, 5), 1.5, WallFace.A, 1.10),     // cocina, encimera
                OnWall(fc, new(0.5, 8), 9.5, WallFace.A, 1.10),     // cocina, muro norte
                OnWall(fc, new(10, 0.6), 0.6, WallFace.A),          // dormitorio 1
                OnWall(fc, new(6.2, 1.0), 1.0, WallFace.B),         // dormitorio 1, junto a la cama
                OnWall(fc, new(10, 7), 7.0, WallFace.A),            // dormitorio 2
                OnWall(fc, new(10, 4.5), 4.5, WallFace.A, 1.10),    // baño (lavabo)
            };
            foreach (var o in outlets) Wire(q1, o, "tubo_electrico");

            // Interruptores junto a las puertas (1,10 m) y una luminaria en el centro de cada habitación.
            Wire(q1, OnWall(FixtureCatalog.Interruptor, new(3.3, 0), 3.3, WallFace.A), "tubo_electrico");
            Wire(q1, OnWall(FixtureCatalog.Interruptor, new(6.2, 3.7), 3.7, WallFace.B), "tubo_electrico");
            foreach (var s in doc.Spaces.Where(s => s.LevelId == L))
                Wire(q1, Free(FixtureCatalog.Luminaria, Geo.Centroid(s.Outline), 2.95), "tubo_electrico");

            // Hidráulica: tanque elevado de 1000 L sobre la cubierta, puntos de agua y desagüe.
            var tank = Free(FixtureCatalog.Tanque, new(8.0, 5.0), 3.0, 1.2);
            var sink = OnWall(FixtureCatalog.PuntoAgua, new(4, 8), 6.0, WallFace.A);         // fregadero
            var basin = OnWall(FixtureCatalog.PuntoAgua, new(8.2, 4), 2.0, WallFace.A);     // lavabo
            var shower = OnWall(FixtureCatalog.PuntoAgua, new(9.5, 4), 3.3, WallFace.A, 1.9); // ducha
            Free(FixtureCatalog.Desague, new(9.0, 5.0), 0.0);
            foreach (var p in new[] { sink, basin, shower }) Wire(tank, p, "tuberia");
        });
        return doc;
    }

    /// <summary>Edificio de 4 plantas iguales con dos apartamentos por planta y escalera central.</summary>
    public static BuildingDocument EdificioApartamentos()
    {
        var doc = new BuildingDocument { Name = "Edificio de apartamentos", Type = BuildingType.Vivienda };
        doc.Edit(e =>
        {
            e.Clear();
            var n0 = e.AddLevel("Planta baja", 0, 3.0, "N0");
            const double h = 2.85;
            var walls = e.AddWallLoop(n0.Id, [P(0, 0), P(16, 0), P(16, 10), P(0, 10)], 0.25, h);
            var partyA = e.AddWall(n0.Id, P(6.5, 0), P(6.5, 10), 0.20, h);
            var partyB = e.AddWall(n0.Id, P(9.5, 0), P(9.5, 10), 0.20, h);
            var midA = e.AddWall(n0.Id, P(0, 5), P(6.5, 5), 0.15, h);
            var midB = e.AddWall(n0.Id, P(9.5, 5), P(16, 5), 0.15, h);
            e.AddWall(n0.Id, P(3.25, 5), P(3.25, 10), 0.10, h);
            e.AddWall(n0.Id, P(12.75, 5), P(12.75, 10), 0.10, h);

            e.AddDoor(partyA, 2.5, 0.9);
            e.AddDoor(partyB, 2.5, 0.9);
            foreach (var w in new[] { midA, midB }) { e.AddDoor(w, 1.6); e.AddDoor(w, 4.9); }
            e.AddWindow(walls[0], 3.2, 2.0);
            e.AddWindow(walls[0], 12.8, 2.0);
            foreach (var a in new[] { 1.6, 4.9, 11.1, 14.4 }) e.AddWindow(walls[2], a, 1.2);
            e.AddWindow(walls[1], 2.5, 1.2);
            e.AddWindow(walls[3], 7.5, 1.2);

            foreach (var (x, apt) in new[] { (3.25, "A"), (12.75, "B") })
            {
                e.TagSpace(n0.Id, P(x, 2.5), $"Apto {apt} · sala-cocina", "estar");
                e.TagSpace(n0.Id, P(x - 1.6, 7.5), $"Apto {apt} · dormitorio 1", "dormitorio");
                e.TagSpace(n0.Id, P(x + 1.6, 7.5), $"Apto {apt} · dormitorio 2", "dormitorio");
            }
            e.TagSpace(n0.Id, P(8, 8), "Escalera y vestíbulo", "circulacion");
            e.AddStair(n0.Id, P(8, 0.6), Math.PI / 2, 1.2, 3.0);
            e.AddSlab(n0.Id, [P(0, 0), P(16, 0), P(16, 10), P(0, 10)], 0.2);

            for (int i = 1; i <= 3; i++)
            {
                var lvl = e.DuplicateLevel(n0.Id, $"Planta {i}");
                if (i == 3)
                {
                    // La última planta no lleva escalera hacia arriba, pero sí cubierta.
                    foreach (var st in doc.Stairs.Where(s => s.LevelId == lvl.Id).ToList()) e.Delete(st.Id);
                    e.AddSlab(lvl.Id, [P(0, 0), P(16, 0), P(16, 10), P(0, 10)], 0.2, SlabKind.Roof);
                }
            }
            // Portal de entrada solo en la planta baja (después de copiar las plantas).
            e.AddDoor(walls[0], 8.0, 1.2, 2.3);
        });
        return doc;
    }

    /// <summary>Oficina de 14 × 9 m: zona abierta, dos despachos, sala de reuniones, office y aseos.</summary>
    public static BuildingDocument Oficina()
    {
        var doc = new BuildingDocument { Name = "Oficina", Type = BuildingType.Comercio };
        doc.Edit(e =>
        {
            e.Clear();
            var n = e.AddLevel("Planta", 0, 3.2, "N0");
            const double h = 3.0;
            var walls = e.AddWallLoop(n.Id, [P(0, 0), P(14, 0), P(14, 9), P(0, 9)], 0.25, h);
            var core = e.AddWall(n.Id, P(9, 0), P(9, 9), 0.15, h);
            e.AddWall(n.Id, P(9, 3), P(14, 3), 0.10, h);
            e.AddWall(n.Id, P(9, 6), P(14, 6), 0.10, h);
            var offices = e.AddWall(n.Id, P(0, 6), P(9, 6), 0.10, h);
            e.AddWall(n.Id, P(4.5, 6), P(4.5, 9), 0.10, h);

            e.AddDoor(walls[0], 4.5, 1.2);
            e.AddWindow(walls[0], 1.5, 1.8);
            e.AddWindow(walls[0], 7.5, 1.8);
            e.AddWindow(walls[0], 11.5, 1.8);
            e.AddWindow(walls[1], 4.5, 1.0);
            e.AddWindow(walls[2], 2.5, 2.0);
            e.AddWindow(walls[2], 7.0, 1.5);
            e.AddWindow(walls[2], 11.5, 1.5);
            e.AddDoor(core, 1.5);
            e.AddDoor(core, 4.5);
            e.AddDoor(core, 7.5);
            e.AddDoor(offices, 2.25);
            e.AddDoor(offices, 6.75);

            e.TagSpace(n.Id, P(4.5, 3), "Zona de trabajo abierta", "oficina");
            e.TagSpace(n.Id, P(2.25, 7.5), "Despacho 1", "oficina");
            e.TagSpace(n.Id, P(6.75, 7.5), "Despacho 2", "oficina");
            e.TagSpace(n.Id, P(11.5, 1.5), "Aseos", "baño");
            e.TagSpace(n.Id, P(11.5, 4.5), "Office (cocina)", "cocina");
            e.TagSpace(n.Id, P(11.5, 7.5), "Sala de reuniones", "oficina");
            e.AddSlab(n.Id, [P(0, 0), P(14, 0), P(14, 9), P(0, 9)], 0.2);
            e.AddSlab(n.Id, [P(0, 0), P(14, 0), P(14, 9), P(0, 9)], 0.2, SlabKind.Roof);
        });
        return doc;
    }
}
