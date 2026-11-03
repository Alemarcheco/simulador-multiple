using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Simulador.Circuitos.Simulation;

/// <summary>No se encontró la biblioteca de ngspice (ngspice.dll en Windows, libngspice.so en Linux).</summary>
public sealed class NgSpiceNotFoundException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Vectores que devuelve ngspice tras una simulación.</summary>
public sealed class SimulationResult
{
    public required string Plot { get; init; }
    public Dictionary<string, double[]> Real { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Complex[]> Complex { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Log { get; } = [];
    public List<string> Errors { get; } = [];

    public bool Ok => Errors.Count == 0 && (Real.Count > 0 || Complex.Count > 0);

    /// <summary>Tensión de una red: ngspice nombra los vectores en minúsculas ("n1", "salida").</summary>
    public double[]? Voltage(string net) => Real.GetValueOrDefault(net) ?? Real.GetValueOrDefault($"v({net})");
}

/// <summary>
/// Envoltorio de la biblioteca compartida de ngspice. ngspice no admite varias simulaciones a la vez,
/// así que todas las llamadas se serializan.
/// </summary>
public static class NgSpice
{
    /// <summary>Variable de entorno con la ruta a ngspice.dll / libngspice.so si no está junto al ejecutable.</summary>
    public const string PathVariable = "NGSPICE_LIBRARY_PATH";
    private const string Lib = "ngspice";

    private static readonly object Gate = new();
    private static bool _initialized;
    private static List<string>? _currentLog;
    private static readonly List<Delegate> KeepAlive = [];

    static NgSpice()
    {
        NativeLibrary.SetDllImportResolver(typeof(NgSpice).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib) return IntPtr.Zero;
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } env) candidates.Add(env);
        string[] names = OperatingSystem.IsWindows() ? ["ngspice.dll"]
            : OperatingSystem.IsMacOS() ? ["libngspice.dylib", "libngspice.0.dylib"]
            : ["libngspice.so.0", "libngspice.so"];
        foreach (var n in names)
        {
            candidates.Add(Path.Combine(AppContext.BaseDirectory, n));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "ngspice", n));
            candidates.Add(n);
        }
        foreach (var c in candidates)
            if (NativeLibrary.TryLoad(c, out var handle)) return handle;
        return IntPtr.Zero;
    }

    /// <summary>True si la biblioteca de ngspice se puede cargar.</summary>
    public static bool IsAvailable
    {
        get
        {
            try { EnsureInitialized(); return true; }
            catch (NgSpiceNotFoundException) { return false; }
        }
    }

    /// <summary>
    /// Ejecuta una netlist completa (con sus líneas de análisis y ".end") y devuelve todos los vectores del resultado.
    /// Bloquea hasta terminar: llámalo desde un hilo de fondo si hay interfaz gráfica.
    /// </summary>
    public static SimulationResult Run(string netlist)
    {
        lock (Gate)
        {
            EnsureInitialized();
            var log = new List<string>();
            _currentLog = log;
            try
            {
                Command("destroy all");
                Command("reset");

                var lines = netlist.Replace("\r\n", "\n").Split('\n');
                var ptrs = new IntPtr[lines.Length + 1];
                try
                {
                    for (int i = 0; i < lines.Length; i++) ptrs[i] = Marshal.StringToHGlobalAnsi(lines[i]);
                    ptrs[^1] = IntPtr.Zero;
                    if (ngSpice_Circ(ptrs) != 0)
                        return Failed("ngspice no pudo cargar el circuito.", log);
                }
                finally
                {
                    foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                }

                if (log.Any(IsFatal))
                    return Failed("Error en la netlist.", log);

                Command("run");

                var plot = Marshal.PtrToStringAnsi(ngSpice_CurPlot()) ?? "";
                var result = new SimulationResult { Plot = plot };
                result.Log.AddRange(log);
                result.Errors.AddRange(log.Where(IsFatal));
                if (plot is "" or "const")
                {
                    if (result.Errors.Count == 0) result.Errors.Add("La simulación no produjo resultados.");
                    return result;
                }
                ReadVectors(plot, result);
                return result;
            }
            finally
            {
                _currentLog = null;
            }
        }
    }

    private static SimulationResult Failed(string message, List<string> log)
    {
        var r = new SimulationResult { Plot = "" };
        r.Log.AddRange(log);
        r.Errors.AddRange(log.Where(IsFatal));
        if (r.Errors.Count == 0) r.Errors.Add(message);
        return r;
    }

    private static bool IsFatal(string line)
    {
        var l = line.ToLowerInvariant();
        return (l.Contains("error") && !l.Contains("no error")) || l.Contains("singular matrix") ||
               l.Contains("timestep too small") || l.Contains("unknown") && l.Contains("model");
    }

    private static void ReadVectors(string plot, SimulationResult result)
    {
        var arr = ngSpice_AllVecs(plot);
        if (arr == IntPtr.Zero) return;
        for (int i = 0; ; i++)
        {
            var p = Marshal.ReadIntPtr(arr, i * IntPtr.Size);
            if (p == IntPtr.Zero) break;
            var name = Marshal.PtrToStringAnsi(p);
            if (string.IsNullOrEmpty(name)) continue;
            var infoPtr = ngGet_Vec_Info($"{plot}.{name}");
            if (infoPtr == IntPtr.Zero) continue;
            var info = Marshal.PtrToStructure<VectorInfo>(infoPtr);
            if (info.Length <= 0) continue;
            if (info.RealData != IntPtr.Zero)
            {
                var data = new double[info.Length];
                Marshal.Copy(info.RealData, data, 0, info.Length);
                result.Real[name] = data;
            }
            else if (info.CompData != IntPtr.Zero)
            {
                var raw = new double[info.Length * 2];
                Marshal.Copy(info.CompData, raw, 0, raw.Length);
                var data = new Complex[info.Length];
                for (int k = 0; k < info.Length; k++) data[k] = new Complex(raw[2 * k], raw[2 * k + 1]);
                result.Complex[name] = data;
            }
        }
    }

    private static void Command(string command)
    {
        var p = Marshal.StringToHGlobalAnsi(command);
        try { ngSpice_Command(p); }
        finally { Marshal.FreeHGlobal(p); }
    }

    private static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized) return;
            SendChar sendChar = (str, _, _) =>
            {
                var s = Marshal.PtrToStringAnsi(str) ?? "";
                // ngspice antepone "stdout " o "stderr " a cada línea.
                if (s.StartsWith("stdout ")) s = s[7..];
                else if (s.StartsWith("stderr ")) s = s[7..];
                _currentLog?.Add(s);
                return 0;
            };
            SendStat sendStat = (_, _, _) => 0;
            ControlledExit exit = (_, _, _, _, _) => 0;
            BgThreadRunning bg = (_, _, _) => 0;
            KeepAlive.AddRange([sendChar, sendStat, exit, bg]);
            try
            {
                ngSpice_Init(sendChar, sendStat, exit, IntPtr.Zero, IntPtr.Zero, bg, IntPtr.Zero);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                throw new NgSpiceNotFoundException(
                    "No se encontró ngspice. En Windows, ngspice.dll debe estar junto a SimuladorMultiple.exe " +
                    $"(se incluye en la versión descargable) o indicada en la variable {PathVariable}.", ex);
            }
            _initialized = true;
        }
    }

    // ----- API de ngspice (sharedspice.h) -----

    [StructLayout(LayoutKind.Sequential)]
    private struct VectorInfo
    {
        public IntPtr Name;
        public int Type;
        public short Flags;
        public IntPtr RealData;
        public IntPtr CompData;
        public int Length;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendChar(IntPtr str, int id, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendStat(IntPtr str, int id, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ControlledExit(int status, [MarshalAs(UnmanagedType.I1)] bool unload, [MarshalAs(UnmanagedType.I1)] bool quit, int id, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BgThreadRunning([MarshalAs(UnmanagedType.I1)] bool running, int id, IntPtr user);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ngSpice_Init(SendChar printfcn, SendStat statfcn, ControlledExit exitfcn,
        IntPtr datfcn, IntPtr initdata, BgThreadRunning bgtrun, IntPtr userData);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ngSpice_Command(IntPtr command);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ngSpice_Circ(IntPtr[] circarray);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ngSpice_CurPlot();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ngSpice_AllVecs(IntPtr plotname);

    private static IntPtr ngSpice_AllVecs(string plot)
    {
        var p = Marshal.StringToHGlobalAnsi(plot);
        try { return ngSpice_AllVecs(p); }
        finally { Marshal.FreeHGlobal(p); }
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ngGet_Vec_Info(IntPtr vecname);

    private static IntPtr ngGet_Vec_Info(string name)
    {
        var p = Marshal.StringToHGlobalAnsi(name);
        try { return ngGet_Vec_Info(p); }
        finally { Marshal.FreeHGlobal(p); }
    }
}
