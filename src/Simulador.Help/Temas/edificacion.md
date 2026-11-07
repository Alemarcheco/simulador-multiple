<!-- modulo: edificacion -->
<!-- orden: 15 -->
# Edificación

Diseña el edificio (plantas, muros, puertas, ventanas, losas, escaleras y habitaciones) y coloca sobre él las instalaciones: tomas, interruptores, luces, tableros, puntos de agua, desagües, tanques y las rutas de tubos y tuberías. No hace cálculo estructural.

> Para empezar: `Plantillas ▾` › «Casa de una planta» y mira la **Vista 3D**.

## Dibujar la planta
<!-- contexto: Planta 2D -->
<!-- palabras: muro, pared, dibujar, planta, plano, puerta, ventana, habitacion, iman, ortogonal, seleccionar -->
Elige una herramienta de la barra:
- `╱ Muro`: clic en el inicio y en cada esquina. Elige espesor, altura y material a la derecha.
- `▭ Habitación rectangular`: dibuja los cuatro muros de una vez.
- `🚪 Puerta` y `🪟 Ventana`: clic sobre un muro para abrir el hueco.
- `▤ Escalera`: recta, en L o en U, con el cálculo de peldaños (2 contrahuellas + 1 huella ≈ 63 cm).
- `🏷 Nombrar habitación`: clic dentro para darle nombre y uso.
- `↖ Seleccionar`: elegir y mover; las propiedades salen a la derecha.

El dibujo tiene imán a los extremos y modo ortogonal. Las habitaciones se detectan solas al cerrar muros, con su área y perímetro. `Ctrl+Z` / `Ctrl+Y` deshacen y rehacen, `Ctrl+S` guarda.

## Niveles y plantas
<!-- palabras: nivel, planta, piso, segundo piso, planta tipo, copiar planta, cota, altura -->
Cada nivel tiene su cota y su altura. Cambia de nivel arriba con **Nivel:**. `+ Nivel vacío` añade uno nuevo y `Copiar planta encima` duplica la actual (útil para edificios con planta tipo).

## Colocar instalaciones y rutas
<!-- palabras: tomacorriente, enchufe, interruptor, luminaria, luz, tablero, punto de agua, desague, tanque, ruta, tuberia, tubo, instalacion -->
- `＋ Colocar elemento`: elige el tipo (toma, interruptor, luminaria, tablero, punto de agua, desagüe, tanque…) y haz clic en la planta.
- `⤳ Trazar ruta (tubo/tubería)`: une elementos por el techo; el programa da la longitud y el desnivel reales.

Las **comprobaciones** avisan de huecos que no caben, elementos dentro de un hueco o tapados por una puerta, escaleras incómodas y habitaciones demasiado pequeñas.

## Asistente: lo que falta y el presupuesto
<!-- contexto: Asistente, Reglas del asistente -->
<!-- palabras: asistente, sugerencias, reglas, aplicar, presupuesto, materiales, precio, excel, normativa -->
La pestaña **Asistente** revisa el proyecto con reglas que puedes editar: tomas por perímetro y mínimos por uso, luminarias, interruptores, ventanas de al menos 1/8 de la superficie del piso, agua por persona y tanque elevado, presión mínima y distancia de evacuación.

- `Aplicar` coloca lo que falta: tomas, luces, interruptores, tanque, puntos de agua, tablero y rutas.
- Abajo salen los **materiales y el presupuesto** con precios editables. `Copiar presupuesto (para Excel)` lo copia para pegarlo en una hoja de cálculo.

## Calcar un plano DXF o escaneado
<!-- palabras: calco, dxf, autocad, escaneado, imagen, plano de referencia, escalar, importar, detectar muros -->
- `📐 Importar DXF…` pone un plano de AutoCAD como calco del nivel actual. `🖼 Importar plano escaneado…` hace lo mismo con una imagen.
- `↔ Escalar con una medida`: marca dos puntos de una distancia conocida y escribe cuánto mide. `✥ Mover calco` y `⟲ Girar calco` lo colocan.
- Al dibujar muros, el imán se engancha a las líneas del DXF. `🧱 Crear muros desde el DXF` crea los muros solo, a partir de las parejas de líneas paralelas del plano (se deshace con `Ctrl+Z` si no te convence).

En **Capas** cambias la opacidad del calco y qué capas se ven.

## Vista 3D, recorrido y sol
<!-- contexto: Vista 3D -->
<!-- palabras: 3d, perspectiva, recorrer, primera persona, caminar, sol, sombras, soleamiento, orientacion, norte -->
- Arrastra para girar y usa la rueda para acercar. `Perspectiva`, `Mostrar cubierta`, `Solo hasta el nivel actual` e `Instalaciones` cambian lo que se ve. `Vista inicial` vuelve al principio.
- `🚶 Recorrer por dentro`: empiezas delante de la puerta de entrada. `W` `A` `S` `D` o flechas para andar, ratón para mirar, `Esc` para salir. Chocas con muros, pasas por las puertas y subes escaleras.
- `☀ Sol y sombras`: elige la ciudad (o coordenadas), la fecha y la hora, y mira las sombras. `▶ Ver pasar el día` las anima. El norte de la planta se guarda con el edificio.

## Exportar plano: PDF, DXF e IFC
<!-- palabras: exportar, pdf, dxf, ifc, bim, revit, imprimir, escala, cajetin -->
`🖨 Exportar (PDF, DXF, IFC)…`:
- **PDF**: planos vectoriales a escala con cajetín y barra de escala, una hoja por nivel.
- **DXF**: en metros, por capas (muros, huecos, escaleras, habitaciones, cotas, electricidad, hidráulica), para abrir en AutoCAD.
- **IFC**: el edificio completo para programas BIM (Revit, ArchiCAD, BIMvision…).

El proyecto se guarda como `.edif.json`.

## ¿Por qué no se detecta la habitación?
<!-- palabras: habitacion no aparece, area, cerrar muros -->
Los muros tienen que cerrar el espacio por completo: sus extremos deben tocarse. Usa el imán al dibujar para que encajen. Si queda un hueco, aparece el aviso «Cierra muros para formar habitaciones».

## ¿Cómo hago un edificio de varias plantas?
<!-- palabras: varias plantas, pisos, edificio alto, apartamentos -->
Dibuja la planta baja, pulsa `Copiar planta encima` tantas veces como pisos iguales tengas y cambia lo que sea distinto. Pon una escalera en cada nivel. La plantilla «Edificio de apartamentos» es un buen punto de partida.

## ¿El calco DXF sale muy grande o muy pequeño?
<!-- palabras: escala dxf, unidades, milimetros, metros -->
El programa lee la unidad del DXF o la deduce del tamaño. Si no acierta, usa `↔ Escalar con una medida` con una cota que conozcas (por ejemplo, el ancho de una puerta).
