using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Simulador.Arduino.Runtime.Avr;

/// <summary>Resultado de compilar: el .hex o los errores (con la línea del sketch cuando se sabe).</summary>
public sealed record CompileResult(string? Hex, string Log, int? ErrorLine, string? ErrorMessage)
{
    public bool Success => Hex is not null;
}

/// <summary>
/// Compila sketches para el ATmega328P con el compilador real de Arduino (avr-gcc), igual que el IDE. Busca, por este
/// orden: <c>arduino-cli</c> (el que trae el Arduino IDE 2 o uno en el PATH) o bien avr-gcc y el núcleo «arduino:avr»
/// instalados por el IDE (o por el sistema en Linux), y entonces hace la compilación él mismo.
/// </summary>
public sealed partial class ArduinoToolchain
{
    private readonly string? _cli;
    private readonly string? _gccBin;
    private readonly string? _core;
    private readonly IReadOnlyList<string> _libraryRoots;

    private ArduinoToolchain(string? cli, string? gccBin, string? core, IReadOnlyList<string> libraryRoots)
    {
        _cli = cli;
        _gccBin = gccBin;
        _core = core;
        _libraryRoots = libraryRoots;
    }

    /// <summary>Qué se encontró, para mostrarlo al usuario.</summary>
    public string Description => _cli is not null ? $"arduino-cli ({_cli})"
        : _gccBin is not null && _core is not null ? $"avr-gcc ({_gccBin}) con el núcleo {_core}"
        : "ninguno";

    public bool IsAvailable => _cli is not null || (_gccBin is not null && _core is not null);

    public const string InstallHelp =
        "Para el modo chip real hace falta el compilador de Arduino. Instala el Arduino IDE 2 (arduino.cc/en/software), " +
        "ábrelo una vez para que descargue «Arduino AVR Boards» y vuelve a intentarlo. También puedes compilar en el IDE " +
        "(Programa → Exportar binario compilado) y abrir aquí el .hex.";

    /// <summary>Busca las herramientas instaladas en este equipo.</summary>
    public static ArduinoToolchain Detect()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        bool windows = OperatingSystem.IsWindows();
        string exe = windows ? ".exe" : "";

        var cliCandidates = new List<string?>
        {
            FindInPath("arduino-cli" + exe),
            Path.Combine(local, "Programs", "Arduino IDE", "resources", "app", "lib", "backend", "resources", "arduino-cli" + exe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Arduino IDE", "resources", "app", "lib", "backend", "resources", "arduino-cli" + exe),
        };
        string? cli = cliCandidates.FirstOrDefault(File.Exists);

        // Instalación del IDE (Arduino15) o del sistema (Linux).
        var arduino15 = new[]
        {
            Path.Combine(local, "Arduino15"),
            Path.Combine(home, ".arduino15"),
            Path.Combine(home, "Library", "Arduino15"),
        }.FirstOrDefault(Directory.Exists);
        string? core = null, gccBin = null;
        if (arduino15 is not null)
        {
            core = Newest(Path.Combine(arduino15, "packages", "arduino", "hardware", "avr"));
            var gcc = Newest(Path.Combine(arduino15, "packages", "arduino", "tools", "avr-gcc"));
            if (gcc is not null && File.Exists(Path.Combine(gcc, "bin", "avr-gcc" + exe))) gccBin = Path.Combine(gcc, "bin");
        }
        if (core is null && Directory.Exists("/usr/share/arduino/hardware/arduino/avr")) core = "/usr/share/arduino/hardware/arduino/avr";
        if (gccBin is null && FindInPath("avr-gcc" + exe) is { } systemGcc) gccBin = Path.GetDirectoryName(systemGcc);

        var roots = new List<string>();
        foreach (var r in new[]
        {
            core is null ? null : Path.Combine(core, "libraries"),
            arduino15 is null ? null : Path.Combine(arduino15, "libraries"),
            Path.Combine(docs, "Arduino", "libraries"),
            Path.Combine(home, "Arduino", "libraries"),
            "/usr/share/arduino/libraries",
        })
            if (r is not null && Directory.Exists(r)) roots.Add(r);
        return new ArduinoToolchain(cli, gccBin, core, roots);
    }

    /// <summary>Herramientas concretas (para las pruebas).</summary>
    public static ArduinoToolchain WithGcc(string gccBin, string core, params string[] libraryRoots) => new(null, gccBin, core, libraryRoots);

    private static string? FindInPath(string file)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            var full = Path.Combine(dir, file);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string? Newest(string dir) =>
        Directory.Exists(dir) ? Directory.GetDirectories(dir).OrderBy(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0)).LastOrDefault() : null;

    /// <summary>Compila el sketch para la placa (Uno o Nano). Tarda unos segundos la primera vez.</summary>
    public async Task<CompileResult> CompileAsync(string source, ArduinoBoard board, CancellationToken cancel = default)
    {
        if (!IsAvailable) return new CompileResult(null, "", null, InstallHelp);
        string work = Path.Combine(Path.GetTempPath(), "simulador-arduino", Guid.NewGuid().ToString("N")[..12]);
        string sketchDir = Path.Combine(work, "sketch");
        Directory.CreateDirectory(sketchDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sketchDir, "sketch.ino"), source, cancel);
            if (_cli is not null)
            {
                string fqbn = board == ArduinoBoard.Nano ? "arduino:avr:nano" : "arduino:avr:uno";
                string outDir = Path.Combine(work, "out");
                var (code, log) = await RunAsync(_cli, ["compile", "--fqbn", fqbn, "--output-dir", outDir, sketchDir], work, cancel);
                string hexPath = Path.Combine(outDir, "sketch.ino.hex");
                if (code == 0 && File.Exists(hexPath)) return new CompileResult(await File.ReadAllTextAsync(hexPath, cancel), log, null, null);
                return Failure(log);
            }
            return await BuildDirectAsync(source, board, work, cancel);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static CompileResult Failure(string log)
    {
        var missing = MissingHeader().Match(log);
        if (missing.Success)
            return new CompileResult(null, log, null,
                $"Falta la librería «{missing.Groups[1].Value}». Instálala en el Arduino IDE (Herramientas → Gestionar librerías) y vuelve a compilar.");
        var m = ErrorLine().Match(log);
        return m.Success
            ? new CompileResult(null, log, int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[2].Value.Trim())
            : new CompileResult(null, log, null, log.Trim().Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "Error al compilar.");
    }

    [GeneratedRegex(@"fatal error:\s*([A-Za-z0-9_]+)\.h: No such file")]
    private static partial Regex MissingHeader();

    [GeneratedRegex(@"sketch\.ino:(\d+):(?:\d+:)?\s*(?:fatal )?error:\s*(.*)")]
    private static partial Regex ErrorLine();

    // ---- Compilación directa con avr-gcc ----

    private async Task<CompileResult> BuildDirectAsync(string source, ArduinoBoard board, string work, CancellationToken cancel)
    {
        string gcc = Path.Combine(_gccBin!, "avr-gcc"), gpp = Path.Combine(_gccBin!, "avr-g++"), objcopy = Path.Combine(_gccBin!, "avr-objcopy");
        if (OperatingSystem.IsWindows()) { gcc += ".exe"; gpp += ".exe"; objcopy += ".exe"; }
        string variant = board == ArduinoBoard.Nano ? "eightanalogin" : "standard";
        string coreSrc = Path.Combine(_core!, "cores", "arduino");
        string variantDir = Path.Combine(_core!, "variants", variant);
        if (!Directory.Exists(variantDir)) variantDir = Path.Combine(_core!, "variants", "standard");
        string boardDefine = board == ArduinoBoard.Nano ? "ARDUINO_AVR_NANO" : "ARDUINO_AVR_UNO";
        var common = new List<string> { "-c", "-g", "-Os", "-w", "-ffunction-sections", "-fdata-sections", "-mmcu=atmega328p", "-DF_CPU=16000000L", "-DARDUINO=10819", $"-D{boardDefine}", "-DARDUINO_ARCH_AVR" };
        var includes = new List<string> { "-I" + coreSrc, "-I" + variantDir };

        // Librerías: las que el sketch incluye y existen en alguna carpeta de librerías.
        var librarySources = new List<string>();
        foreach (Match inc in IncludeLine().Matches(source))
        {
            string name = inc.Groups[1].Value;
            foreach (var root in _libraryRoots)
            {
                string dir = Path.Combine(root, name);
                string src = Directory.Exists(Path.Combine(dir, "src")) ? Path.Combine(dir, "src") : dir;
                if (!File.Exists(Path.Combine(src, name + ".h"))) continue;
                includes.Add("-I" + src);
                librarySources.AddRange(Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                    .Where(f => (f.EndsWith(".cpp", StringComparison.Ordinal) || f.EndsWith(".c", StringComparison.Ordinal))
                        && (!f.Contains(Path.DirectorySeparatorChar + "arch" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                            || f.Contains(Path.DirectorySeparatorChar + "avr" + Path.DirectorySeparatorChar, StringComparison.Ordinal))));
                break;
            }
        }

        var log = new StringBuilder();
        string cpp = Path.Combine(work, "sketch.cpp");
        await File.WriteAllTextAsync(cpp, Preprocess(source), cancel);
        var objects = new List<string>();

        async Task<bool> Compile(string file)
        {
            bool isC = file.EndsWith(".c", StringComparison.Ordinal), isAsm = file.EndsWith(".S", StringComparison.Ordinal);
            string obj = Path.Combine(work, "obj", $"{objects.Count}_{Path.GetFileName(file)}.o");
            Directory.CreateDirectory(Path.GetDirectoryName(obj)!);
            var args = new List<string>(common);
            if (isAsm) args.AddRange(["-x", "assembler-with-cpp"]);
            else if (isC) args.Add("-std=gnu11");
            else args.AddRange(["-std=gnu++11", "-fpermissive", "-fno-exceptions", "-fno-threadsafe-statics"]);
            args.AddRange(includes);
            args.AddRange([file, "-o", obj]);
            var (code, output) = await RunAsync(isC || isAsm ? gcc : gpp, args, work, cancel);
            log.Append(output);
            objects.Add(obj);
            return code == 0;
        }

        if (!await Compile(cpp)) return Failure(log.ToString());
        foreach (var f in Directory.EnumerateFiles(coreSrc).Where(f => f.EndsWith(".c", StringComparison.Ordinal) || f.EndsWith(".cpp", StringComparison.Ordinal) || f.EndsWith(".S", StringComparison.Ordinal)))
            if (!await Compile(f)) return Failure(log.ToString());
        foreach (var f in librarySources)
            if (!await Compile(f)) return Failure(log.ToString());

        string elf = Path.Combine(work, "sketch.elf"), hex = Path.Combine(work, "sketch.hex");
        var link = new List<string> { "-w", "-Os", "-Wl,--gc-sections", "-mmcu=atmega328p", "-o", elf };
        link.AddRange(objects);
        link.Add("-lm");
        var (lc, lout) = await RunAsync(gcc, link, work, cancel);
        log.Append(lout);
        if (lc != 0) return Failure(log.ToString());
        var (oc, oout) = await RunAsync(objcopy, ["-O", "ihex", "-R", ".eeprom", elf, hex], work, cancel);
        log.Append(oout);
        if (oc != 0 || !File.Exists(hex)) return Failure(log.ToString());
        return new CompileResult(await File.ReadAllTextAsync(hex, cancel), log.ToString(), null, null);
    }

    [GeneratedRegex(@"^\s*#\s*include\s*[<""]([A-Za-z0-9_]+)\.h[>""]", RegexOptions.Multiline)]
    private static partial Regex IncludeLine();

    [GeneratedRegex(@"^([A-Za-z_][\w\s\*&:<>,]*?[\s\*&])([A-Za-z_]\w*)\s*\(([^;{}()]*)\)\s*\{", RegexOptions.Multiline)]
    private static partial Regex FunctionHeader();

    /// <summary>
    /// Hace lo mismo que el IDE con un .ino: añade <c>#include &lt;Arduino.h&gt;</c> y los prototipos de las funciones
    /// (así se pueden usar antes de definirlas). La directiva #line mantiene los números de línea del sketch.
    /// </summary>
    public static string Preprocess(string source)
    {
        var prototypes = new StringBuilder();
        foreach (Match m in FunctionHeader().Matches(source))
        {
            string type = m.Groups[1].Value.Trim(), name = m.Groups[2].Value;
            if (type is "else" or "return" || name is "if" or "while" or "for" or "switch") continue;
            if (Depth(source, m.Index) != 0) continue;
            prototypes.Append(type).Append(' ').Append(name).Append('(').Append(m.Groups[3].Value.Trim()).AppendLine(");");
        }
        return "#include <Arduino.h>\n" + prototypes + "#line 1 \"sketch.ino\"\n" + source + "\n";
    }

    /// <summary>Profundidad de llaves en una posición (ignorando comentarios y cadenas de forma sencilla).</summary>
    private static int Depth(string s, int upTo)
    {
        int depth = 0;
        for (int i = 0; i < upTo; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < upTo && s[i + 1] == '/') { while (i < upTo && s[i] != '\n') i++; }
            else if (c == '/' && i + 1 < upTo && s[i + 1] == '*') { i += 2; while (i + 1 < upTo && !(s[i] == '*' && s[i + 1] == '/')) i++; i++; }
            else if (c is '"' or '\'') { char q = c; i++; while (i < upTo && s[i] != q) { if (s[i] == '\\') i++; i++; } }
            else if (c == '{') depth++;
            else if (c == '}') depth--;
        }
        return depth;
    }

    private static async Task<(int Code, string Output)> RunAsync(string file, IEnumerable<string> args, string cwd, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar " + file);
        var stdout = p.StandardOutput.ReadToEndAsync(cancel);
        var stderr = p.StandardError.ReadToEndAsync(cancel);
        await p.WaitForExitAsync(cancel);
        return (p.ExitCode, await stdout + await stderr);
    }
}
