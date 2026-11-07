void setup() {
  Serial.begin(115200);
  pinMode(9, OUTPUT);
}

void loop() {
  int lectura = analogRead(A0);
  analogWrite(9, lectura / 4);
  Serial.println(lectura);
  delay(50);
}
