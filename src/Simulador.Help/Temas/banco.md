<!-- modulo: banco -->
<!-- orden: 90 -->
# Banco de pruebas

Junta en una sola simulación piezas de varios módulos que trabajan a la vez con un reloj común: un PLC con su programa, el esquema eléctrico, motores, bombas, la red hidráulica, turbinas, generadores y compresores. Sirve para probar el proceso completo, por ejemplo cómo un PLC llena un tanque arrancando una bomba.

> Para empezar: elige un **Montaje** arriba y pulsa `▶ Marcha`.

## Montajes incluidos
<!-- palabras: montaje, escenario, llenado de tanque, turbogenerador, turbocompresor, ejemplo -->
- **Llenado de tanque**: PLC en texto estructurado → contactor con relé térmico → motor → bomba → red hidráulica real → tanque con transmisor de nivel.
- **Llenado de tanque (esquema eléctrico real)**: lo mismo, pero el mando y la potencia son un esquema del módulo Instalaciones eléctricas.
- **Turbogenerador**: turbina y generador síncrono en isla con regulador PI de velocidad y carga eléctrica.
- **Turbocompresor**: turbina que mueve un compresor con depósito de aire regulado por el PLC.

## Marcha, pausa y velocidad
<!-- palabras: marcha, pausa, paso, reiniciar, velocidad, rapido, lento, tiempo -->
- `▶ Marcha` y `⏸ Pausa` arrancan y paran el tiempo. `⏭ Paso 0,1 s` avanza un poco para ver qué pasa paso a paso.
- `⟲ Reiniciar` vuelve al estado inicial.
- **Velocidad**: ×0,1 (cámara lenta), ×1 (tiempo real), ×10, ×60 o Máx. (lo más rápido posible). Llenar un tanque lleva minutos; a ×60 se ve en segundos.

El reloj de la barra muestra el tiempo simulado.

## Mandos, fallos y gráficas
<!-- palabras: mando, consigna, fallo, averia, disparo, grafica, tendencia, historico, curva -->
Debajo de la barra están los **mandos** del montaje: pulsadores, consignas y fallos que puedes provocar (por ejemplo disparar el térmico o cambiar la carga). Los cambios se aplican al momento sin parar la simulación.

Abajo, la **gráfica** dibuja la historia de las magnitudes principales (nivel, velocidad, presión, potencia…) para ver cómo responde el proceso.

## Valores, conexiones y programa
<!-- contexto: Valores, Conexiones, Programa PLC -->
<!-- palabras: valores, conexiones, cableado, bornes, programa plc, variables -->
El panel de la derecha tiene tres pestañas:
- **Valores**: todas las magnitudes de cada equipo en vivo.
- **Conexiones**: qué está unido con qué (bornes, cables, ejes y tuberías).
- **Programa PLC**: el programa que ejecuta el PLC del montaje.

## ¿Por qué el tanque no se llena?
<!-- palabras: no se llena, no arranca bomba, tanque vacio -->
Mira en **Valores** si la salida del PLC que manda el contactor está activa y si el motor gira. Si el térmico ha disparado (por ejemplo, porque provocaste un fallo), quita el fallo en los mandos o pulsa `⟲ Reiniciar`.

## ¿Puedo hacer mi propio montaje?
<!-- palabras: montaje propio, nuevo montaje, crear, personalizar -->
De momento el banco trae montajes hechos y se pueden cambiar sus mandos y consignas. Los demás módulos ya publican sus equipos (red eléctrica, red hidráulica, circuito neumático, PLC) para que en el futuro se puedan combinar libremente.
