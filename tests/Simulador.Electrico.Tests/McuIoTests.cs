using Simulador.Arduino.Runtime;
using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico.Tests;

/// <summary>Arduino, ESP32 y Pico montados en el cuadro eléctrico con una placa de 24 V y relés.</summary>
public class McuIoTests
{
    private static PlacedElement El(Schematic s, string tag, string type) =>
        s.Elements.Single(e => e.Tag == tag && e.Type == type);

    private static void Click(Simulator sim, PlacedElement e, double hold = 0.1)
    {
        sim.PointerDown(e.Id);
        sim.Run(hold);
        sim.PointerUp(e.Id);
    }

    /// <summary>Monta el ejemplo con la placa y el programa indicados y comprueba marcha, paro y el guardamotor.</summary>
    private static McuIoBoard StartStop(ArduinoBoard board, string? program)
    {
        var s = Examples.McuStartStop();
        var kf = El(s, "KF1", ElementCatalog.McuTypeId);
        var sim = new Simulator(s);
        var io = new McuIoBoard("KF1", board);
        io.Load(program ?? kf.Texts["codigo"]);
        Assert.Empty(sim.AttachController("KF1", io));

        var coil = El(s, "KM1", "bobina_contactor");
        sim.Run(0.3);
        Assert.True(sim.Status(kf.Id).Powered);
        Assert.False(sim.Status(coil.Id).Actuated);

        Click(sim, El(s, "S1", "pulsador_na"));
        sim.Run(0.2);
        Assert.True(sim.Status(coil.Id).Actuated, $"KM1 debe actuar por K1 ({io.Mcu.FaultMessage})");
        sim.Run(3);
        Assert.InRange(sim.Status(El(s, "M1", "motor_3f").Id).Rpm, 1400, 1500);
        Assert.True(sim.Status(El(s, "H1", "piloto").Id).Brightness > 0.9);

        Click(sim, El(s, "S2", "pulsador_nc"));
        sim.Run(0.2);
        Assert.False(sim.Status(coil.Id).Actuated);
        Assert.Equal(McuState.Running, io.Mcu.State);
        return io;
    }

    [Fact]
    public void Arduino_sketch_runs_the_motor_through_the_relay_board()
    {
        using var io = StartStop(ArduinoBoard.Uno, null);
        Assert.Contains("Motor en marcha", io.Mcu.SerialOutput);
        Assert.Contains("Motor parado", io.Mcu.SerialOutput);
    }

    [Fact]
    public void MicroPython_on_the_pico_runs_the_same_circuit()
    {
        var (i, o) = McuIoBoard.Mapping(ArduinoBoard.Pico);
        using var io = StartStop(ArduinoBoard.Pico, $$"""
            from machine import Pin
            import time
            marcha, paro, guarda = Pin({{i[0]}}, Pin.IN), Pin({{i[2]}}, Pin.IN), Pin({{i[4]}}, Pin.IN)
            contactor, piloto = Pin({{o[0]}}, Pin.OUT), Pin({{o[2]}}, Pin.OUT)
            motor = False
            while True:
                permiso = paro.value() and guarda.value()
                if marcha.value() and permiso:
                    motor = True
                if not permiso:
                    motor = False
                contactor.value(motor)
                piloto.value(motor)
                time.sleep_ms(10)
            """);
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void The_template_of_every_board_latches_K1(string boardId)
    {
        var board = ArduinoBoard.Everything.Single(b => b.Id == boardId);
        using var io = new McuIoBoard("KF1", board);
        io.Load(McuIoBoard.Template(board));
        var dt = TimeSpan.FromMilliseconds(10);
        var t = TimeSpan.Zero;
        void Run(int steps) { for (int k = 0; k < steps; k++) { io.DoStep(t, dt); t += dt; } }
        var inputs = io.Signals.Take(McuIoBoard.Channels).ToList();
        var k1 = io.Signals[McuIoBoard.Channels];
        inputs[1].Value = 24; // paro NC en reposo
        Run(10);
        Assert.Equal(0, k1.Value);
        inputs[0].Value = 24;
        Run(5);
        inputs[0].Value = 0;
        Run(5);
        Assert.True(k1.Value == 1, $"{board.Name}: {io.Mcu.FaultMessage}");
        inputs[1].Value = 0;
        Run(5);
        Assert.Equal(0, k1.Value);
    }

    public static IEnumerable<object[]> Boards => ArduinoBoard.Everything.Select(b => new object[] { b.Id });

    [Fact]
    public void Catalog_board_options_match_the_runtime_boards()
    {
        Assert.Equal(ArduinoBoard.Everything.Select(b => b.Name), ElementCatalog.McuBoards);
        var type = ElementCatalog.Default.Get(ElementCatalog.McuTypeId);
        Assert.Equal(Behavior.Plc, type.Behavior);
        // Mismos bornes que el PLC compacto, en el mismo orden, y después las analógicas.
        Assert.Equal(ElementCatalog.Default.Get("plc_compacto").Terminals.Select(x => x.Id).Concat(["AI1", "AI2"]), type.Terminals.Select(x => x.Id));
    }

    [Fact]
    public void Level_transmitter_drives_the_pump_through_the_analog_input()
    {
        var s = Examples.McuLevelControl();
        var kf = El(s, "KF1", ElementCatalog.McuTypeId);
        var b1 = El(s, "B1", "transmisor_010");
        var h1 = El(s, "H1", "piloto");
        var sim = new Simulator(s);
        using var io = new McuIoBoard("KF1", ArduinoBoard.Uno);
        io.Load(kf.Texts["codigo"]);
        Assert.Empty(sim.AttachController("KF1", io));

        sim.Run(0.5);
        Assert.True(sim.Status(b1.Id).Powered);
        Assert.Equal(5.0, sim.Status(b1.Id).Voltage, 1);
        Assert.Equal(5.0, sim.Status(kf.Id).AnalogInputs[0], 1);
        Assert.False(sim.Status(h1.Id).Brightness > 0.5, "al 50 % la bomba sigue parada");

        sim.SetTransmitter(b1.Id, 20);
        sim.Run(0.5);
        Assert.Equal(2.0, sim.Status(kf.Id).AnalogInputs[0], 1);
        Assert.True(sim.Status(h1.Id).Brightness > 0.9, $"por debajo del 30 % la bomba arranca ({io.Mcu.FaultMessage})");
        sim.SetTransmitter(b1.Id, 60);
        sim.Run(0.5);
        Assert.True(sim.Status(h1.Id).Brightness > 0.9, "histéresis: al 60 % sigue llenando");
        sim.SetTransmitter(b1.Id, 90);
        sim.Run(0.5);
        Assert.False(sim.Status(h1.Id).Brightness > 0.5);
        Assert.Contains("bomba en marcha", io.Mcu.SerialOutput);
        Assert.Contains("bomba parada", io.Mcu.SerialOutput);
    }

    [Fact]
    public void Transmitter_without_supply_gives_zero_volts()
    {
        var s = Examples.McuLevelControl();
        var b1 = El(s, "B1", "transmisor_010");
        s.Wires.RemoveAll(w => w.A.Element == b1.Id && w.A.Terminal == "L+" || w.B.Element == b1.Id && w.B.Terminal == "L+");
        var sim = new Simulator(s);
        sim.Run(0.3);
        Assert.False(sim.Status(b1.Id).Powered);
        Assert.Equal(0, sim.Status(El(s, "KF1", ElementCatalog.McuTypeId).Id).AnalogInputs[0], 2);
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void Analog_input_reaches_the_adc_of_every_board(string boardId)
    {
        var board = ArduinoBoard.Everything.Single(b => b.Id == boardId);
        using var io = new McuIoBoard("KF1", board);
        int pin = io.AnalogPins[1];
        io.Load(board.IsPython
            ? $"from machine import ADC, Pin\nimport time\nadc = ADC(Pin({pin}))\nwhile True:\n    valor = adc.read_u16()\n    time.sleep_ms(10)\n"
            : $"int valor;\nvoid setup() {{}}\nvoid loop() {{ valor = analogRead({pin}); delay(10); }}\n");
        io.Signals.Single(x => x.Id == "KF1.AI2").Value = 7.5;
        var t = TimeSpan.Zero;
        for (int k = 0; k < 10; k++) { io.DoStep(t, TimeSpan.FromMilliseconds(10)); t += TimeSpan.FromMilliseconds(10); }
        var v = io.Mcu.Variables().Single(x => x.Name == "valor").Value;
        double expected = board.IsPython ? 0.75 * 65535 : 0.75 * 1023;
        Assert.InRange(double.Parse(v, System.Globalization.CultureInfo.InvariantCulture), expected - 2, expected + 2);
    }
}
