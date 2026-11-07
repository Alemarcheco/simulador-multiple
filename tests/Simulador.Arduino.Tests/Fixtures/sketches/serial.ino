int contador = 0;

void setup() {
  Serial.begin(9600);
  Serial.println("Hola desde el chip");
}

void loop() {
  while (Serial.available() > 0) {
    char c = Serial.read();
    Serial.print("eco:");
    Serial.println(c);
  }
  contador++;
  Serial.print("t=");
  Serial.println(millis());
  delay(250);
}
