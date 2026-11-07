using Simulador.Arduino.Runtime;
using Simulador.Arduino.Runtime.Sketch;

namespace Simulador.Arduino.Tests;

public class PythonTests
{
    private static ArduinoMcu Run(string source, ArduinoBoard? board = null)
    {
        var mcu = new ArduinoMcu("MCU1", board ?? ArduinoBoard.Pico);
        mcu.Load(source);
        mcu.Start();
        return mcu;
    }

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    /// <summary>Ejecuta un programa que termina y devuelve lo que imprimió.</summary>
    private static string Output(string source)
    {
        using var mcu = Run(source);
        for (int t = 0; t <= 2000 && mcu.State == McuState.Running; t += 50) mcu.Advance(Ms(t));
        Assert.True(mcu.FaultMessage is null, $"{mcu.FaultMessage} (línea {mcu.FaultLine})\n{mcu.SerialOutput}");
        Assert.True(mcu.ProgramEnded);
        return mcu.SerialOutput.Replace("\r", "");
    }

    [Fact]
    public void Language_basics_work()
    {
        string output = Output("""
            # números y texto
            a = 7
            b = 2
            print(a + b, a - b, a * b, a / b, a // b, a % b, a ** b, -7 // 2, -7 % 3)
            print(f"{a} y {b:03d} -> {a / b:.2f}", "%d%%" % 50, "{} {x}".format(1, x="dos"))
            nombre = "micropython"
            print(nombre.upper(), nombre[0:5], nombre[-6:], len(nombre), nombre[::-1][:3])
            print(", ".join(["a", "b", "c"]), "1,2,3".split(","), " hola ".strip())

            # listas, tuplas y diccionarios
            cuadrados = [x * x for x in range(6) if x % 2 == 0]
            print(cuadrados, sum(cuadrados), max(cuadrados), min(3, 1, 2), sorted([3, 1, 2], reverse=True))
            d = {"rojo": 1, "verde": 2}
            d["azul"] = 3
            print(d, d.get("negro", 0), list(d.keys()), "rojo" in d)
            x, y = 1, 2
            x, y = y, x
            print(x, y, (1, 2) + (3,), [1, 2] * 2)
            for i, letra in enumerate("ab"):
                print(i, letra, end=";")
            print()

            # funciones, lambdas y clases
            def factorial(n):
                return 1 if n <= 1 else n * factorial(n - 1)

            doble = lambda v: v * 2
            print(factorial(10), doble(21), list(map(doble, [1, 2])))

            class Contador:
                def __init__(self, inicio=0):
                    self.valor = inicio
                def sumar(self, n=1):
                    self.valor += n
                    return self.valor

            c = Contador(5)
            c.sumar()
            print(c.sumar(3), isinstance(c, Contador))

            # excepciones
            try:
                1 / 0
            except ZeroDivisionError as e:
                print("error:", e)
            finally:
                print("fin")

            class MiError(ValueError):
                pass

            try:
                raise MiError("propio")
            except ValueError as e:
                print("capturado", type(e).__name__ if hasattr(type(e), "__name__") else "MiError")

            total = 0
            i = 0
            while True:
                i += 1
                if i % 2:
                    continue
                if i > 10:
                    break
                total += i
            print("total", total, int("42") + 1, float("2.5"), str(3.0), round(2.675, 2), abs(-3))
            """);
        var lines = output.Split('\n');
        Assert.Equal("9 5 14 3.5 3 1 49 -4 2", lines[0]);
        Assert.Equal("7 y 002 -> 3.50 50% 1 dos", lines[1]);
        Assert.Equal("MICROPYTHON micro python 11 noh", lines[2]);
        Assert.Equal("a, b, c ['1', '2', '3'] hola", lines[3]);
        Assert.Equal("[0, 4, 16] 20 16 1 [3, 2, 1]", lines[4]);
        Assert.Equal("{'rojo': 1, 'verde': 2, 'azul': 3} 0 ['rojo', 'verde', 'azul'] True", lines[5]);
        Assert.Equal("2 1 (1, 2, 3) [1, 2, 1, 2]", lines[6]);
        Assert.Equal("0 a;1 b;", lines[7]);
        Assert.Equal("3628800 42 [2, 4]", lines[8]);
        Assert.Equal("9 True", lines[9]);
        Assert.Equal("error: división entre cero", lines[10]);
        Assert.Equal("fin", lines[11]);
        Assert.StartsWith("capturado", lines[12]);
        Assert.Equal("total 30 43 2.5 3.0 2.67 3", lines[13]);
    }

    [Fact]
    public void Blink_follows_simulated_time_on_the_pico()
    {
        using var mcu = Run("""
            from machine import Pin
            import time
            led = Pin(25, Pin.OUT)
            while True:
                led.on()
                time.sleep(0.5)
                led.off()
                time.sleep_ms(500)
            """);
        var led = mcu.Pin("GP25");
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(10));
        Assert.True(led.OutputHigh);
        Assert.Equal(3.3, led.OutputVolts, 3);
        mcu.Advance(Ms(490));
        Assert.True(led.OutputHigh);
        mcu.Advance(Ms(520));
        Assert.False(led.OutputHigh);
        mcu.Advance(Ms(1020));
        Assert.True(led.OutputHigh);
        Assert.Equal(McuState.Running, mcu.State);
        Assert.InRange(mcu.CurrentLine, 5, 8);
    }

    [Fact]
    public void Button_with_pull_up_and_irq_counter()
    {
        using var mcu = Run("""
            from machine import Pin
            import time
            cuenta = 0
            def pulsado(p):
                global cuenta
                cuenta += 1
            boton = Pin(4, Pin.IN, Pin.PULL_UP)
            boton.irq(trigger=Pin.IRQ_FALLING, handler=pulsado)
            led = Pin(2, Pin.OUT)
            while True:
                led.value(boton.value() == 0)
                time.sleep_ms(5)
            """, ArduinoBoard.Esp32);
        var button = mcu.Pin("GPIO4");
        var led = mcu.Pin("GPIO2");
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(20));
        Assert.Equal(PinMode.InputPullup, button.Mode);
        Assert.False(led.OutputHigh);
        for (int k = 0; k < 3; k++)
        {
            button.Value = 0;
            mcu.Advance(Ms(40 + k * 40));
            Assert.True(led.OutputHigh);
            button.Disconnect();
            mcu.Advance(Ms(60 + k * 40));
            Assert.False(led.OutputHigh);
        }
        Assert.Contains(mcu.Variables(), v => v.Name == "cuenta" && v.Value == "3");
    }

    [Fact]
    public void Pwm_adc_servo_and_buzzer()
    {
        using var mcu = Run("""
            from machine import Pin, PWM, ADC
            import time
            pot = ADC(Pin(26))
            led = PWM(Pin(15), freq=1000)
            servo = PWM(Pin(16), freq=50)
            zumbador = PWM(Pin(18), freq=440, duty_u16=32768)
            while True:
                led.duty_u16(pot.read_u16())
                servo.duty_ns(1_500_000)
                time.sleep_ms(10)
            """);
        var pot = mcu.Pin("GP26");
        pot.Value = 3.3 * 0.25;
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(50));
        var led = mcu.Pin("GP15");
        Assert.Equal(0.25, led.Duty!.Value, 2);
        Assert.Equal(1000, led.PwmHz);
        Assert.Equal(90, mcu.Pin("GP16").ServoAngle!.Value, 1);
        var buzzer = (BuzzerPart)Part.Create(new PartSpec(PartKind.Buzzer, "GP18"), mcu);
        Assert.Equal(440, buzzer.Frequency);
    }

    [Fact]
    public void Timer_callback_runs_periodically()
    {
        using var mcu = Run("""
            from machine import Pin, Timer
            import time
            ticks = 0
            def tic(t):
                global ticks
                ticks += 1
            Timer(-1).init(period=100, mode=Timer.PERIODIC, callback=tic)
            while True:
                time.sleep(1)
            """);
        for (int t = 0; t <= 1050; t += 10) mcu.Advance(Ms(t));
        Assert.Contains(mcu.Variables(), v => v.Name == "ticks" && v.Value == "10");
    }

    [Fact]
    public void Input_reads_a_line_from_the_console()
    {
        using var mcu = Run("""
            nombre = input("¿Nombre? ")
            print("Hola,", nombre)
            """);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(100));
        Assert.Equal(McuState.Running, mcu.State);
        mcu.SerialSend("Andrés\n");
        mcu.Advance(Ms(200));
        Assert.True(mcu.ProgramEnded);
        Assert.Contains("Hola, Andrés", mcu.SerialOutput);
    }

    [Fact]
    public void Runtime_errors_report_the_line_and_a_hint()
    {
        using var mcu = Run("""
            from machine import Pin
            led = Pin(2, Pin.OUT)
            led.on()
            sleep(1)
            """, ArduinoBoard.Esp32);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(10));
        Assert.Equal(McuState.Fault, mcu.State);
        Assert.Equal(4, mcu.FaultLine);
        Assert.Contains("NameError", mcu.FaultMessage);
        Assert.Contains("from time import sleep", mcu.FaultMessage);
    }

    [Fact]
    public void Esp32_input_only_pins_cannot_be_outputs()
    {
        using var mcu = Run("""
            from machine import Pin
            p = Pin(34, Pin.OUT)
            """, ArduinoBoard.Esp32);
        mcu.Advance(Ms(0));
        mcu.Advance(Ms(10));
        Assert.Equal(McuState.Fault, mcu.State);
        Assert.Contains("solo puede ser entrada", mcu.FaultMessage);
    }

    [Theory]
    [InlineData("if True\n    pass\n", 1)]
    [InlineData("x = 1\n  y = 2\n", 2)]
    [InlineData("print('hola)\n", 1)]
    [InlineData("for i in range(3):\nprint(i)\n", 2)]
    public void Syntax_errors_have_a_line(string source, int line)
    {
        var mcu = new ArduinoMcu("MCU1", ArduinoBoard.Pico);
        var ex = Assert.Throws<SketchCompileException>(() => mcu.Load(source));
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void Python_boards_use_gpio_names_and_3v3()
    {
        var esp = new ArduinoMcu("E", ArduinoBoard.Esp32);
        Assert.Equal("GPIO2", esp.Pins[2].Name);
        Assert.Equal(3.3, esp.Pins[2].NominalLevel);
        Assert.Equal(8, esp.Pins.Count(p => p.IsAnalog));
        var pico = new ArduinoMcu("P", ArduinoBoard.Pico);
        Assert.Equal("GP28", pico.Pins[28].Name);
        Assert.Equal(3, pico.Pins.Count(p => p.IsAnalog));
        Assert.DoesNotContain(ArduinoBoard.Esp32, ArduinoBoard.All);
        Assert.Contains(ArduinoBoard.Pico, ArduinoBoard.Everything);
    }

    [Theory]
    [MemberData(nameof(ExampleCases))]
    public void Every_example_runs_with_its_parts(string boardId, string name)
    {
        var board = ArduinoBoard.Python.Single(b => b.Id == boardId);
        var example = PythonExamples.For(board).Single(e => e.Name == name);
        using var mcu = new ArduinoMcu("MCU1", board);
        var parts = example.Setup.Select(s => Part.Create(s, mcu)).ToList();
        mcu.Load(example.Source);
        mcu.Start();
        for (int t = 0; t <= 3000; t += 20)
        {
            foreach (var p in parts) p.Apply();
            mcu.Advance(Ms(t));
        }
        Assert.True(mcu.FaultMessage is null, $"{name} en {board.Name}: {mcu.FaultMessage} (línea {mcu.FaultLine})");
        Assert.True(mcu.State == McuState.Running || mcu.ProgramEnded, $"{name} en {board.Name}: {mcu.State}");
    }

    public static IEnumerable<object[]> ExampleCases =>
        ArduinoBoard.Python.SelectMany(b => PythonExamples.For(b).Select(e => new object[] { b.Id, e.Name }));
}
