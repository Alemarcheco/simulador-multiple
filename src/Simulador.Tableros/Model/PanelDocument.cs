using System.Text.Json;
using System.Text.Json.Serialization;
using Simulador.Electrico.Engine.Model;

namespace Simulador.Tableros.Model;

/// <summary>
/// Tablero (cuadro o panel de automatización) completo. Se guarda como JSON con extensión <c>.tablero</c>.
/// Todas las medidas en milímetros. La placa de montaje tiene su origen en la esquina superior izquierda,
/// con X hacia la derecha e Y hacia abajo, igual que se ve de frente.
/// </summary>
public sealed class PanelDocument
{
    public string Name { get; set; } = "Tablero sin título";
    public string Description { get; set; } = "";
    public Enclosure Enclosure { get; set; } = new();
    public List<DinRail> Rails { get; set; } = [];
    public List<Duct> Ducts { get; set; } = [];
    public List<PanelDevice> Devices { get; set; } = [];
    /// <summary>Conductores dentro del tablero (de borne a borne). Se generan desde el esquema.</summary>
    public List<PanelWire> Wires { get; set; } = [];
    /// <summary>Cables que salen del tablero hacia el campo (motores, captadores, acometida).</summary>
    public List<FieldCable> Cables { get; set; } = [];
    /// <summary>Copia del esquema eléctrico del que sale el tablero (para actualizarlo y regenerar el cableado).</summary>
    public Schematic? Schematic { get; set; }
    /// <summary>Informe de la importación desde un plano PDF (lo encontrado y lo supuesto). Vacío si no viene de un PDF.</summary>
    public List<string> ImportNotes { get; set; } = [];

    public PanelDevice? FindDevice(Guid id) => Devices.FirstOrDefault(d => d.Id == id);
    public PanelDevice? FindTag(string tag) => Devices.FirstOrDefault(d => string.Equals(d.Tag, tag, StringComparison.OrdinalIgnoreCase));
    public DinRail? FindRail(Guid id) => Rails.FirstOrDefault(r => r.Id == id);

    public IEnumerable<PanelDevice> OnPlate => Devices.Where(d => d.Location == DeviceLocation.Plate);
    public IEnumerable<PanelDevice> OnDoor => Devices.Where(d => d.Location == DeviceLocation.Door);

    // Armario de doble frente: cada cosa está en la cara delantera (Rear = false) o en la trasera.
    public IEnumerable<PanelDevice> PlateOf(bool rear) => OnPlate.Where(d => d.Rear == rear);
    public IEnumerable<PanelDevice> DoorOf(bool rear) => OnDoor.Where(d => d.Rear == rear);
    public IEnumerable<DinRail> RailsOf(bool rear) => Rails.Where(r => r.Rear == rear);
    public IEnumerable<Duct> DuctsOf(bool rear) => Ducts.Where(k => k.Rear == rear);
    public IEnumerable<PanelDevice> InField => Devices.Where(d => d.Location == DeviceLocation.Field);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static PanelDocument FromJson(string json) =>
        JsonSerializer.Deserialize<PanelDocument>(json, Json) ?? throw new InvalidDataException("Archivo de tablero vacío.");

    public PanelDocument Clone() => FromJson(ToJson());
}

public enum EnclosureKind { MuralMetalico, ArmarioDePie, CajaPoliester }

/// <summary>Envolvente (caja o armario) y condiciones para el cálculo térmico.</summary>
public sealed class Enclosure
{
    public EnclosureKind Kind { get; set; } = EnclosureKind.MuralMetalico;
    public double Width { get; set; } = 600;
    public double Height { get; set; } = 800;
    public double Depth { get; set; } = 250;
    /// <summary>Distancia de la placa de montaje a cada borde de la envolvente.</summary>
    public double PlateMargin { get; set; } = 25;
    /// <summary>Grado de protección (texto libre: IP54, IP65…).</summary>
    public string Ip { get; set; } = "IP55";
    /// <summary>Temperatura del aire alrededor del tablero (°C).</summary>
    public double AmbientC { get; set; } = 30;
    /// <summary>Temperatura máxima admisible dentro (°C).</summary>
    public double MaxInsideC { get; set; } = 40;
    /// <summary>Caudal del ventilador con filtro (m³/h). 0 = sin ventilación forzada.</summary>
    public double FanFlow { get; set; }
    /// <summary>Lados libres: un armario mural tiene la espalda contra la pared.</summary>
    public bool AgainstWall { get; set; } = true;
    /// <summary>
    /// Doble frente: dos placas espalda con espalda (delantera y trasera), cada una con su puerta. El fondo se reparte
    /// entre las dos caras.
    /// </summary>
    public bool DoubleFront { get; set; }

    /// <summary>Envolvente de catálogo: marca, serie y referencia (null = genérica o a medida).</summary>
    public string? Brand { get; set; }
    public string? Series { get; set; }
    public string? Reference { get; set; }
    /// <summary>Medida de la placa de montaje del catálogo (mm). null = la envolvente menos <see cref="PlateMargin"/> por lado.</summary>
    public double? PlateW { get; set; }
    public double? PlateH { get; set; }
    /// <summary>Hojas de la puerta: 1 o 2 (armarios anchos, con bisagras a los dos lados).</summary>
    public int Doors { get; set; } = 1;
    /// <summary>Color de la chapa (ARGB). null = gris claro de serie.</summary>
    public uint? Color { get; set; }
    /// <summary>Zócalo de 100 mm debajo (armarios de pie).</summary>
    public bool Plinth { get; set; }

    [JsonIgnore] public double PlateWidth => Math.Max(50, Math.Min(Width - 10, PlateW ?? Width - 2 * PlateMargin));
    [JsonIgnore] public double PlateHeight => Math.Max(50, Math.Min(Height - 10, PlateH ?? Height - 2 * PlateMargin));
    /// <summary>Distancia del borde izquierdo de la envolvente a la placa (la placa va centrada).</summary>
    [JsonIgnore] public double PlateLeft => (Width - PlateWidth) / 2;
    /// <summary>Distancia del techo de la envolvente a la placa.</summary>
    [JsonIgnore] public double PlateTop => (Height - PlateHeight) / 2;
    /// <summary>Ancho de la hoja donde van los mandos: la izquierda si hay dos.</summary>
    [JsonIgnore] public double LeafWidth => Doors > 1 ? Width / 2 : Width;
    [JsonIgnore] public EnclosureModel? CatalogModel => EnclosureCatalog.Find(Brand, Reference);

    /// <summary>Se ha cambiado la medida a mano: deja de ser el modelo de catálogo.</summary>
    public void ClearCatalog()
    {
        Brand = Series = Reference = null;
        PlateW = PlateH = null;
        Color = null;
        Doors = 1;
        Plinth = false;
    }
    /// <summary>Hueco libre delante de la placa (fondo útil para los aparatos).</summary>
    [JsonIgnore] public double UsableDepth => DoubleFront ? Math.Max(20, Depth / 2 - 35) : Math.Max(20, Depth - 35);

    public static string KindName(EnclosureKind k) => k switch
    {
        EnclosureKind.ArmarioDePie => "Armario de pie metálico",
        EnclosureKind.CajaPoliester => "Caja de poliéster",
        _ => "Armario mural metálico",
    };
}

/// <summary>Carril DIN de 35 mm, horizontal. (X, Y) = esquina superior izquierda sobre la placa.</summary>
public sealed class DinRail
{
    public const double RailHeight = 35;

    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double Length { get; set; } = 500;
    /// <summary>En la placa trasera (armario de doble frente).</summary>
    public bool Rear { get; set; }

    [JsonIgnore] public double CenterY => Y + RailHeight / 2;
}

/// <summary>Canaleta ranurada. (X, Y) = esquina superior izquierda; Length a lo largo, Width de ancho en la placa.</summary>
public sealed class Duct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double Length { get; set; } = 500;
    public bool Vertical { get; set; }
    public double Width { get; set; } = 40;
    public double Depth { get; set; } = 60;
    /// <summary>En la placa trasera (armario de doble frente).</summary>
    public bool Rear { get; set; }

    [JsonIgnore] public double W => Vertical ? Width : Length;
    [JsonIgnore] public double H => Vertical ? Length : Width;
    [JsonIgnore] public double CenterX => X + W / 2;
    [JsonIgnore] public double CenterY => Y + H / 2;

    public string SizeText => $"{Width:0}×{Depth:0} mm";
}

public enum DeviceLocation { Plate, Door, Field }

public enum TerminalSide { Top, Bottom }

/// <summary>Borne de un aparato tal como se ve en el tablero.</summary>
public sealed class DeviceTerminal
{
    public string Label { get; set; } = "";
    public TerminalSide Side { get; set; }
    /// <summary>Posición a lo ancho del aparato (0 = izquierda, 1 = derecha).</summary>
    public double Slot { get; set; } = 0.5;
}

/// <summary>
/// Aparato del tablero. Reúne todos los elementos del esquema con la misma designación (bobina y contactos de KM1
/// son un solo contactor). En la placa (X, Y) es la esquina superior izquierda; en la puerta, también, medida desde
/// la esquina superior izquierda de la puerta.
/// </summary>
public sealed class PanelDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = "";
    /// <summary>Clave del modelo genérico (<see cref="DeviceLibrary"/>): "contactor", "magnetotermico"…</summary>
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Referencia comercial que quiera poner el usuario (fabricante y código).</summary>
    public string Reference { get; set; } = "";
    public DeviceLocation Location { get; set; } = DeviceLocation.Plate;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 18;
    public double Height { get; set; } = 85;
    public double Depth { get; set; } = 70;
    /// <summary>Potencia disipada en servicio (W), para el cálculo térmico.</summary>
    public double HeatW { get; set; }
    /// <summary>Se monta en carril DIN.</summary>
    public bool OnRail { get; set; } = true;
    public List<DeviceTerminal> Terminals { get; set; } = [];
    /// <summary>Accesorios que van con el aparato (bloque de contactos auxiliares…), para la lista de materiales.</summary>
    public List<string> Accessories { get; set; } = [];
    /// <summary>Corriente asignada (A), cuando se conoce (para la sección de los conductores).</summary>
    public double RatedA { get; set; }
    /// <summary>En la cara trasera (placa o puerta trasera) de un armario de doble frente.</summary>
    public bool Rear { get; set; }

    public DeviceTerminal? Terminal(string label) =>
        Terminals.FirstOrDefault(t => t.Label == label) ?? Terminals.FirstOrDefault(t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase));

    /// <summary>Punto del borne en coordenadas de la placa (o de la puerta).</summary>
    public (double X, double Y) TerminalPoint(DeviceTerminal t) =>
        (X + Width * Math.Clamp(t.Slot, 0, 1), t.Side == TerminalSide.Top ? Y : Y + Height);

    public bool Overlaps(PanelDevice o) =>
        Rear == o.Rear && X < o.X + o.Width - 0.01 && o.X < X + Width - 0.01 && Y < o.Y + o.Height - 0.01 && o.Y < Y + Height - 0.01;
}

public enum WireKind { Potencia, Neutro, Mando, MandoDc, Tierra }

/// <summary>Un extremo de conductor: aparato (por designación) y su borne.</summary>
public sealed record WireEnd(string Tag, string Terminal)
{
    public override string ToString() => $"{Tag}:{Terminal}";
}

/// <summary>Conductor interior del tablero, de un borne a otro.</summary>
public sealed class PanelWire
{
    /// <summary>Número de hilo (el de su potencial: 1, 2, 3… o L1, N…).</summary>
    public string Number { get; set; } = "";
    public WireEnd From { get; set; } = new("", "");
    public WireEnd To { get; set; } = new("", "");
    public WireKind Kind { get; set; }
    public double Section { get; set; } = 1;
    public string Color { get; set; } = "Rojo";
    /// <summary>Longitud por las canaletas más la reserva de las puntas (mm). Lo rellena <see cref="DuctRouter"/>.</summary>
    public double Length { get; set; }
}

/// <summary>Cable de campo: sale de una regleta del tablero hacia un receptor o captador de fuera.</summary>
public sealed class FieldCable
{
    public string Tag { get; set; } = "";
    public string Target { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Conductores activos (sin el de protección).</summary>
    public int Cores { get; set; }
    public bool WithPe { get; set; }
    public double Section { get; set; } = 1.5;
    /// <summary>Bornes de la regleta a los que llega (X1:1, X1:2…).</summary>
    public List<string> Terminals { get; set; } = [];

    public string Designation => $"{Cores + (WithPe ? 1 : 0)}{(WithPe ? "G" : "x")}{Num(Section)}";

    public static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
}
