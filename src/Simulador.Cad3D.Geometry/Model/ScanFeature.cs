using System.Text.Json.Serialization;
using Simulador.Cad3D.Geometry.Scan;

namespace Simulador.Cad3D.Geometry.Model;

/// <summary>Unidades del archivo escaneado.</summary>
public enum ScanUnits
{
    /// <summary>Lo que diga el archivo (USDZ) o, si no, metros cuando la pieza mide menos de 20 unidades.</summary>
    Automatico,
    Metros,
    Centimetros,
    Milimetros,
    Pulgadas,
}

/// <summary>Qué agujeros del escaneo se tapan.</summary>
public enum ScanHoles
{
    /// <summary>Todos, también la base abierta (lo que tocaba la mesa): la pieza queda cerrada.</summary>
    Todos,
    /// <summary>Solo los pequeños (hasta 200 aristas de borde).</summary>
    Pequenos,
    Ninguno,
}

/// <summary>
/// Pieza escaneada (OBJ, STL, PLY, GLB o USDZ de una app de escaneo con LiDAR o fotogrametría) con los arreglos que
/// suelen hacer falta: pasar a milímetros, quitar trozos sueltos, cortar la base, simplificar, tapar agujeros,
/// suavizar, enderezar y poner a la medida real. Es una malla: se puede combinar con otras operaciones con el motor
/// de mallas, pero no exportar a STEP.
/// </summary>
public sealed class ScanFeature : Feature
{
    public override string KindName => "Pieza escaneada";
    public string FilePath { get; set; } = "";

    public ScanUnits Units { get; set; } = ScanUnits.Automatico;
    public UpAxis Up { get; set; } = UpAxis.Automatico;
    public ScanHoles Holes { get; set; } = ScanHoles.Todos;

    [Param("Medida real: ancho X (0 = no cambiar)", Min = 0, Step = 0.5)] public double TargetSizeX { get; set; }
    [Param("Medida real: fondo Y (0 = no cambiar)", Min = 0, Step = 0.5)] public double TargetSizeY { get; set; }
    [Param("Medida real: alto Z (0 = no cambiar)", Min = 0, Step = 0.5)] public double TargetSizeZ { get; set; }
    [Param("Escala extra (%)", Min = 1, Max = 100000, Step = 1)] public double ScalePercent { get; set; } = 100;
    [Param("Quitar trozos sueltos de menos del (% del mayor)", Min = 0, Max = 100, Step = 1)] public double RemovePiecesPercent { get; set; } = 2;
    [Param("Cortar la base: quitar lo de abajo (mm)", Min = 0, Step = 0.5)] public double CutBottom { get; set; }
    [Param("Triángulos como mucho (0 = sin límite)", Min = 0, Max = 2_000_000, Step = 1000)] public int MaxTriangles { get; set; } = 40000;
    [Param("Suavizado (pasadas)", Min = 0, Max = 50)] public int SmoothPasses { get; set; }
    [Param("Enderezar en planta (lado largo según X)")] public bool AlignToAxes { get; set; }
    [Param("Apoyar en Z = 0 y centrar")] public bool PlaceOnFloor { get; set; } = true;

    /// <summary>Último informe del proceso (para mostrarlo en el panel de propiedades).</summary>
    [JsonIgnore] public string Report { get; private set; } = "";

    public override ISolid BuildLocal(IGeometryKernel kernel) => kernel.FromMesh(Process().ToMesh());

    /// <summary>Malla ya arreglada y colocada (posición y giro de la operación), con colores si el archivo los trae.</summary>
    public ScanMesh BuildPlacedMesh()
    {
        var m = Process().Clone();
        var t = Transform3.FromPose(Position, Rotation);
        m.Transform(t.Apply);
        return m;
    }

    // ---------- proceso con caché (reconstruir no vuelve a leer ni a arreglar el archivo si no cambió nada) ----------

    private static readonly Dictionary<string, ScanMesh> ReadCache = new();
    private static readonly Dictionary<string, (ScanMesh Mesh, string Report)> ProcessCache = new();
    private static readonly object Gate = new();

    private (string ReadKey, string Key) Keys()
    {
        var stamp = File.GetLastWriteTimeUtc(FilePath).Ticks;
        var readKey = $"{FilePath}|{stamp}|{Up}";
        return (readKey, FormattableString.Invariant(
            $"{readKey}|{Units}|{Holes}|{TargetSizeX}|{TargetSizeY}|{TargetSizeZ}|{ScalePercent}|{RemovePiecesPercent}|{CutBottom}|{MaxTriangles}|{SmoothPasses}|{AlignToAxes}|{PlaceOnFloor}"));
    }

    /// <summary>Informe del último proceso con estos mismos ajustes, sin calcular nada (null si aún no se hizo).</summary>
    public string? CachedReport()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var (_, key) = Keys();
            lock (Gate) return ProcessCache.TryGetValue(key, out var hit) ? hit.Report : null;
        }
        catch { return null; }
    }

    public ScanMesh Process()
    {
        if (string.IsNullOrWhiteSpace(FilePath)) throw new InvalidOperationException("elige el archivo del escaneo");
        if (!File.Exists(FilePath)) throw new FileNotFoundException($"no se encuentra {FilePath}");
        var (readKey, key) = Keys();
        lock (Gate)
            if (ProcessCache.TryGetValue(key, out var hit)) { Report = hit.Report; return hit.Mesh; }

        ScanMesh? raw;
        lock (Gate) ReadCache.TryGetValue(readKey, out raw);
        if (raw is null)
        {
            raw = ScanReader.Read(FilePath, Up);
            lock (Gate)
            {
                if (ReadCache.Count >= 3) ReadCache.Clear();
                ReadCache[readKey] = raw;
            }
        }
        var (mesh, report) = Repair(raw);
        lock (Gate)
        {
            if (ProcessCache.Count >= 6) ProcessCache.Clear();
            ProcessCache[key] = (mesh, report);
        }
        Report = report;
        return mesh;
    }

    /// <summary>Factor a milímetros según las unidades elegidas.</summary>
    public double UnitFactor(ScanMesh raw)
    {
        switch (Units)
        {
            case ScanUnits.Metros: return 1000;
            case ScanUnits.Centimetros: return 10;
            case ScanUnits.Milimetros: return 1;
            case ScanUnits.Pulgadas: return 25.4;
        }
        if (raw.MetersPerUnit is { } mpu and > 0) return mpu * 1000;
        var s = raw.Size;
        return Math.Max(s.X, Math.Max(s.Y, s.Z)) < 20 ? 1000 : 1;
    }

    /// <summary>Aplica los arreglos en orden. No cambia <paramref name="raw"/>.</summary>
    public (ScanMesh Mesh, string Report) Repair(ScanMesh raw)
    {
        var notes = new List<string>();
        int before = raw.TriangleCount;
        var m = raw.Clone();
        double f = UnitFactor(raw);
        ScanRepair.Scale(m, f);
        notes.Add(f switch { 1000 => "en metros → mm", 10 => "en cm → mm", 25.4 => "en pulgadas → mm", 1 => "en mm", _ => FormattableString.Invariant($"×{f:0.###} → mm") });

        m = ScanRepair.Weld(m, 0.05);
        m = ScanRepair.RemoveSmallPieces(m, RemovePiecesPercent, out var pieces);
        if (pieces > 0) notes.Add($"{pieces} trozo(s) suelto(s) quitado(s)");
        if (AlignToAxes) ScanRepair.RotateZ(m, ScanRepair.PrincipalAngleZ(m));
        if (CutBottom > 0)
        {
            var (min, _) = m.Bounds();
            m = ScanRepair.CutBelow(m, min.Z + CutBottom);
            if (m.TriangleCount == 0) throw new InvalidOperationException("el corte de la base se lleva toda la pieza");
        }
        m = ScanRepair.Simplify(m, MaxTriangles);
        ScanRepair.OrientOutward(m);
        int borders = m.CountBorderEdges();
        if (Holes != ScanHoles.Ninguno && borders > 0)
        {
            int filled = ScanRepair.FillHoles(m, Holes == ScanHoles.Todos ? 0 : 200);
            if (filled > 0) notes.Add($"{filled} agujero(s) tapado(s)");
            ScanRepair.OrientOutward(m);
            borders = m.CountBorderEdges();
        }
        ScanRepair.Smooth(m, SmoothPasses);

        // Medida real: la primera que se indique manda (escala uniforme, sin deformar).
        var size = m.Size;
        double target = TargetSizeX > 0 && size.X > 0 ? TargetSizeX / size.X
            : TargetSizeY > 0 && size.Y > 0 ? TargetSizeY / size.Y
            : TargetSizeZ > 0 && size.Z > 0 ? TargetSizeZ / size.Z : 1;
        ScanRepair.Scale(m, target * ScalePercent / 100.0);
        if (PlaceOnFloor) ScanRepair.PlaceOnFloor(m);

        size = m.Size;
        notes.Insert(0, FormattableString.Invariant($"{before:N0} → {m.TriangleCount:N0} triángulos; {size.X:0.#} × {size.Y:0.#} × {size.Z:0.#} mm"));
        notes.Add(borders == 0 ? "malla cerrada" : $"quedan {borders} aristas abiertas");
        if (raw.HasColors) notes.Add("con colores");
        return (m, string.Join(" · ", notes));
    }
}
