using System.Globalization;

namespace Simulador.Tableros.Model;

/// <summary>
/// Envolvente (caja, armario mural o armario de pie) de un fabricante, con sus medidas de catálogo.
/// Sin logotipos ni dibujos de la marca: el 3D se genera con estas medidas (cuerpo, placa, puerta, zócalo y color).
/// </summary>
/// <param name="Height">Alto exterior (mm). Todas las medidas como las da el fabricante: alto × ancho × fondo.</param>
/// <param name="PlateHeight">Alto de la placa de montaje (mm).</param>
/// <param name="SizeOfficial">true = alto, ancho y fondo leídos en la ficha o el catálogo del fabricante.</param>
/// <param name="PlateOfficial">true = medida de la placa leída en la ficha o el catálogo; false = supuesta.</param>
public sealed record EnclosureModel(
    string Brand, string Series, string Reference, EnclosureKind Kind,
    double Height, double Width, double Depth, double PlateHeight, double PlateWidth,
    int Doors, string Ip, string Material, uint Color, bool SizeOfficial, bool PlateOfficial, string Source)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public string FullReference => $"{Brand} {Reference}";
    public bool FloorStanding => Kind == EnclosureKind.ArmarioDePie;

    /// <summary>Texto corto para las listas: referencia y medida.</summary>
    public string Display => $"{Reference} · {N(Height)}×{N(Width)}×{N(Depth)}{(Doors > 1 ? " · 2 hojas" : "")}";

    /// <summary>Qué medidas son del fabricante y cuáles supuestas.</summary>
    public string Provenance =>
        (SizeOfficial ? "Medidas exteriores: oficiales" : "Medidas exteriores: supuestas (comprobar en el catálogo)") + " · " +
        (PlateOfficial ? "placa de montaje: oficial" : "placa de montaje: supuesta") + $". Fuente: {Source}.";

    private static string N(double v) => v.ToString("0.#", Es);

    /// <summary>Copia las medidas en la envolvente del tablero: la placa y la puerta se ajustan solas.</summary>
    public void ApplyTo(Enclosure e)
    {
        e.Kind = Kind;
        (e.Width, e.Height, e.Depth) = (Width, Height, Depth);
        e.PlateW = PlateWidth;
        e.PlateH = PlateHeight;
        e.Ip = Ip;
        e.Doors = Doors;
        e.Color = Color;
        e.Brand = Brand;
        e.Reference = Reference;
        e.Series = Series;
        e.Plinth = FloorStanding;
        e.AgainstWall = !FloorStanding;
    }
}

/// <summary>
/// Librería de envolventes por fabricante. Solo datos que el fabricante publica gratis (fichas de producto y
/// catálogos en PDF). Cada modelo dice si sus medidas son oficiales o supuestas (<see cref="EnclosureModel.Provenance"/>).
/// </summary>
public static class EnclosureCatalog
{
    // Colores: RAL 7035 (gris luminoso) y ANSI 61 (gris de los armarios norteamericanos).
    public const uint Ral7035 = 0xFFD7D9D3, Ansi61 = 0xFFA3A9A6, Ral7032 = 0xFFC9C5B2;

    private const string EatonSrc = "Eaton, catálogo xEffect CS FK4300-1178GB (enero 2025), tabla de pedidos y medidas";
    private const string HoffmanSrc = "nVent Hoffman, hoja de especificación Concept Spec-00286 H (panel CPxxxx)";
    private const string SeCrnSrc = "Schneider Electric, fichas PanelSeT CRN (se.com) y fichas de placa NSYMM";
    private const string RittalSrc = "Rittal, ficha técnica del artículo (rittal.com, pdf-creator)";
    private const string AbbSrc = "ABB, catálogo de envolventes 1SXU804003C0201 (2017) y folleto SR2/SRX 1SXU804001B0201";
    private const string SeCrnPlateOnly = "Schneider Electric, ficha de la placa NSYMM (la medida exterior sigue la codificación H×An×P de la referencia)";

    /// <summary>Todos los modelos, por marca y serie.</summary>
    public static IReadOnlyList<EnclosureModel> All { get; } = Build();

    public static IReadOnlyList<string> Brands { get; } = All.Select(m => m.Brand).Distinct().ToList();

    public static IEnumerable<EnclosureModel> OfBrand(string brand) => All.Where(m => m.Brand == brand);

    public static EnclosureModel? Find(string? brand, string? reference) =>
        brand is null || reference is null ? null : All.FirstOrDefault(m => m.Brand == brand && m.Reference == reference);

    /// <summary>
    /// Modelos de una marca aptos para el tablero, del más pequeño al más grande: para doble frente solo armarios de
    /// pie (con fondo para dos placas espalda con espalda).
    /// </summary>
    public static IEnumerable<EnclosureModel> Candidates(string brand, bool doubleFront) =>
        OfBrand(brand).Where(m => !doubleFront || (m.FloorStanding && m.Depth >= 500))
            .OrderBy(m => m.Width * m.Height).ThenBy(m => m.Depth);

    private static List<EnclosureModel> Build()
    {
        var list = new List<EnclosureModel>();

        // ---------------- Eaton xEffect CS: armario mural de chapa de acero, RAL 7035, placa galvanizada incluida.
        // Tabla: tipo | alto × ancho × fondo | placa alto × ancho. "-2D" = dos puertas (IP55); el resto una puerta (IP66).
        foreach (var row in """
            CS-2520/150 250 200 150 220 150
            CS-32/150 300 200 150 270 150
            CS-33/150 300 300 150 270 250
            CS-33/200 300 300 200 270 250
            CS-34/200 300 400 200 270 350
            CS-43/150 400 300 150 370 250
            CS-43/200 400 300 200 370 250
            CS-44/150 400 400 150 370 350
            CS-44/200 400 400 200 370 350
            CS-46/200 400 600 200 370 550
            CS-46/250 400 600 250 370 550
            CS-46/300 400 600 300 370 550
            CS-54/150 500 400 150 470 350
            CS-54/200 500 400 200 470 350
            CS-54/250 500 400 250 470 350
            CS-55/250 500 500 250 470 450
            CS-64/150 600 400 150 570 350
            CS-64/200 600 400 200 570 350
            CS-64/250 600 400 250 570 350
            CS-65/150 600 500 150 570 450
            CS-65/200 600 500 200 570 450
            CS-65/250 600 500 250 570 450
            CS-66/200 600 600 200 570 550
            CS-66/250 600 600 250 570 550
            CS-66/300 600 600 300 570 550
            CS-68/300 600 800 300 570 750
            CS-68/300-2D 600 800 300 570 750
            CS-75/200 700 500 200 670 450
            CS-75/250 700 500 250 670 450
            CS-84/200 800 400 200 770 350
            CS-84/250 800 400 250 770 350
            CS-86/200 800 600 200 770 550
            CS-86/250 800 600 250 770 550
            CS-86/300 800 600 300 770 550
            CS-88/200 800 800 200 770 750
            CS-88/300 800 800 300 770 750
            CS-810/300 800 1000 300 770 950
            CS-810/300-2D 800 1000 300 770 950
            CS-106/250 1000 600 250 970 550
            CS-106/300 1000 600 300 970 550
            CS-108/250 1000 800 250 970 750
            CS-108/300 1000 800 300 970 750
            CS-1010/300 1000 1000 300 970 950
            CS-1010/300-2D 1000 1000 300 970 950
            CS-1010/400-2D 1000 1000 400 970 950
            CS-126/250 1200 600 250 1170 550
            CS-128/300 1200 800 300 1170 750
            CS-1210/300 1200 1000 300 1170 950
            CS-1210/300-2D 1200 1000 300 1170 950
            CS-1210/400-2D 1200 1000 400 1170 950
            CS-1212/250 1200 1200 250 1170 1150
            CS-1212/300-2D 1200 1200 300 1170 1150
            CS-1212/400-2D 1200 1200 400 1170 1150
            CS-1410/400-2D 1400 1000 400 1370 950
            CS-1412/400-2D 1400 1200 400 1370 1150
            """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (r, v) = Row(row);
            var two = r.EndsWith("-2D", StringComparison.Ordinal);
            list.Add(new("Eaton", "xEffect CS (mural)", r, EnclosureKind.MuralMetalico, v[0], v[1], v[2], v[3], v[4],
                two ? 2 : 1, two ? "IP55" : "IP66", "Chapa de acero, RAL 7035", Ral7035, true, true, EatonSrc));
        }

        // ---------------- nVent Hoffman Concept (CSD, una puerta): acero pintado ANSI 61, NEMA 4/12, IP66.
        // La referencia lleva las pulgadas alto, ancho y fondo. Tabla en mm: alto ancho fondo | panel alto × ancho.
        foreach (var row in """
            CSD12126 305 305 152 259 259
            CSD16126 406 305 152 361 259
            CSD16166 406 406 152 361 361
            CSD20166 508 406 152 462 361
            CSD20206 508 508 152 462 462
            CSD24166 610 406 152 564 361
            CSD24206 610 508 152 564 462
            CSD24246 610 610 152 564 564
            CSD16128 406 305 203 361 259
            CSD20168 508 406 203 462 361
            CSD20208 508 508 203 462 462
            CSD24168 610 406 203 564 361
            CSD24208 610 508 203 564 462
            CSD24248 610 610 203 564 564
            CSD30208 762 508 203 716 462
            CSD30248 762 610 203 716 564
            CSD30308 762 762 203 716 716
            CSD36248 914 610 203 869 564
            CSD36308 914 762 203 869 716
            CSD36368 914 914 203 869 869
            CSD201610 508 406 254 462 361
            CSD202010 508 508 254 462 462
            CSD242010 610 508 254 564 462
            CSD242410 610 610 254 564 564
            CSD302410 762 610 254 716 564
            CSD303010 762 762 254 716 716
            CSD362410 914 610 254 869 564
            CSD363010 914 762 254 869 716
            CSD363610 914 914 254 869 869
            CSD423610 1067 914 254 1021 869
            CSD482410 1219 610 254 1173 564
            CSD483610 1219 914 254 1173 869
            CSD603610 1524 914 254 1478 869
            CSD242412 610 610 305 564 564
            CSD302412 762 610 305 716 564
            CSD363012 914 762 305 869 716
            CSD363612 914 914 305 869 869
            CSD423612 1067 914 305 1021 869
            CSD483612 1219 914 305 1173 869
            CSD603612 1524 914 305 1478 869
            CSD242416 610 610 406 564 564
            CSD363016 914 762 406 869 716
            CSD483616 1219 914 406 1173 869
            CSD242420 610 610 508 564 564
            CSD302420 762 610 508 716 564
            CSD363020 914 762 508 869 716
            """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (r, v) = Row(row);
            list.Add(new("nVent Hoffman", "Concept CSD (mural)", r, EnclosureKind.MuralMetalico, v[0], v[1], v[2], v[3], v[4],
                1, "IP66 · NEMA 4/12", "Acero dulce pintado, ANSI 61", Ansi61, true, true, HoffmanSrc));
        }

        // ---------------- Schneider Electric PanelSeT CRN (mural de acero, RAL 7035, IP66 IK10). Placas NSYMM.
        // Tabla: referencia alto ancho fondo | placa alto × ancho | 1 = la ficha de la envolvente se ha leído.
        foreach (var row in """
            NSYCRN33150 300 300 150 250 250 0
            NSYCRN43200 400 300 200 350 250 1
            NSYCRN44200 400 400 200 350 350 1
            NSYCRN54200 500 400 200 450 350 0
            NSYCRN55200 500 500 200 450 450 0
            NSYCRN64200 600 400 200 550 350 1
            NSYCRN66200 600 600 200 550 550 1
            NSYCRN75250 700 500 250 650 450 0
            NSYCRN86300 800 600 300 750 550 0
            NSYCRN88300 800 800 300 750 750 0
            NSYCRN106300 1000 600 300 950 550 1
            NSYCRN108300 1000 800 300 950 750 0
            NSYCRN1210300 1200 1000 300 1150 950 0
            """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (r, v) = Row(row);
            var read = v[5] == 1;
            list.Add(new("Schneider Electric", "PanelSeT CRN (mural)", r, EnclosureKind.MuralMetalico, v[0], v[1], v[2], v[3], v[4],
                1, "IP66", "Chapa de acero, RAL 7035", Ral7035, read, true, read ? SeCrnSrc : SeCrnPlateOnly));
        }

        // ---------------- Rittal AX (caja compacta de acero, RAL 7035). Tabla: artículo alto ancho fondo | placa alto × ancho | puertas IP.
        foreach (var row in """
            AX1031.000 300 380 210 275 330 1 66
            AX1033.000 300 300 210 275 250 1 66
            AX1034.000 400 300 210 375 250 1 66
            AX1045.000 500 400 210 475 350 1 66
            AX1050.000 500 500 210 475 450 1 66
            AX1038.000 600 380 210 575 330 1 66
            AX1039.000 380 600 210 355 550 1 66
            AX1060.000 600 600 210 575 550 1 66
            AX1054.000 600 600 250 575 550 1 66
            AX1057.000 700 500 250 675 450 1 66
            AX1076.000 760 600 210 735 550 1 66
            AX1077.000 760 760 210 735 705 1 66
            AX1073.000 760 760 300 735 705 1 66
            AX1037.000 800 400 300 775 345 1 66
            AX1055.000 600 800 300 575 750 1 66
            AX1090.000 1000 600 250 975 545 1 66
            AX1180.000 1000 800 300 975 745 1 66
            AX1100.000 760 1000 210 735 945 2 55
            AX1130.000 760 1000 300 735 945 2 55
            AX1260.000 1200 600 300 1175 545 1 66
            AX1280.000 1200 800 300 1175 745 1 66
            AX1213.000 1200 1000 300 1175 945 2 55
            AX1114.000 1400 1000 300 1375 945 2 55
            """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (r, v) = Row(row);
            list.Add(new("Rittal", "AX (caja compacta)", r[..2] + " " + r[2..], EnclosureKind.MuralMetalico, v[0], v[1], v[2], v[3], v[4],
                (int)v[5], $"IP{v[6]:0}", "Chapa de acero, RAL 7035", Ral7035, true, true, RittalSrc));
        }

        // ---------------- Rittal VX25 (armario de pie ensamblable, RAL 7035, IP55). Sin zócalo (es un accesorio de 100 mm).
        foreach (var row in """
            VX8685.000 1800 600 500 1696 499 1
            VX8686.000 1800 600 600 1696 499 1
            VX8886.000 1800 800 600 1696 699 1
            VX8605.000 2000 600 500 1896 499 1
            VX8606.000 2000 600 600 1896 499 1
            VX8608.000 2000 600 800 1896 499 1
            VX8804.000 2000 800 400 1896 699 1
            VX8805.000 2000 800 500 1896 699 1
            VX8806.000 2000 800 600 1896 699 1
            VX8808.000 2000 800 800 1896 699 1
            VX8005.000 2000 1000 500 1896 899 2
            VX8006.000 2000 1000 600 1896 899 2
            VX8205.000 2000 1200 500 1896 1099 2
            VX8206.000 2000 1200 600 1896 1099 2
            VX8208.000 2000 1200 800 1896 1099 2
            """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (r, v) = Row(row);
            list.Add(new("Rittal", "VX25 (armario de pie)", r[..2] + " " + r[2..], EnclosureKind.ArmarioDePie, v[0], v[1], v[2], v[3], v[4],
                (int)v[5], "IP55", "Chapa de acero, RAL 7035", Ral7035, true, true, RittalSrc));
        }

        // ---------------- ABB SR2 (mural de acero, RAL 7035, IP65, puerta ciega). El catálogo da la placa por la medida
        // de la caja (PF…), no su medida útil: se supone 50 mm menos de alto y de ancho.
        foreach (var row in """
            SRN3215K 300 200 150
            SRN3315K 300 300 150
            SRN3415K 300 400 150
            SRN4315K 400 300 150
            SRN4320K 400 300 200
            SRN4420K 400 400 200
            SRN4620K 400 600 200
            SRN5320K 500 300 200
            SRN5420K 500 400 200
            SRN5425K 500 400 250
            SRN6420K 600 400 200
            SRN6425K 600 400 250
            SRN6625K 600 600 250
            SRN7520K 700 500 200
            SRN7525K 700 500 250
            SRN8625K 800 600 250
            SRN8630K 800 600 300
            SRN8830K 800 800 300
            SRN10625K 1000 600 250
            SRN10630K 1000 600 300
            SRN10830K 1000 800 300
            SRN12630K 1200 600 300
            SRN12830K 1200 800 300
            """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (r, v) = Row(row);
            list.Add(new("ABB", "SR2 (mural)", r, EnclosureKind.MuralMetalico, v[0], v[1], v[2], v[0] - 50, v[1] - 50,
                1, "IP65", "Chapa de acero, RAL 7035", Ral7035, true, false, AbbSrc));
        }

        return list;
    }

    private static (string Reference, double[] Values) Row(string row)
    {
        var parts = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Skip(1).Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray());
    }
}
