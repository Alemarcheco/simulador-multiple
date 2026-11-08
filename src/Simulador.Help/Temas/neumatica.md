<!-- modulo: neumatica -->
<!-- orden: 41 -->
# Neumática

Dibuja circuitos de aire comprimido con símbolos ISO 1219 y simúlalos: los cilindros sacan y meten el vástago, las mangueras con presión se ven en azul y las válvulas cambian de casilla.

> Para empezar: **Archivo › Ejemplos** › «Cilindro de simple efecto con pulsador», pulsa `▶ Simular` y mantén el clic sobre el pulsador.

## Dibujar el circuito
<!-- contexto: Esquema -->
<!-- palabras: dibujar, manguera, colocar, girar, valvula, cilindro, toma, conectar -->
- Clic en el lienzo coloca el componente elegido en la paleta.
- Con la herramienta `Manguera`: clic en una toma, clic en cada codo y clic en la toma de destino. Los tramos quedan rectos.
- `R` gira el componente seleccionado y `Supr` lo borra.

Una toma sin manguera escapa a la atmósfera. Si en una toma acaban varias mangueras, ahí hay una derivación.

## Componentes
<!-- palabras: compresor, deposito, manometro, 3/2, 5/2, pulsador, palanca, bobina, rodillo, final de carrera, regulador de caudal, antirretorno, selectora, simultaneidad, simple efecto, doble efecto -->
- **Aire**: fuente de presión fija, compresor con presostato, depósito, manómetro.
- **Distribuidoras**: 3/2 (normalmente cerrada o abierta) y 5/2, con pulsador, palanca, bobina, doble bobina, rodillo, pilotaje o pilotaje doble.
- **Otras válvulas**: regulador de caudal (con o sin antirretorno), antirretorno, selectora (función O) y de simultaneidad (función Y).
- **Cilindros**: de simple efecto con muelle y de doble efecto. Se configuran émbolo, vástago, carrera, masa, carga, rozamiento y detectores magnéticos.

## Simular y accionar
<!-- palabras: simular, accionar, pulsar, enclavar, velocidad, lento, camara lenta -->
- Para accionar un pulsador, **mantén el clic** sobre él. `Ctrl+clic` lo deja enclavado.
- Un clic cambia una palanca o una bobina simple.
- En una válvula de doble bobina, la mitad izquierda acciona la bobina 14 y la derecha la 12.
- Baja la velocidad hasta ×0,1 para ver los movimientos rápidos.

La pestaña **Vista 3D** muestra los cilindros moviéndose.

## Ejemplos
<!-- palabras: ejemplos, bimanual, vaiven, electroneumatica -->
Cilindro de simple efecto con pulsador, doble efecto con reguladores de caudal, mando bimanual con función Y, vaivén automático con finales de carrera, electroneumática para PLC (bobinas Y1/Y2 y detectores 1B1/1B2) y compresor con depósito y consumo.

Los circuitos se guardan como `.neux`.

## ¿Por qué el cilindro va muy rápido o muy lento?
<!-- palabras: velocidad cilindro, regulador, estrangular -->
La velocidad depende del caudal que pasa por las válvulas. Pon un **regulador de caudal con antirretorno** en la salida de aire del cilindro (regulación a la salida) y ajústalo. Mira el ejemplo 2.

## ¿Por qué el cilindro no se mueve?
<!-- palabras: no se mueve, no sale, no avanza -->
- Comprueba que hay presión: la manguera con aire se ve azul.
- Si la carga es mayor que la fuerza del émbolo (presión × área), no se mueve. Baja la carga o sube la presión.
- Una toma sin manguera escapa a la atmósfera: si te falta una conexión, el aire se va por ahí.
