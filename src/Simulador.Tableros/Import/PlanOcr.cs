using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Simulador.Tableros.Import;

/// <summary>
/// Reconocedor de texto (OCR) para planos cuyo PDF trae las letras dibujadas como trazos (fuentes SHX de los
/// programas de CAD) en lugar de como texto. Devuelve las palabras de cada página en puntos PDF, origen arriba a la
/// izquierda, igual que <see cref="PlanText"/>.
/// </summary>
public interface IPlanOcr
{
    string Name { get; }
    bool IsAvailable { get; }
    /// <summary>Reconoce las páginas pedidas (empezando en 1), de una en una, según termina cada una.</summary>
    IEnumerable<(int Page, List<PlanWord> Words)> Recognize(string pdfPath, IReadOnlyList<int> pages, CancellationToken ct = default);
}

public static class PlanOcr
{
    /// <summary>Resolución a la que se pasan las páginas por el OCR (los textos de los hilos miden 1,5–2 mm).</summary>
    public const int Dpi = 400;

    /// <summary>El primer reconocedor disponible: el que trae Windows 10/11, o Tesseract si está instalado.</summary>
    public static IPlanOcr? Default()
    {
        IPlanOcr[] all = [new WindowsOcr(), new TesseractOcr()];
        return all.FirstOrDefault(o => o.IsAvailable);
    }

    /// <summary>Quita las palabras repetidas en el solape de dos trozos de la página.</summary>
    internal static List<PlanWord> Dedupe(IEnumerable<PlanWord> words)
    {
        var list = new List<PlanWord>();
        foreach (var w in words)
        {
            if (list.Any(o => o.Text == w.Text && Math.Abs(o.X - w.X) < 2 && Math.Abs(o.Y - w.Y) < 2)) continue;
            list.Add(w);
        }
        return list;
    }

    internal static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in OperatingSystem.IsWindows() ? new[] { exe + ".exe", exe } : [exe])
            {
                var full = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }

    /// <summary>Lanza un programa y entrega su salida renglón a renglón.</summary>
    internal static IEnumerable<string> RunLines(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo arrancar {exe}.");
        var err = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (err) err.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { p.Kill(true); } catch (InvalidOperationException) { } });
        string? line;
        while ((line = p.StandardOutput.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            yield return line;
        }
        p.WaitForExit();
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} terminó con error {p.ExitCode}: {err.ToString().Trim()}");
    }

    internal static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>
/// OCR de Windows 10/11 (Windows.Media.Ocr) y su lector de PDF (Windows.Data.Pdf), llamados desde Windows PowerShell,
/// que viene con el sistema: no hay que instalar nada. Cada página se pasa en cuatro trozos con solape para que las
/// letras pequeñas tengan píxeles suficientes.
/// </summary>
public sealed class WindowsOcr : IPlanOcr
{
    public string Name => "OCR de Windows";

    public bool IsAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240) && PowerShell is not null;

    private static string? PowerShell
    {
        get
        {
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var ps = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(ps) ? ps : PlanOcr.FindOnPath("powershell");
        }
    }

    public IEnumerable<(int Page, List<PlanWord> Words)> Recognize(string pdfPath, IReadOnlyList<int> pages, CancellationToken ct = default)
    {
        if (pages.Count == 0) yield break;
        var script = Path.Combine(Path.GetTempPath(), $"simulador-ocr-{Environment.ProcessId}.ps1");
        File.WriteAllText(script, Script, new UTF8Encoding(true));
        try
        {
            var current = -1;
            var words = new List<PlanWord>();
            foreach (var line in PlanOcr.RunLines(PowerShell!,
                         ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                          "-Pdf", Path.GetFullPath(pdfPath), "-Pages", string.Join(',', pages), "-Dpi", PlanOcr.Dpi.ToString(CultureInfo.InvariantCulture)], ct))
            {
                var f = line.Split('\t');
                switch (f[0])
                {
                    case "PAGE":
                        current = int.Parse(f[1], CultureInfo.InvariantCulture);
                        words = [];
                        break;
                    case "W" when f.Length >= 6 && current > 0:
                        words.Add(new PlanWord(f[5], PlanOcr.D(f[1]), PlanOcr.D(f[2]), PlanOcr.D(f[3]), PlanOcr.D(f[4])));
                        break;
                    case "END" when current > 0:
                        yield return (current, PlanOcr.Dedupe(words));
                        current = -1;
                        break;
                    case "ERR":
                        throw new InvalidOperationException(string.Join(' ', f.Skip(1)));
                }
            }
        }
        finally
        {
            try { File.Delete(script); } catch (IOException) { }
        }
    }

    /// <summary>Script de PowerShell 5.1: abre el PDF, pasa cada página por el OCR y escribe una palabra por renglón.</summary>
    internal const string Script = """
        param([string]$Pdf, [string]$Pages, [int]$Dpi = 400)
        # Ojo: en PowerShell las variables no distinguen mayusculas ($pageW no es $word).
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        try {
          Add-Type -AssemblyName System.Runtime.WindowsRuntime
          $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
          $null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
          $null = [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType = WindowsRuntime]
          $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
          $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime]
          $null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
          $null = [Windows.Globalization.Language, Windows.Globalization, ContentType = WindowsRuntime]

          $methods = [System.WindowsRuntimeSystemExtensions].GetMethods()
          $asTaskOp = ($methods | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
          $asTaskAct = ($methods | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
          function Await($op, [Type]$type) {
            $t = $asTaskOp.MakeGenericMethod($type).Invoke($null, @($op))
            $null = $t.Wait(-1)
            $t.Result
          }
          function AwaitAction($act) {
            $t = $asTaskAct.Invoke($null, @($act))
            $null = $t.Wait(-1)
          }

          $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
          if ($null -eq $engine) {
            foreach ($tag in @('es-MX', 'es-ES', 'en-US')) {
              $lang = New-Object Windows.Globalization.Language($tag)
              if ([Windows.Media.Ocr.OcrEngine]::IsLanguageSupported($lang)) { $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($lang); break }
            }
          }
          if ($null -eq $engine) { Write-Output "ERR`tWindows no tiene instalado ningun idioma de reconocimiento de texto (OCR)."; exit 0 }

          $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Pdf)) ([Windows.Storage.StorageFile])
          $doc = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
          $inv = [System.Globalization.CultureInfo]::InvariantCulture
          $scale = $Dpi / 96.0
          $maxPx = [Math]::Min(4000, [Windows.Media.Ocr.OcrEngine]::MaxImageDimension)
          foreach ($p in ($Pages -split ',')) {
            $n = [int]$p
            if ($n -lt 1 -or $n -gt $doc.PageCount) { continue }
            Write-Output "PAGE`t$n"
            $page = $doc.GetPage([uint32]($n - 1))
            $pageW = $page.Size.Width; $pageH = $page.Size.Height
            $tileDip = $maxPx / $scale
            $nx = [Math]::Max(1, [Math]::Ceiling($pageW / ($tileDip * 0.9)))
            $ny = [Math]::Max(1, [Math]::Ceiling($pageH / ($tileDip * 0.9)))
            $tw = [Math]::Min($pageW, $pageW / $nx * 1.08); $th = [Math]::Min($pageH, $pageH / $ny * 1.08)
            for ($iy = 0; $iy -lt $ny; $iy++) {
              for ($ix = 0; $ix -lt $nx; $ix++) {
                $x0 = [Math]::Min($pageW - $tw, $ix * $pageW / $nx); $y0 = [Math]::Min($pageH - $th, $iy * $pageH / $ny)
                $opt = New-Object Windows.Data.Pdf.PdfPageRenderOptions
                $opt.SourceRect = New-Object Windows.Foundation.Rect($x0, $y0, $tw, $th)
                $opt.DestinationWidth = [uint32][Math]::Round($tw * $scale)
                $opt.DestinationHeight = [uint32][Math]::Round($th * $scale)
                $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
                AwaitAction ($page.RenderToStreamAsync($stream, $opt))
                $stream.Seek(0)
                $dec = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
                $bmp = Await ($dec.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
                $res = Await ($engine.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult])
                foreach ($line in $res.Lines) {
                  foreach ($word in $line.Words) {
                    $r = $word.BoundingRect
                    # pixeles del trozo -> DIP de la pagina -> puntos PDF (72/96)
                    $x = ($x0 + $r.X / $scale) * 0.75; $y = ($y0 + $r.Y / $scale) * 0.75
                    $ww = $r.Width / $scale * 0.75; $hh = $r.Height / $scale * 0.75
                    $txt = $word.Text -replace "`t", ' '
                    Write-Output ("W`t{0}`t{1}`t{2}`t{3}`t{4}" -f $x.ToString('0.##', $inv), $y.ToString('0.##', $inv), $ww.ToString('0.##', $inv), $hh.ToString('0.##', $inv), $txt)
                  }
                }
                $bmp.Dispose(); $stream.Dispose()
              }
            }
            $page.Dispose()
            Write-Output "END`t$n"
          }
        } catch {
          Write-Output ("ERR`t" + ($_.Exception.Message -replace "`r?`n", ' '))
        }
        """;
}

/// <summary>
/// Tesseract (libre) con pdftoppm de Poppler, si los dos están instalados y en el PATH. Sirve en Linux y como
/// alternativa en Windows.
/// </summary>
public sealed class TesseractOcr : IPlanOcr
{
    public string Name => "Tesseract";

    public bool IsAvailable => PlanOcr.FindOnPath("tesseract") is not null && PlanOcr.FindOnPath("pdftoppm") is not null;

    public IEnumerable<(int Page, List<PlanWord> Words)> Recognize(string pdfPath, IReadOnlyList<int> pages, CancellationToken ct = default)
    {
        var tesseract = PlanOcr.FindOnPath("tesseract")!;
        var pdftoppm = PlanOcr.FindOnPath("pdftoppm")!;
        var dir = Path.Combine(Path.GetTempPath(), $"simulador-ocr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var n in pages)
            {
                ct.ThrowIfCancellationRequested();
                var prefix = Path.Combine(dir, "p");
                var s = n.ToString(CultureInfo.InvariantCulture);
                foreach (var _ in PlanOcr.RunLines(pdftoppm, ["-f", s, "-l", s, "-r", PlanOcr.Dpi.ToString(CultureInfo.InvariantCulture), "-gray", "-png", "-singlefile", pdfPath, prefix], ct)) { }
                var png = prefix + ".png";
                var words = new List<PlanWord>();
                var k = 72.0 / PlanOcr.Dpi;
                var header = true;
                foreach (var line in PlanOcr.RunLines(tesseract, [png, "stdout", "--psm", "11", "tsv"], ct))
                {
                    if (header) { header = false; continue; }
                    var f = line.Split('\t');
                    if (f.Length < 12 || f[0] != "5" || string.IsNullOrWhiteSpace(f[11])) continue;
                    if (PlanOcr.D(f[10]) < 30) continue;
                    words.Add(new PlanWord(f[11].Trim(), PlanOcr.D(f[6]) * k, PlanOcr.D(f[7]) * k, PlanOcr.D(f[8]) * k, PlanOcr.D(f[9]) * k));
                }
                File.Delete(png);
                yield return (n, words);
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
