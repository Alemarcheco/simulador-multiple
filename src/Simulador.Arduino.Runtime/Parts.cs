namespace Simulador.Arduino.Runtime;

public enum PartKind { Led, Button, Potentiometer, Buzzer, Servo, Ldr, Lm35 }

/// <summary>Pieza que se añade al montaje: tipo, pin de la placa y, para los LEDs, el color.</summary>
public sealed record PartSpec(PartKind Kind, string Pin, string? Color = null, string? Label = null);

/// <summary>
/// Pieza conectada a un pin de la placa. Las de entrada (pulsador, potenciómetro, sensores) ponen la tensión del pin;
/// las de salida (LED, zumbador, servo) leen lo que da la placa.
/// </summary>
public abstract class Part
{
    protected Part(PinPort pin, string label)
    {
        Pin = pin;
        Label = label;
    }

    public PinPort Pin { get; }
    public string Label { get; set; }
    public abstract PartKind Kind { get; }

    /// <summary>true si la pieza da señal al pin (entrada para la placa).</summary>
    public virtual bool IsInput => false;

    /// <summary>Escribe la tensión de la pieza en el pin. Se llama antes de avanzar la simulación.</summary>
    public virtual void Apply() { }

    /// <summary>Texto corto del estado actual.</summary>
    public abstract string Describe();

    /// <summary>Quita la influencia de la pieza sobre el pin (al borrarla).</summary>
    public virtual void Detach() { }

    public static Part Create(PartSpec spec, ArduinoMcu mcu)
    {
        var pin = mcu.Pin(spec.Pin);
        string label = spec.Label ?? DefaultLabel(spec.Kind);
        return spec.Kind switch
        {
            PartKind.Led => new LedPart(pin, label, spec.Color ?? "#FF3B30"),
            PartKind.Button => new ButtonPart(pin, label),
            PartKind.Potentiometer => new PotentiometerPart(pin, label),
            PartKind.Buzzer => new BuzzerPart(pin, label),
            PartKind.Servo => new ServoPart(pin, label),
            PartKind.Ldr => new LdrPart(pin, label),
            _ => new Lm35Part(pin, label),
        };
    }

    public static string DefaultLabel(PartKind k) => k switch
    {
        PartKind.Led => "LED",
        PartKind.Button => "Pulsador",
        PartKind.Potentiometer => "Potenciómetro",
        PartKind.Buzzer => "Zumbador",
        PartKind.Servo => "Servo",
        PartKind.Ldr => "Sensor de luz (LDR)",
        _ => "Sensor de temperatura LM35",
    };

    /// <summary>Si la pieza se puede conectar a ese pin (las analógicas necesitan A0…).</summary>
    public static bool Fits(PartKind k, PinPort p) => k switch
    {
        PartKind.Potentiometer or PartKind.Ldr or PartKind.Lm35 => p.IsAnalog,
        PartKind.Led or PartKind.Buzzer or PartKind.Servo or PartKind.Button => !p.IsAnalogOnly,
        _ => true,
    };
}

/// <summary>LED con su resistencia, del pin a GND. El brillo sigue a la tensión media (PWM incluido).</summary>
public sealed class LedPart(PinPort pin, string label, string color) : Part(pin, label)
{
    public override PartKind Kind => PartKind.Led;
    public string Color { get; set; } = color;

    /// <summary>Brillo de 0 a 1. Un LED rojo necesita ~1,8 V para empezar a lucir.</summary>
    public double Brightness => Pin.IsOutput ? Math.Clamp((Pin.OutputVolts - 1.8) / (Pin.NominalLevel - 1.8), 0, 1) : 0;

    public bool IsOn => Brightness > 0.02;

    public override string Describe() => !Pin.IsOutput ? "apagado (el pin no es salida: falta pinMode OUTPUT)"
        : Pin.Duty is { } d ? $"PWM {d * 100:0} %" : IsOn ? "encendido" : "apagado";
}

/// <summary>Pulsador entre el pin y GND (para usar con INPUT_PULLUP) o entre el pin y 5 V (con resistencia a GND).</summary>
public sealed class ButtonPart(PinPort pin, string label) : Part(pin, label)
{
    public override PartKind Kind => PartKind.Button;
    public override bool IsInput => true;
    public bool Pressed { get; set; }

    /// <summary>true = el pulsador une el pin con GND; false = con 5 V (y una resistencia de pull-down).</summary>
    public bool ToGround { get; set; } = true;

    public override void Apply()
    {
        if (ToGround)
        {
            if (Pressed) Pin.Value = 0;
            else Pin.Disconnect(); // sin pulsar queda al aire: hace falta INPUT_PULLUP
        }
        else Pin.Value = Pressed ? Pin.NominalLevel : 0;
    }

    public override void Detach() => Pin.Disconnect();

    public override string Describe()
    {
        string state = Pressed ? "pulsado" : "suelto";
        if (ToGround && Pin.Mode != PinMode.InputPullup && !Pressed && !Pin.IsOutput)
            state += " · ¡pin al aire! usa pinMode(…, INPUT_PULLUP)";
        return state;
    }
}

/// <summary>Potenciómetro entre 5 V y GND con el cursor en una entrada analógica.</summary>
public sealed class PotentiometerPart(PinPort pin, string label) : Part(pin, label)
{
    public override PartKind Kind => PartKind.Potentiometer;
    public override bool IsInput => true;
    /// <summary>Posición del mando, de 0 a 1.</summary>
    public double Position { get; set; } = 0.5;
    public override void Apply() => Pin.Value = Position * Pin.NominalLevel;
    public override void Detach() => Pin.Disconnect();
    public override string Describe() => $"{Position * 100:0} % → {Position * Pin.NominalLevel:0.00} V";
}

/// <summary>Zumbador pasivo: suena con tone() o con un pin que conmuta.</summary>
public sealed class BuzzerPart(PinPort pin, string label) : Part(pin, label)
{
    public override PartKind Kind => PartKind.Buzzer;
    /// <summary>Frecuencia de tone() o de un PWM audible (MicroPython: PWM con duty entre 0 y 100 %).</summary>
    public double? Frequency => Pin.ToneHz
        ?? (Pin.PwmHz is { } f and >= 20 and <= 20_000 && Pin.Duty is > 0.01 and < 0.99 && Pin.ServoAngle is null ? f : null);
    public bool Sounding => Frequency is not null;
    public override string Describe() => Frequency is { } hz ? $"suena a {hz:0} Hz ({Note(hz)})" : "en silencio";

    private static readonly string[] Names = ["Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si"];

    /// <summary>Nota más cercana (La4 = 440 Hz).</summary>
    public static string Note(double hz)
    {
        if (hz <= 0) return "-";
        int n = (int)Math.Round(12 * Math.Log2(hz / 440)) + 57;
        return $"{Names[((n % 12) + 12) % 12]}{n / 12}";
    }
}

/// <summary>Servo de modelismo (0-180°) controlado con la librería Servo.</summary>
public sealed class ServoPart(PinPort pin, string label) : Part(pin, label)
{
    public override PartKind Kind => PartKind.Servo;
    /// <summary>Ángulo actual. El brazo gira a velocidad limitada (~0,1 s cada 60°), como uno real.</summary>
    public double Angle { get; private set; } = 90;

    public void Animate(TimeSpan dt)
    {
        if (Pin.ServoAngle is not { } target) return;
        double max = 600 * dt.TotalSeconds;
        Angle += Math.Clamp(target - Angle, -max, max);
    }

    public override string Describe() => Pin.ServoAngle is null ? "sin señal (falta attach)" : $"{Angle:0}°";
}

/// <summary>Fotorresistencia con resistencia de 10 kΩ en divisor: más luz, más tensión.</summary>
public sealed class LdrPart(PinPort pin, string label) : Part(pin, label)
{
    public override PartKind Kind => PartKind.Ldr;
    public override bool IsInput => true;
    /// <summary>Iluminación en lux (0 = oscuridad, 1000 = día nublado, 10000 = sol).</summary>
    public double Lux { get; set; } = 300;

    public override void Apply()
    {
        // LDR típica: R ≈ 500 kΩ·lux^-0,8 (≈ 2 kΩ con 1000 lux). Divisor con 10 kΩ a GND.
        double r = 500_000 * Math.Pow(Math.Max(Lux, 0.1), -0.8);
        Pin.Value = Pin.NominalLevel * 10_000 / (10_000 + r);
    }

    public override void Detach() => Pin.Disconnect();
    public override string Describe() => $"{Lux:0} lux → {Pin.Value:0.00} V";
}

/// <summary>Sensor de temperatura LM35: 10 mV por °C.</summary>
public sealed class Lm35Part(PinPort pin, string label) : Part(pin, label)
{
    public override PartKind Kind => PartKind.Lm35;
    public override bool IsInput => true;
    public double Celsius { get; set; } = 22;
    public override void Apply() => Pin.Value = Math.Clamp(Celsius, 0, 150) * 0.01;
    public override void Detach() => Pin.Disconnect();
    public override string Describe() => $"{Celsius:0.0} °C → {Celsius * 0.01:0.000} V";
}
