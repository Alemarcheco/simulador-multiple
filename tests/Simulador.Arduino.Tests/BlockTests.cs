using Simulador.Arduino.Runtime;
using Simulador.Arduino.Runtime.Blocks;
using static Simulador.Arduino.Runtime.Blocks.BlockExamples;

namespace Simulador.Arduino.Tests;

public class BlockTests
{
    [Fact]
    public void Every_example_generates_code_that_compiles_on_every_board()
    {
        foreach (var board in ArduinoBoard.All)
            foreach (var e in BlockExamples.All)
            {
                var code = BlockCodeGenerator.Generate(e.Build(), board);
                using var mcu = new ArduinoMcu("ARD1", board);
                try { mcu.Load(code.Source); }
                catch (Exception ex) { Assert.Fail($"{e.Name} ({board.Name}): {ex.Message}\n{code.Source}"); }
                Assert.NotEmpty(code.LineToBlock);
            }
    }

    [Fact]
    public void Blink_generates_readable_code()
    {
        var code = BlockCodeGenerator.Generate(BlockExamples.All[0].Build());
        Assert.Contains("pinMode(13, OUTPUT);", code.Source);
        Assert.Contains("  digitalWrite(13, HIGH);", code.Source);
        Assert.Contains("delay(1000);", code.Source);
    }

    [Fact]
    public void Line_map_points_at_the_block()
    {
        var prog = BlockExamples.All[0].Build();
        var code = BlockCodeGenerator.Generate(prog);
        var lines = code.Source.Split('\n');
        foreach (var (line, block) in code.LineToBlock)
            Assert.Contains(block.Op == "delay" ? "delay" : block.Op == "pin_mode" ? "pinMode" : "digitalWrite", lines[line - 1]);
    }

    [Fact]
    public void Nested_operators_get_parentheses_only_when_needed()
    {
        var p = new BlockProgram
        {
            Loop =
            [
                B("set_var", ("var", "x"), ("value", B("arith", ("a", B("arith", ("a", 1), ("op", "+"), ("b", 2))), ("op", "*"), ("b", 3)))),
                B("if", ("cond", B("logic", ("a", B("compare", ("a", B("var", ("var", "x"))), ("op", ">"), ("b", 5))), ("op", "&&"), ("b", B("not", ("a", 0)))))),
            ],
        };
        var src = BlockCodeGenerator.Generate(p).Source;
        Assert.Contains("x = (1 + 2) * 3;", src);
        Assert.Contains("if ((x > 5) && !0) {", src);
        Assert.Contains("long x = 0;", src);
    }

    [Fact]
    public void Variable_names_lose_accents_and_spaces()
    {
        Assert.Equal("angulo_maximo", BlockCodeGenerator.VariableName("ángulo máximo"));
        Assert.Equal("ano", BlockCodeGenerator.VariableName("año"));
        Assert.Throws<BlockException>(() => BlockCodeGenerator.VariableName("2x"));
        Assert.Throws<BlockException>(() => BlockCodeGenerator.VariableName("delay"));
    }

    [Fact]
    public void Errors_point_at_the_wrong_block()
    {
        var bad = B("delay", ("ms", Block.Literal("mucho")));
        var ex = Assert.Throws<BlockException>(() => BlockCodeGenerator.Generate(new BlockProgram { Loop = [bad] }));
        Assert.Same(bad.Inputs["ms"], ex.Block);

        var wrongPin = B("digital_write", ("pin", "D60"));
        var ex2 = Assert.Throws<BlockException>(() => BlockCodeGenerator.Generate(new BlockProgram { Loop = [wrongPin] }, ArduinoBoard.Uno));
        Assert.Same(wrongPin, ex2.Block);
    }

    [Fact]
    public void Json_round_trip_keeps_the_program()
    {
        foreach (var e in BlockExamples.All)
        {
            var p = e.Build();
            var copy = BlockProgram.FromJson(p.ToJson());
            Assert.Equal(BlockCodeGenerator.Generate(p).Source, BlockCodeGenerator.Generate(copy).Source);
        }
        Assert.Throws<BlockException>(() => BlockProgram.FromJson("""{"Setup":[{"Op":"volar"}],"Loop":[]}"""));
    }

    private static (ArduinoMcu, List<Part>) Mount(string name)
    {
        var e = BlockExamples.All.Single(x => x.Name == name);
        var mcu = new ArduinoMcu("ARD1", ArduinoBoard.Uno);
        mcu.Load(BlockCodeGenerator.Generate(e.Build()).Source);
        var parts = e.Parts.Select(s => Part.Create(s, mcu)).ToList();
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
        }
    }

    [Fact]
    public void Counter_example_counts_each_press_once()
    {
        var (mcu, parts) = Mount("Contador de pulsaciones");
        using var _ = mcu;
        var button = parts.OfType<ButtonPart>().Single();
        double t = 0;
        for (int i = 0; i < 3; i++)
        {
            button.Pressed = true;
            Run(mcu, parts, ref t, t + 200);
            button.Pressed = false;
            Run(mcu, parts, ref t, t + 200);
        }
        Assert.Contains("Pulsaciones: 3", mcu.SerialOutput);
        Assert.DoesNotContain("Pulsaciones: 4", mcu.SerialOutput);
    }

    [Fact]
    public void Temperature_alarm_beeps_above_30()
    {
        var (mcu, parts) = Mount("Alarma de temperatura");
        using var _ = mcu;
        var sensor = parts.OfType<Lm35Part>().Single();
        var buzzer = parts.OfType<BuzzerPart>().Single();
        double t = 0;
        sensor.Celsius = 20;
        Run(mcu, parts, ref t, 1000);
        Assert.DoesNotContain("calor", mcu.SerialOutput);
        sensor.Celsius = 40;
        bool beeped = false;
        while (t < 2500) { Run(mcu, parts, ref t, t + 10); beeped |= buzzer.Sounding; }
        Assert.True(beeped);
        Assert.Contains("Demasiado calor", mcu.SerialOutput);
    }
}
