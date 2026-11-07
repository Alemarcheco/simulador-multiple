using Simulador.Arduino.Runtime;

namespace Simulador.Arduino.Tests;

/// <summary>Wi‑Fi del ESP32 en MicroPython: conexión, servidor web visto desde el navegador del simulador y urequests.</summary>
public class WifiTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static ArduinoMcu Esp32(string source)
    {
        var mcu = new ArduinoMcu("MCU1", ArduinoBoard.Esp32);
        mcu.Wifi!.AllowInternet = false;
        mcu.Load(source);
        mcu.Start();
        return mcu;
    }

    private static void RunUntil(ArduinoMcu mcu, double fromMs, double toMs, Func<bool>? stop = null)
    {
        for (double t = fromMs; t <= toMs && mcu.State == McuState.Running && stop?.Invoke() != true; t += 20) mcu.Advance(Ms(t));
        Assert.True(mcu.FaultMessage is null, $"{mcu.FaultMessage} (línea {mcu.FaultLine})\n{mcu.SerialOutput}");
    }

    [Fact]
    public void Scan_and_connect_with_the_right_password()
    {
        using var mcu = Esp32("""
            import network, time
            wlan = network.WLAN(network.STA_IF)
            wlan.active(True)
            for red in wlan.scan():
                print(red[0].decode(), red[3])
            wlan.connect("Casa_WiFi", "clave1234")
            print("conectando", wlan.status() == network.STAT_CONNECTING)
            while not wlan.isconnected():
                time.sleep_ms(100)
            print("IP:", wlan.ifconfig()[0], wlan.status("rssi"))
            """);
        RunUntil(mcu, 0, 5000);
        var output = mcu.SerialOutput.Replace("\r", "");
        Assert.True(mcu.ProgramEnded, output);
        Assert.Contains("Casa_WiFi -48\nTaller -63\nVecino_5G -82", output);
        Assert.Contains("conectando True", output);
        Assert.Contains("IP: 192.168.1.50 -48", output);
        Assert.Equal("Casa_WiFi", mcu.Wifi!.ConnectedSsid);
    }

    [Fact]
    public void Wrong_password_and_unknown_network_give_status_codes()
    {
        using var mcu = Esp32("""
            import network, time
            wlan = network.WLAN(network.STA_IF)
            wlan.active(True)
            wlan.connect("Casa_WiFi", "mala")
            time.sleep(2)
            print(wlan.status() == network.STAT_WRONG_PASSWORD, wlan.isconnected())
            wlan.connect("NoExiste", "x")
            print(wlan.status() == network.STAT_NO_AP_FOUND)
            wlan.connect("Taller")
            time.sleep(2)
            print(wlan.isconnected())
            """);
        RunUntil(mcu, 0, 6000);
        var lines = mcu.SerialOutput.Replace("\r", "").Split('\n').Where(l => l.Length > 0 && !l.StartsWith('[')).ToArray();
        Assert.Equal(["True False", "True", "True"], lines);
        Assert.Contains("[Wi‑Fi] «Casa_WiFi»: contraseña incorrecta", mcu.SerialOutput);
    }

    [Fact]
    public void Web_server_answers_the_simulator_browser_and_toggles_the_led()
    {
        using var mcu = Esp32("""
            import network, socket, time
            from machine import Pin
            led = Pin(2, Pin.OUT)
            wlan = network.WLAN(network.STA_IF)
            wlan.active(True)
            wlan.connect("Casa_WiFi", "clave1234")
            while not wlan.isconnected():
                time.sleep_ms(100)
            s = socket.socket()
            s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            s.bind(socket.getaddrinfo("0.0.0.0", 80)[0][-1])
            s.listen(1)
            while True:
                cl, addr = s.accept()
                peticion = cl.recv(1024).decode()
                if "GET /on" in peticion:
                    led.value(1)
                elif "GET /off" in peticion:
                    led.value(0)
                estado = "ENCENDIDO" if led.value() else "APAGADO"
                cl.send("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n")
                cl.send("<h1>LED " + estado + "</h1><a href='/on'>Encender</a>")
                cl.close()
            """);
        var wifi = mcu.Wifi!;
        RunUntil(mcu, 0, 4000, () => wifi.ListeningPort == 80);
        Assert.Equal(80, wifi.ListeningPort);

        var ex = wifi.Browse("192.168.1.50/on", 0);
        RunUntil(mcu, 4020, 6000, () => ex.Done);
        Assert.True(ex.Done && ex.Error is null, ex.Error);
        Assert.Equal(200, ex.Status);
        Assert.Contains("LED ENCENDIDO", ex.Body);
        Assert.True(mcu.Pins[2].OutputHigh);

        var off = wifi.Browse("http://192.168.1.50/off", 0);
        RunUntil(mcu, 6020, 8000, () => off.Done);
        Assert.Contains("LED APAGADO", off.Body);
        Assert.False(mcu.Pins[2].OutputHigh);

        Assert.NotNull(wifi.Browse("http://192.168.1.99/", 0).Error);
    }

    [Fact]
    public void Urequests_reads_json_from_the_virtual_server_and_internet()
    {
        using var mcu = Esp32("""
            import network, time, urequests, json
            wlan = network.WLAN(network.STA_IF)
            wlan.active(True)
            wlan.connect("Taller")
            while not wlan.isconnected():
                time.sleep_ms(100)
            r = urequests.get("http://simulador.local/api/tiempo")
            datos = r.json()
            r.close()
            print(r.status_code, datos["ciudad"], isinstance(datos["temperatura"], float))
            r = urequests.post("https://ejemplo.org/api", json={"valor": 3, "ok": True})
            print(r.status_code, r.text)
            print(json.dumps({"a": [1, 2.5, None, "ñ"]}), json.loads('{"x": [true, -2e1]}'))
            """);
        mcu.Wifi!.InternetHandler = (method, url, body) => (201, $"{method} {url} {body}");
        // La petición «a Internet» va en otro hilo: se deja correr hasta que el programa acabe.
        var wall = System.Diagnostics.Stopwatch.StartNew();
        for (double t = 0; mcu.State == McuState.Running && !mcu.ProgramEnded && wall.ElapsedMilliseconds < 20_000; t += 20)
        {
            mcu.Advance(Ms(t));
            if (t > 3000) Thread.Sleep(1);
        }
        Assert.True(mcu.FaultMessage is null, mcu.FaultMessage);
        var output = mcu.SerialOutput.Replace("\r", "");
        Assert.True(mcu.ProgramEnded, $"{mcu.State} {mcu.Uptime} [{output}]");
        Assert.Contains("200 Simulópolis True", output);
        Assert.Contains("201 POST https://ejemplo.org/api {\"valor\": 3, \"ok\": true}", output);
        Assert.Contains("{\"a\": [1, 2.5, null, \"ñ\"]} {'x': [True, -20.0]}", output);
    }

    [Fact]
    public void Requests_without_wifi_fail_with_a_clear_error()
    {
        using var mcu = Esp32("""
            import urequests
            urequests.get("http://simulador.local/api/hora")
            """);
        for (int t = 0; t <= 1000 && mcu.State == McuState.Running; t += 50) mcu.Advance(Ms(t));
        Assert.Contains("OSError", mcu.FaultMessage);
        Assert.Contains("Wi‑Fi", mcu.FaultMessage);
    }

    [Fact]
    public void The_pico_has_no_network_module()
    {
        using var mcu = new ArduinoMcu("MCU1", ArduinoBoard.Pico);
        Assert.Null(mcu.Wifi);
        mcu.Load("import network");
        mcu.Start();
        for (int t = 0; t <= 200 && mcu.State == McuState.Running; t += 50) mcu.Advance(Ms(t));
        Assert.Contains("ImportError", mcu.FaultMessage);
        Assert.Contains("Pico W", mcu.FaultMessage);
    }
}

public class WifiExampleTests
{
    private static ArduinoExample Example(string name) => PythonExamples.For(ArduinoBoard.Esp32).Single(e => e.Name.StartsWith(name, StringComparison.Ordinal));

    private static ArduinoMcu Start(string name)
    {
        var mcu = new ArduinoMcu("MCU1", ArduinoBoard.Esp32);
        mcu.Wifi!.AllowInternet = false;
        mcu.Load(Example(name).Source);
        mcu.Start();
        return mcu;
    }

    private static void Run(ArduinoMcu mcu, ref double t, double ms, Func<bool>? stop = null)
    {
        for (double end = t + ms; t < end && mcu.State == McuState.Running && stop?.Invoke() != true; t += 20)
            mcu.Advance(TimeSpan.FromMilliseconds(t));
        Assert.True(mcu.FaultMessage is null, $"{mcu.FaultMessage} (línea {mcu.FaultLine})\n{mcu.SerialOutput}");
    }

    [Fact]
    public void Connect_example_lists_networks_and_lights_the_led()
    {
        using var mcu = Start("Wi‑Fi: buscar");
        double t = 0;
        Run(mcu, ref t, 6000);
        Assert.True(mcu.ProgramEnded, mcu.SerialOutput);
        Assert.Contains("Casa_WiFi    canal  6  -48 dBm  con clave", mcu.SerialOutput);
        Assert.Contains("Conectado. IP 192.168.1.50 router 192.168.1.1", mcu.SerialOutput);
        Assert.True(mcu.Pins[2].OutputHigh);
    }

    [Fact]
    public void Server_example_serves_the_page_with_links()
    {
        using var mcu = Start("Wi‑Fi: servidor");
        double t = 0;
        Run(mcu, ref t, 6000, () => mcu.Wifi!.ListeningPort == 80);
        Assert.Contains("Servidor listo", mcu.SerialOutput);
        var ex = mcu.Wifi!.Browse("http://192.168.1.50/on", (long)(t * 10_000));
        Run(mcu, ref t, 2000, () => ex.Done);
        Assert.Null(ex.Error);
        Assert.Contains("<h1>LED ENCENDIDO</h1>", ex.Body);
        Assert.True(mcu.Pins[2].OutputHigh);
        Assert.Contains("192.168.1.10 pide /on", mcu.SerialOutput);
    }

    [Fact]
    public void Weather_example_reads_the_json()
    {
        using var mcu = Start("Wi‑Fi: pedir");
        double t = 0;
        Run(mcu, ref t, 6000);
        Assert.Contains("Simulópolis:", mcu.SerialOutput);
        Assert.Contains("% de humedad", mcu.SerialOutput);
    }
}
