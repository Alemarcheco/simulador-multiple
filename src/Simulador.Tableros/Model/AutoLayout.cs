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
        // Armarios de varios cuerpos (para planos grandes).
        (1600, 2000, 500), (2000, 2000, 600), (2400, 2000, 600), (3200, 2000, 600),
    ];

    public const double DuctWidth = 40, DuctDepth = 60, SideDuct = 40;
    /// <summary>Holgura entre una canaleta y los aparatos (mm).</summary>
    public const double Clearance = 25;
    public const double Gap = 0; // los aparatos modulares van juntos
    public const double BigGap = 10;

    /// <summary>Armarios de pie de doble frente (dos placas espalda con espalda): ancho × alto × fondo total.</summary>
    public static readonly (double W, double H, double D)[] DoubleFrontSizes =
    [
        (600, 1800, 600), (800, 1800, 600), (800, 2000, 600), (1000, 2000, 600), (1200, 2000, 800), (1600, 2000, 800),
        (2000, 2000, 800), (2400, 2000, 800), (3200, 2000, 800),
    ];

    /// <summary>Coloca todo y ajusta la envolvente. Devuelve false si ni el armario más grande alcanza.</summary>
    public static bool Arrange(PanelDocument doc, bool chooseEnclosure = true)
    {
        var plate = doc.OnPlate.OrderBy(d => SchematicImport.Order(d.Kind)).ThenBy(d => d.Tag, TagComparer.Instance).ToList();
        var maxDepth = plate.Select(d => d.Depth).DefaultIfEmpty(0).Max();
        var e = doc.Enclosure;
        var fits = false;
        var front = plate;
        List<PanelDevice> rear = [];
        if (chooseEnclosure)
        {
            foreach (var (w, h, dp) in e.DoubleFront ? DoubleFrontSizes : StandardSizes)
            {
                (e.Width, e.Height, e.Depth) = (w, h, dp);
                if (e.UsableDepth < maxDepth + 10) continue;
                e.Kind = h >= 1800 ? EnclosureKind.ArmarioDePie : EnclosureKind.MuralMetalico;
                e.AgainstWall = h < 1800 && !e.DoubleFront;
                (front, rear) = Split(doc, plate);
                if (TryRows(doc, front, false, apply: false) && TryRows(doc, rear, true, apply: false) && DoorFits(doc)) { fits = true; break; }
            }
            if (!fits)
            {
                var (w, h, dp) = (e.DoubleFront ? DoubleFrontSizes : StandardSizes)[^1];
                (e.Width, e.Height, e.Depth) = (w, h, dp);
                (front, rear) = Split(doc, plate);
            }
        }
        else (front, rear) = Split(doc, plate);
        // Sin doble frente no queda nada detrás.
        if (!e.DoubleFront)
        {
            foreach (var d in doc.Devices) d.Rear = false;
            doc.Rails.RemoveAll(r => r.Rear);
            doc.Ducts.RemoveAll(k => k.Rear);
        }
        fits = TryRows(doc, front, false, apply: true) & (rear.Count == 0 || TryRows(doc, rear, true, apply: true)) | fits;
        if (e.DoubleFront && rear.Count == 0)
        {
            doc.Rails.RemoveAll(r => r.Rear);
            doc.Ducts.RemoveAll(k => k.Rear);
        }
        ArrangeDoor(doc);
        return fits;
    }

    /// <summary>
    /// Doble frente: llena la placa delantera por orden (de la alimentación a los receptores: protecciones y
    /// potencia delante) y lo que no cabe va a la trasera (mando, autómatas, regletas). Si todo cabe delante, se
    /// reparte por mitades para dejar reserva en las dos caras.
    /// </summary>
    private static (List<PanelDevice> Front, List<PanelDevice> Rear) Split(PanelDocument doc, List<PanelDevice> plate)
    {
        if (!doc.Enclosure.DoubleFront || plate.Count < 2) return (plate, []);
        var k = plate.Count;
        while (k > 0 && !TryRows(doc, plate.Take(k).ToList(), false, apply: false)) k--;
        // Reparto equilibrado: como mucho la mitad de la anchura total delante, sin partir un grupo si se puede.
        var total = plate.Sum(d => d.Width);
        var half = 0;
        double acc = 0;
        while (half < k && acc + plate[half].Width <= total / 2 + 1) acc += plate[half++].Width;
        var cut = Math.Min(k, Math.Max(1, half));
        // Si el corte cae dentro de un grupo, lo lleva al principio del grupo siguiente.
        var grouped = cut;
        while (grouped < k && grouped > 0 && Group(plate[grouped].Kind) == Group(plate[grouped - 1].Kind)) grouped++;
        bool Fits(int c) => TryRows(doc, plate.Take(c).ToList(), false, apply: false) && TryRows(doc, plate.Skip(c).ToList(), true, apply: false);
        if (Fits(grouped)) cut = grouped;
        else
        {
            // Si no, el corte más cercano a la mitad con el que caben las dos caras (primero en cambios de grupo).
            var candidates = Enumerable.Range(1, k).OrderBy(c => c < k && Group(plate[c].Kind) == Group(plate[c - 1].Kind) ? 1 : 0).ThenBy(c => Math.Abs(c - cut));
            var found = candidates.Cast<int?>().FirstOrDefault(c => Fits(c!.Value));
            cut = found ?? grouped;
        }
        return (plate.Take(cut).ToList(), plate.Skip(cut).ToList());
    }

    private static bool DoorFits(PanelDocument doc)
    {
        var door = doc.OnDoor.ToList();
        if (door.Count == 0) return true;
        // Filas de puerta (de Ø22 y de controladores grandes) en el 60 % de arriba de la altura.
        var rows = DoorRows(door.OrderBy(d => d.Kind == "controlador" ? 0 : 1).ToList(), doc.Enclosure.Width);
        return rows.All(r => r.Sum(Pitch) <= doc.Enclosure.Width - 100 + 0.01)
               && 80 + rows.Sum(r => r.Max(d => Math.Max(30, d.Height)) + 60) < doc.Enclosure.Height * 0.7;
    }

    /// <summary>Reparte los aparatos de la placa en filas. Con <paramref name="apply"/> = false solo comprueba si caben.</summary>
    private static bool TryRows(PanelDocument doc, List<PanelDevice> devices, bool rear, bool apply)
    {
        if (devices.Count == 0 && !apply) return true;
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
            if (d.Width > usable && !apply) return false;
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
        // Si no cabe, con apply se coloca igual (lo que sobra queda por debajo de la placa y lo marcan las comprobaciones).
        var ok = y <= ph && devices.All(d => d.Width <= usable);
        if (!apply) return ok;

        doc.Rails.RemoveAll(r => r.Rear == rear);
        doc.Ducts.RemoveAll(k => k.Rear == rear);
        foreach (var d in devices) d.Rear = rear;
        doc.Ducts.Add(new Duct { X = 0, Y = 0, Length = pw, Width = DuctWidth, Depth = DuctDepth, Rear = rear });
        doc.Ducts.Add(new Duct { X = 0, Y = DuctWidth, Length = ph - DuctWidth, Vertical = true, Width = SideDuct, Depth = DuctDepth, Rear = rear });
        doc.Ducts.Add(new Duct { X = pw - SideDuct, Y = DuctWidth, Length = ph - DuctWidth, Vertical = true, Width = SideDuct, Depth = DuctDepth, Rear = rear });

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
                rail = new DinRail { X = SideDuct + 5, Y = top + h / 2 - DinRail.RailHeight / 2, Length = pw - 2 * SideDuct - 10, Rear = rear };
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
            doc.Ducts.Add(new Duct { X = SideDuct, Y = ductY, Length = pw - 2 * SideDuct, Width = DuctWidth, Depth = DuctDepth, Rear = rear });
        }

        return ok;
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
        foreach (var d in doc.OnDoor) d.Rear = false;
        var door = doc.OnDoor.OrderBy(d => d.Kind switch { "controlador" => -1, "instrumento" => 0, "seta" => 1, "selector" => 2, "pulsador" => 3, "piloto" => 4, _ => 5 })
            .ThenBy(d => d.Tag, TagComparer.Instance).ToList();
        if (door.Count == 0) return;
        var w = doc.Enclosure.Width;
        var y = Math.Min(200, doc.Enclosure.Height * 0.2);
        foreach (var row in DoorRows(door, w))
        {
            var rowW = row.Sum(Pitch);
            var x = (w - rowW) / 2;
            var h = row.Max(d => d.Height);
            // Baja la fila hasta que lo que lleva detrás no choque con un aparato hondo de la placa.
            for (var tries = 0; tries < 200; tries++)
            {
                var xx = x;
                foreach (var d in row)
                {
                    d.X = Math.Round(xx + (Pitch(d) - d.Width) / 2, 1);
                    d.Y = Math.Round(y + (h - d.Height) / 2, 1);
                    xx += Pitch(d);
                }
                if (!row.Any(d => ClashesBehind(doc, d)) || y + h > doc.Enclosure.Height - 80) break;
                y += 10;
            }
            y += h + 60;
        }
    }

    /// <summary>Paso de un aparato de puerta: 60 mm para los de Ø22, su ancho y 30 mm para los grandes (controladores).</summary>
    private static double Pitch(PanelDevice d) => Math.Max(60, d.Width + 30);

    private static List<List<PanelDevice>> DoorRows(List<PanelDevice> door, double width)
    {
        var rows = new List<List<PanelDevice>>();
        List<PanelDevice>? row = null;
        double used = 0;
        foreach (var d in door)
        {
            if (row is null || used + Pitch(d) > width - 100)
            {
                rows.Add(row = []);
                used = 0;
            }
            row.Add(d);
            used += Pitch(d);
        }
        return rows;
    }

    /// <summary>true si un mando de la puerta choca (por fondo) con el aparato de la placa que tiene detrás.</summary>
    public static bool ClashesBehind(PanelDocument doc, PanelDevice door)
    {
        var e = doc.Enclosure;
        return doc.PlateOf(door.Rear).Any(p => p.X + e.PlateMargin < door.X + door.Width && door.X < p.X + e.PlateMargin + p.Width
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
            var maxY = doc.DoorOf(d.Rear).Where(o => o != d).Select(o => o.Y + o.Height).DefaultIfEmpty(100).Max();
            d.X = doc.Enclosure.Width / 2 - d.Width / 2;
            d.Y = maxY + 30;
            return;
        }
        if (d.Location == DeviceLocation.Field) return;
        var others = doc.PlateOf(d.Rear).Where(o => o != d).ToList();
        var ducts = doc.DuctsOf(d.Rear).ToList();
        if (d.OnRail)
        {
            foreach (var rail in doc.RailsOf(d.Rear).OrderBy(r => r.Y))
            {
                var y = rail.CenterY - d.Height / 2;
                for (var x = rail.X + 5; x + d.Width <= rail.X + rail.Length - 5; x += 1)
                {
                    d.X = x;
                    d.Y = y;
                    if (!others.Any(o => o.Overlaps(d)) && !ducts.Any(k => Intersects(k, d))) return;
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
                    if (!others.Any(o => o.Overlaps(d)) && !ducts.Any(k => Intersects(k, d))) return;
                }
        }
        d.X = doc.Enclosure.PlateWidth + 40;
        d.Y = others.Where(o => o.X > doc.Enclosure.PlateWidth).Select(o => o.Y + o.Height + 10).DefaultIfEmpty(0).Max();
    }

    public static bool Intersects(Duct k, PanelDevice d) =>
        d.X < k.X + k.W - 0.01 && k.X < d.X + d.Width - 0.01 && d.Y < k.Y + k.H - 0.01 && k.Y < d.Y + d.Height - 0.01;
}
