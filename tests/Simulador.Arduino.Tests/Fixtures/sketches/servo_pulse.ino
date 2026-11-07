// Impulsos de servo hechos a mano (1,5 ms cada 20 ms): sin la librería Servo.
void setup() {
  pinMode(9, OUTPUT);
}

void loop() {
  digitalWrite(9, HIGH);
  delayMicroseconds(1500);
  digitalWrite(9, LOW);
  delay(18);
  delayMicroseconds(500);
}
