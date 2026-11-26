namespace Simulador.Tableros.Model;

/// <summary>
/// Distribución automática: elige la envolvente normalizada más pequeña en la que cabe todo y coloca los aparatos
/// en filas de carril DIN separadas por canaletas horizontales, con dos canaletas verticales a los lados.
/// En la puerta, los mandos y pilotos en filas centradas.
/// </summary>
public static class AutoLayout
{
    /// <summary>Medidas habituales de envolventes (ancho × alto × fondo, mm).</summary>
    public static readonly (double W, double H, double D)[] StandardSizes =
    [
        (300, 400, 200), (400, 500, 200), (400, 600, 250), (500, 700, 250), (600, 800, 250), (600, 1000, 300),
        (800, 1000, 300), (800, 1200, 300), (600, 1800, 400), (800, 1800, 400), (1000, 2000, 400), (1200, 2000, 500),
    ];

    public const double DuctWidth = 40, DuctDepth = 60, SideDuct = 40;
    /// <summary>Holgura entre una canaleta y los aparatos (mm).</summary>
    public const double Clearance = 25;
    public const double Gap = 0; // los aparatos modulares van juntos
    public const double BigGap = 10;

    /// <summary>Coloca todo y ajusta la envolvente. Devuelve false si ni el armario más grande alcanza.</summary>
    public static bool Arrange(PanelDocument doc, bool chooseEnclosure = true)
    {
        var plate = doc.OnPlate.ToList();
        var maxDepth = plate.Select(d => d.Depth).DefaultIfEmpty(0).Max();
        var fits = false;
        if (chooseEnclosure)
        {
            foreach (var (w, h, dp) in StandardSizes)
            {
                if (dp - 35 < maxDepth + 10) continue;
                doc.Enclosure.Width = w;
                doc.Enclosure.Height = h;
                doc.Enclosure.Depth = dp;
                doc.Enclosure.Kind = h >= 1800 ? EnclosureKind.ArmarioDePie : EnclosureKind.MuralMetalico;
                doc.Enclosure.AgainstWall = h < 1800;
                if (TryRows(doc, plate, apply: false) && DoorFits(doc)) { fits = true; break; }
            }
            if (!fits)
            {
                var (w, h, dp) = StandardSizes[^1];
                (doc.Enclosure.Width, doc.Enclosure.Height, doc.Enclosure.Depth) = (w, h, dp);
            }
        }
        fits = TryRows(doc, plate, apply: true) | fits;
        ArrangeDoor(doc);
        return fits;
    }

    private static bool DoorFits(PanelDocument doc)
    {
        var door = doc.OnDoor.ToList();
        if (door.Count == 0) return true;
        var perRow = Math.Max(1, (int)((doc.Enclosure.Width - 100) / 60));
        var rows = (door.Count + perRow - 1) / perRow;
        return 80 + rows * 90 < doc.Enclosure.Height * 0.6;
    }

    /// <summary>Reparte los aparatos de la placa en filas. Con <paramref name="apply"/> = false solo comprueba si caben.</summary>
    private static bool TryRows(PanelDocument doc, List<PanelDevice> devices, bool apply)
    {
        var pw = doc.Enclosure.PlateWidth;
        var ph = doc.Enclosure.PlateHeight;
        double x0 = SideDuct + Clearance - 10, x1 = pw - SideDuct - Clearance + 10;
        var usable = x1 - x0;

        // Filas: carril, salvo aparatos grandes de placa (variador, transformadores) que van en su propia fila.
        var rows = new List<List<PanelDevice>>();
        var current = new List<PanelDevice>();
        double width = 0;
        string? lastGroup = null;
        foreach (var d in devices.OrderBy(d => SchematicImport.Order(d.Kind)).ThenBy(d => d.Tag, TagComparer.Instance))
        {
            var group = Group(d.Kind);
            var need = d.Width + (current.Count > 0 ? GapBetween(current[^1], d) : 0);
            // Cambio de grupo (protecciones → mando → borneras): nueva fila si la actual ya va llena a medias.
            var newRow = current.Count > 0 && (width + need > usable || (group != lastGroup && (group == "bornes" || width > usable * 0.6)));
            if (d.Width > usable) return false;
            if (newRow)
            {
                rows.Add(current);
                current = [];
                width = 0;
                need = d.Width;
            }
            current.Add(d);
            width += need;
            lastGroup = group;
        }
        if (current.Count > 0) rows.Add(current);

        // Alto: canaleta arriba, y por cada fila: holgura, fila, holgura, canaleta.
        var y = DuctWidth;
        var rowY = new List<double>();
        foreach (var r in rows)
        {
            y += Clearance;
            rowY.Add(y);
            y += r.Max(d => d.Height) + Clearance + DuctWidth;
        }
        if (y > ph) return false;
        if (!apply) return true;

        doc.Rails.Clear();
        doc.Ducts.Clear();
        doc.Ducts.Add(new Duct { X = 0, Y = 0, Length = pw, Width = DuctWidth, Depth = DuctDepth });
        doc.Ducts.Add(new Duct { X = 0, Y = DuctWidth, Length = ph - DuctWidth, Vertical = true, Width = SideDuct, Depth = DuctDepth });
        doc.Ducts.Add(new Duct { X = pw - SideDuct, Y = DuctWidth, Length = ph - DuctWidth, Vertical = true, Width = SideDuct, Depth = DuctDepth });

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var top = rowY[i];
            var h = r.Max(d => d.Height);
            var railDevices = r.Where(d => d.OnRail).ToList();
            DinRail? rail = null;
            if (railDevices.Count > 0)
            {
                // El carril va a la altura del centro de los aparatos más altos de la fila.
                rail = new DinRail { X = SideDuct + 5, Y = top + h / 2 - DinRail.RailHeight / 2, Length = pw - 2 * SideDuct - 10 };
                doc.Rails.Add(rail);
            }
            var x = x0;
            PanelDevice? prev = null;
            foreach (var d in r)
            {
                if (prev is not null) x += GapBetween(prev, d);
                d.X = Math.Round(x, 1);
                d.Y = Math.Round(d.OnRail && rail is not null ? rail.CenterY - d.Height / 2 : top + (h - d.Height) / 2, 1);
                x += d.Width;
                prev = d;
            }
            var ductY = top + h + Clearance;
            doc.Ducts.Add(new Duct { X = SideDuct, Y = ductY, Length = pw - 2 * SideDuct, Width = DuctWidth, Depth = DuctDepth });
        }

        // Si sobra altura, la canaleta inferior baja al fondo de la placa (espacio libre para ampliar).
        return true;
    }

    private static string Group(string kind) => kind switch
    {
        "regleta" or "barra_pe" => "bornes",
        "variador" or "trafo_potencia" => "grandes",
        "plc" or "mcu" or "rele" or "temporizador" or "telerruptor" or "fuente" => "mando",
        _ => "potencia",
    };

    private static double GapBetween(PanelDevice a, PanelDevice b) =>
        a.Kind == "regleta" || b.Kind == "regleta" || !a.OnRail || !b.OnRail || a.Kind != b.Kind && (a.Width >= 45 || b.Width >= 45) ? BigGap : Gap;

    /// <summary>Mandos y pilotos en la puerta: filas centradas en la mitad superior, setas primero.</summary>
    public static void ArrangeDoor(PanelDocument doc)
    {
        var door = doc.OnDoor.OrderBy(d => d.Kind switch { "instrumento" => 0, "seta" => 1, "selector" => 2, "pulsador" => 3, "piloto" => 4, _ => 5 })
            .ThenBy(d => d.Tag, TagComparer.Instance).ToList();
        if (door.Count == 0) return;
        var w = doc.Enclosure.Width;
        const double pitch = 60;
        var perRow = Math.Max(1, (int)((w - 100) / pitch));
        var y = Math.Min(200, doc.Enclosure.Height * 0.2);
        for (var start = 0; start < door.Count; start += perRow)
        {
            var row = door.Skip(start).Take(perRow).ToList();
            var rowW = row.Count * pitch;
            var x = (w - rowW) / 2;
            var h = row.Max(d => d.Height);
            // Baja la fila hasta que lo que lleva detrás no choque con un aparato hondo de la placa.
            for (var tries = 0; tries < 200; tries++)
            {
                var xx = x;
                foreach (var d in row)
                {
                    d.X = Math.Round(xx + (pitch - d.Width) / 2, 1);
                    d.Y = Math.Round(y + (h - d.Height) / 2, 1);
                    xx += pitch;
                }
                if (!row.Any(d => ClashesBehind(doc, d)) || y + h > doc.Enclosure.Height - 80) break;
                y += 10;
            }
            y += h + 60;
        }
    }

    /// <summary>true si un mando de la puerta choca (por fondo) con el aparato de la placa que tiene detrás.</summary>
    public static bool ClashesBehind(PanelDocument doc, PanelDevice door)
    {
        var e = doc.Enclosure;
        return doc.OnPlate.Any(p => p.X + e.PlateMargin < door.X + door.Width && door.X < p.X + e.PlateMargin + p.Width
                                    && p.Y + e.PlateMargin < door.Y + door.Height && door.Y < p.Y + e.PlateMargin + p.Height
                                    && p.Depth + door.Depth > e.UsableDepth + 10);
    }

    /// <summary>
    /// Busca hueco libre en un carril para un aparato nuevo (de izquierda a derecha y de arriba abajo).
    /// Si no lo hay, lo deja a la derecha, fuera de la placa, para que el usuario lo coloque.
    /// </summary>
    public static void PlaceInFreeSpot(PanelDocument doc, PanelDevice d)
    {
        if (d.Location == DeviceLocation.Door)
        {
            var maxY = doc.OnDoor.Where(o => o != d).Select(o => o.Y + o.Height).DefaultIfEmpty(100).Max();
            d.X = doc.Enclosure.Width / 2 - d.Width / 2;
            d.Y = maxY + 30;
            return;
        }
        if (d.Location == DeviceLocation.Field) return;
        var others = doc.OnPlate.Where(o => o != d).ToList();
        if (d.OnRail)
        {
            foreach (var rail in doc.Rails.OrderBy(r => r.Y))
            {
                var y = rail.CenterY - d.Height / 2;
                for (var x = rail.X + 5; x + d.Width <= rail.X + rail.Length - 5; x += 1)
                {
                    d.X = x;
                    d.Y = y;
                    if (!others.Any(o => o.Overlaps(d)) && !doc.Ducts.Any(k => Intersects(k, d))) return;
                }
            }
        }
        else
        {
            var pw = doc.Enclosure.PlateWidth;
            var ph = doc.Enclosure.PlateHeight;
            for (var y = 0.0; y + d.Height <= ph; y += 5)
                for (var x = 0.0; x + d.Width <= pw; x += 5)
                {
                    d.X = x;
                    d.Y = y;
                    if (!others.Any(o => o.Overlaps(d)) && !doc.Ducts.Any(k => Intersects(k, d))) return;
                }
        }
        d.X = doc.Enclosure.PlateWidth + 40;
        d.Y = others.Where(o => o.X > doc.Enclosure.PlateWidth).Select(o => o.Y + o.Height + 10).DefaultIfEmpty(0).Max();
    }

    public static bool Intersects(Duct k, PanelDevice d) =>
        d.X < k.X + k.W - 0.01 && k.X < d.X + d.Width - 0.01 && d.Y < k.Y + k.H - 0.01 && k.Y < d.Y + d.Height - 0.01;
}
