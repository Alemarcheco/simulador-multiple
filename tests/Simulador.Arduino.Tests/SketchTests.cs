using Simulador.Arduino.Runtime;
using Simulador.Arduino.Runtime.Sketch;

namespace Simulador.Arduino.Tests;

public class SketchTests
{
    private static ArduinoMcu Run(string source, ArduinoBoard? board = null)
    {
        var mcu = new ArduinoMcu("ARD1", board ?? ArduinoBoard.Uno);
        mcu.Load(source);
        mcu.Start();
        return mcu;
    }

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Blink_follows_simulated_time()
    {
        using var mcu = Run("""
            void setup() { pinMode(LED_BUILTIN, OUTPUT); }
            void loop() {
              digitalWrite(LED_BUILTIN, HIGH);
              delay(1000);
              digitalWrite(LED_BUILTIN, LOW);
              delay(1000);
            }
            """);
        var led = mcu.Pin("D13");
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(10));
        Assert.True(led.OutputHigh);
        mcu.Advance(Ms(999));
        Assert.True(led.OutputHigh);
        mcu.Advance(Ms(1001));
        Assert.False(led.OutputHigh);
        mcu.Advance(Ms(2001));
        Assert.True(led.OutputHigh);
        Assert.Equal(McuState.Running, mcu.State);
    }

    [Fact]
    public void Button_with_pullup_and_serial()
    {
        using var mcu = Run("""
            const int boton = 2;
            int pulsaciones = 0;
            bool antes = HIGH;
            void setup() {
              Serial.begin(9600);
              pinMode(boton, INPUT_PULLUP);
              pinMode(8, OUTPUT);
            }
            void loop() {
              bool ahora = digitalRead(boton);
              if (antes == HIGH && ahora == LOW) {
                pulsaciones++;
                Serial.print("Pulsaciones: ");
                Serial.println(pulsaciones);
              }
              digitalWrite(8, ahora == LOW);
              antes = ahora;
              delay(10);
            }
            """);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(50));
        Assert.False(mcu.Pin("D8").OutputHigh);
        mcu.Pin("D2").Value = 0; // pulsador a GND
        mcu.Advance(Ms(100));
        Assert.True(mcu.Pin("D8").OutputHigh);
        mcu.Pin("D2").Disconnect();
        mcu.Advance(Ms(150));
        mcu.Pin("D2").Value = 0;
        mcu.Advance(Ms(200));
        Assert.Equal("Pulsaciones: 1\r\nPulsaciones: 2\r\n", mcu.SerialOutput);
        Assert.Contains(mcu.Variables(), v => v.Name == "pulsaciones" && v.Value == "2");
    }

    [Fact]
    public void Analog_read_map_and_pwm()
    {
        using var mcu = Run("""
            int brillo;
            void setup() { pinMode(9, OUTPUT); }
            void loop() {
              int pot = analogRead(A0);
              brillo = map(pot, 0, 1023, 0, 255);
              analogWrite(9, brillo);
            }
            """);
        mcu.Pin("A0").Value = 2.5;
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(5));
        Assert.Equal(0.498, mcu.Pin("D9").Duty!.Value, 2);
        Assert.Equal(2.49, mcu.Pin("D9").Value, 1);
    }

    [Fact]
    public void Int_is_16_bits_on_avr_and_32_on_larger_boards()
    {
        const string src = """
            int x = 32767;
            long y;
            unsigned long t;
            void setup() { x = x + 1; y = 70000L; t = 0; t = t - 1; }
            void loop() { delay(100); }
            """;
        using var mcu = Run(src);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(1));
        var vars = mcu.Variables().ToDictionary(v => v.Name, v => v.Value);
        Assert.Equal("-32768", vars["x"]);
        Assert.Equal("70000", vars["y"]);
        Assert.Equal("4294967295", vars["t"]);
    }

    [Fact]
    public void Functions_arrays_loops_switch_and_strings()
    {
        using var mcu = Run("""
            #define N 5
            int datos[N] = {3, 1, 4, 1, 5};
            String texto;
            int suma(int a[], int n) {
              int s = 0;
              for (int i = 0; i < n; i++) s += a[i];
              return s;
            }
            int factorial(int n) { return n <= 1 ? 1 : n * factorial(n - 1); }
            void setup() {
              Serial.begin(115200);
              Serial.println(suma(datos, N));
              Serial.println(factorial(5));
              int k = 0;
              while (true) { k++; if (k == 3) break; }
              switch (k) {
                case 1: Serial.println("uno"); break;
                case 3: Serial.println("tres");
                default: Serial.println("sigue");
              }
              texto = "Hola";
              texto += " mundo";
              Serial.println(texto + " " + String(texto.length()));
              Serial.println(3.14159, 3);
              Serial.println(255, HEX);
              float media = suma(datos, N) / (float)N;
              Serial.println(media);
            }
            void loop() { }
            """);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(5));
        Assert.Equal("14\r\n120\r\ntres\r\nsigue\r\nHola mundo 10\r\n3.142\r\nFF\r\n2.80\r\n", mcu.SerialOutput);
    }

    [Fact]
    public void Millis_based_timing_without_delay()
    {
        using var mcu = Run("""
            unsigned long antes = 0;
            bool estado = false;
            void setup() { pinMode(13, OUTPUT); }
            void loop() {
              if (millis() - antes >= 250) {
                antes = millis();
                estado = !estado;
                digitalWrite(13, estado);
              }
            }
            """);
        mcu.Advance(Ms(0));
        int changes = 0;
        bool last = false;
        for (int t = 1; t <= 1010; t++)
        {
            mcu.Advance(Ms(t));
            if (mcu.Pin("D13").OutputHigh != last) { changes++; last = !last; }
        }
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Servo_and_tone()
    {
        using var mcu = Run("""
            #include <Servo.h>
            Servo motor;
            void setup() { motor.attach(9); motor.write(45); tone(8, 440, 200); }
            void loop() { }
            """);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(10));
        Assert.Equal(45, mcu.Pin("D9").ServoAngle);
        Assert.Equal(440, mcu.Pin("D8").ToneHz);
        mcu.Advance(Ms(250));
        Assert.Null(mcu.Pin("D8").ToneHz);
    }

    [Fact]
    public void Serial_input_is_read_by_the_sketch()
    {
        using var mcu = Run("""
            void setup() { Serial.begin(9600); pinMode(13, OUTPUT); }
            void loop() {
              if (Serial.available() > 0) {
                char c = Serial.read();
                if (c == '1') digitalWrite(13, HIGH);
                if (c == '0') digitalWrite(13, LOW);
              }
            }
            """);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(5));
        mcu.SerialSend("1");
        mcu.Advance(Ms(10));
        Assert.True(mcu.Pin("D13").OutputHigh);
        mcu.SerialSend("0");
        mcu.Advance(Ms(15));
        Assert.False(mcu.Pin("D13").OutputHigh);
    }

    [Theory]
    [InlineData("void setup() { pinMode(13, OUTPUT) }\nvoid loop() {}", 1, "punto y coma")]
    [InlineData("void setup() {}\n", 1, "loop")]
    [InlineData("void setup() {\n  int x = 1;\n  x = y + 1;\n}\nvoid loop() {}", 3, "'y' no está declarada")]
    public void Errors_point_to_the_line(string source, int line, string fragment)
    {
        var mcu = new ArduinoMcu("ARD1", ArduinoBoard.Uno);
        try
        {
            mcu.Load(source);
            mcu.Start();
            mcu.Advance(Ms(0));
            mcu.Advance(Ms(1));
            Assert.Equal(McuState.Fault, mcu.State);
            Assert.Equal(line, mcu.FaultLine);
            Assert.Contains(fragment, mcu.FaultMessage);
        }
        catch (SketchCompileException ex)
        {
            Assert.Equal(line, ex.Line);
            Assert.Contains(fragment, ex.Message);
        }
        finally { mcu.Dispose(); }
    }

    [Fact]
    public void Array_out_of_bounds_stops_with_a_clear_message()
    {
        using var mcu = Run("int a[3];\nvoid setup() {\n  a[3] = 1;\n}\nvoid loop() {}");
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(1));
        Assert.Equal(McuState.Fault, mcu.State);
        Assert.Equal(3, mcu.FaultLine);
        Assert.Contains("fuera del array", mcu.FaultMessage);
    }

    [Fact]
    public void Stop_and_restart_resets_the_board()
    {
        using var mcu = Run("void setup() { pinMode(13, OUTPUT); digitalWrite(13, HIGH); }\nvoid loop() { delay(10); }");
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(5));
        Assert.True(mcu.Pin("D13").OutputHigh);
        mcu.Stop();
        Assert.False(mcu.Pin("D13").IsOutput);
        mcu.Start();
        mcu.Advance(Ms(100));
        mcu.Advance(Ms(105));
        Assert.True(mcu.Pin("D13").OutputHigh);
    }

    [Fact]
    public void Mega_has_more_pins()
    {
        using var mcu = Run("void setup() { pinMode(52, OUTPUT); digitalWrite(52, HIGH); }\nvoid loop() {}", ArduinoBoard.Mega);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(1));
        Assert.True(mcu.Pin("D52").OutputHigh);
        Assert.Equal(16, mcu.Pins.Count(p => p.IsAnalog));
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Every_example_compiles_and_runs_on_every_board(string name)
    {
        var example = ArduinoExamples.All.Single(e => e.Name == name);
        foreach (var board in ArduinoBoard.All)
        {
            using var mcu = new ArduinoMcu("ARD1", board);
            mcu.Load(example.Source);
            mcu.Start();
            for (int t = 0; t <= 3000; t += 20) mcu.Advance(Ms(t));
            Assert.True(mcu.State == McuState.Running, $"{name} en {board.Name}: {mcu.FaultMessage}");
        }
    }

    public static IEnumerable<object[]> ExampleNames => ArduinoExamples.All.Select(e => new object[] { e.Name });
}
