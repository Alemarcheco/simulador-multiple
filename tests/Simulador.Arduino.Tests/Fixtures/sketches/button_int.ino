volatile int pulsaciones = 0;

void contar() {
  pulsaciones++;
}

void setup() {
  Serial.begin(9600);
  pinMode(2, INPUT_PULLUP);
  pinMode(13, OUTPUT);
  attachInterrupt(digitalPinToInterrupt(2), contar, FALLING);
}

void loop() {
  digitalWrite(13, digitalRead(2) == LOW ? HIGH : LOW);
  static int ultimo = -1;
  if (pulsaciones != ultimo) {
    ultimo = pulsaciones;
    Serial.print("pulsaciones=");
    Serial.println(ultimo);
  }
}
