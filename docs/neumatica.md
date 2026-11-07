# Módulo Neumática

Pestaña «Neumática» (💨). Sirve para dibujar circuitos de aire comprimido con símbolos ISO 1219 y simularlos: los
cilindros sacan y meten el vástago, las mangueras con presión se ven en azul y las válvulas cambian de casilla.
El código está en `src/Simulador.Hidraulica/Neumatica/`, dentro del mismo proyecto que Hidráulica, para aprovechar la
vista 3D y la gráfica. Los circuitos se guardan en archivos `.neux` (JSON).

## Componentes

| Grupo | Componentes |
|---|---|
| Producción de aire | Fuente de aire de presión fija, compresor con presostato, depósito de aire, manómetro |
| Distribuidoras | Válvulas 3/2 (normalmente cerrada o abierta) y 5/2. Accionamientos: pulsador, palanca con enclavamiento, bobina, doble bobina, rodillo (final de carrera movido por la leva de un cilindro), pilotaje y pilotaje doble |
| Otras válvulas | Regulador de caudal, con o sin antirretorno; antirretorno; selectora (O); simultaneidad (Y) |
| Actuadores | Cilindro de simple efecto (con muelle) y de doble efecto. Se configuran émbolo, vástago, carrera, masa, carga, rozamiento y detectores magnéticos |

Una toma sin manguera escapa a la atmósfera. Si en una toma acaban varias mangueras, ahí hay una derivación.

## Uso

En diseño:
- Clic en el lienzo coloca el componente elegido.
- Con la herramienta «Manguera», clic en una toma, clic en cada codo (los tramos quedan rectos) y clic en la toma de destino.
- R gira el componente seleccionado y Supr lo borra.

En simulación:
- Para accionar un pulsador hay que mantener el clic sobre él. Ctrl+clic lo deja enclavado.
- Un clic cambia una palanca o una bobina simple.
- En una válvula de doble bobina, la mitad izquierda acciona la bobina 14 y la derecha la 12.
- La velocidad se puede bajar hasta ×0,1 para ver los movimientos rápidos.

## Cálculo

- Cada grupo de tomas unidas es un volumen de aire isotermo, con p·V = n·p₀. Su volumen suma el de las mangueras, el de
  los depósitos y el de las cámaras de los cilindros, que cambia con el émbolo.
- El caudal por válvulas y reguladores sigue la norma ISO 6358, con conductancia C y relación crítica b: el flujo es
  sónico por debajo de b y subsónico por encima.
- El paso de aire de cada intervalo se limita para que las presiones nunca se crucen, lo que hace el cálculo estable con
  pasos de 0,2 ms.
- El émbolo se mueve según la fuerza de las presiones (cámara A sobre el émbolo y cámara B sobre la corona), la carga,
  el muelle y el rozamiento seco y viscoso. Se para en los topes.
- El compresor da menos caudal cuanto más presión hay y su presostato lo arranca y lo para.
- Al empezar se llenan las mangueras con los cilindros quietos, para que no haya un golpe de arranque.

## Ejemplos

1. Cilindro de simple efecto con pulsador.
2. Doble efecto con reguladores de caudal, con regulación a la salida.
3. Mando bimanual con función Y.
4. Vaivén automático con finales de carrera de rodillo.
5. Electroneumática para PLC: doble bobina Y1/Y2 y detectores 1B1/1B2.
6. Compresor, depósito y consumo.

## Banco de pruebas combinado

`PneumaticCircuitComponent` mete un circuito entero en el banco de pruebas:
- Cada bobina es una entrada digital (DI) llamada `Equipo.Y1`.
- Cada detector es una salida digital (DO) PNP llamada `Equipo.1B1`.
- Cada manómetro publica su medida en `Equipo.PI1.Medida`, como salida analógica (AO) en pascales.
- `Snapshot()` publica la posición y la velocidad de cada vástago, y si cada válvula está accionada.
