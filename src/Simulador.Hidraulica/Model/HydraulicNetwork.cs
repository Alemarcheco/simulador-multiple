namespace Simulador.Hidraulica.Model;

/// <summary>Diseño completo de un proceso hidráulico: nudos, enlaces, instrumentos y fluido.</summary>
public sealed class HydraulicNetwork
{
    public string Name { get; set; } = "Proceso hidráulico";
    public Fluid Fluid { get; set; } = Fluid.Water;
    public List<HydraulicNode> Nodes { get; set; } = [];
    public List<HydraulicLink> Links { get; set; } = [];
    public List<Sensor> Sensors { get; set; } = [];

    public HydraulicNode? FindNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);
    public HydraulicLink? FindLink(string id) => Links.FirstOrDefault(l => l.Id == id);

    public T Add<T>(T node) where T : HydraulicNode { Nodes.Add(node); return node; }
    public T Connect<T>(T link, string from, string to) where T : HydraulicLink
    {
        link.From = from;
        link.To = to;
        Links.Add(link);
        return link;
    }

    /// <summary>Primer identificador libre con el prefijo dado ("N" → N1, N2…).</summary>
    public string NextId(string prefix)
    {
        var used = Nodes.Select(n => n.Id).Concat(Links.Select(l => l.Id)).Concat(Sensors.Select(s => s.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
            if (!used.Contains(prefix + i)) return prefix + i;
    }

    /// <summary>Quita un nudo y todo lo que cuelga de él.</summary>
    public void RemoveNode(string id)
    {
        Nodes.RemoveAll(n => n.Id == id);
        foreach (var l in Links.Where(l => l.From == id || l.To == id).ToList())
            RemoveLink(l.Id);
        Sensors.RemoveAll(s => s.Target == id);
    }

    /// <summary>Cambia el identificador de un elemento y actualiza todo lo que lo referencia.</summary>
    public bool Rename(string oldId, string newId)
    {
        if (string.IsNullOrWhiteSpace(newId) || oldId == newId) return false;
        if (FindNode(newId) is not null || FindLink(newId) is not null || Sensors.Any(s => s.Id == newId)) return false;
        if (FindNode(oldId) is { } n)
        {
            n.Id = newId;
            foreach (var l in Links) { if (l.From == oldId) l.From = newId; if (l.To == oldId) l.To = newId; }
        }
        else if (FindLink(oldId) is { } l) l.Id = newId;
        else if (Sensors.FirstOrDefault(s => s.Id == oldId) is { } s) { s.Id = newId; return true; }
        else return false;
        foreach (var s in Sensors.Where(s => s.Target == oldId)) s.Target = newId;
        return true;
    }

    public void RemoveLink(string id)
    {
        Links.RemoveAll(l => l.Id == id);
        Sensors.RemoveAll(s => s.Target == id);
    }

    /// <summary>Errores que impiden simular y avisos de diseño.</summary>
    public ValidationReport Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Nodes.Select(n => n.Id).Concat(Links.Select(l => l.Id)).Concat(Sensors.Select(s => s.Id)))
            if (string.IsNullOrWhiteSpace(id)) errors.Add("Hay un elemento sin identificador.");
            else if (!ids.Add(id)) errors.Add($"El identificador '{id}' está repetido.");

        if (!Nodes.Any(n => n is Reservoir or Tank))
            errors.Add("Hace falta al menos un depósito o tanque que fije el nivel de referencia.");

        foreach (var l in Links)
        {
            if (FindNode(l.From) is null || FindNode(l.To) is null)
                errors.Add($"{l.Id}: los dos extremos deben estar conectados a un nudo, tanque o depósito.");
            else if (l.From == l.To)
                errors.Add($"{l.Id}: entrada y salida en el mismo nudo.");
            switch (l)
            {
                case Pipe p when p.Length <= 0 || p.Diameter <= 0:
                    errors.Add($"{p.Id}: longitud y diámetro deben ser mayores que cero."); break;
                case Valve v when v.Diameter <= 0:
                    errors.Add($"{v.Id}: el diámetro debe ser mayor que cero."); break;
                case Pump b when b.ShutoffHead <= 0 || b.RatedFlow <= 0 || b.RatedHead >= b.ShutoffHead:
                    errors.Add($"{b.Id}: la curva necesita altura a caudal cero > altura nominal > 0 y caudal nominal > 0."); break;
            }
        }

        foreach (var t in Nodes.OfType<Tank>())
            if (t.Diameter <= 0 || t.MaxLevel <= 0)
                errors.Add($"{t.Id}: diámetro y altura máxima deben ser mayores que cero.");
            else if (t.InitialLevel < 0 || t.InitialLevel > t.MaxLevel)
                warnings.Add($"{t.Id}: el nivel inicial está fuera de 0–{t.MaxLevel} m.");

        foreach (var j in Nodes.OfType<Junction>())
            if (!Links.Any(l => l.From == j.Id || l.To == j.Id))
                warnings.Add($"{j.Id}: nudo sin conectar.");

        foreach (var s in Sensors)
        {
            bool ok = s.Kind switch
            {
                SensorKind.Flow => FindLink(s.Target) is not null,
                SensorKind.Level => FindNode(s.Target) is Tank,
                _ => FindNode(s.Target) is not null,
            };
            if (!ok) errors.Add($"{s.Id}: no está colocado sobre un elemento válido.");
        }

        return new ValidationReport(errors, warnings);
    }
}

public sealed record ValidationReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}
