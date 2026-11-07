# Avisos de terceros

## avr8js

El núcleo de la CPU AVR del modo «chip real» de Arduino (`src/Simulador.Arduino.Runtime/Avr/AvrCpu.cs`,
`AvrInstruction.cs` y `AvrUsart.cs`) está traducido a C# de [avr8js](https://github.com/wokwi/avr8js) 0.21.1.

```
The MIT License (MIT)

Copyright (c) 2019-2025 Uri Shaked

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Núcleo de Arduino (solo en las pruebas)

Los archivos `tests/Simulador.Arduino.Tests/Fixtures/sketches/*.hex` son programas de ejemplo compilados con el núcleo
«arduino:avr» (LGPL 2.1), que se enlaza dentro de cada programa. Sus fuentes están en
https://github.com/arduino/ArduinoCore-avr. La aplicación no incluye el núcleo: usa el que tenga instalado el Arduino IDE.
