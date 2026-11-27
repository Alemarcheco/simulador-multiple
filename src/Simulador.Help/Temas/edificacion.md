<!-- modulo: edificacion -->
<!-- orden: 15 -->
# Edificación

Diseña el edificio (plantas, muros, puertas, ventanas, losas, escaleras y habitaciones) con columnas y vigas, y coloca sobre él las instalaciones: luces, contactos, apagadores, cajas, tableros, puntos de agua, desagües y tanques, con su tubería. La instalación eléctrica se puede simular. No hace cálculo estructural.

> Para empezar: **Archivo › Plantillas** › «Casa de una planta» y mira la **Vista 3D**.

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
- `＋ Colocar elemento`: elige el grupo y el tipo (luminaria, contacto, apagador, caja, tablero, equipo, punto de agua, desagüe, tanque…) y haz clic en la planta. Cada tipo se pone solo a su altura: los contactos y apagadores en la cara del muro o de la columna más cercana, las lámparas en el techo o colgadas bajo la viga.
- `⤳ Trazar tubería`: une elementos uno tras otro (cada clic sigue desde el último; clic derecho para terminar). El programa da la longitud y el desnivel reales.

Las **comprobaciones** avisan de huecos que no caben, elementos dentro de un hueco o tapados por una puerta, escaleras incómodas y habitaciones demasiado pequeñas.

## Instalación eléctrica: luces, contactos y apagadores
<!-- contexto: Instalación eléctrica -->
<!-- palabras: lampara, foco, luminaria, led, spot, arbotante, reflector, campana, contacto, tomacorriente, gfci, 220, trifasico, apagador, escalera, tres vias, cuatro vias, atenuador, sensor, chalupa, caja, registro, condulet, caja de paso, tablero, centro de carga, medidor -->
En la paleta **Instalaciones** hay, por grupos:
- **Luminarias**: plafón, colgante, empotrado LED, panel LED, lineal LED, spot, arbotante (de muro), reflector, campana industrial, luz de emergencia y ventilador de techo.
- **Contactos** (NOM): dúplex 127 V, GFCI para baño y cocina, 220 V, trifásico industrial, de piso y de tierra aislada.
- **Apagadores**: sencillo, de escalera (3 vías), de 4 vías, doble, atenuador y sensor de movimiento.
- **Cajas y registros**: chalupa, caja cuadrada, octagonal, caja de paso, registro de piso y condulet.
- **Tablero y acometida**: centro de carga, base de medidor e interruptor de seguridad.
- **Equipos fijos**: minisplit, calentador, bomba de agua y motor trifásico.

Para decir qué apagador enciende cada lámpara usa `🔗 Enlazar apagador con luminarias`: clic en el apagador y luego en sus lámparas. También desde las propiedades de la lámpara, en «Se enciende con». Dos apagadores de escalera enlazados a la misma lámpara la mandan desde dos sitios.

`⚡ Diseñar instalación automáticamente` lo hace todo de una vez: enlaza cada lámpara con el apagador de su habitación, crea los circuitos (alumbrado 15 A con cable 14, contactos 20 A con cable 12, GFCI en zonas húmedas, un circuito por equipo), los reparte entre fases, traza la tubería y elige calibres y diámetros. Después puedes cambiar cualquier cosa.

## Tubería eléctrica, columnas y vigas
<!-- palabras: tuberia electrica, conduit, poliducto, pvc, emt, pared delgada, pared gruesa, charola, diametro, relleno, columna, viga, nave, industria, estructura -->
Antes de trazar, elige **Por dónde va** (techo/losa, piso, muro o por la estructura bajo las vigas), el **Tipo de tubo** (PVC ligero o pesado, poliducto, conduit pared delgada EMT o pared gruesa, flexible metálico, charola) y el **Diámetro**. En la tabla de canalizaciones verás los conductores de cada tubo y su relleno (máximo 40 %).

En **Estructura** están `▪ Columna` (clic donde va) y `═ Viga` (de una columna a otra), de concreto, acero o madera. Sirven para naves e industrias: los contactos y apagadores se pueden poner en las columnas, las campanas cuelgan bajo las vigas y la tubería «por la estructura» corre por debajo de ellas. La plantilla «Nave industrial con instalación eléctrica» es un ejemplo completo.

## Cuadro de cargas y comprobaciones
<!-- contexto: Instalación eléctrica -->
<!-- palabras: cuadro de cargas, circuito, interruptor, termomagnetico, calibre, awg, caida de tension, desbalance, fase, nom, csv -->
La pestaña **⚡ Instalación eléctrica** muestra cada centro de carga con sus circuitos: tensión (127 V, 220 V o trifásico), fase, interruptor, cable, GFCI, carga, corriente y caída de tensión. Todo se puede cambiar ahí mismo. Debajo están las canalizaciones y los avisos según la NOM-001-SEDE-2012 (orientativos): 180 VA por contacto, interruptor que no supere lo que aguanta el cable, caída ≤ 3 %, GFCI en baño y cocina, relleno de tubos y desbalance entre fases.

`Cuadro de cargas (CSV)` lo abre en Excel y `Esquema (.selx)` lo pasa al módulo **Instalaciones eléctricas**. Un proyecto real lo firma un perito o una unidad de verificación.

## Simular la instalación
<!-- palabras: simular, encender, prender luz, apagar, probar, gfci test, disparo, breaker, consumo -->
Pulsa `▶ Simular instalación eléctrica`. Con la simulación en marcha:
- Clic en un **apagador** enciende o apaga sus lámparas (en el doble, `Mayús` + clic usa la segunda tecla). Las lámparas encendidas iluminan la planta y la Vista 3D.
- Clic en un **contacto** enchufa un aparato (televisión, computadora, licuadora, plancha…) y otro clic lo cambia.
- `Mayús` + clic en un **GFCI** pulsa su botón TEST; otro clic lo rearma.
- Clic en un **equipo** lo arranca o lo para.

A la derecha ves el consumo y la corriente de cada circuito; si te pasas, el interruptor se dispara como el de verdad (con `Abrir` y `Rearmar`).

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
**Archivo › Exportar (PDF, DXF, IFC)…**:
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
