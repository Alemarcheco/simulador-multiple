<!-- modulo: hidraulica -->
<!-- orden: 40 -->
# Hidráulica

Diseña procesos con depósitos, tanques, tuberías, bombas, válvulas e instrumentos y simúlalos en el tiempo: caudales, presiones y niveles. También calcula aspersores, calderines (grupos de presión) y el golpe de ariete, y muestra la instalación en 3D.

> Para empezar: `Ejemplos ▾` › «Bombeo a tanque elevado» y pulsa `▶ Simular`.

## Dibujar la red
<!-- contexto: Esquema -->
<!-- palabras: dibujar, tuberia, bomba, valvula, tanque, deposito, union, nudo, paleta, propiedades -->
1. Elige una herramienta en la paleta de la izquierda y haz clic en el lienzo.
2. Tuberías, bombas y válvulas se trazan con dos clics: origen y destino. Ese sentido es el caudal positivo. Un clic en vacío crea una unión.
3. Clic derecho o `Esc` vuelve a seleccionar.
4. Cambia las propiedades a la derecha (mm, L/s, %, rpm). Al crear una tubería, cada cuadro del lienzo es 1 m de longitud.

## Simular
<!-- palabras: simular, caudal, presion, nivel, arrancar bomba, variador, velocidad, abrir valvula -->
Pulsa `▶ Simular`. Durante la simulación puedes arrancar o parar bombas, cambiar su velocidad (como con un variador) y abrir o cerrar válvulas, y ver al momento cómo cambian caudales, presiones y niveles.

La red se calcula con el método del gradiente; las tuberías con Darcy-Weisbach, las bombas con su curva y las leyes de semejanza.

## Vista 3D
<!-- contexto: Vista 3D -->
<!-- palabras: 3d, tres dimensiones, girar, animacion -->
La sub-pestaña **Vista 3D** muestra la instalación con sus cotas reales y se anima mientras simula: nivel de los tanques, bandas que avanzan con el caudal, ventiladores de las bombas y color de las válvulas según su apertura.

Ratón: botón izquierdo gira, derecho desplaza, rueda acerca, doble clic encuadra.

## Aspersores, calderín y presostato
<!-- palabras: aspersor, riego, descarga libre, calderin, tanque presurizado, grupo de presion, presostato, hidroneumatico -->
- **Aspersor**: un nudo con «Descarga a 1 bar» mayor que cero echa agua según la presión (a 4 bar da el doble que a 1 bar).
- **Calderín**: tanque cerrado con aire arriba. El aire se comprime al llenarse y empuja el agua a la red.
- **Presostato**: una bomba con presostato arranca por debajo de la presión de arranque y para al llegar a la de paro.

Ejemplos: «Grupo de presión con calderín» y «Riego por aspersión».

## Golpe de ariete
<!-- palabras: golpe de ariete, sobrepresion, cierre valvula, joukowsky, onda -->
Selecciona una válvula y pulsa `Analizar golpe de ariete…`. Elige el tiempo de cierre y verás la presión máxima y mínima, la velocidad de la onda, el tiempo crítico 2L/a y la sobrepresión de Joukowsky. Avisa si la presión baja tanto que se rompe la columna de agua.

Sirve para conducciones simples: depósito o tanque, tuberías en serie y la válvula.

## Guardar e informe
<!-- palabras: guardar, hidx, informe, lista de materiales, exportar -->
`Guardar…` crea un `.hidx`. `Informe…` guarda un informe con la lista de materiales y los resultados de la simulación.

## ¿Por qué no sube el agua al tanque?
<!-- palabras: no sube, no llega, caudal cero, bomba no bombea -->
- La bomba tiene que estar en marcha y su altura (H0) debe superar el desnivel más las pérdidas.
- Revisa el sentido: la bomba se dibuja de la aspiración a la impulsión.
- Un depósito vacío no entrega agua.
- Si la bomba tiene presostato, ignora la orden de marcha y obedece a la presión.

## ¿Cómo conecto la red con un PLC?
<!-- palabras: plc, electrovalvula, banco de pruebas, control de nivel -->
Abre el ejemplo «Control de nivel con electroválvula». Para manejarlo con un PLC real y un motor eléctrico, usa el **Banco de pruebas**, que ya trae un montaje de llenado de tanque. Ver [Banco de pruebas](#banco).
