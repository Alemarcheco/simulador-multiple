namespace Simulador.Arduino.Runtime;

/// <summary>
/// Programas de ejemplo en MicroPython para ESP32 y Raspberry Pi Pico. El mismo ejemplo usa en cada placa los pines
/// que le corresponden (LED integrado en GPIO2 del ESP32 o en GP25 de la Pico, ADC en GPIO34 o GP26…).
/// </summary>
public static class PythonExamples
{
    private sealed record Pins(bool Esp, int Led, int Button, int Pot, int Ldr, int Temp, int Red, int Amber, int Green, int PwmLed, int Servo, int Buzzer, int[] Row);

    private static Pins PinsFor(ArduinoBoard b) => b == ArduinoBoard.Esp32
        ? new(true, 2, 4, 34, 35, 32, 25, 26, 27, 18, 13, 19, [13, 12, 14, 27, 26, 25])
        : new(false, 25, 14, 26, 27, 28, 13, 12, 11, 15, 16, 18, [2, 3, 4, 5, 6, 7]);

    /// <summary>Ejemplos de la placa, de más fácil a más difícil.</summary>
    public static IReadOnlyList<ArduinoExample> For(ArduinoBoard board)
    {
        var p = PinsFor(board);
        string n(int pin) => board.PinName(pin);
        // En el ESP32 el ADC necesita atenuación de 11 dB para medir hasta 3,3 V.
        string Attn(string v) => p.Esp ? $"\n{v}.atten(ADC.ATTN_11DB)   # rango completo: 0 a 3,3 V" : "";
        var list = new List<ArduinoExample>
        {
            new("Parpadeo (Blink)", $"El LED de la placa ({n(p.Led)}) se enciende y se apaga cada medio segundo.", $$"""
                # Parpadeo del LED integrado en la placa
                from machine import Pin
                import time

                led = Pin({{p.Led}}, Pin.OUT)

                while True:
                    led.value(not led.value())   # cambia de encendido a apagado
                    time.sleep(0.5)
                """),
            new("Pulsador y LED", $"Mientras se pulsa el botón de {n(p.Button)} se enciende el LED. Usa la resistencia de pull-up interna.", $$"""
                # Pulsador entre el pin y GND. Con PULL_UP, sin pulsar se lee 1 y pulsado 0.
                from machine import Pin

                boton = Pin({{p.Button}}, Pin.IN, Pin.PULL_UP)
                led = Pin({{p.Led}}, Pin.OUT)

                while True:
                    if boton.value() == 0:
                        led.on()
                    else:
                        led.off()
                """, [new(PartKind.Button, n(p.Button)), new(PartKind.Led, n(p.Led), "#FF3B30")]),
            new("Semáforo", $"Rojo ({n(p.Red)}), ámbar ({n(p.Amber)}) y verde ({n(p.Green)}) con sus tiempos, usando una función.", $$"""
                from machine import Pin
                from time import sleep

                rojo = Pin({{p.Red}}, Pin.OUT)
                ambar = Pin({{p.Amber}}, Pin.OUT)
                verde = Pin({{p.Green}}, Pin.OUT)

                def encender(luz, segundos):
                    luz.on()
                    sleep(segundos)
                    luz.off()

                while True:
                    encender(verde, 4)
                    encender(ambar, 1)
                    encender(rojo, 4)
                """, [new(PartKind.Led, n(p.Red), "#FF3B30", "Rojo"), new(PartKind.Led, n(p.Amber), "#FFB020", "Ámbar"), new(PartKind.Led, n(p.Green), "#39D353", "Verde")]),
            new("LED que respira (PWM)", $"Un PWM en {n(p.PwmLed)} sube y baja el brillo del LED con duty_u16 (0 a 65535).", $$"""
                from machine import Pin, PWM
                import time

                led = PWM(Pin({{p.PwmLed}}), freq=1000)

                while True:
                    for brillo in range(0, 65536, 2048):        # sube
                        led.duty_u16(brillo)
                        time.sleep_ms(20)
                    for brillo in range(65535, -1, -2048):      # baja
                        led.duty_u16(brillo)
                        time.sleep_ms(20)
                """, [new(PartKind.Led, n(p.PwmLed), "#2F81F7")]),
            new("Potenciómetro regula un LED", $"Lee el potenciómetro de {n(p.Pot)} con el ADC y lo usa como brillo del LED de {n(p.PwmLed)}. Muestra la tensión en la consola.", $$"""
                from machine import Pin, ADC, PWM
                import time

                pot = ADC(Pin({{p.Pot}})){{Attn("pot")}}
                led = PWM(Pin({{p.PwmLed}}), freq=1000)

                while True:
                    valor = pot.read_u16()             # 0 a 65535
                    led.duty_u16(valor)
                    voltios = valor * 3.3 / 65535
                    print(f"ADC = {valor:5d}  ->  {voltios:.2f} V")
                    time.sleep(0.25)
                """, [new(PartKind.Potentiometer, n(p.Pot)), new(PartKind.Led, n(p.PwmLed), "#FFB020")]),
            new("Contador con interrupción", $"Cada pulsación del botón de {n(p.Button)} dispara una interrupción (irq) que suma uno al contador, sin tener que leer el pin en el bucle.", $$"""
                from machine import Pin
                import time

                pulsaciones = 0

                def al_pulsar(pin):
                    global pulsaciones
                    pulsaciones += 1

                boton = Pin({{p.Button}}, Pin.IN, Pin.PULL_UP)
                boton.irq(trigger=Pin.IRQ_FALLING, handler=al_pulsar)
                led = Pin({{p.Led}}, Pin.OUT)

                anterior = 0
                while True:
                    if pulsaciones != anterior:
                        anterior = pulsaciones
                        print("Pulsaciones:", pulsaciones)
                        led.value(pulsaciones % 2)    # el LED cambia en cada pulsación
                    time.sleep_ms(50)
                """, [new(PartKind.Button, n(p.Button)), new(PartKind.Led, n(p.Led), "#39D353")]),
            new("Temporizador (Timer)", "Un temporizador hace parpadear el LED por su cuenta mientras el programa principal cuenta segundos en la consola.", $$"""
                from machine import Pin, Timer
                import time

                led = Pin({{p.Led}}, Pin.OUT)

                def parpadear(t):
                    led.value(not led.value())

                temporizador = Timer({{(p.Esp ? "0" : "-1")}})
                temporizador.init(period=250, mode=Timer.PERIODIC, callback=parpadear)

                segundos = 0
                while True:
                    time.sleep(1)
                    segundos += 1
                    print("Han pasado", segundos, "s")
                """, [new(PartKind.Led, n(p.Led), "#FF3B30")]),
            new("Servo de barrido", $"Un servo en {n(p.Servo)} gira de 0° a 180° y vuelve. El PWM es de 50 Hz con impulsos de 0,5 a 2,5 ms.", $$"""
                from machine import Pin, PWM
                import time

                servo = PWM(Pin({{p.Servo}}), freq=50)

                def angulo(grados):
                    # 0° = 0,5 ms y 180° = 2,5 ms de impulso
                    impulso_ns = int(500_000 + grados * 2_000_000 / 180)
                    servo.duty_ns(impulso_ns)

                while True:
                    for g in range(0, 181, 10):
                        angulo(g)
                        time.sleep_ms(60)
                    for g in range(180, -1, -10):
                        angulo(g)
                        time.sleep_ms(60)
                """, [new(PartKind.Servo, n(p.Servo))]),
            new("Melodía con zumbador", $"Un PWM en {n(p.Buzzer)} toca notas en un zumbador; la frecuencia de cada nota está en un diccionario.", $$"""
                from machine import Pin, PWM
                import time

                NOTAS = {"Do": 262, "Re": 294, "Mi": 330, "Fa": 349, "Sol": 392, "La": 440, "Si": 494}
                melodia = [("Do", 1), ("Do", 1), ("Re", 2), ("Do", 2), ("Fa", 2), ("Mi", 4)]

                zumbador = PWM(Pin({{p.Buzzer}}), freq=440, duty_u16=0)

                for nota, tiempos in melodia:
                    zumbador.freq(NOTAS[nota])
                    zumbador.duty_u16(32768)      # 50 %: suena
                    print("Nota:", nota)
                    time.sleep_ms(200 * tiempos)
                    zumbador.duty_u16(0)          # silencio entre notas
                    time.sleep_ms(50)

                zumbador.deinit()
                print("Fin de la melodía")
                """, [new(PartKind.Buzzer, n(p.Buzzer))]),
            new("Luz automática con sensor (LDR)", $"Una fotorresistencia en {n(p.Ldr)}: si hay poca luz se enciende la lámpara de {n(p.Red)}, con histéresis para que no parpadee.", $$"""
                from machine import Pin, ADC
                import time

                ldr = ADC(Pin({{p.Ldr}})){{Attn("ldr")}}
                lampara = Pin({{p.Red}}, Pin.OUT)

                ENCENDER = 15000   # por debajo: está oscuro
                APAGAR = 25000     # por encima: hay luz

                while True:
                    luz = ldr.read_u16()
                    if luz < ENCENDER and not lampara.value():
                        lampara.on()
                        print("Oscuro (", luz, "): lámpara encendida")
                    elif luz > APAGAR and lampara.value():
                        lampara.off()
                        print("Hay luz (", luz, "): lámpara apagada")
                    time.sleep_ms(200)
                """, [new(PartKind.Ldr, n(p.Ldr)), new(PartKind.Led, n(p.Red), "#FFF4C2", "Lámpara")]),
            new("Termómetro LM35", $"Un LM35 en {n(p.Temp)} da 10 mV por °C. Se calcula la temperatura y por encima de 30 °C se enciende el ventilador ({n(p.Green)}).", $$"""
                from machine import Pin, ADC
                import time

                sensor = ADC(Pin({{p.Temp}})){{Attn("sensor")}}
                ventilador = Pin({{p.Green}}, Pin.OUT)

                def leer_temperatura():
                    voltios = sensor.read_u16() * 3.3 / 65535
                    return voltios * 100          # 10 mV por grado

                while True:
                    t = leer_temperatura()
                    ventilador.value(t > 30)
                    estado = "ENCENDIDO" if ventilador.value() else "apagado"
                    print(f"Temperatura: {t:.1f} °C   ventilador {estado}")
                    time.sleep(1)
                """, [new(PartKind.Lm35, n(p.Temp)), new(PartKind.Led, n(p.Green), "#2F81F7", "Ventilador")]),
            new("Coche fantástico (lista de LEDs)", "Seis LEDs guardados en una lista se encienden uno tras otro, de ida y vuelta.", $$"""
                from machine import Pin
                import time

                leds = [Pin(n, Pin.OUT) for n in {{"[" + string.Join(", ", p.Row) + "]"}}]

                while True:
                    for led in leds + leds[-2:0:-1]:     # ida y vuelta
                        led.on()
                        time.sleep_ms(80)
                        led.off()
                """, p.Row.Select(r => new PartSpec(PartKind.Led, n(r), "#FF3B30")).ToList()),
            new("Encender un LED desde la consola", "Escribe on u off en la consola (y pulsa Enviar) para encender o apagar el LED. Usa input() como en el ordenador.", $$"""
                from machine import Pin

                led = Pin({{p.Led}}, Pin.OUT)
                print("Escribe 'on' para encender y 'off' para apagar")

                while True:
                    orden = input("> ").strip().lower()
                    if orden == "on":
                        led.on()
                        print("LED encendido")
                    elif orden == "off":
                        led.off()
                        print("LED apagado")
                    else:
                        print("No entiendo:", orden)
                """, [new(PartKind.Led, n(p.Led), "#39D353")]),
            new("Dado electrónico", $"Al pulsar el botón de {n(p.Button)} sale un número del 1 al 6 (random.randint), que se muestra con seis LEDs y en la consola.", $$"""
                from machine import Pin
                import random
                import time

                boton = Pin({{p.Button}}, Pin.IN, Pin.PULL_UP)
                leds = [Pin(n, Pin.OUT) for n in {{"[" + string.Join(", ", p.Row) + "]"}}]

                def mostrar(numero):
                    for i, led in enumerate(leds):
                        led.value(i < numero)

                while True:
                    if boton.value() == 0:
                        for _ in range(10):              # animación antes del resultado
                            mostrar(random.randint(1, 6))
                            time.sleep_ms(60)
                        dado = random.randint(1, 6)
                        mostrar(dado)
                        print("Ha salido un", dado)
                        while boton.value() == 0:        # espera a que se suelte
                            time.sleep_ms(10)
                    time.sleep_ms(10)
                """, [new PartSpec(PartKind.Button, n(p.Button)), .. p.Row.Select(r => new PartSpec(PartKind.Led, n(r), "#39D353"))]),
        };
        if (!p.Esp)
            list.Insert(7, new("Sensor de temperatura interno", "El RP2040 lleva un sensor de temperatura en el canal 4 del ADC. Se lee y se muestra en la consola.", """
                from machine import ADC
                import time

                sensor = ADC(4)    # canal 4: sensor interno del RP2040

                while True:
                    voltios = sensor.read_u16() * 3.3 / 65535
                    grados = 27 - (voltios - 0.706) / 0.001721   # fórmula del fabricante
                    print("Temperatura del chip: %.1f °C" % grados)
                    time.sleep(2)
                """));
        return list;
    }
}
