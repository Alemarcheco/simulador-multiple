<!-- modulo: circuitos -->
<!-- orden: 10 -->
# Circuitos y PCB

Diseña circuitos electrónicos de principio a fin: eliges componentes de las librerías, dibujas el esquema, lo simulas con ngspice (voltímetro, osciloscopio y Bode), pasas a la placa (PCB), la ruteas y sacas los archivos para fabricarla en casa o en un fabricante.

La pestaña tiene cinco sub-pestañas que siguen ese orden: **Librerías**, **Esquemático**, **Simulación**, **PCB** y **Fabricación**.

> Para empezar rápido: en **Esquemático** abre **Archivo › Ejemplos**, elige uno (por ejemplo el intermitente con 555) y ve a **Simulación**.

## Librerías: importar componentes
<!-- contexto: Librerías -->
<!-- palabras: kicad, simbolo, huella, footprint, modelo spice, importar, fabricante, componente nuevo -->
El programa trae una librería básica (resistencias, condensadores, diodos, transistores, fuentes, 555…) con huellas de pads grandes, pensadas para placa casera.

Para añadir más:
- `Importar archivos…` lee símbolos de KiCad (`.kicad_sym`), huellas (`.kicad_mod`) y modelos SPICE de fabricantes (`.lib`, `.mod`).
- `Importar carpeta…` importa de una vez una carpeta entera, por ejemplo una librería `.pretty` de huellas de KiCad.

Lo importado se guarda en tu carpeta de datos y aparece la próxima vez que abras el programa.

## Dibujar el esquema
<!-- contexto: Esquemático -->
<!-- palabras: colocar, cable, cablear, unir, conectar, etiqueta, masa, gnd, girar, espejo, valor, referencia -->
1. Escribe en `Buscar componente…` (por ejemplo «resistencia» o «555») y elige uno de la lista.
2. Haz clic en el esquema para colocarlo. Puedes colocar varios seguidos; `Esc` o clic derecho para terminar.
3. Para unir, elige la herramienta `Cable` (tecla `W`): clic en un pin, clic en cada esquina y doble clic, clic derecho o `Esc` para terminar. `Espacio` cambia el sentido de la esquina.
4. Selecciona un componente para cambiar a la derecha su **Referencia** (R1, C2…), su **Valor** y su **Huella (PCB)**.

Teclas: `R` gira 90°, `M` refleja, `W` cable, `L` etiqueta de red, `F` ver todo, `Supr` borra, `Ctrl+Z` / `Ctrl+Y` deshacer y rehacer, `Ctrl+S` guardar.

Los valores admiten los sufijos de SPICE: `4.7k`, `100n`, `10u`, `2meg` (f p n u m k meg g). Ojo: `M` es mili, para mega escribe `meg`.

## Etiquetas de red y masa
<!-- contexto: Esquemático -->
<!-- palabras: etiqueta, net label, nombre de red, gnd, tierra, 0, nodo -->
Con la herramienta `Etiqueta` (tecla `L`) pones un nombre sobre un cable o un pin. Todos los puntos con la misma etiqueta quedan unidos aunque no haya un cable entre ellos, lo que deja los esquemas más limpios.

El símbolo de **masa** es el nodo 0 de la simulación: todas las tensiones se miden respecto a él. La etiqueta `GND` equivale a masa. Todo circuito que quieras simular necesita al menos una masa.

## Simular el circuito
<!-- contexto: Simulación -->
<!-- palabras: ngspice, simular, voltimetro, tension, corriente, osciloscopio, transitorio, bode, frecuencia, analisis -->
En la sub-pestaña **Simulación** elige el análisis y pulsa `▶  Simular`:

- **Punto de trabajo (voltímetro)**: tensiones de cada nudo respecto a masa y corrientes de las fuentes, en reposo. Las tensiones también aparecen escritas sobre el esquema.
- **Transitorio (osciloscopio)**: cómo cambian las señales en el tiempo. Elige qué señales ver, el tiempo total y el paso.
- **Respuesta en frecuencia (Bode)**: ganancia y fase frente a la frecuencia. Necesita una fuente con AC (el generador senoidal ya la trae).

`⚡ Ver la corriente en el esquema` anima puntos que recorren los cables: cuanto más rápidos, más corriente. Si algo falla, abre `Mensajes de ngspice` para ver el detalle, y `Netlist SPICE` en el esquema para ver lo que se envía al simulador.

## Diseñar la placa (PCB)
<!-- contexto: PCB -->
<!-- palabras: pcb, placa, pista, rutear, ratsnest, huella, colocar, drc, autorruteo, autorrutear -->
1. Pulsa `⟳ Actualizar desde esquema`: aparecen las huellas de todos los componentes y las líneas finas (ratsnest) que indican qué hay que unir.
2. Coloca los componentes arrastrándolos (`R` gira). Pon juntos los que van conectados para que las pistas sean cortas.
3. Dibuja las pistas con la herramienta `Pista` (tecla `X`): clic en un pad, clic en cada esquina y termina sobre el otro pad. Las esquinas van a 45°. `Espacio` cambia la esquina.
4. O deja que lo haga el programa: `🧩 Colocar` busca la colocación más compacta y `✨ Autorrutear` traza las pistas.
5. Pulsa `✔ Comprobar (DRC)` para ver separaciones demasiado pequeñas, cortos y conexiones sin rutear. Los problemas se marcan en rojo.

Las reglas por defecto son para placa casera: pista de 0,8 mm y separación de 0,5 mm. La placa se guarda como `.placa`.

## Fabricar la placa
<!-- contexto: Fabricación -->
<!-- palabras: fabricar, gerber, taladros, excellon, pdf, transferencia termica, planchar, imprimir, cnc, jlcpcb, pcbway -->
En **Fabricación** hay dos caminos:

- **Placa casera (transferencia térmica)**: `🖨 Guardar PDF para transferencia` crea un PDF a escala 1:1 para imprimir con impresora láser y planchar sobre la placa. Elige la capa, las copias por hoja, si se espeja la impresión y si se marcan los centros de los taladros (ayuda a centrar la broca). La hoja trae una regla de 50 mm: mídela con una regla de verdad para comprobar que la impresora no ha cambiado la escala.
- **Fabricante o fresadora CNC**: `📦 Exportar Gerber y taladros (.zip)` crea un `.zip` con los Gerber (RS-274X) y el archivo de taladros Excellon, que es lo que piden los fabricantes de placas.

## Hacer una caja para la placa
<!-- palabras: caja, carcasa, encapsulado, 3d, impresion 3d -->
Guarda la placa (`.placa`) y ve a la pestaña **Diseño 3D**. Pulsa `Caja para placa…` y elige el archivo: se crea una caja a medida con postes en los taladros de montaje y huecos para los conectores. Ver [Diseño 3D](#cad3d).

## ¿Por qué la simulación da error?
<!-- palabras: error, falla, no simula, singular matrix, no converge, ngspice error -->
Las causas más comunes:
- Falta la **masa**: todo circuito necesita al menos un símbolo de masa o una etiqueta `GND`.
- Hay un pin suelto o un nudo que solo toca un componente. Revisa que los cables lleguen justo al pin (el extremo se marca al conectar).
- Un valor mal escrito, por ejemplo `4,7k` con coma: usa punto (`4.7k`).
- Un condensador o una fuente sin camino a masa en continua.

El detalle está en `Mensajes de ngspice`.

## ¿Qué significa 'M' en un valor?
<!-- palabras: mega, mili, sufijo, unidades, meg -->
En SPICE `m` y `M` significan mili (0,001). Para mega (1 000 000) escribe `meg`, por ejemplo `1meg` para 1 MΩ.

## ¿Cómo veo la corriente por los cables?
<!-- palabras: corriente animada, puntos, flujo -->
Después de simular, pulsa `⚡ Ver la corriente` (en Simulación o en el esquema). Unos puntos recorren los cables: el sentido es el de la corriente y la velocidad indica su valor. `■ Parar la corriente` la detiene.

## ¿Falta la huella de un componente en la PCB?
<!-- palabras: huella, footprint, no aparece, falta -->
Selecciona el componente en el esquema y escribe su **Huella (PCB)** a la derecha, o importa la librería de huellas en **Librerías**. Al volver a pulsar `⟳ Actualizar desde esquema` aparecerá en la placa.

## ¿Cómo sé que el PDF de transferencia está a escala?
<!-- palabras: escala, tamaño, imprimir, impresora, ajustar pagina -->
Imprime con la opción «tamaño real» o «100 %» (sin «ajustar a la página») y mide la regla de 50 mm que lleva la hoja. Si no mide 50 mm, la impresora ha cambiado la escala.
