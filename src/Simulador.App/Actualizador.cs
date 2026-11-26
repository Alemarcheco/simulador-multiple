using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Simulador.App;

/// <summary>Una versión publicada en GitHub Releases.</summary>
public sealed record VersionPublicada(Version Version, string? Instalador, long Bytes, string Pagina);

/// <summary>
/// Busca, descarga e instala versiones nuevas desde GitHub Releases del proyecto.
/// La CI publica en cada fusión a main la versión v0.1.N con SimuladorMultiple-Setup.exe
/// (.github/workflows/build.yml) y compila el programa con esa misma versión.
/// </summary>
public static class Actualizador
{
    public const string Repositorio = "Alemarcheco/simulador-multiple";
    public const string NombreInstalador = "SimuladorMultiple-Setup.exe";
    public const string PaginaDescargas = "https://github.com/" + Repositorio + "/releases/latest";
    private const string ApiUltima = "https://api.github.com/repos/" + Repositorio + "/releases/latest";

    private static readonly HttpClient Http = CrearCliente();

    /// <summary>Versión de este programa (la pone la CI con -p:Version=0.1.N).</summary>
    public static Version VersionActual { get; } = Normalizar(typeof(Actualizador).Assembly.GetName().Version);

    /// <summary>Versión encontrada al arrancar, si es más nueva que la instalada.</summary>
    public static VersionPublicada? Disponible { get; private set; }

    /// <summary>Se avisa (en cualquier hilo) cuando se detecta una versión más nueva.</summary>
    public static event Action<VersionPublicada>? HayNueva;

    private static HttpClient CrearCliente()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub exige un User-Agent; no se envía nada del usuario.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SimuladorMultiple-Actualizador");
        return http;
    }

    public static Version Normalizar(Version? v) =>
        v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Lee "v0.1.154" (o "0.1.154") como versión.</summary>
    public static Version? LeerEtiqueta(string? etiqueta)
    {
        if (string.IsNullOrWhiteSpace(etiqueta)) return null;
        var t = etiqueta.Trim().TrimStart('v', 'V');
        return Version.TryParse(t, out var v) ? Normalizar(v) : null;
    }

    public static bool EsMasNueva(Version publicada, Version actual) => Normalizar(publicada) > Normalizar(actual);

    /// <summary>Interpreta la respuesta de la API de GitHub para la última versión publicada.</summary>
    public static VersionPublicada? LeerRespuesta(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        var version = LeerEtiqueta(raiz.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null);
        if (version is null) return null;
        string? url = null;
        long bytes = 0;
        if (raiz.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (!string.Equals(a.GetProperty("name").GetString(), NombreInstalador, StringComparison.OrdinalIgnoreCase)) continue;
                url = a.GetProperty("browser_download_url").GetString();
                bytes = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
            }
        }
        var pagina = raiz.TryGetProperty("html_url", out var h) ? h.GetString() ?? PaginaDescargas : PaginaDescargas;
        return new VersionPublicada(version, url, bytes, pagina);
    }

    /// <summary>Pregunta a GitHub cuál es la última versión publicada.</summary>
    public static async Task<VersionPublicada?> BuscarAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ApiUltima);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var publicada = LeerRespuesta(json);
        if (publicada is not null && EsMasNueva(publicada.Version, VersionActual))
        {
            var primeraVez = Disponible is null || Disponible.Version != publicada.Version;
            Disponible = publicada;
            if (primeraVez) HayNueva?.Invoke(publicada);
        }
        return publicada;
    }

    /// <summary>Comprobación silenciosa al arrancar: sin conexión o con error, no dice nada.</summary>
    public static async Task BuscarEnSegundoPlanoAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
            await BuscarAsync().ConfigureAwait(false);
        }
        catch
        {
            // Sin internet o GitHub no responde: ya se podrá buscar a mano desde Ayuda.
        }
    }

    /// <summary>Descarga el instalador a la carpeta temporal e informa del avance (0–1).</summary>
    public static async Task<string> DescargarAsync(VersionPublicada v, IProgress<double> avance, CancellationToken ct)
    {
        if (v.Instalador is null) throw new InvalidOperationException("Esta versión no tiene instalador para descargar.");
        var carpeta = Path.Combine(Path.GetTempPath(), "SimuladorMultiple-actualizacion");
        Directory.CreateDirectory(carpeta);
        var destino = Path.Combine(carpeta, $"SimuladorMultiple-Setup-{v.Version}.exe");
        var parcial = destino + ".descargando";

        using (var resp = await Http.GetAsync(v.Instalador, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? v.Bytes;
            await using var origen = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var archivo = File.Create(parcial);
            var buffer = new byte[81920];
            long leidos = 0;
            int n;
            while ((n = await origen.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await archivo.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                leidos += n;
                if (total > 0) avance.Report(Math.Min(1.0, (double)leidos / total));
            }
            if (total > 0 && leidos != total) throw new IOException("La descarga se cortó antes de terminar. Inténtalo de nuevo.");
        }
        File.Move(parcial, destino, overwrite: true);
        return destino;
    }

    /// <summary>
    /// Abre el instalador en modo silencioso (solo enseña la barra de progreso). El instalador cierra
    /// el programa si sigue abierto, instala encima en la misma carpeta y lo vuelve a abrir (/RELANZAR=1).
    /// </summary>
    public static void Instalar(string instalador)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = instalador,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELANZAR=1",
            UseShellExecute = true,
        });
    }

    /// <summary>Abre una página en el navegador.</summary>
    public static void AbrirEnNavegador(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { /* sin navegador predeterminado */ }
    }
}
