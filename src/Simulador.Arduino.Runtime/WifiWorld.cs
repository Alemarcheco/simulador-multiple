using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Simulador.Arduino.Runtime;

/// <summary>Red Wi‑Fi simulada que ve el ESP32 al hacer scan().</summary>
public sealed record WifiNetwork(string Ssid, string Password, int Rssi, int Channel)
{
    public bool Open => Password.Length == 0;
}

/// <summary>Petición del navegador del simulador al servidor web del ESP32, y su respuesta.</summary>
public sealed class HttpExchange(string method, string path, int port, string host, long createdTicks)
{
    public string Method { get; } = method;
    public string Path { get; } = path;
    public int Port { get; } = port;
    public string Host { get; } = host;
    /// <summary>Instante (tiempo de la placa) en que se hizo la petición, para el tiempo de espera.</summary>
    public long CreatedTicks { get; } = createdTicks;

    public string RequestText =>
        $"{Method} {Path} HTTP/1.1\r\nHost: {Host}\r\nUser-Agent: Simulador\r\nAccept: text/html\r\nConnection: close\r\n\r\n";

    private readonly StringBuilder _raw = new();
    private volatile bool _done;
    public bool Done => _done;
    public bool Accepted { get; internal set; }
    public string? Error { get; private set; }
    public string Raw { get { lock (_raw) return _raw.ToString(); } }

    internal void Append(string s) { lock (_raw) _raw.Append(s); }

    internal void Complete() => _done = true;

    public void Fail(string error)
    {
        Error = error;
        _done = true;
    }

    /// <summary>Código de estado de la respuesta (200, 404…), o null si no tiene línea de estado.</summary>
    public int? Status
    {
        get
        {
            var raw = Raw;
            if (!raw.StartsWith("HTTP/", StringComparison.Ordinal)) return null;
            var parts = raw.Split(' ', 3);
            return parts.Length > 1 && int.TryParse(parts[1], out var s) ? s : null;
        }
    }

    /// <summary>Cuerpo de la respuesta (lo que va tras la primera línea vacía), o todo si no lleva cabeceras HTTP.</summary>
    public string Body
    {
        get
        {
            var raw = Raw;
            if (!raw.StartsWith("HTTP/", StringComparison.Ordinal)) return raw;
            int i = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            int j = raw.IndexOf("\n\n", StringComparison.Ordinal);
            if (i >= 0 && (j < 0 || i < j)) return raw[(i + 4)..];
            return j >= 0 ? raw[(j + 2)..] : "";
        }
    }
}

/// <summary>
/// El Wi‑Fi del ESP32 simulado: redes a las que se puede conectar, su dirección IP, las peticiones del navegador del
/// simulador a su servidor web y las peticiones HTTP que hace el programa (a «simulador.local» o a Internet).
/// </summary>
public sealed class WifiWorld
{
    public const string StaIp = "192.168.1.50";
    public const string ApIp = "192.168.4.1";
    public const string VirtualHost = "simulador.local";

    private readonly ConcurrentQueue<HttpExchange> _pending = new();

    public List<WifiNetwork> Networks { get; } =
    [
        new("Casa_WiFi", "clave1234", -48, 6),
        new("Taller", "", -63, 1),
        new("Vecino_5G", "no-la-sabes", -82, 11),
    ];

    /// <summary>Red a la que está conectado en modo estación, o null.</summary>
    public string? ConnectedSsid { get; internal set; }

    /// <summary>Nombre del punto de acceso propio (modo AP activo), o null.</summary>
    public string? AccessPoint { get; internal set; }

    /// <summary>Puerto en el que escucha el servidor del programa (socket.listen), o null.</summary>
    public int? ListeningPort { get; internal set; }

    /// <summary>Si las direcciones que no son de simulador.local se piden de verdad a Internet.</summary>
    public bool AllowInternet { get; set; } = true;

    /// <summary>Para pruebas: responde a las peticiones a Internet sin salir del ordenador.</summary>
    public Func<string, string, string?, (int Status, string Body)>? InternetHandler { get; set; }

    public string Describe()
    {
        var parts = new List<string>();
        if (ConnectedSsid is not null) parts.Add($"conectado a «{ConnectedSsid}», IP {StaIp}");
        if (AccessPoint is not null) parts.Add($"punto de acceso «{AccessPoint}», IP {ApIp}");
        if (ListeningPort is { } p) parts.Add($"servidor web escuchando en el puerto {p}");
        return parts.Count == 0 ? "sin conexión" : string.Join(" · ", parts);
    }

    internal void Reset()
    {
        ConnectedSsid = null;
        AccessPoint = null;
        ListeningPort = null;
        while (_pending.TryDequeue(out var x)) x.Fail("La placa se ha reiniciado.");
    }

    /// <summary>
    /// Pide una página al ESP32 desde el navegador del simulador. La responde el programa cuando hace accept() en su
    /// socket. Devuelve la petición, que se completa más tarde (o falla al momento si la dirección no es la de la placa).
    /// </summary>
    public HttpExchange Browse(string url, long nowTicks)
    {
        url = url.Trim();
        if (!url.Contains("://", StringComparison.Ordinal)) url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "http")
        {
            var bad = new HttpExchange("GET", "/", 80, url, nowTicks);
            bad.Fail("Dirección no válida. Escribe algo como http://192.168.1.50/");
            return bad;
        }
        var ex = new HttpExchange("GET", uri.PathAndQuery, uri.Port, uri.Host, nowTicks);
        bool reachable = (uri.Host == StaIp && ConnectedSsid is not null) || (uri.Host == ApIp && AccessPoint is not null);
        if (!reachable)
        {
            ex.Fail(ConnectedSsid is null && AccessPoint is null
                ? "No se puede conectar: el ESP32 no está conectado a ninguna red Wi‑Fi."
                : $"No se encuentra {uri.Host}. La placa está en {(ConnectedSsid is not null ? StaIp : ApIp)}.");
            return ex;
        }
        _pending.Enqueue(ex);
        return ex;
    }

    internal HttpExchange? NextRequest(int port)
    {
        // Las peticiones a otro puerto se rechazan (nadie escucha allí).
        while (_pending.TryPeek(out var ex) && ex.Port != port)
        {
            _pending.TryDequeue(out _);
            ex.Fail($"Nadie escucha en el puerto {ex.Port} (el programa usa el {port}).");
        }
        if (!_pending.TryDequeue(out var next)) return null;
        next.Accepted = true;
        return next;
    }

    /// <summary>Petición HTTP del programa (urequests). simulador.local responde con datos de prueba.</summary>
    internal (int Status, string Body) Fetch(string method, string url, string? body, double simSeconds)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new ArgumentException($"URL no válida: {url}");
        if (uri.Host == VirtualHost) return Virtual(uri.AbsolutePath, simSeconds);
        if (InternetHandler is not null) return InternetHandler(method, url, body);
        if (!AllowInternet) throw new InvalidOperationException("el acceso a Internet está desactivado en el simulador");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SimuladorMultiple-ESP32/1.0");
        using var msg = new HttpRequestMessage(new HttpMethod(method), uri);
        if (body is not null) msg.Content = new StringContent(body, Encoding.UTF8, body.TrimStart().StartsWith('{') ? "application/json" : "text/plain");
        using var resp = client.Send(msg);
        using var reader = new StreamReader(resp.Content.ReadAsStream());
        var text = reader.ReadToEnd();
        if (text.Length > 64_000) text = text[..64_000];
        return ((int)resp.StatusCode, text);
    }

    private static (int, string) Virtual(string path, double t)
    {
        var inv = CultureInfo.InvariantCulture;
        // Datos que cambian despacio con el tiempo de simulación, para que se vea algo vivo.
        double temp = 24 + 4 * Math.Sin(t / 60), hum = 60 + 10 * Math.Cos(t / 90);
        return path.TrimEnd('/') switch
        {
            "/api/tiempo" => (200, string.Format(inv, "{{\"ciudad\": \"Simulópolis\", \"temperatura\": {0:0.0}, \"humedad\": {1:0}, \"cielo\": \"{2}\"}}",
                temp, hum, temp > 26 ? "soleado" : "nubes y claros")),
            "/api/hora" => (200, string.Format(inv, "{{\"segundos\": {0:0}, \"texto\": \"{1:hh\\:mm\\:ss}\"}}", t, TimeSpan.FromSeconds(t))),
            "/api/chiste" => (200, "{\"chiste\": \"¿Qué le dice un bit a otro? Nos vemos en el bus.\"}"),
            "" or "/index.html" => (200, "<html><body><h1>simulador.local</h1><p>Prueba /api/tiempo, /api/hora o /api/chiste.</p></body></html>"),
            _ => (404, "{\"error\": \"no existe\"}"),
        };
    }
}
