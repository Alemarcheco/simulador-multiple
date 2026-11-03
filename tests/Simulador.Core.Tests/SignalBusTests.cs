using Simulador.Core.Signals;

namespace Simulador.Core.Tests;

public class SignalBusTests
{
    [Fact]
    public void Propagates_output_to_connected_input()
    {
        var bus = new SignalBus();
        var output = new SignalPort("PLC1.Q0.0", SignalKind.DO, SignalUnit.Bool, 24);
        var input = new SignalPort("KM1.A1", SignalKind.DI, SignalUnit.Bool, 24);

        var result = bus.Connect(output, input);
        output.Value = 1;

        Assert.Empty(result.Warnings);
        Assert.Equal(1, input.Value);
    }

    [Fact]
    public void Warns_but_connects_on_nominal_mismatch()
    {
        var bus = new SignalBus();
        var output = new SignalPort("A.Out", SignalKind.DO, SignalUnit.Bool, 24);
        var input = new SignalPort("B.In", SignalKind.DI, SignalUnit.Bool, 5);

        var result = bus.Connect(output, input);

        Assert.True(result.Connected);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Disconnect_stops_propagation()
    {
        var bus = new SignalBus();
        var output = new SignalPort("A.Out", SignalKind.AO, SignalUnit.Volt);
        var input = new SignalPort("B.In", SignalKind.AI, SignalUnit.Volt);
        bus.Connect(output, input);
        bus.Disconnect(output, input);

        output.Value = 7.5;

        Assert.Equal(0, input.Value);
    }
}
