using System.Diagnostics;
using Simulador.Arduino.Runtime;
using Simulador.Arduino.Runtime.Avr;

namespace Simulador.Arduino.Tests;

/// <summary>Modo chip real: programas compilados con el núcleo de Arduino corriendo en el ATmega328P emulado.</summary>
public class ChipTests
{
    private static string Hex(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sketches", name + ".hex"));

    private static ArduinoMcu Boot(string name, ArduinoBoard? board = null)
    {
        var mcu = new ArduinoMcu("ARD1", board ?? ArduinoBoard.Uno);
        mcu.LoadHex(Hex(name));
        mcu.Start();
        return mcu;
    }

    private static void Run(ArduinoMcu mcu, ref double ms, double until, Action? each = null)
    {
        while (ms < until)
        {
            ms += 5;
            each?.Invoke();
            mcu.Advance(TimeSpan.FromMilliseconds(ms));
        }
    }

    [Fact]
    public void Intel_hex_is_parsed_and_checked()
    {
        var words = IntelHex.Parse(":0400000001020304F2\n:00000001FF\n");
        Assert.Equal([0x0201, 0x0403], words);
        Assert.Throws<FormatException>(() => IntelHex.Parse(":0400000001020304F3\n"));
    }

    [Fact]
    public void Blink_toggles_pin_13_with_real_timing()
    {
        using var mcu = Boot("blink");
        Assert.True(mcu.IsChipMode);
        var led = mcu.Pin("D13");
        double ms = 0;
        var changes = new List<(double Ms, bool High)>();
        bool last = false;
        Run(mcu, ref ms, 1000, () =>
        {
            if (led.OutputHigh != last) { last = led.OutputHigh; changes.Add((ms, last)); }
        });
        Assert.Equal(McuState.Running, mcu.State);
        Assert.True(led.IsOutput);
        Assert.InRange(changes.Count, 8, 11);
        // delay(100): cada cambio unos 100 ms después del anterior.
        for (int i = 2; i < changes.Count; i++) Assert.InRange(changes[i].Ms - changes[i - 1].Ms, 90, 110);
    }

    [Fact]
    public void Serial_prints_and_echoes()
    {
        using var mcu = Boot("serial");
        double ms = 0;
        Run(mcu, ref ms, 600);
        Assert.Equal(9600, mcu.SerialBaud);
        Assert.StartsWith("Hola desde el chip\r\n", mcu.SerialOutput);
        // delay(250) más lo que tarda en imprimir: el segundo millis() sale hacia 250 (millis() del núcleo real redondea).
        Assert.Matches(@"t=2[4-6]\d\r\n", mcu.SerialOutput);
        mcu.SerialSend("x");
        Run(mcu, ref ms, 1200);
        Assert.Contains("eco:x", mcu.SerialOutput);
    }

    [Fact]
    public void Analog_read_drives_hardware_pwm()
    {
        using var mcu = Boot("analog_pwm");
        var a0 = mcu.Pin("A0");
        var d9 = mcu.Pin("D9");
        double ms = 0;
        a0.Value = 2.5;
        Run(mcu, ref ms, 300);
        Assert.Contains("512", mcu.SerialOutput);
        Assert.InRange(d9.Duty ?? 0, 0.45, 0.55);
        a0.Value = 5;
        Run(mcu, ref ms, 600);
        Assert.Contains("1023", mcu.SerialOutput);
        // analogWrite(255) no usa PWM: el núcleo de Arduino pone el pin en HIGH.
        Assert.Null(d9.Duty);
        Assert.True(d9.OutputHigh);
    }

    [Fact]
    public void Tone_is_measured_on_the_pin()
    {
        using var mcu = Boot("tone");
        double ms = 0;
        Run(mcu, ref ms, 200);
        Assert.InRange(mcu.Pin("D8").ToneHz ?? 0, 435, 445);
    }

    [Fact]
    public void External_interrupt_counts_presses()
    {
        using var mcu = Boot("button_int");
        var button = mcu.Pin("D2");
        var led = mcu.Pin("D13");
        double ms = 0;
        Run(mcu, ref ms, 100);
        Assert.Equal(PinMode.InputPullup, button.Mode);
        for (int i = 0; i < 3; i++)
        {
            button.Value = 0;
            Run(mcu, ref ms, ms + 50);
            Assert.True(led.OutputHigh);
            button.Disconnect();
            Run(mcu, ref ms, ms + 50);
            Assert.False(led.OutputHigh);
        }
        Assert.Contains("pulsaciones=3", mcu.SerialOutput);
    }

    [Fact]
    public void Servo_pulses_become_an_angle()
    {
        using var mcu = Boot("servo_pulse");
        double ms = 0;
        Run(mcu, ref ms, 200);
        Assert.InRange(mcu.Pin("D9").ServoAngle ?? -1, 80, 100);
    }

    [Fact]
    public void Functions_can_be_used_before_their_definition()
    {
        using var mcu = Boot("later_function");
        double ms = 0;
        Run(mcu, ref ms, 100);
        Assert.Contains("42", mcu.SerialOutput);
    }

    [Fact]
    public void Runs_faster_than_real_time()
    {
        using var mcu = Boot("serial");
        var sw = Stopwatch.StartNew();
        double ms = 0;
        Run(mcu, ref ms, 2000);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"2 s simulados tardaron {sw.Elapsed.TotalSeconds:0.00} s");
    }

    [Fact]
    public void Mega_is_not_supported_yet()
    {
        using var mcu = new ArduinoMcu("ARD1", ArduinoBoard.Mega);
        Assert.Throws<NotSupportedException>(() => mcu.LoadHex(Hex("blink")));
    }

    [Fact]
    public void Preprocess_adds_prototypes_and_keeps_line_numbers()
    {
        string cpp = ArduinoToolchain.Preprocess("void setup() {\n}\nvoid loop() {\n  if (x) {\n  }\n}\nint doble(int x) {\n  return x * 2;\n}\n");
        Assert.Contains("int doble(int x);", cpp);
        Assert.DoesNotContain("if(x);", cpp.Replace(" ", ""));
        Assert.Contains("#line 1 \"sketch.ino\"", cpp);
    }
}
