<!-- modulo: cad3d -->
<!-- orden: 60 -->
# Diseño 3D

Diseña piezas para imprimir en 3D. Cada pieza es una lista de **operaciones** en orden (caja, cilindro, esfera, extrusión de un boceto, revolución, redondeo, chaflán, repetición, simetría, texto…) que suman, restan o intersecan. Puedes cambiar cualquier operación más tarde y la pieza se recalcula. Al final exportas a STL o 3MF para el programa de tu impresora.

> Para empezar: `Ejemplos ▾` abre piezas ya hechas (cajas, soporte en L, engranaje, maceta, separador, atril) que puedes cambiar e imprimir.

## Crear una pieza
<!-- palabras: pieza, operacion, caja, cilindro, esfera, sumar, restar, agujero, interseccion, historial, propiedades -->
1. `Nuevo` empieza un diseño; `Añadir pieza` crea otra pieza dentro del mismo diseño.
2. Añade operaciones con los botones: `Caja`, `Caja redondeada`, `Cilindro o cono`, `Esfera`, `Agujero` (un cilindro que resta material), `Extrusión`, revolución…
3. Selecciona una operación en **Operaciones (en orden)** y cambia a la derecha sus medidas (en mm), su posición y si **suma**, **resta** o **interseca**.
4. Ordena con subir y bajar en el historial, `Duplicar` copia y `Eliminar` borra.

Vistas rápidas: `Frente`, `Lado`, `Arriba`, `Iso` y `Encuadrar`. Ratón: izquierdo gira, derecho desplaza, rueda acerca.

## Dibujar un boceto (perfil 2D)
<!-- palabras: boceto, sketch, perfil, extrusion, revolucion, puntos, cotas, arco, redondear esquina, rejilla, poligono, circulo, rectangulo -->
Las operaciones **Extrusión** (perfil 2D libre extruido) y de revolución (perfil girado alrededor de Z) usan un boceto. Pulsa `Editar boceto…`:
- Añade, mueve y borra puntos en la rejilla (`Ajustar a rejilla`). `F` encuadra.
- En **Puntos y cotas (mm)** escribes la longitud exacta de cada tramo.
- Arrastra el cuadradito central de un tramo para convertirlo en **arco**, o escribe su radio. `Redondear esquina` mete un arco tangente en el punto elegido.
- `Rectángulo`, `Polígono` y `Círculo` reemplazan el boceto por una forma hecha.

El editor avisa si el perfil se cruza o no tiene área.

## Reglas del boceto
<!-- palabras: reglas, restricciones, constraints, horizontal, vertical, paralelo, perpendicular, igual, cota fija, punto fijo -->
En **Reglas** puedes fijar que un tramo sea horizontal o vertical, que dos tramos sean paralelos, perpendiculares o de igual longitud, o fijar una cota o un punto. Al mover puntos, el programa mueve lo mínimo para que se sigan cumpliendo. Si una regla choca con las demás, te lo dice y no la pone.

## Redondeos, chaflanes, repeticiones y simetría
<!-- palabras: redondeo, fillet, chaflan, repetir, patron, matriz, array, circular, simetria, espejo -->
- **Redondeo** y **Chaflán** suavizan las aristas de lo anterior (necesitan el motor OpenCASCADE, que va incluido).
- **Repetición**: repite las últimas operaciones en fila, en rejilla o en círculo. Cada copia suma o resta como su original.
- **Simetría**: añade la imagen en espejo de lo anterior respecto a X, Y o Z.

## Texto en relieve o grabado
<!-- palabras: texto, letras, grabar, relieve, nombre, fuente, logo -->
La operación de texto escribe letras con una fuente de Windows. Elige la altura de las mayúsculas en mm, negrita y alineación. En modo **Hueco** las letras se graban; si no, salen en relieve.

## Caja para una placa electrónica
<!-- palabras: caja, carcasa, encapsulado, placa, pcb, tapa, postes, conectores, ventilacion -->
- `Caja para placa…` abre un `.placa` de **Circuitos y PCB** y crea una caja a su medida, con postes en los taladros de montaje y huecos en las paredes para los conectores.
- `Caja con tapa…` crea un encapsulado con tapa con reborde, postes y ventilación a partir de las medidas interiores que elijas.

## Exportar para imprimir: STL, 3MF y STEP
<!-- palabras: exportar, stl, 3mf, step, imprimir, impresora 3d, cura, prusaslicer, bambu, laminador, slicer, freecad -->
- `Exportar STL…`: el formato que acepta cualquier laminador (Cura, PrusaSlicer, Bambu Studio…). Junta todas las piezas visibles.
- `Exportar 3MF…`: cada pieza va como un objeto separado y con su color.
- `Exportar STEP…`: geometría exacta para otros programas CAD (FreeCAD, Fusion, SolidWorks). `Importar STEP…` añade un modelo STEP a la pieza, por ejemplo modelos 3D de componentes de KiCad.

El diseño se guarda como `.cad3d` y se puede reabrir y cambiar.

## ¿Por qué no aparece el agujero?
<!-- palabras: agujero no se ve, restar no funciona, hueco -->
Una operación que resta solo quita material de lo que está **antes** en la lista. Sube el agujero por debajo de la caja en **Operaciones (en orden)**, y comprueba que esté dentro de la pieza (mira sus posiciones X, Y, Z).

## ¿Por qué no puedo redondear o exportar STEP?
<!-- palabras: opencascade, necesita, no disponible, error redondeo -->
Redondeo, chaflán, revolución y STEP necesitan el motor exacto OpenCASCADE. La versión instalada lo trae; si falta, el programa sigue funcionando con un motor más sencillo y esas operaciones quedan desactivadas. Reinstalar la última versión lo resuelve.

## ¿La vista 3D se ve en negro o va lenta?
<!-- palabras: negro, opengl, tarjeta grafica, lento, no se ve -->
El visor usa OpenGL. Si la tarjeta gráfica no responde en unos segundos, el programa cambia solo a un visor por software, más lento pero que funciona en cualquier equipo. Actualizar el controlador de la tarjeta gráfica suele ayudar.
