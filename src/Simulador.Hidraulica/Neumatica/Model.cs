using System.Text.Json.Serialization;

namespace Simulador.Hidraulica.Neumatica;

/// <summary>Toma de un componente: nombre ISO 5599 ("1", "2", "4", "14"…) y posición respecto al centro, en píxeles del esquema.</summary>
public readonly record struct PortDef(string Name, double Dx, double Dy);

/// <summary>Componente neumático colocado en el esquema.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(AirSupply), "fuente")]
[JsonDerivedType(typeof(Compressor), "compresor")]
[JsonDerivedType(typeof(AirReceiver), "deposito")]
[JsonDerivedType(typeof(DirectionalValve), "distribuidor")]
[JsonDerivedType(typeof(FlowControl), "regulador")]
[JsonDerivedType(typeof(CheckValve), "antirretorno")]
[JsonDerivedType(typeof(LogicValve), "logica")]
[JsonDerivedType(typeof(Cylinder), "cilindro")]
[JsonDerivedType(typeof(Gauge), "manometro")]
public abstract class PneumaticComponent
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Giro del símbolo en grados (0, 90, 180, 270).</summary>
    public int Rotation { get; set; }

    [JsonIgnore] public abstract string TypeName { get; }
    /// <summary>Tomas sin girar.</summary>
    [JsonIgnore] public abstract IReadOnlyList<PortDef> Ports { get; }

    /// <summary>Posición absoluta de una toma en el esquema (con el giro aplicado).</summary>
    public (double X, double Y) PortPosition(string port)
    {
        var p = Ports.First(q => q.Name == port);
        var (dx, dy) = Rotate(p.Dx, p.Dy, Rotation);
        return (X + dx, Y + dy);
    }

    public bool HasPort(string port) => Ports.Any(p => p.Name == port);

    public static (double, double) Rotate(double dx, double dy, int degrees) => (((degrees % 360) + 360) % 360) switch
    {
        90 => (-dy, dx),
        180 => (-dx, -dy),
        270 => (dy, -dx),
        _ => (dx, dy),
    };
}

/// <summary>Toma de aire comprimido ideal (red de aire con regulador): presión constante.</summary>
public sealed class AirSupply : PneumaticComponent
{
    /// <summary>Presión manométrica (Pa).</summary>
    public double Pressure { get; set; } = 6e5;
    public override string TypeName => "Fuente de aire comprimido";
    public override IReadOnlyList<PortDef> Ports { get; } = [new("1", 0, -20)];
}

/// <summary>Compresor con presostato: da menos caudal cuanto más presión hay y para entre dos presiones.</summary>
public sealed class Compressor : PneumaticComponent
{
    /// <summary>Caudal libre a presión cero, en m³/s de aire normal.</summary>
    public double FreeAirDelivery { get; set; } = 200.0 / 60_000;
    /// <summary>Presión máxima que puede alcanzar el compresor (caudal cero), Pa manométricos.</summary>
    public double MaxPressure { get; set; } = 10e5;
    public double CutInPressure { get; set; } = 6e5;
    public double CutOutPressure { get; set; } = 8e5;
    public override string TypeName => "Compresor";
    public override IReadOnlyList<PortDef> Ports { get; } = [new("1", 0, -22)];
}

/// <summary>Depósito de aire (calderín): añade volumen al punto donde se conecta.</summary>
public sealed class AirReceiver : PneumaticComponent
{
    /// <summary>Volumen interior (m³).</summary>
    public double Volume { get; set; } = 0.02;
    /// <summary>Presión inicial (Pa manométricos).</summary>
    public double InitialPressure { get; set; }
    public override string TypeName => "Depósito de aire";
    public override IReadOnlyList<PortDef> Ports { get; } = [new("1", 0, -40)];
}

public enum ValveWays { ThreeTwo, FiveTwo }

public enum ValveActuation
{
    /// <summary>Pulsador con retorno por muelle.</summary>
    PushButton,
    /// <summary>Palanca con enclavamiento.</summary>
    Lever,
    /// <summary>Bobina con retorno por muelle.</summary>
    Solenoid,
    /// <summary>Dos bobinas (biestable, con memoria).</summary>
    DoubleSolenoid,
    /// <summary>Rodillo accionado por la leva del vástago de un cilindro (final de carrera).</summary>
    Roller,
    /// <summary>Pilotaje neumático por la toma 14, con retorno por muelle.</summary>
    Pilot,
    /// <summary>Pilotaje por 14 y 12 (biestable, con memoria).</summary>
    DoublePilot,
}

/// <summary>Válvula distribuidora 3/2 o 5/2.</summary>
public sealed class DirectionalValve : PneumaticComponent
{
    public ValveWays Ways { get; set; } = ValveWays.FiveTwo;
    public ValveActuation Actuation { get; set; } = ValveActuation.PushButton;
    /// <summary>Solo 3/2: en reposo da paso de 1 a 2 (normalmente abierta).</summary>
    public bool NormallyOpen { get; set; }
    /// <summary>Caudal nominal C (m³/(s·Pa)), ISO 6358.</summary>
    public double Conductance { get; set; } = 1.0e-8;
    /// <summary>Relación crítica de presiones b (ISO 6358).</summary>
    public double CriticalRatio { get; set; } = 0.3;
    /// <summary>Presión mínima de pilotaje (Pa manométricos).</summary>
    public double PilotPressure { get; set; } = 1.5e5;
    /// <summary>Bobina 14 (y bobina 12 en las de doble bobina), nombre para el PLC: "Y1", "Y2"…</summary>
    public string Coil14 { get; set; } = "";
    public string Coil12 { get; set; } = "";
    /// <summary>Rodillo: cilindro cuya leva lo acciona y posición de la leva (0 = recogido, 1 = fuera).</summary>
    public string RollerCylinder { get; set; } = "";
    public double RollerPosition { get; set; } = 1;
    /// <summary>Posición inicial en las biestables (true = accionada, paso 1→4 o 1→2).</summary>
    public bool InitiallyActuated { get; set; }

    [JsonIgnore] public bool IsBistable => Actuation is ValveActuation.Lever or ValveActuation.DoubleSolenoid or ValveActuation.DoublePilot;
    [JsonIgnore] public bool HasSpring => !IsBistable;

    public override string TypeName => $"Válvula {(Ways == ValveWays.ThreeTwo ? "3/2" : "5/2")} {ActuationName(Actuation)}";

    public static string ActuationName(ValveActuation a) => a switch
    {
        ValveActuation.PushButton => "de pulsador",
        ValveActuation.Lever => "de palanca",
        ValveActuation.Solenoid => "electroválvula",
        ValveActuation.DoubleSolenoid => "de doble bobina",
        ValveActuation.Roller => "de rodillo",
        ValveActuation.Pilot => "pilotada",
        ValveActuation.DoublePilot => "biestable pilotada",
        _ => "",
    };

    [JsonIgnore]
    public override IReadOnlyList<PortDef> Ports
    {
        get
        {
            var list = Ways == ValveWays.ThreeTwo
                ? new List<PortDef> { new("2", 20, -20), new("1", 12, 20) }
                : new List<PortDef> { new("4", 10, -20), new("2", 30, -20), new("1", 20, 20) };
            if (Actuation is ValveActuation.Pilot or ValveActuation.DoublePilot) list.Add(new("14", -60, 0));
            if (Actuation is ValveActuation.DoublePilot) list.Add(new("12", 60, 0));
            return list;
        }
    }
}

/// <summary>Regulador de caudal (estrangulador), con o sin antirretorno en paralelo.</summary>
public sealed class FlowControl : PneumaticComponent
{
    public double MaxConductance { get; set; } = 0.6e-8;
    /// <summary>Apertura del tornillo, de 0 a 1.</summary>
    public double Opening { get; set; } = 0.3;
    /// <summary>Antirretorno en paralelo: estrangula de 1 a 2 y deja paso libre de 2 a 1.</summary>
    public bool OneWay { get; set; } = true;
    public override string TypeName => OneWay ? "Regulador de caudal unidireccional" : "Estrangulador";
    public override IReadOnlyList<PortDef> Ports { get; } = [new("1", 0, 30), new("2", 0, -30)];
}

/// <summary>Válvula antirretorno: solo deja pasar de 1 a 2.</summary>
public sealed class CheckValve : PneumaticComponent
{
    public double Conductance { get; set; } = 1.0e-8;
    public override string TypeName => "Válvula antirretorno";
    public override IReadOnlyList<PortDef> Ports { get; } = [new("1", 0, 24), new("2", 0, -24)];
}

public enum LogicKind
{
    /// <summary>Selectora de circuito (función O): sale el aire de la entrada con más presión.</summary>
    Or,
    /// <summary>Válvula de simultaneidad (función Y): solo sale aire si llega por las dos entradas.</summary>
    And,
}

/// <summary>Válvula selectora (O) o de simultaneidad (Y).</summary>
public sealed class LogicValve : PneumaticComponent
{
    public LogicKind Kind { get; set; } = LogicKind.Or;
    public double Conductance { get; set; } = 0.8e-8;
    public override string TypeName => Kind == LogicKind.Or ? "Válvula selectora (O)" : "Válvula de simultaneidad (Y)";
    public override IReadOnlyList<PortDef> Ports { get; } = [new("1a", -30, 0), new("1b", 30, 0), new("2", 0, -20)];
}

public enum CylinderKind { SingleActing, DoubleActing }

/// <summary>Cilindro neumático. La toma A empuja el émbolo hacia fuera; la B (doble efecto) lo recoge.</summary>
public sealed class Cylinder : PneumaticComponent
{
    public CylinderKind Kind { get; set; } = CylinderKind.DoubleActing;
    public double Bore { get; set; } = 0.032;
    public double RodDiameter { get; set; } = 0.012;
    public double Stroke { get; set; } = 0.2;
    /// <summary>Masa móvil (émbolo, vástago y carga), kg.</summary>
    public double Mass { get; set; } = 2;
    /// <summary>Fuerza de la carga que se opone a la salida (N); negativa si ayuda.</summary>
    public double Load { get; set; }
    /// <summary>Rozamiento seco (N) y viscoso (N·s/m).</summary>
    public double Friction { get; set; } = 15;
    public double ViscousFriction { get; set; } = 60;
    /// <summary>Simple efecto: fuerza del muelle recogido (N) y rigidez (N/m).</summary>
    public double SpringPreload { get; set; } = 25;
    public double SpringRate { get; set; } = 150;
    /// <summary>Posición inicial, de 0 (recogido) a 1 (fuera).</summary>
    public double InitialPosition { get; set; }
    /// <summary>Nombres de los detectores magnéticos de final de carrera (vacío = sin detector): "1S1", "1S2"…</summary>
    public string SensorRetracted { get; set; } = "";
    public string SensorExtended { get; set; } = "";

    [JsonIgnore] public double PistonArea => Math.PI * Bore * Bore / 4;
    [JsonIgnore] public double AnnulusArea => PistonArea - Math.PI * RodDiameter * RodDiameter / 4;
    /// <summary>Volumen muerto de cada cámara (m³): el que queda con el émbolo en el tope.</summary>
    [JsonIgnore] public double DeadVolume => PistonArea * 0.006;

    public override string TypeName => Kind == CylinderKind.SingleActing ? "Cilindro de simple efecto" : "Cilindro de doble efecto";

    /// <summary>Largo del cuerpo dibujado (px); el vástago sale por la derecha.</summary>
    public const double BodyLength = 140, BodyHeight = 40, RodTravel = 100;

    [JsonIgnore]
    public override IReadOnlyList<PortDef> Ports => Kind == CylinderKind.SingleActing
        ? [new("A", -50, 20)]
        : [new("A", -50, 20), new("B", 50, 20)];
}

/// <summary>Manómetro.</summary>
public sealed class Gauge : PneumaticComponent
{
    public override string TypeName => "Manómetro";
    public override IReadOnlyList<PortDef> Ports { get; } = [new("1", 0, 22)];
}

/// <summary>Tubo flexible entre dos tomas. Varias mangueras en una misma toma forman una derivación.</summary>
public sealed class Tube
{
    public string Id { get; set; } = "";
    public string FromComponent { get; set; } = "";
    public string FromPort { get; set; } = "";
    public string ToComponent { get; set; } = "";
    public string ToPort { get; set; } = "";
    /// <summary>Puntos intermedios del trazado en el esquema.</summary>
    public List<PortPoint> Points { get; set; } = [];
    public double InnerDiameter { get; set; } = 0.004;
    public double Length { get; set; } = 0.5;

    [JsonIgnore] public double Volume => Math.PI * InnerDiameter * InnerDiameter / 4 * Length;
}

public readonly record struct PortPoint(double X, double Y);

/// <summary>Esquema neumático completo.</summary>
public sealed class PneumaticCircuit
{
    public string Name { get; set; } = "Circuito neumático";
    public string Description { get; set; } = "";
    public List<PneumaticComponent> Components { get; set; } = [];
    public List<Tube> Tubes { get; set; } = [];

    public PneumaticComponent? Find(string id) => Components.FirstOrDefault(c => c.Id == id);
    public T Get<T>(string id) where T : PneumaticComponent =>
        Find(id) as T ?? throw new KeyNotFoundException($"No hay ningún {typeof(T).Name} '{id}'.");

    public T Add<T>(T c) where T : PneumaticComponent { Components.Add(c); return c; }

    public Tube Connect(string from, string fromPort, string to, string toPort, double length = 0.5, params PortPoint[] points)
    {
        var t = new Tube { Id = NextId("M"), FromComponent = from, FromPort = fromPort, ToComponent = to, ToPort = toPort, Length = length, Points = [.. points] };
        Tubes.Add(t);
        return t;
    }

    public string NextId(string prefix)
    {
        var used = Components.Select(c => c.Id).Concat(Tubes.Select(t => t.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
            if (!used.Contains(prefix + i)) return prefix + i;
    }

    public void Remove(string id)
    {
        Components.RemoveAll(c => c.Id == id);
        Tubes.RemoveAll(t => t.Id == id || t.FromComponent == id || t.ToComponent == id);
    }

    public bool Rename(string oldId, string newId)
    {
        if (string.IsNullOrWhiteSpace(newId) || oldId == newId) return false;
        if (Find(newId) is not null || Tubes.Any(t => t.Id == newId)) return false;
        if (Find(oldId) is { } c)
        {
            c.Id = newId;
            foreach (var t in Tubes)
            {
                if (t.FromComponent == oldId) t.FromComponent = newId;
                if (t.ToComponent == oldId) t.ToComponent = newId;
            }
            foreach (var v in Components.OfType<DirectionalValve>().Where(v => v.RollerCylinder == oldId)) v.RollerCylinder = newId;
            return true;
        }
        if (Tubes.FirstOrDefault(t => t.Id == oldId) is { } tube) { tube.Id = newId; return true; }
        return false;
    }

    /// <summary>Errores que impiden simular y avisos de diseño.</summary>
    public (List<string> Errors, List<string> Warnings) Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Components.Select(c => c.Id).Concat(Tubes.Select(t => t.Id)))
            if (string.IsNullOrWhiteSpace(id)) errors.Add("Hay un elemento sin identificador.");
            else if (!ids.Add(id)) errors.Add($"El identificador '{id}' está repetido.");

        if (!Components.Any(c => c is AirSupply or Compressor))
            errors.Add("Hace falta una fuente de aire comprimido o un compresor.");

        foreach (var t in Tubes)
        {
            if (Find(t.FromComponent) is not { } a || !a.HasPort(t.FromPort) || Find(t.ToComponent) is not { } b || !b.HasPort(t.ToPort))
                errors.Add($"{t.Id}: los dos extremos deben ir a una toma de un componente.");
            else if (t.FromComponent == t.ToComponent && t.FromPort == t.ToPort)
                errors.Add($"{t.Id}: empieza y acaba en la misma toma.");
            if (t.InnerDiameter <= 0 || t.Length <= 0) errors.Add($"{t.Id}: diámetro y longitud deben ser mayores que cero.");
        }

        foreach (var c in Components)
        {
            switch (c)
            {
                case Cylinder cy when cy.Bore <= 0 || cy.Stroke <= 0 || cy.Mass <= 0 || cy.RodDiameter >= cy.Bore || cy.RodDiameter < 0:
                    errors.Add($"{cy.Id}: revisa diámetros, carrera y masa (el vástago debe ser más fino que el émbolo)."); break;
                case DirectionalValve { Actuation: ValveActuation.Roller } v when Find(v.RollerCylinder) is not Cylinder:
                    errors.Add($"{v.Id}: el rodillo debe estar asociado a un cilindro (escribe su identificador)."); break;
                case Compressor k when k.CutInPressure >= k.CutOutPressure || k.CutOutPressure >= k.MaxPressure:
                    errors.Add($"{k.Id}: presión de arranque < presión de paro < presión máxima."); break;
                case AirReceiver r when r.Volume <= 0:
                    errors.Add($"{r.Id}: el volumen debe ser mayor que cero."); break;
            }
            foreach (var p in c.Ports)
                if (!Tubes.Any(t => (t.FromComponent == c.Id && t.FromPort == p.Name) || (t.ToComponent == c.Id && t.ToPort == p.Name)))
                    if (c is AirSupply or Compressor or AirReceiver or Gauge || (c is DirectionalValve && p.Name == "1"))
                        warnings.Add($"{c.Id}: la toma {p.Name} está sin conectar.");
        }
        return (errors, warnings);
    }
}
