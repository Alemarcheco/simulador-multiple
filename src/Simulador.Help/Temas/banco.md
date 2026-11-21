<!-- modulo: banco -->
<!-- orden: 90 -->
# Banco de pruebas

Junta en una sola simulación piezas de varios módulos que trabajan a la vez con un reloj común: PLC y Arduino con su programa, pulsadores y pilotos, contactores, motores, generadores, turbinas, compresores, bombas, tuberías y tanques. Sirve para probar el proceso completo, por ejemplo cómo un PLC llena un tanque arrancando una bomba.

Tiene dos partes:
- **Mesa libre**: armas tu propio montaje con piezas de todos los módulos, lo cableas, lo simulas y lo mides con instrumentos.
- **Montajes animados**: montajes ya preparados con un dibujo animado de la planta y gráficas de tendencia.

> Para empezar: en **Mesa libre** pulsa `📚 Ejemplos ▾`, elige uno y pulsa `▶ Simular`.

## Mesa libre: arma tu propio montaje
<!-- contexto: Mesa libre -->
<!-- palabras: mesa, montaje propio, crear, armar, piezas, catalogo, banco libre, nuevo montaje -->
A la izquierda está la lista de **piezas**, agrupada: Mando y señales, PLC y Arduino, Eléctrico, Máquinas y aire, Hidráulica e Instrumentos. En el centro está la **mesa** y a la derecha la **ficha** de lo que tengas seleccionado.

1. Pulsa una pieza de la lista y haz clic en la mesa para ponerla. Con `Mayús` pulsada puedes poner varias seguidas; `Esc` cancela.
2. Arrastra una pieza para moverla. Arrastra el fondo (o con el botón derecho) para desplazarte y usa la rueda del ratón para acercar o alejar. `⤢ Ver todo` encaja todo el montaje.
3. En la ficha cambias el nombre de la pieza (KM1, M1…), sus datos (potencia, ajuste del térmico, diámetro de la tubería…) y, en el PLC o el Arduino, su programa.
4. `🗑 Quitar` o la tecla `Supr` quitan la pieza o la conexión seleccionada.

En edición, cada pieza enseña sus datos principales; en marcha, sus valores en vivo (velocidad, corriente, nivel…).

La mesa se ve de dos formas, en dos pestañas sobre el mismo montaje: **🧊 Vista 3D** (los equipos sobre la mesa) y **🔗 Diagrama de bloques** (cómo se conecta cada pieza por dentro). Lo que cambias en una se ve en la otra.

## Vista 3D: los equipos sobre la mesa
<!-- contexto: Vista 3D -->
<!-- palabras: 3d, tres dimensiones, vista 3d, equipos, maqueta, girar, orbitar, mesa 3d, ver en 3d -->
Cada pieza aparece como un equipo sobre su placa: cajas de pulsadores y pilotos, el PLC con sus LED de entradas y salidas, la placa Arduino con sus relés, el contactor, el motor con aletas, la turbina, el tanque transparente con el agua dentro, el multímetro, el osciloscopio… Los **bornes** son los tacos de color del borde de cada placa (los mismos colores que en el diagrama) y los **cables** salen de ellos y se apoyan en la mesa.

- **Girar**: arrastra el fondo con el botón izquierdo. **Desplazar**: botón derecho. **Acercar**: rueda del ratón. **Doble clic** en el fondo o `⤢ Ver todo` encuadra la mesa.
- **Elegir** una pieza: haz clic en ella; su placa se pone azul y su ficha sale a la derecha.
- **Mover**: arrastra la pieza por la mesa.
- **Unir**: arrastra desde un borne hasta otro, igual que en el diagrama. Pasa el ratón por un borne para ver qué es.
- **Poner** una pieza nueva: elígela en la lista y haz clic en la mesa.

En marcha, todo se mueve con la simulación: los ejes de motores, turbinas, bombas y compresores giran a su velocidad, el agua del tanque sube y baja, los pilotos y los LED se encienden, y por los cables que llevan algo corren unas cuentas de luz (azules en las tuberías). Haz clic sobre un pulsador o un selector para accionarlo. Una placa roja o amarilla avisa de un error o de un aviso en esa pieza.

## Diagrama de bloques: cómo se conecta cada pieza
<!-- contexto: Diagrama de bloques -->
<!-- palabras: diagrama de bloques, bloques, esquema, conexiones, detras, como se conecta, cableado, 2d -->
Es lo que hay detrás de la vista 3D: cada pieza es un **bloque** con su nombre, sus bornes a los lados y sus datos o valores en vivo abajo, y cada unión es una línea del color de lo que lleva. Aquí se ve de un vistazo qué está unido con qué, y es la forma más cómoda de cablear montajes grandes. En marcha, los cables que llevan señal o energía se iluminan y muestran el sentido.

## Unir piezas: los bornes y sus colores
<!-- palabras: cablear, cable, conectar, unir, borne, tuberia, eje, colores, arrastrar -->
Para unir dos piezas, **arrastra desde un borne hasta otro**. El color del borne dice qué es:
- **Verde**: señal digital (pulsadores, contactos, entradas y salidas del PLC, bobinas).
- **Naranja**: señal analógica (0-10 V, %, nivel, presión…).
- **Rojo** (cuadrado): potencia eléctrica trifásica.
- **Gris** (cuadrado): eje mecánico.
- **Turquesa**: aire comprimido.
- **Azul**: agua (tuberías, bombas, tanques) y tomas de los transmisores.
- **Morado**: puntas de medida de los instrumentos.

Un borne relleno está conectado; uno hueco está libre. Pasa el ratón por encima de un borne para ver qué es y con qué está unido.

## Qué se puede unir con qué
<!-- palabras: no deja unir, regla, salida, entrada, causalidad, fuente, embarrado, reparto -->
La mesa no deja hacer uniones que no tendrían sentido y explica por qué en la barra de estado:
- Una **salida** de señal va a una **entrada**. Dos salidas juntas se pelearían; dos entradas juntas no reciben nada.
- Una entrada tiene **una sola fuente**. Para combinar señales usa un PLC o un Arduino.
- Un cable de potencia une algo que **da tensión** (red, generador, contactor, embarrado) con algo que **consume** (motor, carga, contactor). Para alimentar varios equipos desde la misma red pon un **Embarrado (reparto)**.
- Un **eje** lo mueve una sola máquina (motor o turbina) y lo frena una carga (bomba, compresor, generador).
- Las tuberías unen bocas de agua entre sí. Cada red de tuberías necesita al menos un **depósito** o un **tanque**.
- Los **transmisores hidráulicos** se ponen con su `Toma`: el de nivel en un tanque, el de presión en un tanque, depósito o unión, y el caudalímetro en una tubería, bomba o válvula.
- Las **puntas de medida** se pueden poner en cualquier borne, aunque ya esté ocupado.

## Simular y accionar
<!-- palabras: simular, marcha, pausa, paso, reiniciar, detener, velocidad, pulsar, pulsador, mandos -->
- `▶ Simular` comprueba el montaje y arranca. Si hay errores (⛔), no arranca y te los enseña en **Avisos**.
- `⏸ Pausa` congela el tiempo, `⏭ Paso` avanza 0,1 s, `⟲ Reiniciar` vuelve al principio y `⏹ Detener` vuelve a la edición.
- **Velocidad**: de ×0,1 (cámara lenta) a Máx. Llenar un tanque lleva minutos; a ×60 se ve en segundos.
- **Pulsadores y selectores**: haz clic sobre ellos en la propia mesa (mantén pulsado un pulsador).
- Los demás **mandos** salen en la ficha de la pieza seleccionada: consignas, carga eléctrica, consumo de aire, corte de red, rearme del térmico, RUN/STOP del PLC…
- Los cables se iluminan cuando llevan algo: señal a 1, corriente, giro o caudal.

Mientras simulas no se pueden poner ni quitar piezas: pulsa `⏹ Detener` para cambiar el montaje.

## Avisos: qué pasa y cómo arreglarlo
<!-- contexto: Avisos -->
<!-- palabras: avisos, errores, error, advertencia, problema, diagnostico, termico disparado, explicacion -->
La pestaña **Avisos** explica en palabras sencillas lo que está mal y qué hacer:
- **Antes de simular** revisa el montaje: piezas sin alimentar, bobinas sin mando, tuberías al aire, redes sin depósito, programas con errores, tensiones que no casan…
- **Mientras simulas** vigila lo que pasa: térmico disparado, motor que no arranca o va sobrecargado, turbina disparada por sobrevelocidad, tanque rebosando o vacío, bomba a válvula cerrada, agua demasiado rápida en una tubería, frecuencia fuera de lo normal…

⛔ es un error, ⚠ un aviso e ℹ una nota. La pieza afectada se marca en rojo o ámbar en la mesa; haz clic en el aviso para seleccionarla. Debajo de **Ahora mismo** queda **Lo que ha pasado**, con el instante de cada aviso.

## Instrumentos: multímetro, osciloscopio y línea de tiempo
<!-- contexto: Instrumentos -->
<!-- palabras: instrumento, multimetro, polimetro, osciloscopio, analizador logico, linea de tiempo, medir, medida, sonda, punta -->
Pon un instrumento (grupo **Instrumentos**) y une sus puntas a los bornes que quieras medir. Sus pantallas salen en la pestaña **Instrumentos** al simular.
- **Multímetro**: en un cable mide corriente, tensión y potencia; en un eje, velocidad, par y potencia; en una tubería, caudal o presión; en una señal, su valor.
- **Osciloscopio**: 4 canales con escala automática. En su ficha eliges el ancho de pantalla (segundos) y cada cuánto toma una muestra. Ideal para ver el pico de corriente al arrancar un motor.
- **Línea de tiempo de señales**: 8 canales digitales; enseña cuándo se encienden y se apagan pulsadores, salidas del PLC y contactores.

## Guardar y abrir montajes
<!-- palabras: guardar, abrir, archivo, simx, nuevo, ejemplo, ejemplos -->
`Guardar` y `Abrir…` (también en el menú **Archivo**) usan archivos `.simx`. `Nuevo` deja la mesa vacía y `📚 Ejemplos ▾` abre montajes preparados:
- Marcha y paro de un motor con PLC (principiante).
- Marcha y paro con Arduino y placa de relés (principiante).
- Llenado de tanque: PLC, motor y bomba (intermedio).
- Turbogenerador en isla con regulador PI (intermedio).

## Montajes animados
<!-- contexto: Montajes animados -->
<!-- palabras: montaje animado, escenario, llenado de tanque, turbogenerador, turbocompresor, planta, animacion -->
Montajes ya preparados con un dibujo animado de la planta. Elige uno arriba y pulsa `▶ Marcha`:
- **Llenado de tanque**: PLC en texto estructurado → contactor con relé térmico → motor → bomba → red hidráulica real → tanque con transmisor de nivel.
- **Llenado de tanque (esquema eléctrico real)**: lo mismo, pero el mando y la potencia son un esquema del módulo Instalaciones eléctricas.
- **Turbogenerador**: turbina y generador síncrono en isla con regulador PI de velocidad y carga eléctrica.
- **Turbocompresor**: turbina que mueve un compresor con depósito de aire regulado por el PLC.

Debajo de la barra están los **mandos** del montaje (consignas y fallos que puedes provocar) y abajo la **gráfica** con la historia de las magnitudes principales. `⏭ Paso 0,1 s` avanza poco a poco.

## Valores, conexiones y programa
<!-- contexto: Valores, Conexiones, Programa PLC -->
<!-- palabras: valores, conexiones, cableado, bornes, programa plc, variables -->
En **Montajes animados**, el panel de la derecha tiene tres pestañas:
- **Valores**: todas las magnitudes de cada equipo en vivo.
- **Conexiones**: qué está unido con qué (bornes, cables, ejes y tuberías).
- **Programa PLC**: el programa que ejecuta el PLC del montaje.

## ¿Por qué no me deja unir dos bornes?
<!-- palabras: no deja unir, no conecta, no se puede unir -->
Lee el mensaje de la barra de estado: dice qué tipo es cada borne y por qué no casan (dos salidas, dos fuentes de tensión, un eje con un cable, una entrada que ya tiene fuente…). Mira [Qué se puede unir con qué](#banco/que-se-puede-unir-con-que).

## ¿Por qué no arranca la simulación?
<!-- palabras: no simula, no arranca, error, no puedo simular -->
Hay algún error (⛔) en el montaje. Abre **Avisos**: cada error dice en qué pieza está y cómo arreglarlo. Las notas (ℹ) y avisos (⚠) no impiden simular.

## ¿Cómo alimento varios motores de la misma red?
<!-- palabras: varios motores, varias cargas, repartir, embarrado, barras -->
Pon un **Embarrado (reparto)**: une la red (o el contactor general) a su `Entrada` y cada motor o carga a una de sus salidas `S1`…`S4`. El embarrado suma las corrientes y el multímetro en su entrada te da el total.

## ¿Por qué el tanque no se llena?
<!-- palabras: no se llena, no arranca bomba, tanque vacio -->
Mira si la salida del PLC que manda el contactor está activa (en la ficha del PLC o con la línea de tiempo) y si el motor gira. Si el térmico ha disparado, **Avisos** te lo explica: corrige el ajuste y pulsa `Rearmar térmico` en la ficha del contactor, o `⟲ Reiniciar`.
