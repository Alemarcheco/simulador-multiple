using System.Globalization;

namespace Simulador.Tableros.Model;

public enum CheckLevel { Info, Aviso, Error }

/// <summary>Resultado de una comprobación. <see cref="Tag"/> señala el aparato afectado (si lo hay).</summary>
public sealed record PanelCheck(CheckLevel Level, string Message, string? Tag = null);

/// <summary>Balance térmico simplificado de la envolvente.</summary>
public sealed record ThermalResult(double PowerW, double SurfaceM2, double RiseK, double InsideC, double FanNeededM3h);

/// <summary>
/// Comprobaciones del tablero: solapes, aparatos fuera de la placa o sin carril, fondo, llenado de canaletas y de
/// carriles, reserva para ampliaciones, coherencia con el esquema y temperatura interior.
/// </summary>
public static class PanelChecks
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Coeficiente de transmisión de una envolvente de chapa pintada (W/m²·K).</summary>
    public const double KSteel = 5.5;
    public const double KPolyester = 3.5;

    /// <summary>
    /// Calor y temperatura interior. Superficie eficaz según los factores de IEC 60890 (techo 1,4; laterales y
    /// frente 0,9; espalda 0 si va contra la pared), calor de los aparatos y de los conductores (≈ 5 % más).
    /// Con ventilador, ΔT = P / (k·A + V/3,1).
    /// </summary>
    public static ThermalResult Thermal(PanelDocument doc)
    {
        var e = doc.Enclosure;
        double w = e.Width / 1000, h = e.Height / 1000, d = e.Depth / 1000;
        var area = 1.4 * w * d + 0.9 * (w * h + 2 * h * d) + (e.AgainstWall ? 0 : 0.9 * w * h);
        var k = e.Kind == EnclosureKind.CajaPoliester ? KPolyester : KSteel;
        var p = doc.Devices.Where(x => x.Location != DeviceLocation.Field).Sum(x => x.HeatW) * 1.05;
        var rise = p / (k * area + e.FanFlow / 3.1);
        var allowed = e.MaxInsideC - e.AmbientC;
        var fan = allowed <= 0 ? double.PositiveInfinity : Math.Max(0, 3.1 * (p - k * area * allowed) / allowed);
        return new ThermalResult(p, area, rise, e.AmbientC + rise, fan);
    }

    public static List<PanelCheck> Run(PanelDocument doc, IReadOnlyList<WireRoute>? routes = null)
    {
        var list = new List<PanelCheck>();
        var e = doc.Enclosure;
        var plate = doc.OnPlate.ToList();
        string N(double v, string f = "0") => v.ToString(f, Es);
        foreach (var note in doc.ImportNotes) list.Add(new(CheckLevel.Info, "Plano PDF: " + note));

        foreach (var d in plate)
        {
            if (d.X < -0.5 || d.Y < -0.5 || d.X + d.Width > e.PlateWidth + 0.5 || d.Y + d.Height > e.PlateHeight + 0.5)
                list.Add(new(CheckLevel.Error, $"{d.Tag} está fuera de la placa de montaje.", d.Tag));
            if (d.Depth > e.UsableDepth)
                list.Add(new(CheckLevel.Error, $"{d.Tag} mide {N(d.Depth)} mm de fondo y la envolvente solo deja {N(e.UsableDepth)} mm: la puerta no cerrará.", d.Tag));
            if (d.OnRail && !doc.RailsOf(d.Rear).Any(r => Math.Abs(r.CenterY - (d.Y + d.Height / 2)) < 3 && d.X >= r.X - 0.5 && d.X + d.Width <= r.X + r.Length + 0.5))
                list.Add(new(CheckLevel.Aviso, $"{d.Tag} se monta en carril DIN y no está centrado sobre ninguno.", d.Tag));
            foreach (var k in doc.DuctsOf(d.Rear).Where(k => AutoLayout.Intersects(k, d)))
                list.Add(new(CheckLevel.Error, $"{d.Tag} tapa una canaleta.", d.Tag));
        }
        for (var i = 0; i < plate.Count; i++)
            for (var j = i + 1; j < plate.Count; j++)
                if (plate[i].Overlaps(plate[j]))
                    list.Add(new(CheckLevel.Error, $"{plate[i].Tag} y {plate[j].Tag} se solapan.", plate[i].Tag));

        var door = doc.OnDoor.ToList();
        for (var i = 0; i < door.Count; i++)
        {
            var d = door[i];
            if (d.X < 20 || d.Y < 20 || d.X + d.Width > e.Width - 20 || d.Y + d.Height > e.Height - 20)
                list.Add(new(CheckLevel.Error, $"{d.Tag} queda fuera de la puerta o demasiado cerca del borde.", d.Tag));
            for (var j = i + 1; j < door.Count; j++)
            {
                var o = door[j];
                // Pulsadores de Ø22: al menos 30 mm entre centros más el anillo; aquí, que no se toquen los recuadros.
                if (d.Overlaps(o)) list.Add(new(CheckLevel.Error, $"{d.Tag} y {o.Tag} se tocan en la puerta.", d.Tag));
            }
            // Lo que lleva detrás no debe chocar con lo de la placa (fondo del mando + fondo del aparato de la placa).
            var behind = plate.Where(p => p.Rear == d.Rear && p.X + e.PlateMargin < d.X + d.Width && d.X < p.X + e.PlateMargin + p.Width
                                          && p.Y + e.PlateMargin < d.Y + d.Height && d.Y < p.Y + e.PlateMargin + p.Height)
                .OrderByDescending(p => p.Depth).FirstOrDefault();
            if (behind is not null && behind.Depth + d.Depth > e.UsableDepth + 10)
                list.Add(new(CheckLevel.Aviso, $"Detrás de {d.Tag} está {behind.Tag}: juntos miden {N(behind.Depth + d.Depth)} mm de fondo y hay {N(e.UsableDepth + 10)} mm.", d.Tag));
        }

        // Carriles: ocupación y reserva.
        foreach (var r in doc.Rails)
        {
            var used = plate.Where(d => d.OnRail && d.Rear == r.Rear && Math.Abs(r.CenterY - (d.Y + d.Height / 2)) < 3 && d.X < r.X + r.Length && d.X + d.Width > r.X).Sum(d => d.Width);
            var pct = used / Math.Max(1, r.Length);
            if (pct > 1) list.Add(new(CheckLevel.Error, $"El carril a {N(r.Y)} mm{(r.Rear ? " de la placa trasera" : "")} tiene más aparatos ({N(used)} mm) de los que caben ({N(r.Length)} mm)."));
        }
        var railTotal = doc.Rails.Sum(r => r.Length);
        var railUsed = plate.Where(d => d.OnRail).Sum(d => d.Width);
        if (railTotal > 0)
        {
            var free = 1 - railUsed / railTotal;
            list.Add(new(free < 0.2 ? CheckLevel.Aviso : CheckLevel.Info,
                free < 0.2 ? $"Solo queda un {N(free * 100)} % de carril libre: se recomienda dejar un 20 % de reserva para ampliaciones."
                           : $"Carril libre para ampliaciones: {N(free * 100)} % ({N(railTotal - railUsed)} mm)."));
        }

        // Canaletas.
        if (routes is not null && doc.Ducts.Count > 0)
            foreach (var (k, f) in DuctRouter.Fill(doc, routes))
            {
                if (f > 0.75) list.Add(new(CheckLevel.Error, $"Canaleta {(k.Vertical ? "vertical" : "horizontal")} {k.SizeText} a ({N(k.X)}, {N(k.Y)}) llena al {N(f * 100)} %: usa una más grande."));
                else if (f > 0.5) list.Add(new(CheckLevel.Aviso, $"Canaleta {(k.Vertical ? "vertical" : "horizontal")} {k.SizeText} a ({N(k.X)}, {N(k.Y)}) llena al {N(f * 100)} % (se recomienda no pasar del 50 %)."));
            }
        if (doc.Ducts.Count == 0 && doc.Wires.Count > 0)
            list.Add(new(CheckLevel.Aviso, "No hay canaletas: las longitudes de los hilos se estiman en línea recta por los ejes."));

        // Coherencia con el esquema.
        if (doc.Schematic is not null)
        {
            var analysis = SchematicImport.Analyze(doc.Schematic);
            foreach (var d in analysis.Devices.Where(d => doc.FindTag(d.Tag) is null))
                list.Add(new(CheckLevel.Error, $"{d.Tag} está en el esquema y falta en el tablero. Usa «Actualizar desde el esquema».", d.Tag));
            foreach (var d in doc.Devices.Where(d => d.Location != DeviceLocation.Field && d.Kind is not ("barra_pe") && analysis.Devices.All(a => !a.Tag.Equals(d.Tag, StringComparison.OrdinalIgnoreCase))))
                list.Add(new(CheckLevel.Info, $"{d.Tag} no está en el esquema (añadido a mano).", d.Tag));
        }

        // Bornes con más de dos hilos.
        foreach (var g in doc.Wires.SelectMany(w => new[] { w.From, w.To }).GroupBy(x => x).Where(g => g.Count() > 2))
            list.Add(new(CheckLevel.Aviso, $"Al borne {g.Key} llegan {g.Count()} hilos (máximo recomendado: 2).", g.Key.Tag));

        // Temperatura.
        var th = Thermal(doc);
        if (th.InsideC > e.MaxInsideC)
            list.Add(new(CheckLevel.Aviso,
                $"Dentro se llegaría a unos {N(th.InsideC)} °C (máximo {N(e.MaxInsideC)} °C) con {N(th.PowerW)} W de pérdidas. " +
                (double.IsFinite(th.FanNeededM3h) ? $"Hace falta un ventilador con filtro de al menos {N(Math.Ceiling(th.FanNeededM3h / 10) * 10)} m³/h o una envolvente mayor." : "La temperatura ambiente ya supera el máximo.")));
        else
            list.Add(new(CheckLevel.Info, $"Temperatura interior estimada: {N(th.InsideC)} °C ({N(th.PowerW)} W disipados, {N(th.SurfaceM2, "0.00")} m² eficaces)."));

        return list.OrderByDescending(c => c.Level).ToList();
    }
}
