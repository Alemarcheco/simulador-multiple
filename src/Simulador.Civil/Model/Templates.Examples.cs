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
                var normal = w.Direction.Perp() * (face == WallFace.A ? 1 : -1);
                return e.AddFixture(new PlacedFixture(e.NewId("F"), t.ModuleId, t.Kind, e.NewComponentId(t.ComponentPrefix), null, L, w.Id, pos,
                    Math.Atan2(normal.Y, normal.X), Math.Max(0.1, t.Size)));
            }
            PlacedFixture Free(FixtureType t, Point2 at, double z, double size = 0.3) =>
                e.AddFixture(new PlacedFixture(e.NewId("F"), t.ModuleId, t.Kind, e.NewComponentId(t.ComponentPrefix), null, L, null, new Point3(at, z), 0, size, size));
            void Wire(PlacedFixture a, PlacedFixture b, string kind) =>
                e.AddRoute(new Route(e.NewId("R"), a.ModuleId, kind, a.Id, b.Id, RouteBuilder.ViaCeiling(doc, a, b)));

            var fc = FixtureCatalog.Tomacorriente;
            // Centro de carga en el pasillo, junto a la entrada a las habitaciones, y base de medidor en la fachada.
            OnWall(FixtureCatalog.Tablero, new(6.2, 0.5), 0.6, WallFace.A);
            OnWall(FixtureCatalog.Medidor, new(9.0, 0), 9.4, WallFace.B);

            // Contactos (0,30 m), uno o dos por habitación y en la cocina a altura de encimera.
            OnWall(fc, new(4, 0), 4.0, WallFace.A);             // sala, muro sur
            OnWall(fc, new(0, 0.5), 7.5, WallFace.A);           // sala, muro oeste
            OnWall(fc, new(1.5, 5), 1.5, WallFace.A, 1.10);     // cocina, encimera
            OnWall(fc, new(0.5, 8), 9.5, WallFace.A, 1.10);     // cocina, muro norte
            OnWall(fc, new(10, 0.6), 0.6, WallFace.A);          // dormitorio 1
            OnWall(fc, new(6.2, 1.0), 1.0, WallFace.B);         // dormitorio 1, junto a la cama
            OnWall(fc, new(10, 7), 7.0, WallFace.A);            // dormitorio 2
            OnWall(fc, new(10, 4.5), 4.5, WallFace.A, 1.10);    // baño (lavabo)
            // Minisplit en el dormitorio 1 con su contacto de 220 V.
            OnWall(FixtureCatalog.AireAcondicionado, new(8, 4), 2.5, WallFace.B);
            OnWall(FixtureCatalog.Contacto220, new(8, 4), 3.4, WallFace.B, 2.0);

            // Una luminaria en el centro de cada habitación y un arbotante en la entrada.
            var lamps = doc.Spaces.Where(s => s.LevelId == L).ToDictionary(s => s.Name, s => Free(FixtureCatalog.Luminaria, Geo.Centroid(s.Outline), 2.95));
            var porch = OnWall(FixtureCatalog.Arbotante, new(1.85, 0), 1.85, WallFace.B);

            // Apagadores (1,20 m) junto a las puertas: doble en la entrada (sala y arbotante), de escalera en el pasillo.
            var entry = OnWall(FixtureCatalog.Doble, new(3.3, 0), 3.3, WallFace.A);
            var hall1 = OnWall(FixtureCatalog.Escalera, new(5, 3.2), 3.2, WallFace.B);
            var hall2 = OnWall(FixtureCatalog.Escalera, new(5, 7.2), 7.2, WallFace.B);
            OnWall(FixtureCatalog.Interruptor, new(6.2, 3.7), 3.7, WallFace.B);      // dormitorio 1
            OnWall(FixtureCatalog.Interruptor, new(2.5, 5), 4.5, WallFace.A);        // cocina
            OnWall(FixtureCatalog.Interruptor, new(6.2, 5.6), 5.6, WallFace.B);      // baño
            OnWall(FixtureCatalog.Interruptor, new(6.2, 7.6), 7.6, WallFace.B);      // dormitorio 2
            var el = e.Electrical;
            el.Set(new Electrical.FixtureElectrical(lamps["Sala-comedor"].Id, ControlledBy: [entry.Id]));
            el.Set(new Electrical.FixtureElectrical(porch.Id, ControlledBy: [entry.Id + "#2"]));
            el.Set(new Electrical.FixtureElectrical(lamps["Pasillo"].Id, ControlledBy: [hall1.Id, hall2.Id]));
            Electrical.ElectricalDesigner.AutoSwitches(doc, e);
            Electrical.ElectricalDesigner.AutoCircuits(doc, e);
            Electrical.ElectricalDesigner.AutoConduits(doc, e);
            Electrical.ElectricalDesigner.AutoSizeWires(doc, e);
            Electrical.ElectricalDesigner.AutoSizeConduits(doc, e);

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

    /// <summary>
    /// Nave de 30 × 15 m con marcos de acero (columnas cada 6 m y vigas de lado a lado) y su instalación eléctrica:
    /// campanas industriales colgadas de las vigas, contactos trifásicos y de 127 V en las columnas, dos máquinas con
    /// su desconectador, alumbrado de la oficina, luces de emergencia y tubería conduit a la vista bajo las vigas.
    /// </summary>
    public static BuildingDocument NaveConInstalacionElectrica()
    {
        var doc = NaveIndustrial();
        doc.Name = "Nave industrial con instalación eléctrica";
        const string L = "N0";
        doc.Edit(e =>
        {
            AddSteelFrames(e, L);
        });
        doc.Edit(e =>
        {
            PlacedFixture At(FixtureType t, double x, double y, double? h = null) =>
                FixturePlacer.Place(doc, e, t, L, new(x, y), h) ?? throw new InvalidOperationException($"No cabe {t.Name} en ({x}, {y}).");

            // Centro de carga y base de medidor junto a la puerta de la oficina.
            At(FixtureCatalog.Tablero, 8.0, 0.2);
            At(FixtureCatalog.Medidor, 9.5, -0.2);

            // Campanas: tres por viga intermedia, colgadas 0,5 m bajo la viga.
            for (int x = 6; x <= 24; x += 6)
                foreach (var y in new[] { 3.75, 7.5, 11.25 })
                    At(FixtureCatalog.Campana, x, y);
            // Apagadores de las campanas en la columna junto al portón.
            var s1 = At(FixtureCatalog.Interruptor, 12.0, 0.45);
            var s2 = At(FixtureCatalog.Interruptor, 18.0, 0.45);

            // Contactos en las columnas: trifásicos para máquinas portátiles y de 127 V con GFCI para herramienta.
            foreach (var x in new[] { 12.0, 24.0 }) At(FixtureCatalog.ContactoTrifasico, x, 14.55);
            foreach (var x in new[] { 6.0, 24.0, 29.8 }) At(FixtureCatalog.ContactoGfci, x, 0.45);

            // Dos máquinas con su interruptor de seguridad en la columna más cercana.
            At(FixtureCatalog.Motor, 15, 10);
            At(FixtureCatalog.Motor, 21, 10);
            At(FixtureCatalog.Desconectador, 18.0, 14.55);

            // Oficina: paneles LED bajo su techo de 3 m, apagador y contactos. Aseos: plafón.
            At(FixtureCatalog.PanelLed, 4.75, 1.6, -4.0);
            At(FixtureCatalog.PanelLed, 4.75, 3.4, -4.0);
            At(FixtureCatalog.Interruptor, 5.95, 4.6);
            At(FixtureCatalog.Tomacorriente, 3.6, 2.5);
            At(FixtureCatalog.Tomacorriente, 5.0, 0.15);
            At(FixtureCatalog.Luminaria, 1.75, 2.5, -4.05);
            At(FixtureCatalog.Interruptor, 3.45, 1.5);

            // Luces de emergencia sobre las salidas.
            At(FixtureCatalog.Emergencia, 18.6, 0.2);
            At(FixtureCatalog.Emergencia, 10.5, 0.2);

            // Las campanas de las dos primeras vigas con un apagador y las otras dos con el otro.
            foreach (var f in doc.Fixtures.Where(f => f.Kind == FixtureCatalog.Campana.Kind))
                e.Electrical.Set(new Electrical.FixtureElectrical(f.Id, ControlledBy: [f.Position.X < 15 ? s1.Id : s2.Id]));
            Electrical.ElectricalDesigner.AutoSwitches(doc, e);
            Electrical.ElectricalDesigner.AutoCircuits(doc, e);
            Electrical.ElectricalDesigner.AutoConduits(doc, e);
            Electrical.ElectricalDesigner.AutoSizeWires(doc, e);
            Electrical.ElectricalDesigner.AutoSizeConduits(doc, e);
        });
        return doc;
    }

    /// <summary>Columnas de acero cada 6 m en los muros largos y vigas de lado a lado.</summary>
    private static void AddSteelFrames(BuildingDocument.Editor e, string level)
    {
        for (int x = 0; x <= 30; x += 6)
        {
            double cx = Math.Clamp(x, 0.2, 29.8);
            e.AddColumn(level, new(cx, 0.2), 0.35, 0.35, 0, "acero");
            e.AddColumn(level, new(cx, 14.8), 0.35, 0.35, 0, "acero");
            e.AddBeam(level, new(cx, 0.2), new(cx, 14.8), 0.25, 0.60, "acero");
        }
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
