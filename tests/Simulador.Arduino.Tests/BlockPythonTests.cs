using Simulador.Arduino.Runtime;
using Simulador.Arduino.Runtime.Blocks;

namespace Simulador.Arduino.Tests;

/// <summary>Los bloques generan MicroPython en el ESP32 y la Raspberry Pi Pico.</summary>
public class BlockPythonTests
{
    public static IEnumerable<object[]> PythonBoards => ArduinoBoard.Python.Select(b => new object[] { b.Id });

    private static ArduinoBoard Board(string id) => ArduinoBoard.Everything.Single(b => b.Id == id);

    private static (ArduinoMcu, List<Part>, BlockCode) Mount(ArduinoBoard board, string name)
    {
        var e = BlockExamples.For(board).Single(x => x.Name == name);
        var code = BlockCodeGenerator.Generate(e.Build(), board);
        var mcu = new ArduinoMcu("MCU1", board);
        mcu.Load(code.Source);
        var parts = e.Parts.Select(s => Part.Create(s, mcu)).ToList();
        mcu.Start();
        return (mcu, parts, code);
    }

    private static void Run(ArduinoMcu mcu, List<Part> parts, ref double t, double until, BlockCode code)
    {
        while (t < until && mcu.State == McuState.Running)
        {
            t += 10;
            foreach (var p in parts) p.Apply();
            mcu.Advance(TimeSpan.FromMilliseconds(t));
        }
        Assert.True(mcu.FaultMessage is null, $"{mcu.FaultMessage} (línea {mcu.FaultLine})\n{code.Source}");
    }

    [Theory]
    [MemberData(nameof(PythonBoards))]
    public void Every_example_runs_as_micropython(string boardId)
    {
        var board = Board(boardId);
        foreach (var e in BlockExamples.For(board))
        {
            Assert.All(e.Parts, s => Assert.Contains(s.Pin, Enumerable.Range(0, board.PinCount).Select(board.PinName)));
            Assert.DoesNotContain("pin 13", e.Description);
            var (mcu, parts, code) = Mount(board, e.Name);
            using var _ = mcu;
            Assert.StartsWith("# Programa hecho con bloques (MicroPython)", code.Source);
            Assert.NotEmpty(code.LineToBlock);
            double t = 0;
            Run(mcu, parts, ref t, 1500, code);
            Assert.Equal(McuState.Running, mcu.State);
        }
    }

    [Fact]
    public void Blink_on_the_esp32_is_readable_and_blinks_gpio2()
    {
        var (mcu, parts, code) = Mount(ArduinoBoard.Esp32, "Parpadeo");
        using var _ = mcu;
        Assert.Contains("pin2 = Pin(2)", code.Source);
        Assert.Contains("pin2.init(Pin.OUT)", code.Source);
        Assert.Contains("while True:\n    pin2.value(1)\n    time.sleep_ms(int(1000))", code.Source);
        double t = 0;
        Run(mcu, parts, ref t, 500, code);
        Assert.True(mcu.Pins[2].OutputHigh);
        Run(mcu, parts, ref t, 1500, code);
        Assert.False(mcu.Pins[2].OutputHigh);
        // La línea que se ejecuta apunta a su bloque.
        Assert.Equal("delay", code.LineToBlock[mcu.CurrentLine].Op);
    }

    [Fact]
    public void Counter_counts_presses_on_the_pico()
    {
        var (mcu, parts, code) = Mount(ArduinoBoard.Pico, "Contador de pulsaciones");
        using var _ = mcu;
        var button = parts.OfType<ButtonPart>().Single();
        double t = 0;
        for (int i = 0; i < 3; i++)
        {
            button.Pressed = true;
            Run(mcu, parts, ref t, t + 200, code);
            button.Pressed = false;
            Run(mcu, parts, ref t, t + 200, code);
        }
        Assert.Contains("Pulsaciones: 3", mcu.SerialOutput);
        Assert.DoesNotContain("Pulsaciones: 4", mcu.SerialOutput);
    }

    [Fact]
    public void Potentiometer_sets_the_pwm_like_on_arduino()
    {
        var (mcu, parts, code) = Mount(ArduinoBoard.Esp32, "Potenciómetro regula un LED");
        using var _ = mcu;
        var pot = parts.OfType<PotentiometerPart>().Single();
        pot.Position = 0.5;
        double t = 0;
        Run(mcu, parts, ref t, 600, code);
        Assert.InRange(mcu.Pins[18].Duty ?? -1, 0.45, 0.55);
        Assert.Contains("GPIO34 = 512", mcu.SerialOutput);
    }

    [Fact]
    public void Servo_and_tone_work_on_micropython()
    {
        var (mcu, parts, code) = Mount(ArduinoBoard.Pico, "Servo con potenciómetro");
        using (mcu)
        {
            parts.OfType<PotentiometerPart>().Single().Position = 1;
            double t = 0;
            Run(mcu, parts, ref t, 500, code);
            Assert.InRange(mcu.Pins[15].ServoAngle ?? -1, 175, 180);
        }
        var (mcu2, parts2, code2) = Mount(ArduinoBoard.Esp32, "Alarma de temperatura");
        using (mcu2)
        {
            var sensor = parts2.OfType<Lm35Part>().Single();
            var buzzer = parts2.OfType<BuzzerPart>().Single();
            sensor.Celsius = 20;
            double t = 0;
            Run(mcu2, parts2, ref t, 1000, code2);
            Assert.Contains("Temperatura: 2", mcu2.SerialOutput);
            Assert.DoesNotContain("calor", mcu2.SerialOutput);
            sensor.Celsius = 40;
            bool beeped = false;
            while (t < 2500) { Run(mcu2, parts2, ref t, t + 10, code2); beeped |= buzzer.Sounding; }
            Assert.True(beeped);
            Assert.Contains("Demasiado calor", mcu2.SerialOutput);
        }
    }

    [Fact]
    public void Python_errors_point_at_the_block()
    {
        var p = new BlockProgram();
        p.Loop.Add(BlockExamples.B("set_var", ("var", "print"), ("value", 1)));
        var ex = Assert.Throws<BlockException>(() => BlockCodeGenerator.Generate(p, ArduinoBoard.Esp32));
        Assert.Same(p.Loop[0], ex.Block);

        var q = new BlockProgram();
        q.Setup.Add(BlockExamples.B("pin_mode", ("pin", "GPIO34"), ("mode", "OUTPUT")));
        Assert.Contains("solo puede ser entrada", Assert.Throws<BlockException>(() => BlockCodeGenerator.Generate(q, ArduinoBoard.Esp32)).Message);

        var r = new BlockProgram();
        r.Loop.Add(BlockExamples.B("digital_write", ("pin", "D13"), ("level", "HIGH")));
        Assert.Contains("no tiene el pin D13", Assert.Throws<BlockException>(() => BlockCodeGenerator.Generate(r, ArduinoBoard.Pico)).Message);
    }
}
