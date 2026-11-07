namespace Simulador.Arduino.Runtime;

/// <summary>Ejemplo con el montaje que necesita (piezas y pines).</summary>
public sealed record ArduinoExample(string Name, string Description, string Source, IReadOnlyList<PartSpec>? Parts = null)
{
    public IReadOnlyList<PartSpec> Setup => Parts ?? [];
}

/// <summary>Sketches de ejemplo, de más fácil a más difícil. Todos funcionan en Uno, Nano y Mega.</summary>
public static class ArduinoExamples
{
    public static IReadOnlyList<ArduinoExample> All { get; } =
    [
        new("Parpadeo (Blink)", "El primer programa: el LED de la placa (pin 13) se enciende y se apaga cada segundo.", """
            // Parpadeo del LED integrado en la placa (pin 13).
            void setup() {
              pinMode(LED_BUILTIN, OUTPUT);   // el pin 13 es una salida
            }

            void loop() {
              digitalWrite(LED_BUILTIN, HIGH); // enciende
              delay(1000);                     // espera 1 segundo
              digitalWrite(LED_BUILTIN, LOW);  // apaga
              delay(1000);
            }
            """),
        new("Pulsador y LED", "Mientras se pulsa el botón del pin 2 se enciende el LED del pin 13. Usa la resistencia de pull-up interna.", """
            // Pulsador entre el pin 2 y GND. Con INPUT_PULLUP, sin pulsar se lee HIGH y pulsado LOW.
            const int pinBoton = 2;
            const int pinLed = 13;

            void setup() {
              pinMode(pinBoton, INPUT_PULLUP);
              pinMode(pinLed, OUTPUT);
            }

            void loop() {
              if (digitalRead(pinBoton) == LOW) {
                digitalWrite(pinLed, HIGH);
              } else {
                digitalWrite(pinLed, LOW);
              }
            }
            """, [new(PartKind.Button, "D2"), new(PartKind.Led, "D13", "#FF3B30")]),
        new("Semáforo", "Rojo (pin 10), ámbar (pin 11) y verde (pin 12) con sus tiempos.", """
            const int rojo = 10;
            const int ambar = 11;
            const int verde = 12;

            void setup() {
              pinMode(rojo, OUTPUT);
              pinMode(ambar, OUTPUT);
              pinMode(verde, OUTPUT);
            }

            void encender(int pin, int ms) {
              digitalWrite(pin, HIGH);
              delay(ms);
              digitalWrite(pin, LOW);
            }

            void loop() {
              encender(rojo, 5000);
              encender(verde, 4000);
              encender(ambar, 1500);
            }
            """, [new(PartKind.Led, "D10", "#FF3B30", "Rojo"), new(PartKind.Led, "D11", "#FFB020", "Ámbar"), new(PartKind.Led, "D12", "#39D353", "Verde")]),
        new("Luz que se enciende poco a poco (PWM)", "analogWrite en el pin 9 sube y baja el brillo del LED.", """
            // Solo los pines con ~ tienen PWM (en el Uno: 3, 5, 6, 9, 10 y 11).
            const int led = 9;

            void setup() {
              pinMode(led, OUTPUT);
            }

            void loop() {
              for (int brillo = 0; brillo <= 255; brillo += 5) {
                analogWrite(led, brillo);
                delay(30);
              }
              for (int brillo = 255; brillo >= 0; brillo -= 5) {
                analogWrite(led, brillo);
                delay(30);
              }
            }
            """, [new(PartKind.Led, "D9", "#FF3B30")]),
        new("Potenciómetro regula un LED", "Lee el potenciómetro en A0 (0-1023) y lo convierte en brillo (0-255) para el pin 9. Muestra el valor por el monitor serie.", """
            int lectura = 0;
            int brillo = 0;

            void setup() {
              Serial.begin(9600);
              pinMode(9, OUTPUT);
            }

            void loop() {
              lectura = analogRead(A0);                 // 0 V → 0, 5 V → 1023
              brillo = map(lectura, 0, 1023, 0, 255);
              analogWrite(9, brillo);
              Serial.print("A0 = ");
              Serial.print(lectura);
              Serial.print("  brillo = ");
              Serial.println(brillo);
              delay(200);
            }
            """, [new(PartKind.Potentiometer, "A0"), new(PartKind.Led, "D9", "#FF3B30")]),
        new("Parpadeo sin delay (millis)", "Dos LEDs parpadean a ritmos distintos sin bloquear el programa, usando millis().", """
            unsigned long antesA = 0;
            unsigned long antesB = 0;
            bool estadoA = LOW;
            bool estadoB = LOW;

            void setup() {
              pinMode(12, OUTPUT);
              pinMode(13, OUTPUT);
            }

            void loop() {
              unsigned long ahora = millis();
              if (ahora - antesA >= 500) {     // cada medio segundo
                antesA = ahora;
                estadoA = !estadoA;
                digitalWrite(12, estadoA);
              }
              if (ahora - antesB >= 130) {     // mucho más rápido
                antesB = ahora;
                estadoB = !estadoB;
                digitalWrite(13, estadoB);
              }
            }
            """, [new(PartKind.Led, "D12", "#39D353"), new(PartKind.Led, "D13", "#4DA3FF")]),
        new("Coche fantástico (array de LEDs)", "Seis LEDs en los pines 2 a 7 se encienden uno tras otro, de ida y vuelta.", """
            int leds[] = {2, 3, 4, 5, 6, 7};
            const int total = 6;

            void setup() {
              for (int i = 0; i < total; i++) {
                pinMode(leds[i], OUTPUT);
              }
            }

            void loop() {
              for (int i = 0; i < total; i++) {
                digitalWrite(leds[i], HIGH);
                delay(80);
                digitalWrite(leds[i], LOW);
              }
              for (int i = total - 2; i > 0; i--) {
                digitalWrite(leds[i], HIGH);
                delay(80);
                digitalWrite(leds[i], LOW);
              }
            }
            """, [new(PartKind.Led, "D2", "#FF3B30"), new(PartKind.Led, "D3", "#FF3B30"), new(PartKind.Led, "D4", "#FF3B30"), new(PartKind.Led, "D5", "#FF3B30"), new(PartKind.Led, "D6", "#FF3B30"), new(PartKind.Led, "D7", "#FF3B30")]),
        new("Encender un LED desde el monitor serie", "Escribe 1 o 0 en el monitor serie para encender o apagar el LED del pin 13.", """
            void setup() {
              Serial.begin(9600);
              pinMode(13, OUTPUT);
              Serial.println("Escribe 1 para encender y 0 para apagar");
            }

            void loop() {
              if (Serial.available() > 0) {
                char orden = Serial.read();
                if (orden == '1') {
                  digitalWrite(13, HIGH);
                  Serial.println("LED encendido");
                } else if (orden == '0') {
                  digitalWrite(13, LOW);
                  Serial.println("LED apagado");
                }
              }
            }
            """, [new(PartKind.Led, "D13", "#FFE14D")]),
        new("Servo de barrido", "Un servo en el pin 9 gira de 0° a 180° y vuelve.", """
            #include <Servo.h>

            Servo miServo;

            void setup() {
              miServo.attach(9);
            }

            void loop() {
              for (int angulo = 0; angulo <= 180; angulo += 2) {
                miServo.write(angulo);
                delay(15);
              }
              for (int angulo = 180; angulo >= 0; angulo -= 2) {
                miServo.write(angulo);
                delay(15);
              }
            }
            """, [new(PartKind.Servo, "D9")]),
        new("Melodía con zumbador", "tone() toca notas en un zumbador conectado al pin 8.", """
            // Frecuencias en Hz de las notas.
            #define DO  262
            #define RE  294
            #define MI  330
            #define FA  349
            #define SOL 392

            int melodia[] = {DO, RE, MI, FA, SOL, SOL, 0, MI, MI, RE, DO};
            int duracion[] = {250, 250, 250, 250, 500, 500, 250, 250, 250, 250, 500};

            void setup() { }

            void loop() {
              for (int i = 0; i < 11; i++) {
                if (melodia[i] > 0) tone(8, melodia[i], duracion[i]);
                delay(duracion[i] * 1.3);
              }
              noTone(8);
              delay(1000);
            }
            """, [new(PartKind.Buzzer, "D8")]),
        new("Luz automática con sensor (LDR)", "Una fotorresistencia en A1: si hay poca luz se enciende la lámpara del pin 7, con histéresis para que no parpadee.", """
            const int sensor = A1;
            const int lampara = 7;
            const int umbralOscuro = 300;
            const int umbralClaro = 400;
            bool encendida = false;

            void setup() {
              Serial.begin(9600);
              pinMode(lampara, OUTPUT);
            }

            void loop() {
              int luz = analogRead(sensor);
              if (!encendida && luz < umbralOscuro) {
                encendida = true;
                Serial.println("Oscuro: enciendo");
              }
              if (encendida && luz > umbralClaro) {
                encendida = false;
                Serial.println("Claro: apago");
              }
              digitalWrite(lampara, encendida);
              delay(100);
            }
            """, [new(PartKind.Ldr, "A1"), new(PartKind.Led, "D7", "#FFE14D", "Lámpara")]),
        new("Termómetro LM35", "Un sensor LM35 en A0 da 10 mV por °C. Se calcula la temperatura y se enciende un ventilador (pin 6) por encima de 30 °C.", """
            float temperatura;

            void setup() {
              Serial.begin(9600);
              pinMode(6, OUTPUT);
            }

            void loop() {
              int lectura = analogRead(A0);
              float voltios = lectura * 5.0 / 1023.0;
              temperatura = voltios * 100.0;        // 10 mV/°C
              Serial.print("Temperatura: ");
              Serial.print(temperatura, 1);
              Serial.println(" C");
              digitalWrite(6, temperatura > 30.0);
              delay(500);
            }
            """, [new(PartKind.Lm35, "A0"), new(PartKind.Led, "D6", "#4DA3FF", "Ventilador")]),
        new("Dado electrónico", "Al pulsar el botón del pin 2 sale un número del 1 al 6, que se muestra con LEDs en los pines 8 a 13 y por el monitor serie.", """
            int leds[] = {8, 9, 10, 11, 12, 13};

            void mostrar(int n) {
              for (int i = 0; i < 6; i++) {
                digitalWrite(leds[i], i < n ? HIGH : LOW);
              }
            }

            void setup() {
              Serial.begin(9600);
              pinMode(2, INPUT_PULLUP);
              for (int i = 0; i < 6; i++) pinMode(leds[i], OUTPUT);
              randomSeed(analogRead(A5));
            }

            void loop() {
              if (digitalRead(2) == LOW) {
                // animación: números rápidos antes del resultado
                for (int k = 0; k < 10; k++) {
                  mostrar(random(1, 7));
                  delay(60);
                }
                int dado = random(1, 7);
                mostrar(dado);
                Serial.print("Ha salido un ");
                Serial.println(dado);
                while (digitalRead(2) == LOW) delay(10);  // espera a que se suelte
              }
            }
            """, [new(PartKind.Button, "D2"), new(PartKind.Led, "D8", "#39D353"), new(PartKind.Led, "D9", "#39D353"), new(PartKind.Led, "D10", "#39D353"), new(PartKind.Led, "D11", "#39D353"), new(PartKind.Led, "D12", "#39D353"), new(PartKind.Led, "D13", "#39D353")]),
    ];
}
