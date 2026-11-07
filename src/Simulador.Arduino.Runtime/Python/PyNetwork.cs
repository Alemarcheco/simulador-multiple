using System.Globalization;
using System.Text;

namespace Simulador.Arduino.Runtime.Python;

/// <summary>
/// Wi‑Fi del ESP32: módulos network, socket, urequests y json. Las redes, la IP y el navegador del simulador están en
/// <see cref="WifiWorld"/>; aquí está lo que ve el programa de MicroPython.
/// </summary>
internal sealed partial class PyInterpreter
{
    internal const long StatIdle = 1000, StatConnecting = 1001, StatGotIp = 1010,
        StatNoApFound = 201, StatWrongPassword = 202, StatBeaconTimeout = 200, StatHandshakeTimeout = 204;

    /// <summary>Lo que tarda en conectarse a una red (asociación + DHCP), en ticks de 100 ns.</summary>
    private const long ConnectTicks = 15_000_000;

    private WifiWorld? Wifi { get; }

    // Conexión en curso: se resuelve cuando pasa ConnectTicks, aunque el programa no pregunte.
    private (WlanObj Wlan, long At)? _wifiPending;

    private void InitNetworkModules()
    {
        Func<PyModuleObj> needWifi(Func<PyModuleObj> factory, string name) => () =>
            Wifi is null
                ? throw new PyError("ImportError", $"no existe el módulo '{name}': {Board.Name} no tiene Wi‑Fi (la Pico W sí). Prueba con el ESP32.")
                : factory();
        _moduleFactories["network"] = needWifi(NetworkModule, "network");
        _moduleFactories["socket"] = needWifi(() => SocketModule("socket"), "socket");
        _moduleFactories["usocket"] = needWifi(() => SocketModule("usocket"), "usocket");
        _moduleFactories["urequests"] = needWifi(() => RequestsModule("urequests"), "urequests");
        _moduleFactories["requests"] = needWifi(() => RequestsModule("requests"), "requests");
        _moduleFactories["json"] = () => JsonModule("json");
        _moduleFactories["ujson"] = () => JsonModule("ujson");
    }

    private void PollWifi()
    {
        if (_wifiPending is { } p && Host.Now >= p.At)
        {
            _wifiPending = null;
            p.Wlan.FinishConnect();
        }
    }

    private static PyError OsError(string msg) => new("OSError", msg);

    // ---- network ----

    private WlanObj? _sta, _ap;

    private PyModuleObj NetworkModule()
    {
        var wlanType = new PyType("WLAN", a =>
        {
            long iface = a.Has(0, "interface_id") ? PyOps.ToLong(a.Get(0, "interface_id")) : 0;
            if (iface is not (0 or 1)) throw new PyError("ValueError", "interfaz Wi‑Fi no válida: usa network.STA_IF o network.AP_IF");
            // Como en la placa, WLAN(STA_IF) devuelve siempre el mismo objeto.
            return iface == 0 ? _sta ??= new WlanObj(this, 0) : _ap ??= new WlanObj(this, 1);
        }, new Dictionary<string, object?> { ["IF_STA"] = 0L, ["IF_AP"] = 1L });
        string hostname = "esp32-simulador";
        return Module("network",
            ("WLAN", wlanType),
            ("STA_IF", 0L), ("AP_IF", 1L),
            ("STAT_IDLE", StatIdle), ("STAT_CONNECTING", StatConnecting), ("STAT_GOT_IP", StatGotIp),
            ("STAT_NO_AP_FOUND", StatNoApFound), ("STAT_WRONG_PASSWORD", StatWrongPassword),
            ("STAT_BEACON_TIMEOUT", StatBeaconTimeout), ("STAT_HANDSHAKE_TIMEOUT", StatHandshakeTimeout),
            ("AUTH_OPEN", 0L), ("AUTH_WEP", 1L), ("AUTH_WPA_PSK", 2L), ("AUTH_WPA2_PSK", 3L), ("AUTH_WPA_WPA2_PSK", 4L),
            ("hostname", F("hostname", a => { if (a.Pos.Count > 0) hostname = PyOps.Str(a.Pos[0]); return a.Pos.Count > 0 ? null : hostname; })),
            ("country", F("country", a => a.Pos.Count > 0 ? null : "ES")));
    }

    internal sealed class WlanObj(PyInterpreter py, long iface) : PyNative
    {
        private bool _active;
        private long _status = StatIdle;
        private WifiNetwork? _target;
        private string? _key;
        private string _apEssid = "ESP32-Simulador", _apPassword = "";

        private WifiWorld W => py.Wifi!;
        private bool IsSta => iface == 0;

        public override string TypeName => "WLAN";
        public override string Repr() => $"<WLAN {(IsSta ? "STA_IF" : "AP_IF")}>";

        public override bool HasAttr(string name) => GetAttr(name) is not null;

        private void NeedActive(string what)
        {
            if (!_active) throw OsError($"Wifi Not Started: activa la interfaz con wlan.active(True) antes de {what}");
        }

        public void FinishConnect()
        {
            if (_target is null) return;
            if (!_active) { _status = StatIdle; return; }
            if (!_target.Open && _target.Password != _key)
            {
                _status = StatWrongPassword;
                py.Host.SerialWrite($"[Wi‑Fi] «{_target.Ssid}»: contraseña incorrecta\n");
                return;
            }
            _status = StatGotIp;
            W.ConnectedSsid = _target.Ssid;
        }

        private object? Active(PyArgs a)
        {
            if (a.Pos.Count == 0) return _active;
            _active = PyOps.Truthy(a.Pos[0]);
            if (IsSta && !_active) Disconnect();
            if (!IsSta) W.AccessPoint = _active ? _apEssid : null;
            py.Spend(500_000);
            return null;
        }

        private void Disconnect()
        {
            if (py._wifiPending?.Wlan == this) py._wifiPending = null;
            _target = null;
            if (W.ConnectedSsid is not null) W.ConnectedSsid = null;
            _status = StatIdle;
        }

        private object? Connect(PyArgs a)
        {
            if (!IsSta) throw OsError("el punto de acceso (AP_IF) no se conecta a otra red; usa WLAN(network.STA_IF)");
            NeedActive("connect()");
            string ssid = PyOps.Str(a.Get(0, "ssid") ?? throw new PyError("TypeError", "connect() necesita el nombre de la red (ssid)"));
            object? key = a.Get(1, "key") ?? a.Get(1, "password");
            Disconnect();
            _key = key is null ? "" : PyOps.Str(key);
            _target = W.Networks.FirstOrDefault(n => n.Ssid == ssid);
            if (_target is null)
            {
                _status = StatNoApFound;
                py.Host.SerialWrite($"[Wi‑Fi] no se encuentra la red «{ssid}». Redes del simulador: {string.Join(", ", W.Networks.Select(n => n.Ssid))}\n");
                return null;
            }
            _status = StatConnecting;
            py._wifiPending = (this, py.Host.Now + ConnectTicks);
            py.Spend(10_000);
            return null;
        }

        private object? Scan()
        {
            if (!IsSta) throw OsError("scan() solo funciona en WLAN(network.STA_IF)");
            NeedActive("scan()");
            py.Sleep(12_000_000); // un barrido de canales dura poco más de un segundo
            return new PyList(W.Networks.OrderByDescending(n => n.Rssi).Select((n, i) => (object?)new PyTuple(
            [
                n.Ssid,
                new string([(char)0x24, (char)0x0A, (char)0xC4, (char)0x10, (char)(0x20 + i), (char)0x5E]),
                (long)n.Channel, (long)n.Rssi, n.Open ? 0L : 3L, false,
            ])).ToList());
        }

        private object? Status(PyArgs a)
        {
            py.PollWifi();
            if (a.Pos.Count == 0) return IsSta ? _status : (_active ? StatGotIp : StatIdle);
            string what = PyOps.Str(a.Pos[0]);
            return what switch
            {
                "rssi" when IsSta && _status == StatGotIp => (long)_target!.Rssi,
                "rssi" => throw OsError("no está conectado: no hay intensidad de señal (rssi)"),
                "stations" => new PyList([]),
                _ => throw new PyError("ValueError", $"status('{what}') no existe; prueba status() o status('rssi')"),
            };
        }

        private (string Ip, string Mask, string Gw, string Dns) Addr()
        {
            if (!IsSta) return _active ? (WifiWorld.ApIp, "255.255.255.0", WifiWorld.ApIp, "0.0.0.0") : ("0.0.0.0", "0.0.0.0", "0.0.0.0", "0.0.0.0");
            return _status == StatGotIp ? (WifiWorld.StaIp, "255.255.255.0", "192.168.1.1", "192.168.1.1") : ("0.0.0.0", "0.0.0.0", "0.0.0.0", "0.0.0.0");
        }

        private object? Config(PyArgs a)
        {
            if (a.Pos.Count == 1 && a.Kw.Count == 0)
            {
                string p = PyOps.Str(a.Pos[0]);
                return p switch
                {
                    "mac" => IsSta ? "$\nÄ\u0010 ^" : "$\nÄ\u0010 _",
                    "essid" or "ssid" => IsSta ? _target?.Ssid ?? "" : _apEssid,
                    "channel" => (long)(IsSta ? _target?.Channel ?? 1 : 1),
                    "hostname" or "dhcp_hostname" => "esp32-simulador",
                    "authmode" => IsSta ? (_target is { Open: false } ? 3L : 0L) : (_apPassword.Length > 0 ? 3L : 0L),
                    "txpower" => 20L,
                    "max_clients" => 4L,
                    _ => throw new PyError("ValueError", $"config('{p}') no existe en el simulador"),
                };
            }
            foreach (var (k, v) in a.Kw)
            {
                switch (k)
                {
                    case "essid" or "ssid": _apEssid = PyOps.Str(v); break;
                    case "password" or "key": _apPassword = PyOps.Str(v); break;
                    case "authmode" or "channel" or "hidden" or "max_clients" or "txpower" or "pm" or "reconnects" or "hostname" or "dhcp_hostname": break;
                    default: throw new PyError("ValueError", $"config({k}=…) no existe en el simulador");
                }
            }
            if (!IsSta && _apPassword.Length is > 0 and < 8)
                throw new PyError("ValueError", "la contraseña de un punto de acceso WPA2 necesita al menos 8 caracteres");
            if (!IsSta && _active) W.AccessPoint = _apEssid;
            return null;
        }

        public override object? GetAttr(string name) => name switch
        {
            "active" => F(name, Active),
            "connect" => F(name, Connect),
            "disconnect" => F(name, _ => { Disconnect(); return null; }),
            "isconnected" => F(name, _ =>
            {
                py.PollWifi();
                return IsSta ? _status == StatGotIp : _active;
            }),
            "scan" => F(name, _ => Scan()),
            "status" => F(name, Status),
            "ifconfig" => F(name, a =>
            {
                py.PollWifi();
                if (a.Pos.Count > 0) return null; // IP fija: el simulador sigue usando la suya
                var (ip, mask, gw, dns) = Addr();
                return new PyTuple([ip, mask, gw, dns]);
            }),
            "ipconfig" => F(name, a =>
            {
                py.PollWifi();
                var (ip, mask, gw, dns) = Addr();
                return a.Pos.Count == 0 ? null : PyOps.Str(a.Pos[0]) switch
                {
                    "addr4" => new PyTuple([ip, mask]),
                    "gw4" => gw,
                    "dns" => dns,
                    "dhcp4" => true,
                    var p => throw new PyError("ValueError", $"ipconfig('{p}') no existe en el simulador"),
                };
            }),
            "config" => F(name, Config),
            _ => null,
        };
    }

    // ---- socket ----

    private PyModuleObj SocketModule(string name)
    {
        var sockType = new PyType("socket", _ => new SocketObj(this), []);
        return Module(name,
            ("socket", sockType),
            ("AF_INET", 2L), ("AF_INET6", 10L), ("SOCK_STREAM", 1L), ("SOCK_DGRAM", 2L), ("IPPROTO_TCP", 6L),
            ("SOL_SOCKET", 1L), ("SO_REUSEADDR", 4L),
            ("getaddrinfo", F("getaddrinfo", a =>
            {
                string host = PyOps.Str(Arg(a, 0, "getaddrinfo"));
                long port = PyOps.ToLong(Arg(a, 1, "getaddrinfo"));
                string ip = host switch
                {
                    "" or "0.0.0.0" => "0.0.0.0",
                    WifiWorld.VirtualHost => "192.168.1.2",
                    _ when System.Net.IPAddress.TryParse(host, out _) => host,
                    _ => Wifi!.ConnectedSsid is null ? throw OsError("-202: sin conexión a Internet, no se puede resolver " + host) : "93.184.216.34",
                };
                return new PyList([new PyTuple([2L, 1L, 0L, "", new PyTuple([ip, port])])]);
            })));
    }

    internal sealed class SocketObj(PyInterpreter py) : PyNative
    {
        private int _port = -1;
        private double? _timeout;
        private bool _listening;

        public override string TypeName => "socket";
        public override string Repr() => $"<socket state={(_listening ? "listen" : "0")}>";
        public override bool HasAttr(string name) => GetAttr(name) is not null;

        private object? Accept()
        {
            if (!_listening) throw OsError("EINVAL: llama a listen() antes de accept()");
            long start = py.Host.Now;
            while (true)
            {
                if (py.Wifi!.NextRequest(_port) is { } ex)
                {
                    py.Spend(20_000);
                    return new PyTuple([new ConnObj(py, ex), new PyTuple(["192.168.1.10", 50_000L + Random.Shared.Next(9999)])]);
                }
                if (_timeout is { } t && py.Host.Now - start >= (long)(t * 10_000_000))
                    throw OsError("ETIMEDOUT: ningún navegador se ha conectado");
                py.Sleep(10_000); // espera 1 ms y atiende interrupciones mientras tanto
            }
        }

        public override object? GetAttr(string name) => name switch
        {
            "setsockopt" => F(name, _ => null),
            "setblocking" => F(name, a => { _timeout = a.Pos.Count > 0 && !PyOps.Truthy(a.Pos[0]) ? 0 : null; return null; }),
            "settimeout" => F(name, a => { _timeout = a.Pos.Count == 0 || a.Pos[0] is null ? null : PyOps.ToDouble(a.Pos[0]); return null; }),
            "bind" => F(name, a =>
            {
                if (Arg(a, 0, "bind") is not PyTuple { Items.Length: 2 } t)
                    throw new PyError("TypeError", "bind() necesita una tupla (dirección, puerto), p. ej. ('0.0.0.0', 80)");
                _port = (int)PyOps.ToLong(t.Items[1]);
                return null;
            }),
            "listen" => F(name, _ =>
            {
                if (_port < 0) _port = 80;
                if (py.Wifi!.ConnectedSsid is null && py.Wifi.AccessPoint is null)
                    py.Host.SerialWrite("[Wi‑Fi] aviso: el servidor escucha, pero el ESP32 aún no está conectado a ninguna red\n");
                _listening = true;
                py.Wifi.ListeningPort = _port;
                return null;
            }),
            "accept" => F(name, _ => Accept()),
            "connect" => F(name, _ => throw OsError("en el simulador las peticiones a otros servidores se hacen con urequests.get(url)")),
            "close" => F(name, _ =>
            {
                if (_listening && py.Wifi!.ListeningPort == _port) py.Wifi.ListeningPort = null;
                _listening = false;
                return null;
            }),
            "fileno" => F(name, _ => 54L),
            _ => null,
        };
    }

    /// <summary>Conexión aceptada: una petición del navegador del simulador.</summary>
    internal sealed class ConnObj(PyInterpreter py, HttpExchange ex) : PyNative
    {
        private int _read;
        private bool _closed;

        public override string TypeName => "socket";
        public override bool HasAttr(string name) => GetAttr(name) is not null;

        private object? Recv(PyArgs a)
        {
            var text = ex.RequestText;
            int n = a.Pos.Count > 0 ? (int)Math.Max(1, PyOps.ToLong(a.Pos[0])) : text.Length;
            int take = Math.Min(n, text.Length - _read);
            var chunk = take <= 0 ? "" : text.Substring(_read, take);
            _read += Math.Max(0, take);
            py.Spend(5_000);
            return chunk;
        }

        private object? ReadLine()
        {
            var text = ex.RequestText;
            if (_read >= text.Length) return "";
            int nl = text.IndexOf('\n', _read);
            int end = nl < 0 ? text.Length : nl + 1;
            var line = text[_read..end];
            _read = end;
            return line;
        }

        private object? Send(PyArgs a)
        {
            if (_closed) throw OsError("EBADF: la conexión ya está cerrada");
            var s = PyOps.Str(Arg(a, 0, "send"));
            ex.Append(s);
            py.Spend(10_000 + s.Length * 10); // ≈1 µs por byte por la red
            return (long)s.Length;
        }

        public override object? GetAttr(string name) => name switch
        {
            "recv" or "read" => F(name, Recv),
            "readline" => F(name, _ => ReadLine()),
            "send" or "sendall" or "write" => F(name, Send),
            "makefile" => F(name, _ => this),
            "settimeout" or "setblocking" or "setsockopt" => F(name, _ => null),
            "close" => F(name, _ =>
            {
                if (!_closed) { _closed = true; ex.Complete(); }
                return null;
            }),
            _ => null,
        };
    }

    // ---- urequests ----

    private PyModuleObj RequestsModule(string name)
    {
        PyBuiltin Verb(string method) => F(method.ToLowerInvariant(), a => Request(method, a, 0));
        return Module(name,
            ("get", Verb("GET")), ("post", Verb("POST")), ("put", Verb("PUT")),
            ("patch", Verb("PATCH")), ("delete", Verb("DELETE")), ("head", Verb("HEAD")),
            ("request", F("request", a => Request(PyOps.Str(Arg(a, 0, "request")).ToUpperInvariant(), a, 1))));
    }

    private object? Request(string method, PyArgs a, int first)
    {
        string url = PyOps.Str(a.Get(first, "url") ?? throw new PyError("TypeError", "falta la dirección (url)"));
        if (Wifi!.ConnectedSsid is null)
            throw OsError("-202: el ESP32 no está conectado a ninguna red Wi‑Fi (conéctalo antes con network.WLAN)");
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new PyError("ValueError", $"URL no válida: {url} (tiene que empezar por http:// o https://)");
        string? body = null;
        if (a.Kw.TryGetValue("json", out var j) && j is not null) body = JsonDumps(j);
        else if ((a.Kw.TryGetValue("data", out var d) || a.Pos.Count > first + 1) && (d ?? a.Get(first + 1, "data")) is { } data) body = PyOps.Str(data);
        bool local = url.Contains("://" + WifiWorld.VirtualHost, StringComparison.OrdinalIgnoreCase);
        double simSeconds = Host.Now / 10_000_000.0;
        // simulador.local responde al momento; Internet va en otro hilo para no congelar el simulador.
        var task = local ? Task.FromResult(Wifi.Fetch(method, url, body, simSeconds)) : Task.Run(() => Wifi.Fetch(method, url, body, simSeconds));
        // Mientras llega la respuesta el reloj de la placa sigue corriendo (y se atienden las interrupciones).
        var wall = System.Diagnostics.Stopwatch.StartNew();
        Sleep(local ? 300_000 : 1_500_000);
        while (!task.IsCompleted && wall.ElapsedMilliseconds < 10_000) Sleep(100_000);
        if (!task.IsCompleted) throw OsError("ETIMEDOUT: el servidor no responde");
        if (task.IsFaulted)
        {
            var e = task.Exception!.GetBaseException();
            throw OsError(e is HttpRequestException or TaskCanceledException or InvalidOperationException
                ? $"no se pudo conectar con {url}: {e.Message}" : e.Message);
        }
        var (status, text) = task.Result;
        return new ResponseObj(this, status, method == "HEAD" ? "" : text);
    }

    internal sealed class ResponseObj(PyInterpreter py, int status, string text) : PyNative
    {
        public override string TypeName => "Response";
        public override string Repr() => $"<Response [{status}]>";
        public override bool HasAttr(string name) => GetAttr(name) is not null;

        public override object? GetAttr(string name) => name switch
        {
            "status_code" => (long)status,
            "reason" => status switch { 200 => "OK", 201 => "Created", 404 => "Not Found", 500 => "Internal Server Error", _ => "" },
            "text" => text,
            "content" => text,
            "headers" => new PyDict(),
            "json" => F(name, _ => py.JsonLoads(text)),
            "close" => F(name, _ => null),
            _ => null,
        };
    }

    // ---- json ----

    private PyModuleObj JsonModule(string name) => Module(name,
        ("loads", F("loads", a => JsonLoads(PyOps.Str(Arg(a, 0, "loads"))))),
        ("dumps", F("dumps", a => JsonDumps(Arg(a, 0, "dumps")))),
        ("load", F("load", a => JsonLoads(PyOps.Str(Call(GetAttr(Arg(a, 0, "load"), "read"), new PyArgs([], PyArgs.NoKw)))))),
        ("dump", F("dump", a =>
        {
            Call(GetAttr(Arg(a, 1, "dump"), "write"), new PyArgs([JsonDumps(Arg(a, 0, "dump"))], PyArgs.NoKw));
            return null;
        })));

    internal string JsonDumps(object? v)
    {
        var sb = new StringBuilder();
        void W(object? x, int depth)
        {
            if (depth > 50) throw new PyError("ValueError", "estructura demasiado anidada para JSON");
            switch (x)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append(double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity");
                    else sb.Append(PyOps.Repr(d));
                    break;
                case string s: Quote(sb, s); break;
                case PyList list: Seq(list.Items, depth); break;
                case PyTuple t: Seq(t.Items, depth); break;
                case PyDict dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (var (k, val) in dict.Items)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        Quote(sb, k is string ks ? ks : PyOps.Str(k));
                        sb.Append(": ");
                        W(val, depth + 1);
                    }
                    sb.Append('}');
                    break;
                default: throw new PyError("TypeError", $"un objeto {PyOps.TypeName(x)} no se puede pasar a JSON");
            }
        }
        void Seq(IEnumerable<object?> items, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (var i in items)
            {
                if (!first) sb.Append(", ");
                first = false;
                W(i, depth + 1);
            }
            sb.Append(']');
        }
        W(v, 0);
        return sb.ToString();
    }

    private static void Quote(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    internal object? JsonLoads(string text)
    {
        int i = 0;
        PyError Bad(string what) => new("ValueError", $"JSON no válido: {what} (posición {i})");
        void Ws() { while (i < text.Length && char.IsWhiteSpace(text[i])) i++; }
        object? Value(int depth)
        {
            if (depth > 50) throw Bad("demasiado anidado");
            Ws();
            if (i >= text.Length) throw Bad("se esperaba un valor");
            char c = text[i];
            if (c == '{')
            {
                i++;
                var d = new PyDict();
                Ws();
                if (i < text.Length && text[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws();
                    if (i >= text.Length || text[i] != '"') throw Bad("se esperaba una clave entre comillas");
                    string key = Str();
                    Ws();
                    if (i >= text.Length || text[i] != ':') throw Bad("falta ':'");
                    i++;
                    d.Items[key] = Value(depth + 1);
                    Ws();
                    if (i < text.Length && text[i] == ',') { i++; continue; }
                    if (i < text.Length && text[i] == '}') { i++; return d; }
                    throw Bad("falta ',' o '}'");
                }
            }
            if (c == '[')
            {
                i++;
                var l = new PyList([]);
                Ws();
                if (i < text.Length && text[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Items.Add(Value(depth + 1));
                    Ws();
                    if (i < text.Length && text[i] == ',') { i++; continue; }
                    if (i < text.Length && text[i] == ']') { i++; return l; }
                    throw Bad("falta ',' o ']'");
                }
            }
            if (c == '"') return Str();
            if (Word("true")) return true;
            if (Word("false")) return false;
            if (Word("null")) return null;
            int start = i;
            if (i < text.Length && (text[i] == '-' || text[i] == '+')) i++;
            bool isFloat = false;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] is '.' or 'e' or 'E' || (text[i] is '-' or '+' && text[i - 1] is 'e' or 'E')))
            {
                if (text[i] is '.' or 'e' or 'E') isFloat = true;
                i++;
            }
            var num = text[start..i];
            if (num.Length == 0 || num is "-" or "+") throw Bad($"carácter inesperado '{c}'");
            if (!isFloat && long.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var lv)) return lv;
            if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv)) return dv;
            throw Bad($"número no válido '{num}'");
        }
        bool Word(string w)
        {
            if (string.CompareOrdinal(text, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }
        string Str()
        {
            i++; // comilla
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= text.Length) throw Bad("texto sin cerrar");
                char c = text[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= text.Length) throw Bad("escape incompleto");
                char e = text[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > text.Length || !int.TryParse(text.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                            throw Bad("escape \\u no válido");
                        sb.Append((char)cp);
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }
        var result = Value(0);
        Ws();
        if (i < text.Length) throw Bad("sobra texto al final");
        return result;
    }
}
