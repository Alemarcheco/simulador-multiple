<!-- modulo: plc -->
<!-- orden: 30 -->
# PLC y microcontroladores

Programa y prueba autómatas (PLC) y placas como Arduino, ESP32 o Raspberry Pi Pico sin tener el equipo delante. Tiene dos sub-pestañas:

- **PLC**: programas en los cinco lenguajes de la norma IEC 61131-3 (texto estructurado, Ladder, bloques de función, lista de instrucciones y SFC/GRAFCET) sobre perfiles de varias marcas.
- **Arduino**: código C++ o MicroPython, o bloques tipo Scratch, con la placa dibujada y sus pines en vivo.

## Primer programa de PLC
<!-- contexto: PLC -->
<!-- palabras: empezar, cargar, run, stop, compilar, ciclo, scan, perfil, marca -->
1. Elige el **PLC** arriba (genérico IEC, Siemens S7-1200 y perfiles de otras marcas). Cada marca usa sus direcciones, por ejemplo `%IX0.0` en IEC o `I0.0` en Siemens.
2. Elige un lenguaje en las pestañas de abajo y un **Ejemplo**, o escribe el tuyo.
3. Pulsa `Compilar y cargar`. Si hay un error, la barra de estado dice la línea (o el segmento, en Ladder).
4. Pulsa `▶ RUN`. En **Panel de E/S** acciona las entradas: `Pulsar` mientras mantienes el clic, `Fijo` para dejarla puesta. Las salidas se encienden como pilotos.
5. La **Tabla de observación** muestra cada variable y su valor en vivo. `■ STOP` para parar.

El **Ciclo (ms)** es cada cuánto el PLC lee entradas, ejecuta el programa y escribe salidas.

## Ladder (LD)
<!-- contexto: Ladder (LD) -->
<!-- palabras: ladder, escalera, contacto, bobina, set, reset, flanco, segmento, temporizador, contador, ton, tof, ctu, ctd -->
El programa se dibuja en **segmentos**. Pulsa `+ Segmento`, selecciona un sitio y elige un elemento de la paleta:
- Contactos: `┤ ├` normalmente abierto, `┤/├` normalmente cerrado, `┤P├` y `┤N├` flanco de subida y de bajada.
- Bobinas: `( )` normal, `(/)` negada, `(S)` activar y `(R)` desactivar.
- Bloques: `TON`, `TOF` (temporizadores), `CTU`, `CTD` (contadores), `MOVE` y comparación `[CMP]`.
- `⑂ Paralelo` abre una rama en paralelo (función O). Los elementos en serie hacen la función Y.

**Archivo › Guardar programa…** crea un `.ladder.json`, que también se puede cargar en el PLC dibujado en **Instalaciones eléctricas**.

## Bloques de función (FBD)
<!-- contexto: Bloques de función (FBD) -->
<!-- palabras: fbd, bloques, and, or, puerta logica, red, negar -->
Funciona por **redes**: pulsa `+ Red`, selecciónala y elige bloques de la paleta (Y, O, temporizadores, contadores…). `+ Entrada` y `+ Salida` añaden patas, `○ Negar` invierte una, y `↑ Subir` / `↓ Bajar` ordenan las redes.

## Texto estructurado e IL
<!-- contexto: Texto estructurado (ST), Lista de instrucciones (IL) -->
<!-- palabras: st, scl, texto estructurado, il, lista de instrucciones, awl, if, then, variables -->
En **Texto estructurado (ST)** escribes con `IF … THEN … END_IF`, asignaciones `:=`, temporizadores y contadores como en cualquier PLC. Declara variables con su dirección, por ejemplo `Marcha AT %IX0.0 : BOOL;`.

**Lista de instrucciones (IL)** es el lenguaje tipo ensamblador (`LD`, `AND`, `ST`…).

## SFC / GRAFCET
<!-- contexto: SFC / GRAFCET -->
<!-- palabras: grafcet, sfc, etapas, transiciones, secuencia -->
Describe procesos por etapas: cada etapa activa unas acciones y se pasa a la siguiente cuando se cumple su transición. Las etapas y transiciones se escriben como texto a la izquierda y el GRAFCET se dibuja solo a su lado, con la etapa activa resaltada mientras está en RUN. Parte de un **Ejemplo** para ver la forma de escribirlo.

## Arduino, ESP32 y Pico
<!-- contexto: Arduino, Código -->
<!-- palabras: arduino, uno, nano, mega, esp32, raspberry pi pico, micropython, c++, sketch, ino, placa, pines, monitor serie -->
1. Elige la **placa**: Arduino Uno, Nano o Mega (C++), ESP32 o Raspberry Pi Pico (MicroPython).
2. Elige un **Ejemplo** o escribe tu código. `✔ Verificar` comprueba que está bien escrito.
3. `▶ Ejecutar` lo pone en marcha: la línea que se ejecuta se marca, los pines de la placa cambian de color y el **Monitor serie** muestra lo que imprime el programa. Abajo puedes escribir texto y `Enviar` a la placa.
4. `■ Detener` lo para.

El **motor** puede ser *Intérprete* (arranca al instante y marca la línea en ejecución) o *Chip real (ATmega328P)*, que ejecuta el programa compilado como en la placa de verdad. `Abrir…` admite `.ino`, `.cpp`, `.py` y programas ya compilados `.hex`.

## Piezas: LED, pulsador, potenciómetro, servo…
<!-- palabras: arduino, esp32, pico, pieza, montaje, led, pulsador, potenciometro, servo, zumbador, ldr, lm35, sensor, componente -->
En **Montaje**, `+ Añadir pieza` conecta piezas a los pines: LED, pulsador, potenciómetro, servo, zumbador, sensor de luz (LDR), sensor de temperatura LM35 y entradas analógicas de 0-10 V. Mientras se ejecuta, pulsa los pulsadores o mueve los potenciómetros y mira cómo responde el programa. `✕` quita una pieza.

## Programar con bloques
<!-- contexto: Bloques -->
<!-- palabras: arduino, esp32, pico, bloques, scratch, blockly, principiante, sin codigo -->
En la pestaña **Bloques** encajas piezas tipo Scratch en vez de escribir código. Sirven para cualquier placa: generan el código C++ o MicroPython, que puedes ver en la pestaña **Código** para aprender.

## Wi-Fi del ESP32
<!-- palabras: wifi, esp32, servidor web, navegador, http, api, internet -->
Con el ESP32, el panel **Wi-Fi y navegador** simula una red. El programa puede conectarse y pedir datos a un servidor de pruebas, o montar su propio servidor web y verlo en el navegador del simulador escribiendo la IP del ESP32. No sale a internet de verdad.

## ¿Por qué no cambia la salida del PLC?
<!-- palabras: salida no cambia, no funciona, no enciende, stop, fallo -->
- ¿Pulsaste `Compilar y cargar` después del último cambio? Los cambios no se aplican hasta cargar.
- ¿Está en `▶ RUN`? La barra de estado dice RUN, STOP o FALLO.
- ¿Las direcciones son las del perfil elegido? `%IX0.0` es IEC; en Siemens es `I0.0`.
- En Ladder, una bobina escrita en dos segmentos: gana la última.

## ¿Cómo uso mi programa en un esquema eléctrico?
<!-- palabras: plc en esquema, conectar plc, kf1, instalaciones electricas -->
Guarda el Ladder como `.ladder.json`. En **Instalaciones eléctricas** coloca el **PLC compacto**, y en sus Propiedades carga ese archivo. Ver [PLC dentro del esquema](#electrico/plc-dentro-del-esquema).

## ¿Qué Arduino puedo simular?
<!-- palabras: placas, modelos, compatible -->
Arduino Uno, Nano y Mega en C/C++, y ESP32 y Raspberry Pi Pico en MicroPython. El intérprete admite lo más usado de Arduino (`digitalWrite`, `analogRead`, `delay`, `Serial`, `Servo`…). Si usas algo que no admite, prueba el motor *Chip real*.
