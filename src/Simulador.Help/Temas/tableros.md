<!-- modulo: tableros -->
<!-- orden: 22 -->
# Tableros eléctricos

Diseña el **tablero por dentro y por fuera**: la envolvente, la placa de montaje con sus carriles DIN y canaletas, dónde va cada aparato, los mandos de la puerta, el recorrido y el largo de cada hilo, las regletas de bornes y la lista de materiales. Todo sale del esquema de **Instalaciones eléctricas**, así que no se dibuja dos veces.

> Para empezar: **Archivo › Ejemplos** › «Arranque estrella-triángulo». Mira la placa, la puerta y la **Vista 3D**, y después las pestañas **Cableado** y **Materiales**.

Las medidas son las habituales del mercado (módulos de 18 mm, contactores de 45 mm, pulsadores de Ø22 mm…) sin marca: escribe la referencia de tu fabricante en cada aparato.

## Crear el tablero desde un esquema
<!-- palabras: importar, esquema, selx, instalaciones electricas, generar, desde esquema, enlazar -->
- **Archivo › Nuevo desde el esquema de Instalaciones eléctricas** usa el esquema que tengas abierto en esa pestaña.
- **Archivo › Nuevo desde un esquema .selx…** lo lee de un archivo.

El programa junta en **un aparato** todo lo que tiene la misma designación (la bobina, los polos y los contactos de `KM1` son un solo contactor), elige su modelo y medidas, decide si va en la **placa**, en la **puerta** (pulsadores, setas, selectores, pilotos, instrumentos) o **fuera** (motores, lámparas, captadores, la acometida) y crea las **regletas**: `X0` para la alimentación y `X1` para lo que sale al campo. Después elige la envolvente normalizada más pequeña en la que cabe todo y lo coloca en filas.

Si cambias el esquema, pulsa `🔄 Actualizar desde el esquema`: añade lo nuevo en el primer hueco libre, quita lo que ya no está y deja en su sitio lo que ya habías colocado.

## Placa de montaje
<!-- contexto: Placa de montaje -->
<!-- palabras: placa, mover, arrastrar, carril, din, canaleta, iman, colocar, aparato, zoom -->
- **Mover**: arrastra un aparato. Se centra solo en el carril más cercano y se pega a los aparatos de al lado. Con las flechas se mueve 1 mm (con `Mayús`, 10 mm).
- **Añadir**: `🧩 Añadir aparato` (lo pone en el primer hueco libre), `➖ Carril DIN`, `▭ Canaleta H` y `▯ Canaleta V`.
- **Borrar**: `🗑 Borrar` o `Supr`. **Deshacer**: `Ctrl+Z`.
- **Vista**: rueda para acercar, botón derecho o central para desplazar, `⤢ Ajustar` para encuadrar.
- `〰 Hilos` dibuja el recorrido de cada conductor por las canaletas, con su color. Al seleccionar un aparato se resaltan sus hilos.
- `⚙ Distribuir` vuelve a elegir la envolvente y a colocarlo todo.

Los aparatos con algún problema (solapados, fuera de la placa, tapando una canaleta…) se marcan con un recuadro rojo discontinuo.

## Puerta
<!-- contexto: Puerta -->
<!-- palabras: puerta, pulsador, seta, piloto, selector, instrumento, mando, frente, 22 mm -->
Los mandos y pilotos van en la puerta. Arrástralos igual que en la placa. En gris claro se ve lo que hay detrás en la placa: si un mando cae encima de un aparato muy profundo, **Comprobaciones** avisa de que la puerta no cerrará. Sus hilos pasan a la placa por la bisagra (izquierda) con un bucle de 30 cm para poder abrir.

Para pasar un aparato de la placa a la puerta (o al revés), cambia su **Ubicación** en Propiedades.

## Vista 3D
<!-- contexto: Vista 3D -->
<!-- palabras: 3d, girar, puerta abierta, envolvente, ver, maqueta -->
El tablero en 3D: envolvente, placa, carriles, canaletas, aparatos, mandos de la puerta y los hilos. Arrastra para girar, botón derecho para desplazar y rueda para acercar. Con el deslizador abres o cierras la puerta; las casillas ocultan la puerta, la envolvente o los hilos para ver mejor el interior.

## Cableado y bornes
<!-- contexto: Cableado -->
<!-- palabras: cableado, hilos, de-a, lista de cableado, numero de hilo, seccion, color, longitud, regleta, borne, cable de campo, manguera -->
- **Cableado**: cada hilo de borne a borne (`KM1:2 → F2:1`), con el **número de su potencial**, la sección, el color y el largo. Los hilos de un mismo potencial se encadenan del más alto al más cercano, para que ningún borne reciba más de dos.
- Colores: potencia negro, neutro azul claro, mando en alterna rojo, mando en continua azul oscuro, tierra verde-amarillo. La sección de potencia sale de la corriente de la protección; el mando, 1 mm² (0,75 mm² en continua).
- El largo va por las canaletas por el camino más corto, más 60 mm por punta.
- **Bornes y cables**: el plano de cada regleta (qué hilo llega por dentro y qué cable sale por fuera) y la lista de **cables de campo** (por ejemplo `4G2,5` para un motor).

Cada lista tiene `📋 Copiar para Excel` y `💾 Guardar CSV…`. En **Archivo › Exportar listas (CSV)** están todas.

## Lista de materiales
<!-- contexto: Materiales -->
<!-- palabras: materiales, bom, presupuesto, referencias, cantidad, pedido, compra -->
Agrupa los aparatos iguales en una línea con sus designaciones, y añade lo que se olvida: placa de montaje, metros de carril y de canaleta, bornes por sección, tapas y topes finales, metros de conductor por sección y color (+10 % de recortes), punteras, marcadores, mangueras y prensaestopas. Escribe la **referencia del fabricante** en Propiedades de cada aparato y aparecerá en su columna.

## Comprobaciones y temperatura
<!-- contexto: Comprobaciones -->
<!-- palabras: comprobaciones, errores, avisos, temperatura, calor, ventilador, ventilacion, reserva, llenado, solape, fondo -->
Se repasan solas con cada cambio:
- Aparatos solapados, fuera de la placa, sin carril o tapando canaletas.
- Fondo: aparatos que no caben con la puerta cerrada, o mandos de la puerta que chocan con lo de detrás.
- **Llenado de canaletas**: aviso por encima del 50 % y error por encima del 75 %.
- **Reserva de carril**: se recomienda dejar un 20 % libre para ampliaciones.
- Bornes con más de dos hilos y aparatos que faltan o sobran respecto al esquema.
- **Temperatura interior**: suma el calor de los aparatos y lo compara con lo que disipa la envolvente (superficie eficaz como en IEC 60890). Si se pasa, dice qué caudal de ventilador hace falta; al distribuir se elige uno normalizado.

Haz clic en un aviso con designación para ir al aparato.

## Envolvente
<!-- palabras: envolvente, armario, caja, medida, tamaño, ip, fondo, poliester, pie -->
Sin nada seleccionado, Propiedades muestra la envolvente: tipo (mural, de pie, poliéster), medida normalizada o a medida, grado IP, temperatura ambiente, máxima interior y ventilador. `Redistribuir en esta medida` recoloca los aparatos sin cambiar la caja.

## ¿Por qué un aparato sale fuera de la placa, a la derecha?
<!-- palabras: fuera, derecha, no cabe, sin colocar -->
Al actualizar desde el esquema, lo nuevo se pone en el primer hueco libre del carril. Si no hay hueco, se deja a la derecha para que lo coloques tú: añade un carril, mueve otros aparatos o pulsa `⚙ Distribuir`.

## ¿Por qué no salen hilos de un aparato?
<!-- palabras: sin hilos, no cablea, manual, añadido a mano -->
Los hilos salen del esquema. Un aparato añadido a mano en el tablero no está en el esquema, así que no tiene conexiones: añádelo en **Instalaciones eléctricas** con la misma designación y pulsa `🔄 Actualizar desde el esquema`.

## ¿Es el catálogo de algún fabricante?
<!-- palabras: fabricante, marca, schneider, siemens, abb, catalogo, propanel -->
No. Son modelos genéricos con medidas típicas para que el diseño sea realista. Escribe en cada aparato la referencia de la marca que uses; las medidas se pueden ajustar en Propiedades.
