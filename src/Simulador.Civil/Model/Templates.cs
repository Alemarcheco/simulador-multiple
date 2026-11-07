using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>Plantillas para empezar rápido: se cargan y luego se editan libremente.</summary>
public static partial class BuildingTemplates
{
    public static IReadOnlyList<(string Name, Func<BuildingDocument> Create)> All { get; } =
    [
        ("Casa de una planta (2 dormitorios)", Casa),
        ("Casa de dos plantas con escalera", CasaDosPlantas),
        ("Hotel (3 plantas tipo)", Hotel),
        ("Nave industrial con oficina", NaveIndustrial),
        ("Casa con instalación eléctrica e hidráulica", CasaConInstalaciones),
        ("Edificio de apartamentos (4 plantas)", EdificioApartamentos),
        ("Oficina con sala de reuniones", Oficina),
    ];

    private static Point2 P(double x, double y) => new(x, y);

    /// <summary>Casa de 10 × 8 m: sala-comedor, cocina, dos dormitorios, baño y pasillo.</summary>
    public static BuildingDocument Casa()
    {
        var doc = new BuildingDocument { Name = "Casa de una planta", Type = BuildingType.Vivienda };
        doc.Edit(e =>
        {
            e.Clear();
            var n = e.AddLevel("Planta baja", 0, 3.0, "N0");
            BuildHouseFloor(e, n.Id, withStair: false);
            e.AddSlab(n.Id, [P(0, 0), P(10, 0), P(10, 8), P(0, 8)], 0.15, SlabKind.Floor);
            e.AddSlab(n.Id, [P(0, 0), P(10, 0), P(10, 8), P(0, 8)], 0.15, SlabKind.Roof);
        });
        return doc;
    }

    public static BuildingDocument CasaDosPlantas()
    {
        var doc = new BuildingDocument { Name = "Casa de dos plantas", Type = BuildingType.Vivienda };
        doc.Edit(e =>
        {
            e.Clear();
            var n0 = e.AddLevel("Planta baja", 0, 2.8, "N0");
            BuildHouseFloor(e, n0.Id, withStair: true);
            e.AddSlab(n0.Id, [P(0, 0), P(10, 0), P(10, 8), P(0, 8)], 0.15, SlabKind.Floor);
            var n1 = e.AddLevel("Planta alta", 2.8, 3.0, "N1");
            var up = e.AddWallLoop(n1.Id, [P(0, 0), P(10, 0), P(10, 8), P(0, 8)], 0.20, 2.85);
            e.AddWindow(up[0], 2.5, 1.5);
            e.AddWindow(up[0], 7.5, 1.5);
            e.AddWindow(up[1], 2.0, 1.2);
            e.AddWindow(up[1], 6.0, 1.2);
            e.AddWindow(up[2], 2.5, 1.5);
            e.AddWindow(up[3], 4.0, 1.2);
            var mid = e.AddWall(n1.Id, P(5, 0), P(5, 8), 0.15, 2.85);
            e.AddWall(n1.Id, P(5, 4), P(10, 4), 0.15, 2.85);
            e.AddDoor(mid, 5.5);
            e.TagSpace(n1.Id, P(2.5, 4), "Estar y escalera", "estar");
            e.TagSpace(n1.Id, P(7.5, 2), "Dormitorio principal", "dormitorio");
            e.TagSpace(n1.Id, P(7.5, 6), "Dormitorio 3", "dormitorio");
            e.AddSlab(n1.Id, [P(0, 0), P(10, 0), P(10, 8), P(0, 8)], 0.15, SlabKind.Floor);
            e.AddSlab(n1.Id, [P(0, 0), P(10, 0), P(10, 8), P(0, 8)], 0.15, SlabKind.Roof);
        });
        return doc;
    }

    private static void BuildHouseFloor(BuildingDocument.Editor e, string level, bool withStair)
    {
        const double h = 2.85;
        var south = e.AddWall(level, P(0, 0), P(10, 0), 0.20, h);
        var east = e.AddWall(level, P(10, 0), P(10, 8), 0.20, h);
        var north = e.AddWall(level, P(10, 8), P(0, 8), 0.20, h);
        var west = e.AddWall(level, P(0, 8), P(0, 0), 0.20, h);

        // Tabiques: sala-comedor (oeste), cocina (noroeste), pasillo, dormitorios y baño (este).
        var t1 = e.AddWall(level, P(5, 0), P(5, 8), 0.15, h);
        var kitchen = e.AddWall(level, P(0, 5), P(5, 5), 0.15, h);
        var corridor = e.AddWall(level, P(6.2, 0), P(6.2, 8), 0.10, h);
        var t3 = e.AddWall(level, P(6.2, 4), P(10, 4), 0.15, h);
        var t4 = e.AddWall(level, P(6.2, 6), P(10, 6), 0.10, h);

        e.AddDoor(south, 2.5, 0.9);
        e.AddWindow(south, 1.0, 1.2);
        e.AddWindow(south, 8.1, 1.2);
        e.AddWindow(north, 7.5, 1.5);
        e.AddWindow(north, 2.0, 1.2);
        e.AddWindow(east, 2.0, 1.2);
        e.AddWindow(east, 5.0, 0.6, 0.6, 1.5);
        e.AddWindow(west, 1.5, 1.2);
        e.AddWindow(west, 5.5, 1.5);
        e.AddDoor(t1, 2.5, 0.9);
        e.AddDoor(t1, 6.5, 0.9);
        e.AddDoor(kitchen, 3.8, 0.9);
        e.AddDoor(corridor, 3.0);
        e.AddDoor(corridor, 5.0, 0.7);
        e.AddDoor(corridor, 7.0);
        _ = t3; _ = t4;

        e.TagSpace(level, P(2.5, 2.5), "Sala-comedor", "estar");
        e.TagSpace(level, P(2.5, 6.5), "Cocina", "cocina");
        e.TagSpace(level, P(5.6, 4), "Pasillo", "circulacion");
        e.TagSpace(level, P(8, 2), "Dormitorio 1", "dormitorio");
        e.TagSpace(level, P(8, 5), "Baño", "baño");
        e.TagSpace(level, P(8, 7), "Dormitorio 2", "dormitorio");

        if (withStair)
            e.AddStair(level, P(0.6, 0.4), Math.PI / 2, 0.9, 2.8);
    }

    /// <summary>Hotel: planta baja con recepción y tres plantas tipo con pasillo central y 8 habitaciones.</summary>
    public static BuildingDocument Hotel()
    {
        var doc = new BuildingDocument { Name = "Hotel", Type = BuildingType.Hotel };
        doc.Edit(e =>
        {
            e.Clear();
            var n0 = e.AddLevel("Planta baja", 0, 3.5, "N0");
            var front = e.AddWall(n0.Id, P(0, 0), P(24, 0), 0.25, 3.3);
            e.AddWall(n0.Id, P(24, 0), P(24, 12), 0.25, 3.3);
            e.AddWall(n0.Id, P(24, 12), P(0, 12), 0.25, 3.3);
            e.AddWall(n0.Id, P(0, 12), P(0, 0), 0.25, 3.3);
            e.AddDoor(front, 8.0, 1.8, 2.4);
            e.AddWindow(front, 3.0, 2.5, 1.6, 0.8);
            e.AddWindow(front, 13.0, 2.5, 1.6, 0.8);
            var service = e.AddWall(n0.Id, P(16, 0), P(16, 12), 0.15, 3.3);
            e.AddDoor(service, 3.0, 0.9);
            e.AddDoor(service, 9.0, 0.9);
            e.AddWall(n0.Id, P(16, 6), P(24, 6), 0.15, 3.3);
            e.TagSpace(n0.Id, P(8, 6), "Recepción y vestíbulo", "estar");
            e.TagSpace(n0.Id, P(20, 3), "Cocina", "cocina");
            e.TagSpace(n0.Id, P(20, 9), "Cuarto técnico (bombas, tanque, tablero)", "tecnico");
            // 3,5 m son 20 escalones: en U, con descanso a media altura, y llegando arriba al pasillo central.
            e.AddStair(n0.Id, P(14.5, 4.7), -Math.PI / 2, 1.2, 3.5, StairShape.U, turnLeft: false);
            e.AddSlab(n0.Id, [P(0, 0), P(24, 0), P(24, 12), P(0, 12)], 0.2);

            var n1 = e.AddLevel("Planta 1", 3.5, 3.0, "N1");
            var facade = e.AddWallLoop(n1.Id, [P(0, 0), P(24, 0), P(24, 12), P(0, 12)], 0.25, 2.8);
            e.AddWindow(facade[1], 6.0, 1.2);
            e.AddWindow(facade[3], 6.0, 1.2);
            var c1 = e.AddWall(n1.Id, P(0, 4.6), P(24, 4.6), 0.15, 2.8);
            var c2 = e.AddWall(n1.Id, P(0, 7.4), P(24, 7.4), 0.15, 2.8);
            for (int i = 1; i < 4; i++)
            {
                e.AddWall(n1.Id, P(i * 6, 0), P(i * 6, 4.6), 0.15, 2.8);
                e.AddWall(n1.Id, P(i * 6, 7.4), P(i * 6, 12), 0.15, 2.8);
            }
            for (int i = 0; i < 4; i++)
            {
                e.AddWindow(facade[0], i * 6 + 3.5, 1.6, 1.4, 0.9);
                e.AddWindow(facade[2], 24 - (i * 6 + 3.5), 1.6, 1.4, 0.9);
                e.AddDoor(c1, i * 6 + 1.0, 0.8);
                e.AddDoor(c2, 24 - (i * 6 + 1.0), 0.8);
                e.TagSpace(n1.Id, P(i * 6 + 3, 2.3), $"Habitación 1{i + 1:00}", "habitacion_hotel");
                e.TagSpace(n1.Id, P(i * 6 + 3, 9.7), $"Habitación 1{i + 5:00}", "habitacion_hotel");
            }
            e.TagSpace(n1.Id, P(12, 6), "Pasillo", "circulacion");
            e.AddSlab(n1.Id, [P(0, 0), P(24, 0), P(24, 12), P(0, 12)], 0.2);
            e.DuplicateLevel(n1.Id, "Planta 2");
            var n3 = e.DuplicateLevel(n1.Id, "Planta 3");
            e.AddSlab(n3.Id, [P(0, 0), P(24, 0), P(24, 12), P(0, 12)], 0.2, SlabKind.Roof);
        });
        return doc;
    }

    /// <summary>Nave de 30 × 15 m con oficina y aseos en una esquina y portón de carga.</summary>
    public static BuildingDocument NaveIndustrial()
    {
        var doc = new BuildingDocument { Name = "Nave industrial", Type = BuildingType.Industria };
        doc.Edit(e =>
        {
            e.Clear();
            var n = e.AddLevel("Nave", 0, 7.0, "N0");
            var s = e.AddWall(n.Id, P(0, 0), P(30, 0), 0.25, 7.0);
            e.AddWall(n.Id, P(30, 0), P(30, 15), 0.25, 7.0);
            var no = e.AddWall(n.Id, P(30, 15), P(0, 15), 0.25, 7.0);
            e.AddWall(n.Id, P(0, 15), P(0, 0), 0.25, 7.0);
            var o1 = e.AddWall(n.Id, P(6, 0), P(6, 5), 0.15, 3.0);
            e.AddWall(n.Id, P(0, 5), P(6, 5), 0.15, 3.0);
            e.AddWall(n.Id, P(3.5, 0), P(3.5, 5), 0.10, 3.0);
            e.AddDoor(s, 15, 5.0, 5.0);
            e.AddDoor(s, 1.5, 0.9);
            e.AddDoor(o1, 4.0, 0.9);
            for (int i = 0; i < 5; i++) e.AddWindow(no, 3 + i * 6, 3.0, 1.0, 5.0);
            e.TagSpace(n.Id, P(18, 8), "Zona de producción", "nave");
            e.TagSpace(n.Id, P(1.75, 2.5), "Aseos", "baño");
            e.TagSpace(n.Id, P(4.75, 2.5), "Oficina", "oficina");
            e.AddSlab(n.Id, [P(0, 0), P(30, 0), P(30, 15), P(0, 15)], 0.2);
            e.AddSlab(n.Id, [P(0, 0), P(30, 0), P(30, 15), P(0, 15)], 0.1, SlabKind.Roof);
        });
        return doc;
    }
}
