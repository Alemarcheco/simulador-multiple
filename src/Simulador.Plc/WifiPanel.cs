using System.Net;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Simulador.Arduino.Runtime;

namespace Simulador.Plc;

/// <summary>
/// Wi‑Fi del ESP32: estado de la conexión, redes del simulador y un navegador sencillo para abrir las páginas que
/// sirve el programa (como si fuera el móvil conectado a la misma red).
/// </summary>
internal sealed partial class WifiPanel : StackPanel
{
    /// <summary>Lo que espera el navegador a que el programa acepte la conexión, en tiempo de la placa.</summary>
    public static readonly TimeSpan BrowserTimeout = TimeSpan.FromSeconds(3);

    private readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _networks = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8 };
    private readonly TextBox _url = new() { Text = "http://" + WifiWorld.StaIp + "/", FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace") };
    private readonly TextBlock _pageStatus = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _page = new() { TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _links = new();
    private ArduinoMcu? _mcu;
    private HttpExchange? _pending;
    private Uri? _pendingUri;
    private string _lastState = "";

    public WifiPanel()
    {
        Spacing = 6;
        var open = new Button { Content = "Abrir", Margin = new Thickness(6, 0, 0, 0) };
        open.Click += (_, _) => Browse(_url.Text ?? "");
        _url.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Browse(_url.Text ?? ""); };
        var bar = new DockPanel();
        DockPanel.SetDock(open, Dock.Right);
        bar.Children.Add(open);
        bar.Children.Add(_url);

        var page = new StackPanel { Spacing = 6 };
        page.Children.Add(_pageStatus);
        page.Children.Add(_page);
        page.Children.Add(_links);
        var browser = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = page,
        };
        _pageStatus.Text = "Navegador del simulador: abre la IP del ESP32 cuando el programa tenga un servidor web.";

        Children.Add(new TextBlock { Text = "Wi‑Fi y navegador", FontSize = 16, Margin = new Thickness(0, 6, 0, 0) });
        Children.Add(_state);
        Children.Add(_networks);
        Children.Add(bar);
        Children.Add(browser);
    }

    public ArduinoMcu? Mcu
    {
        get => _mcu;
        set
        {
            _mcu = value;
            IsVisible = value?.Wifi is not null;
            _pending = null;
            _page.Text = "";
            _links.Children.Clear();
            _lastState = "";
            if (value?.Wifi is { } w)
                _networks.Text = "Redes al alcance: " + string.Join(" · ", w.Networks.Select(n =>
                    $"{n.Ssid} ({n.Rssi} dBm, {(n.Open ? "abierta" : "clave «" + n.Password + "»")})"))
                    + $". Servidor de pruebas: http://{WifiWorld.VirtualHost}/api/tiempo, /api/hora y /api/chiste.";
            Refresh();
        }
    }

    public void Browse(string url)
    {
        if (_mcu?.Wifi is not { } wifi) return;
        _url.Text = url;
        _pending = wifi.Browse(url, _mcu.Uptime.Ticks);
        _pendingUri = Uri.TryCreate(url.Contains("://", StringComparison.Ordinal) ? url : "http://" + url, UriKind.Absolute, out var u) ? u : null;
        _pageStatus.Text = $"Conectando con {_pendingUri?.Host ?? url}…";
        Refresh();
    }

    public void Refresh()
    {
        if (_mcu?.Wifi is not { } wifi) return;
        string state = _mcu.State == McuState.Running ? "Wi‑Fi: " + wifi.Describe() : "Wi‑Fi: placa parada";
        if (state != _lastState) _state.Text = _lastState = state;
        if (_pending is not { } ex) return;
        if (!ex.Done)
        {
            bool timeout = _mcu.State != McuState.Running || _mcu.Uptime.Ticks - ex.CreatedTicks > BrowserTimeout.Ticks;
            if (!timeout) return;
            ex.Fail(_mcu.State != McuState.Running
                ? "La placa no está en marcha."
                : ex.Accepted
                    ? "El programa aceptó la conexión pero no la cerró con close(): la página no termina de cargar."
                    : "Tiempo de espera agotado: el programa no está atendiendo conexiones (falta s.accept() en un bucle).");
        }
        _pending = null;
        Show(ex);
    }

    private void Show(HttpExchange ex)
    {
        _links.Children.Clear();
        if (ex.Error is not null)
        {
            _pageStatus.Text = "No se puede abrir la página";
            _page.Text = ex.Error;
            return;
        }
        var raw = ex.Raw;
        _pageStatus.Text = $"{ex.Method} {ex.Path} → " + (ex.Status is { } s ? $"{s}" : "respuesta sin cabecera HTTP") + $" · {raw.Length} bytes";
        var body = ex.Body;
        _page.Text = HtmlToText(body) is { Length: > 0 } text ? text : "(página vacía)";
        foreach (var (href, label) in Links(body))
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4) };
            string target = _pendingUri is not null && Uri.TryCreate(_pendingUri, href, out var abs) ? abs.ToString() : href;
            b.Click += (_, _) => Browse(target);
            ToolTip.SetTip(b, target);
            _links.Children.Add(b);
        }
    }

    internal static string HtmlToText(string html)
    {
        var s = StyleOrScript().Replace(html, "");
        s = BlockTag().Replace(s, "\n");
        s = AnyTag().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        var lines = s.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    internal static IEnumerable<(string Href, string Label)> Links(string html)
    {
        foreach (Match m in Anchor().Matches(html))
        {
            var label = WebUtility.HtmlDecode(AnyTag().Replace(m.Groups[2].Value, "")).Trim();
            yield return (m.Groups[1].Value, label.Length > 0 ? label : m.Groups[1].Value);
        }
    }

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleOrScript();

    [GeneratedRegex(@"<\s*(br|/p|/h\d|/div|/li|/tr|/title|hr)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t\r]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"<a\s[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Anchor();
}
