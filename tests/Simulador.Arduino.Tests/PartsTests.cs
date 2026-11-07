using Simulador.Arduino.Runtime;

namespace Simulador.Arduino.Tests;

public class PartsTests
{
    private static (ArduinoMcu Mcu, List<Part> Parts) Mount(string exampleName)
    {
        var e = ArduinoExamples.All.Single(x => x.Name == exampleName);
        var mcu = new ArduinoMcu("ARD1", ArduinoBoard.Uno);
        mcu.Load(e.Source);
        var parts = e.Setup.Select(s => Part.Create(s, mcu)).ToList();
        mcu.Start();
        return (mcu, parts);
    }

    private static void Run(ArduinoMcu mcu, List<Part> parts, ref double t, double until)
    {
        while (t < until)
        {
            t += 10;
            foreach (var p in parts) p.Apply();
            mcu.Advance(TimeSpan.FromMilliseconds(t));
            foreach (var s in parts.OfType<ServoPart>()) s.Animate(TimeSpan.FromMilliseconds(10));
        }
    }

    [Fact]
    public void Potentiometer_sets_led_brightness()
    {
        var (mcu, parts) = Mount("Potenciómetro regula un LED");
        using var _ = mcu;
        var pot = parts.OfType<PotentiometerPart>().Single();
        var led = parts.OfType<LedPart>().Single();
        double t = 0;
        pot.Position = 0;
        Run(mcu, parts, ref t, 300);
        Assert.False(led.IsOn);
        pot.Position = 1;
        Run(mcu, parts, ref t, 600);
        Assert.Equal(1, led.Brightness, 2);
        pot.Position = 0.6;
        Run(mcu, parts, ref t, 900);
        Assert.InRange(led.Brightness, 0.3, 0.6);
    }

    [Fact]
    public void Button_to_ground_lights_the_led()
    {
        var (mcu, parts) = Mount("Pulsador y LED");
        using var _ = mcu;
        var button = parts.OfType<ButtonPart>().Single();
        var led = parts.OfType<LedPart>().Single();
        double t = 0;
        Run(mcu, parts, ref t, 50);
        Assert.False(led.IsOn);
        button.Pressed = true;
        Run(mcu, parts, ref t, 100);
        Assert.True(led.IsOn);
        button.Pressed = false;
        Run(mcu, parts, ref t, 150);
        Assert.False(led.IsOn);
    }

    [Fact]
    public void Servo_arm_moves_at_limited_speed()
    {
        var (mcu, parts) = Mount("Servo de barrido");
        using var _ = mcu;
        var servo = parts.OfType<ServoPart>().Single();
        double t = 0;
        Run(mcu, parts, ref t, 1000);
        Assert.InRange(servo.Angle, 1, 179);
        Assert.Contains("°", servo.Describe());
    }

    [Fact]
    public void Ldr_darkness_turns_the_lamp_on_with_hysteresis()
    {
        var (mcu, parts) = Mount("Luz automática con sensor (LDR)");
        using var _ = mcu;
        var ldr = parts.OfType<LdrPart>().Single();
        var lamp = parts.OfType<LedPart>().Single();
        double t = 0;
        ldr.Lux = 1000;
        Run(mcu, parts, ref t, 300);
        Assert.False(lamp.IsOn);
        ldr.Lux = 1;
        Run(mcu, parts, ref t, 600);
        Assert.True(lamp.IsOn);
        Assert.Contains("Oscuro", mcu.SerialOutput);
    }

    [Fact]
    public void Lm35_starts_the_fan_above_30_degrees()
    {
        var (mcu, parts) = Mount("Termómetro LM35");
        using var _ = mcu;
        var sensor = parts.OfType<Lm35Part>().Single();
        var fan = parts.OfType<LedPart>().Single();
        double t = 0;
        sensor.Celsius = 25;
        Run(mcu, parts, ref t, 700);
        Assert.False(fan.IsOn);
        sensor.Celsius = 35;
        Run(mcu, parts, ref t, 1400);
        Assert.True(fan.IsOn);
        Assert.Contains("Temperatura: 35", mcu.SerialOutput);
    }

    [Fact]
    public void Buzzer_reports_the_note()
    {
        var (mcu, parts) = Mount("Melodía con zumbador");
        using var _ = mcu;
        var buzzer = parts.OfType<BuzzerPart>().Single();
        double t = 0;
        Run(mcu, parts, ref t, 100);
        Assert.Equal(262, buzzer.Frequency);
        Assert.Equal("Do4", BuzzerPart.Note(262));
        Assert.Equal("La4", BuzzerPart.Note(440));
    }

    [Fact]
    public void Every_example_setup_fits_every_board()
    {
        foreach (var board in ArduinoBoard.All)
            foreach (var e in ArduinoExamples.All)
            {
                using var mcu = new ArduinoMcu("ARD1", board);
                foreach (var spec in e.Setup)
                {
                    var part = Part.Create(spec, mcu);
                    Assert.True(Part.Fits(spec.Kind, part.Pin), $"{e.Name}: {spec.Kind} en {spec.Pin} ({board.Name})");
                }
            }
    }
}
