using Simulador.Cad3D.Geometry.Model;

namespace Simulador.Cad3D.Geometry.Templates;

/// <summary>Un ejemplo prediseñado listo para abrir, editar e imprimir.</summary>
public sealed record CadExample(string Name, string Description, Func<bool, CadDocument> Create);

/// <summary>
/// Ejemplos prediseñados. El parámetro <c>exact</c> indica si hay motor exacto (OpenCASCADE): con él se añaden
/// redondeos y chaflanes; sin él el ejemplo se construye igual pero con aristas vivas.
/// </summary>
public static class Examples
{
    public static IReadOnlyList<CadExample> All { get; } = new[]
    {
        new CadExample("Caja electrónica con tapa", "Caja de 70×45×22 mm con postes M3, hueco USB y tapa ventilada.", ElectronicsBox),
        new CadExample("Caja para Arduino Uno", "A medida del Arduino Uno R3: postes en sus 4 taladros y huecos para USB-B y alimentación.", ArduinoUnoBox),
        new CadExample("Soporte en L", "Escuadra de 40×40 mm con dos agujeros avellanados por ala y refuerzo.", LBracket),
        new CadExample("Engranaje recto", "Módulo 2, 20 dientes, 8 mm de grueso y eje de 5 mm con plano en D.", SpurGear),
        new CadExample("Maceta", "Maceta cónica de 90 mm con plato y agujero de drenaje.", Planter),
        new CadExample("Separador hexagonal M3", "Separador de 10 mm entre caras y 15 mm de alto para tornillo M3.", HexStandoff),
        new CadExample("Soporte para móvil", "Atril de escritorio para teléfono, con ranura para el cable.", PhoneStand),
    };

    public static CadDocument ElectronicsBox(bool exact)
    {
        var doc = EnclosureTemplate.Create(new EnclosureParameters
        {
            InnerX = 70, InnerY = 45, InnerZ = 22, VentSlots = 5,
            Standoffs = new[] { new Standoff(5, 5), new Standoff(65, 5), new Standoff(5, 40), new Standoff(65, 40) },
            Cutouts = new[] { new WallCutout("Hueco USB", Wall.Frontal, 29, 8, 12, 7) },
        }, "Caja electrónica");
        if (exact)
        {
            doc.Parts[0].Features.Insert(1, new FilletFeature { Name = "Redondeo base", Radius = 1.5, Edges = EdgeSelection.Inferiores });
            doc.Parts[1].Features.Insert(1, new FilletFeature { Name = "Redondeo tapa", Radius = 1, Edges = EdgeSelection.Inferiores });
        }
        return doc;
    }

    public static CadDocument ArduinoUnoBox(bool exact)
    {
        // Arduino Uno R3: placa de 68,6 × 53,3 mm; taladros de 3,2 mm (coordenadas desde la esquina del USB).
        const double margin = 2;
        var holes = new (double X, double Y)[] { (13.97, 2.54), (15.24, 50.8), (66.04, 7.62), (66.04, 35.56) };
        var p = new EnclosureParameters
        {
            InnerX = 68.6 + 2 * margin, InnerY = 53.3 + 2 * margin, InnerZ = 30,
            StandoffHeight = 5,
            Standoffs = holes.Select(h => new Standoff(h.X + margin, h.Y + margin, 6, 2.6)).ToArray(),
            Cutouts = new[]
            {
                // USB-B (12 × 11 mm) y jack de 5,5 mm (9 × 11 mm) en la pared izquierda, sobre la placa (5 + 1,6 mm).
                new WallCutout("Hueco USB-B", Wall.Izquierda, 38.1 + margin - 6.5, 5.5, 13, 12),
                new WallCutout("Hueco alimentación", Wall.Izquierda, 7.6 + margin - 5, 5.5, 10, 12),
            },
            VentSlots = 6,
        };
        var doc = EnclosureTemplate.Create(p, "Caja Arduino Uno");
        if (exact) doc.Parts[0].Features.Insert(1, new FilletFeature { Name = "Redondeo base", Radius = 1.5, Edges = EdgeSelection.Inferiores });
        return doc;
    }

    public static CadDocument LBracket(bool exact)
    {
        var part = new Part { Name = "Escuadra", Color = "#5BBF6A" };
        part.Features.Add(new BoxFeature { Name = "Ala horizontal", SizeX = 40, SizeY = 20, SizeZ = 4 });
        part.Features.Add(new BoxFeature { Name = "Ala vertical", SizeX = 4, SizeY = 20, SizeZ = 40 });
        part.Features.Add(new ExtrudeFeature
        {
            Name = "Refuerzo",
            Profile = new() { new(0, 0), new(16, 0), new(0, 16) },
            Height = 3,
            Position = new Vec3(4, 11.5, 4),
            Rotation = new Vec3(90, 0, 0),
        });
        foreach (var (x, name) in new[] { (14.0, "1"), (30.0, "2") })
        {
            part.Features.Add(new CylinderFeature { Name = $"Agujero ala H {name}", Mode = CombineMode.Hueco, Diameter = 4.4, Height = 10, Position = new Vec3(x, 10, -1), Segments = 32 });
            part.Features.Add(new CylinderFeature { Name = $"Avellanado ala H {name}", Mode = CombineMode.Hueco, Diameter = 4.4, TopDiameter = 9, Height = 2.3, Position = new Vec3(x, 10, 1.71), Segments = 32 });
            part.Features.Add(new CylinderFeature { Name = $"Agujero ala V {name}", Mode = CombineMode.Hueco, Diameter = 4.4, Height = 10, Position = new Vec3(-1, 10, x), Rotation = new Vec3(0, 90, 0), Segments = 32 });
        }
        if (exact) part.Features.Insert(3, new FilletFeature { Name = "Redondeo", Radius = 1, Edges = EdgeSelection.Verticales });
        return new CadDocument { Name = "Soporte en L", Parts = { part } };
    }

    public static CadDocument SpurGear(bool exact)
    {
        var part = new Part { Name = "Engranaje", Color = "#E8A33D" };
        part.Features.Add(new ExtrudeFeature { Name = "Dientes", Profile = GearProfile(module: 2, teeth: 20), Height = 8 });
        part.Features.Add(new CylinderFeature { Name = "Eje", Mode = CombineMode.Hueco, Diameter = 5.2, Height = 10, Position = new Vec3(0, 0, -1), Segments = 48 });
        // Plano en D para que el eje no patine.
        part.Features.Add(new BoxFeature { Name = "Plano del eje", Mode = CombineMode.Solido, SizeX = 6, SizeY = 1.2, SizeZ = 8, Position = new Vec3(-3, 1.6, 0) });
        return new CadDocument { Name = "Engranaje m2 z20", Parts = { part } };
    }

    /// <summary>Perfil de engranaje recto con flancos de evolvente (ángulo de presión 20°).</summary>
    public static List<Vec2> GearProfile(double module, int teeth, int flankPoints = 6)
    {
        double rp = module * teeth / 2;                 // radio primitivo
        double rb = rp * Math.Cos(20 * Math.PI / 180); // radio base
        double ra = rp + module;                        // radio exterior
        double rf = Math.Max(rp - 1.25 * module, 1);    // radio de pie
        double pitchAngle = 2 * Math.PI / teeth;
        // Medio espesor del diente en el primitivo (en ángulo) + ángulo de evolvente en el primitivo.
        double inv(double r) { double a = Math.Acos(Math.Min(1, rb / r)); return Math.Tan(a) - a; }
        double halfTooth = Math.PI / (2 * teeth) + inv(rp);

        var pts = new List<Vec2>();
        for (int t = 0; t < teeth; t++)
        {
            double c = t * pitchAngle;
            var flank = new List<(double R, double A)>();
            double rStart = Math.Max(rb, rf);
            for (int i = 0; i <= flankPoints; i++)
            {
                double r = rStart + (ra - rStart) * i / flankPoints;
                flank.Add((r, halfTooth - inv(r)));
            }
            // Pie del hueco anterior, flanco derecho subiendo, cabeza, flanco izquierdo bajando.
            pts.Add(Polar(rf, c - pitchAngle / 2 + 0.0001));
            pts.Add(Polar(rf, c - flank[0].A));
            foreach (var (r, a) in flank) pts.Add(Polar(r, c - a));
            foreach (var (r, a) in Enumerable.Reverse(flank)) pts.Add(Polar(r, c + a));
            pts.Add(Polar(rf, c + flank[0].A));
        }
        // Quita puntos repetidos (cuando el pie coincide con el inicio del flanco).
        var clean = new List<Vec2>();
        foreach (var p in pts)
            if (clean.Count == 0 || Math.Abs(p.X - clean[^1].X) + Math.Abs(p.Y - clean[^1].Y) > 1e-6) clean.Add(p);
        return clean;

        static Vec2 Polar(double r, double a) => new(r * Math.Cos(a), r * Math.Sin(a));
    }

    public static CadDocument Planter(bool exact)
    {
        var pot = new Part { Name = "Maceta", Color = "#C9534F" };
        pot.Features.Add(new CylinderFeature { Name = "Cuerpo", Diameter = 70, TopDiameter = 90, Height = 80, Segments = 96 });
        pot.Features.Add(new CylinderFeature { Name = "Interior", Mode = CombineMode.Hueco, Diameter = 64.5, TopDiameter = 85, Height = 80, Position = new Vec3(0, 0, 3), Segments = 96 });
        pot.Features.Add(new CylinderFeature { Name = "Drenaje", Mode = CombineMode.Hueco, Diameter = 8, Height = 10, Position = new Vec3(0, 0, -1), Segments = 32 });
        var dish = new Part { Name = "Plato", Color = "#8E5B3A" };
        dish.Features.Add(new CylinderFeature { Name = "Plato", Diameter = 90, TopDiameter = 96, Height = 10, Position = new Vec3(110, 0, 0), Segments = 96 });
        dish.Features.Add(new CylinderFeature { Name = "Hueco plato", Mode = CombineMode.Hueco, Diameter = 84, TopDiameter = 90, Height = 10, Position = new Vec3(110, 0, 2), Segments = 96 });
        if (exact)
        {
            pot.Features.Add(new FilletFeature { Name = "Redondeo base", Radius = 2, Edges = EdgeSelection.Inferiores });
            dish.Features.Add(new FilletFeature { Name = "Redondeo plato", Radius = 1.5, Edges = EdgeSelection.Inferiores });
        }
        return new CadDocument { Name = "Maceta", Parts = { pot, dish } };
    }

    public static CadDocument HexStandoff(bool exact)
    {
        var part = new Part { Name = "Separador", Color = "#9B6FD0" };
        // Hexágono de 10 mm entre caras: radio al vértice = 10 / √3.
        part.Features.Add(new CylinderFeature { Name = "Hexágono", Diameter = 2 * 10 / Math.Sqrt(3), Height = 15, Segments = 6 });
        part.Features.Add(new CylinderFeature { Name = "Taladro M3", Mode = CombineMode.Hueco, Diameter = 3.3, Height = 17, Position = new Vec3(0, 0, -1), Segments = 32 });
        if (exact) part.Features.Add(new ChamferFeature { Name = "Chaflán", Distance = 0.5, Edges = EdgeSelection.Superiores });
        return new CadDocument { Name = "Separador M3", Parts = { part } };
    }

    public static CadDocument PhoneStand(bool exact)
    {
        var part = new Part { Name = "Atril", Color = "#3FB6B2" };
        // Perfil lateral en el plano XZ, extruido 70 mm de ancho (se dibuja en XY y se gira 90° en X).
        part.Features.Add(new ExtrudeFeature
        {
            Name = "Perfil",
            Profile = new()
            {
                new(0, 0), new(80, 0), new(80, 4), new(26, 4), new(26, 14), new(20, 14), new(20, 8),
                new(14, 8), new(48, 85), new(43, 87), new(8, 8), new(0, 4),
            },
            Height = 70,
            Rotation = new Vec3(90, 0, 0),
            Position = new Vec3(0, 70, 0),
        });
        part.Features.Add(new BoxFeature { Name = "Ranura del cable", Mode = CombineMode.Hueco, SizeX = 40, SizeY = 12, SizeZ = 20, Position = new Vec3(10, 29, -1) });
        if (exact) part.Features.Add(new FilletFeature { Name = "Redondeo", Radius = 1, Edges = EdgeSelection.Verticales });
        return new CadDocument { Name = "Soporte para móvil", Parts = { part } };
    }
}
