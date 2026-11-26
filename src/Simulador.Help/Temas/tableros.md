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

## Importar un plano en PDF
<!-- palabras: pdf, plano, importar pdf, eplan, solidworks electrical, autocad electrical, leer plano, otro programa, cad, ocr, shx, letras dibujadas, escaneado -->
**Archivo › Importar plano PDF…** lee un plano eléctrico hecho con otro programa (EPLAN, SolidWorks Electrical, AutoCAD Electrical, See Electrical…) y monta el tablero solo:

1. Busca las **designaciones** de los aparatos (`-Q1`, `-KM1`, `=A1+B2-K3`…) en todas las páginas.
2. En la **lista de materiales** lee la descripción, la referencia y el calibre (`C16`, `25 A`, `9-14 A`, `3P`, `5,5 kW`), y con eso decide qué es cada aparato y su ancho.
3. En las **listas de cables** y los **planos de bornes** lee qué borne va con cuál (`-KM1:2 → -X1:1`, o en columnas `-KM1 | 2 | -X1 | 1`), con el número de hilo, la sección y el color. En los esquemas lee las **etiquetas de los hilos** con origen y destino, como las de SolidWorks Electrical: `CB1(3) / CB120(4)` une el borne 3 de CB1 con el 4 de CB120, y `TB-120L-1(1)` es el borne 1 de la regleta TB-120L.
   También entiende la nomenclatura americana (CB, TB, PS, CR, PB, PL…), los instrumentos ISA (PIT-3202 transmisor, PSH-81A presostato, FCV-3201 válvula: van al campo) y los módulos de entradas y salidas de los controladores (C1-DI, DO-C1, C2R1).
4. Elige la envolvente, coloca los aparatos en carriles con sus canaletas, pone los mandos en la puerta, tiende los hilos, saca los cables de campo y calcula si hace falta ventilador.

El informe de lo que encontró y lo que supuso sale arriba en **Comprobaciones** (líneas «Plano PDF»). Después puedes mover, cambiar medidas o referencias como en cualquier tablero y guardarlo como `.tablero`.

Ten en cuenta:
- **Letras dibujadas.** Muchos programas (SolidWorks Electrical, AutoCAD con fuentes SHX) exportan las letras como rayitas, no como texto. Esas páginas se pasan por un **reconocedor de texto (OCR)**: en Windows 10/11 el que trae Windows, sin instalar nada (si no, Tesseract si está instalado). Tarda unos segundos por página; la barra de estado dice por cuál va. El OCR puede confundir letras parecidas (B/8, O/0, I/1): las variantes de una misma designación se juntan solas, pero revisa la lista del informe.
- Solo se lee el **texto** del PDF, no los dibujos. Lo que está solo dibujado (una línea entre dos bornes sin lista de cables) no se puede leer: los aparatos salen colocados pero sin esos hilos. Si tu programa de CAD puede sacar la lista de cables o de bornes, inclúyela en el PDF.
- Un PDF **escaneado** (una foto del papel) también pasa por el OCR, con peor resultado cuanto peor sea la imagen.
- Los aparatos que no reconoce quedan como «Aparato genérico» y el informe los nombra para que los revises.

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
Sin nada seleccionado, Propiedades muestra la envolvente: tipo (mural, de pie, poliéster), medida normalizada o a medida, **doble frente**, grado IP, temperatura ambiente, máxima interior y ventilador. `Redistribuir en esta medida` recoloca los aparatos sin cambiar la caja.

## Armario de doble frente
<!-- palabras: doble frente, doble cara, delantera, trasera, detras, espalda, dos placas, dos puertas, back to back -->
Un armario de doble frente tiene **dos placas de montaje espalda con espalda**, una que se ve desde delante y otra desde detrás, **cada una con su puerta**. Sirve para tableros grandes que no caben en una sola cara sin hacerse muy anchos.

- **En un tablero ya hecho**: sin nada seleccionado, marca **Doble frente** en Propiedades (Envolvente). Se elige un armario de doble frente y los aparatos se reparten: la alimentación y las protecciones delante, y el mando, los autómatas y las regletas detrás.
- **Al importar un plano**: usa **Archivo › Importar plano PDF en armario de doble frente…**. Con **Importar plano PDF…** el programa elige solo: pasa a doble frente cuando el plano lo dice o cuando a una sola cara el armario saldría de más de 1200 mm de ancho y con doble frente sale más estrecho.
- **Ver y editar cada cara**: el botón `↻ Cara delantera` / `↻ Cara trasera` de la barra cambia la placa y la puerta que ves. La trasera se dibuja como la ve quien está detrás del armario. Lo que añadas (aparatos, carriles, canaletas) va a la cara que estás viendo.
- **Hilos entre caras**: pasan por el hueco entre las dos placas, por la esquina de arriba a la izquierda vista de frente. A su largo se suma medio fondo del armario más 10 cm.
- **Vista 3D**: gira el armario para ver la placa y la puerta traseras.
- La lista de materiales cuenta dos placas de montaje, y las comprobaciones (carriles, canaletas, choques) se hacen en cada cara por separado.


## ¿Por qué un aparato sale fuera de la placa, a la derecha?
<!-- palabras: fuera, derecha, no cabe, sin colocar -->
Al actualizar desde el esquema, lo nuevo se pone en el primer hueco libre del carril. Si no hay hueco, se deja a la derecha para que lo coloques tú: añade un carril, mueve otros aparatos o pulsa `⚙ Distribuir`.

## ¿Por qué no salen hilos de un aparato?
<!-- palabras: sin hilos, no cablea, manual, añadido a mano -->
Los hilos salen del esquema. Un aparato añadido a mano en el tablero no está en el esquema, así que no tiene conexiones: añádelo en **Instalaciones eléctricas** con la misma designación y pulsa `🔄 Actualizar desde el esquema`.

## ¿Es el catálogo de algún fabricante?
<!-- palabras: fabricante, marca, schneider, siemens, abb, catalogo, propanel -->
No. Son modelos genéricos con medidas típicas para que el diseño sea realista. Escribe en cada aparato la referencia de la marca que uses; las medidas se pueden ajustar en Propiedades.
