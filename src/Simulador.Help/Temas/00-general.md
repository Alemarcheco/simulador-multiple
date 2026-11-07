<!-- modulo: general -->
<!-- orden: 0 -->
# Cómo usar el programa

Simulador múltiple reúne varios simuladores en una sola ventana. Cada uno está en su propia pestaña, arriba: **Circuitos y PCB**, **Edificación**, **Instalaciones eléctricas**, **PLC y microcontroladores**, **Hidráulica**, **Neumática**, **Diseño 3D** y **Banco de pruebas**.

> Pulsa `F1` en cualquier momento para abrir esta ayuda justo en la guía de la pestaña en la que estás. Arriba de esta ventana puedes escribir una pregunta o unas palabras para buscar.

Todo funciona sin conexión a internet: las guías, los ejemplos y los simuladores van dentro del programa.

## Primeros pasos
<!-- palabras: empezar, comenzar, inicio, principiante, tutorial, aprender -->
1. Elige una pestaña arriba. La primera vez que la abres tarda un momento en prepararse.
2. Casi todas las pestañas tienen un botón `Ejemplos ▾`. Abrir un ejemplo y pulsar `▶ Simular` es la forma más rápida de ver cómo funciona.
3. Cambia algo del ejemplo (un valor, un componente, una conexión) y vuelve a simular para ver la diferencia.
4. Cuando quieras empezar algo tuyo, pulsa `Nuevo` y guarda con `Guardar…` o `Ctrl+S`.

La **barra de estado**, abajo del todo, te cuenta lo que acaba de pasar: archivo abierto, errores al compilar o simular, avisos, etc. Si algo no responde como esperas, mírala primero.

## Buscar en la ayuda
<!-- palabras: buscador, buscar, pregunta, encontrar, ayuda, f1 -->
- Escribe en la caja de arriba con tus palabras, por ejemplo «¿cómo hago una placa?», «exportar STL» o «el motor no arranca». No hace falta poner acentos ni mayúsculas.
- Los resultados salen a la izquierda, primero los que tienen más palabras de tu pregunta. Usa `↓` para bajar y `Enter` para abrir.
- `Esc` borra la búsqueda; si ya está vacía, cierra la ayuda.
- `← Atrás` (o `Alt+←`) vuelve al tema anterior.
- Sin buscar, a la izquierda ves todas las guías. Cada guía tiene sus temas y sus **Preguntas frecuentes**.

## Teclas que sirven en todo el programa
<!-- palabras: atajos, teclado, combinaciones, shortcuts, teclas rapidas -->
- `F1`: ayuda de la pestaña activa.
- `Ctrl+Z` / `Ctrl+Y`: deshacer y rehacer (en los editores que lo permiten).
- `Ctrl+S`: guardar.
- `R`: girar el componente seleccionado 90°.
- `Supr`: borrar lo seleccionado.
- `Esc` o clic derecho: dejar de colocar o de dibujar y volver a seleccionar.
- `F`: encuadrar, para ver todo el dibujo en pantalla (esquemas, placas y bocetos).
- Rueda del ratón: acercar y alejar. En las vistas 3D, arrastrar con el botón izquierdo gira y con el derecho desplaza.

Cada guía cuenta las teclas propias de su pestaña.

## Archivos de cada pestaña
<!-- palabras: extension, formato, guardar, abrir, archivo, fichero, json -->
Cada simulador guarda sus proyectos en su propio tipo de archivo. Son archivos de texto (JSON), así que se pueden copiar, enviar por correo y guardar en una memoria USB sin problemas.

- Circuitos y PCB: esquemas `.esquema` y placas `.placa`.
- Edificación: `.edif.json`. Exporta a DXF, PDF e IFC.
- Instalaciones eléctricas: `.selx`.
- PLC: programas Ladder `.ladder.json` y bloques FBD; Arduino abre y guarda bocetos de código.
- Hidráulica: `.hidx`. Neumática: `.neux`.
- Diseño 3D: `.cad3d`. Exporta a STL, 3MF y STEP.

Los datos que importas (librerías, catálogos, símbolos) se copian a la carpeta de datos del usuario, en `%LOCALAPPDATA%\SimuladorMultiple`, para tenerlos la próxima vez.

## Cómo se conectan los simuladores entre sí
<!-- palabras: integrar, combinar, conectar, juntos, cosimulacion, co-simulacion -->
Las pestañas no son islas:

- Un **PLC** programado en la pestaña PLC se puede dibujar dentro de un esquema de **Instalaciones eléctricas** y mover contactores de verdad.
- La placa diseñada en **Circuitos y PCB** sirve para crear su caja a medida en **Diseño 3D** (`Caja para placa…`).
- **Edificación** da el edificio sobre el que se colocan tomas, luces, puntos de agua y rutas de tuberías.
- El **Banco de pruebas** junta en una sola simulación piezas de varios módulos: PLC, esquema eléctrico, motor, bomba, red hidráulica, turbinas y generadores.

## Instalar y actualizar
<!-- palabras: descargar, instalador, version, actualizacion, nueva version, windows protegio -->
Cada mejora publica una versión nueva. El instalador de la última versión está en la página de Releases del proyecto en GitHub (`SimuladorMultiple-Setup.exe`). No pide permisos de administrador ni necesita instalar .NET.

Como el instalador no está firmado, Windows puede mostrar «Windows protegió su PC». Pulsa **Más información** y luego **Ejecutar de todas formas**. También hay una versión portable en `.zip` que se usa sin instalar.

Para actualizar basta con instalar la nueva versión encima: tus archivos no se tocan.

## ¿La ayuda necesita internet?
<!-- palabras: offline, sin conexion, conexion -->
No. Todas las guías van dentro del programa y se abren al instante con `F1`, con o sin conexión.

## ¿Cómo veo la ayuda de una pestaña concreta?
<!-- palabras: contexto, pestaña activa -->
Ve a esa pestaña y pulsa `F1`, o usa el botón `❓ Ayuda (F1)` de la barra de estado. Si dentro de la pestaña estás en una sub-pestaña con tema propio (por ejemplo **PCB** en Circuitos o **Vista 3D** en Edificación), la ayuda se abre directamente en ese tema. También puedes elegir cualquier guía en la lista de la izquierda.

## ¿Por qué una pestaña tarda en abrirse la primera vez?
<!-- palabras: lento, carga, tarda, espera -->
Cada simulador se prepara la primera vez que abres su pestaña, para que el programa arranque rápido. Las siguientes veces ya está listo.

## ¿Puedo deshacer un error?
<!-- palabras: deshacer, volver atras, ctrl z, borre sin querer -->
Sí, en los editores de esquemas, placas y planos: `Ctrl+Z` deshace y `Ctrl+Y` rehace. Si cerraste sin guardar, el cambio no se puede recuperar, así que guarda a menudo con `Ctrl+S`.

## ¿El programa se ve claro u oscuro?
<!-- palabras: tema oscuro, modo oscuro, colores, apariencia -->
Sigue el modo claro u oscuro de Windows. Cámbialo en Configuración de Windows › Personalización › Colores y el programa lo sigue.
