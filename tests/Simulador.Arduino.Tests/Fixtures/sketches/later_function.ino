void setup() {
  Serial.begin(9600);
  Serial.println(doble(21));
}

void loop() {
}

int doble(int x) {
  return x * 2;
}
