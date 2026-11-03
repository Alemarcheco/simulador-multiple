using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Plc.Tests;

public class ExamplesTests
{
    public static IEnumerable<object[]> Profiles => PlcProfiles.All.Select(p => new object[] { p.Id });

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_example_compiles_and_runs_on_every_brand(string profileId)
    {
        var profile = PlcProfiles.Get(profileId);
        foreach (var e in Simulador.Plc.Examples.StructuredText)
        {
            var cpu = new PlcCpu("PLC1", profile);
            cpu.LoadStructuredText(e.Source);
            cpu.Start();
            for (int i = 1; i <= 20; i++) cpu.Scan(TimeSpan.FromMilliseconds(10 * i));
            Assert.True(cpu.State == PlcState.Run, $"{e.Name}: {cpu.FaultMessage}");
        }
        foreach (var e in Simulador.Plc.Examples.InstructionList)
        {
            var cpu = new PlcCpu("PLC1", profile);
            cpu.LoadInstructionList(e.Source);
            cpu.Start();
            for (int i = 1; i <= 20; i++) cpu.Scan(TimeSpan.FromMilliseconds(10 * i));
            Assert.True(cpu.State == PlcState.Run, $"{e.Name}: {cpu.FaultMessage}");
        }
        foreach (var e in Simulador.Plc.Examples.Sfc)
        {
            var cpu = new PlcCpu("PLC1", profile);
            cpu.LoadSfc(e.Source);
            cpu.Start();
            for (int i = 1; i <= 20; i++) cpu.Scan(TimeSpan.FromMilliseconds(10 * i));
            Assert.True(cpu.State == PlcState.Run, $"{e.Name}: {cpu.FaultMessage}");
        }
        foreach (var e in Simulador.Plc.Examples.Ladder)
        {
            var cpu = new PlcCpu("PLC1", profile);
            cpu.LoadLadder(e.Program);
            cpu.Start();
            for (int i = 1; i <= 20; i++) cpu.Scan(TimeSpan.FromMilliseconds(10 * i));
            Assert.True(cpu.State == PlcState.Run, $"{e.Name}: {cpu.FaultMessage}");
        }
    }

    [Fact]
    public void Ladder_star_delta_switches_from_star_to_delta()
    {
        var cpu = new PlcCpu("PLC1", new GenericIecProfile());
        cpu.LoadLadder(Simulador.Plc.Examples.Ladder.Single(e => e.Name.StartsWith("Arranque estrella")).Program);
        cpu.Start();
        var port = (string a) => cpu.Signals.Single(s => s.Id == "PLC1." + a);
        port("%IX0.1").Value = 24;
        port("%IX0.2").Value = 24;
        port("%IX0.0").Value = 24;
        var t = TimeSpan.Zero;
        for (int i = 0; i < 10; i++) cpu.Scan(t += TimeSpan.FromMilliseconds(10));
        port("%IX0.0").Value = 0;
        Assert.Equal(1, port("%QX0.1").Value); // estrella
        Assert.Equal(0, port("%QX0.2").Value);
        for (int i = 0; i < 700; i++) cpu.Scan(t += TimeSpan.FromMilliseconds(10));
        Assert.Equal(0, port("%QX0.1").Value);
        Assert.Equal(1, port("%QX0.2").Value); // triángulo
        Assert.Equal(1, port("%QX0.0").Value);
    }

    [Fact]
    public void Toggle_relay_example_switches_on_and_off()
    {
        var cpu = new PlcCpu("PLC1", new GenericIecProfile());
        cpu.LoadLadder(Simulador.Plc.Examples.Ladder.Single(e => e.Name.StartsWith("Telerruptor")).Program);
        cpu.Start();
        var input = cpu.Signals.Single(s => s.Id == "PLC1.%IX0.0");
        var lamp = cpu.Signals.Single(s => s.Id == "PLC1.%QX0.0");
        var t = TimeSpan.Zero;
        void Press()
        {
            input.Value = 24; cpu.Scan(t += TimeSpan.FromMilliseconds(10));
            input.Value = 0; cpu.Scan(t += TimeSpan.FromMilliseconds(10));
        }
        Press();
        Assert.Equal(1, lamp.Value);
        Press();
        Assert.Equal(0, lamp.Value);
    }
}
