<!-- modulo: electrico -->
<!-- orden: 20 -->
# Instalaciones eléctricas

Dibuja el esquema de mando y el de potencia en la misma hoja y simúlalo en tiempo real: pulsas con el ratón y ves cómo actúan contactores, relés y temporizadores, cómo arrancan los motores y cuándo disparan las protecciones.

> Para empezar: **Archivo › Ejemplos** › «Arranque directo con autorretención», pulsa `▶` y haz clic en el pulsador de marcha.

## Dibujar un esquema
<!-- contexto: Esquema -->
<!-- palabras: colocar, cablear, cable, borne, mover, girar, borrar, libreria, componente -->
- **Colocar**: elige un componente en la librería de la izquierda y haz clic en la hoja. Clic derecho o `Esc` para terminar.
- **Cablear**: clic en un borne y clic en otro. Con `Mayús` sigues cableando desde el último borne.
- **Editar**: arrastra para mover, `R` gira 90°, `Supr` borra. A la derecha cambias designación, parámetros, numeración de bornes y el aparato de catálogo.

Al pasar el ratón por un componente de la librería aparece abajo una vista ampliada con los bornes numerados, la descripción y las referencias de catálogo, para verlo antes de colocarlo.

## Designaciones: KM1, F2, S1…
<!-- palabras: designacion, nombre, iec 81346, km1, contacto auxiliar, bobina, mismo aparato -->
Todo lo que tiene la **misma designación es un mismo aparato**. La bobina `KM1` mueve todos los contactos `KM1`, estén donde estén en la hoja. El relé térmico `F2` abre su contacto `F2` (95-96). Un pulsador doble son dos contactos `S1`.

Así puedes dibujar la bobina en el circuito de mando y los contactos principales en el de potencia, como en un esquema real.

## Simular y accionar
<!-- palabras: simular, pulsar, pulsador, rearmar, disparo, fusible, diferencial, boton de prueba, carga del motor -->
Pulsa `▶` y luego:
- Clic en pulsadores, interruptores, setas, finales de carrera y captadores para accionarlos.
- Clic en un magnetotérmico, guardamotor o diferencial para abrirlo o rearmarlo. `Mayús+clic` en un diferencial pulsa su botón de prueba.
- Clic en un fusible fundido para reponerlo.
- Con un motor seleccionado puedes cambiar su carga; con el variador, su consigna.

## Ver la corriente y las tensiones
<!-- palabras: corriente animada, tension, colores, puntos amarillos, falstad -->
Con los botones de la barra, los cables se colorean con su tensión instantánea (verde positiva, rojo negativa, gris sin tensión) y unos puntos amarillos los recorren con la corriente. En continua avanzan; en alterna van y vienen, despacio para poder seguirlos. Cuanto más rápidos, más corriente.

## Qué componentes hay
<!-- palabras: libreria, contactor, rele, temporizador, magnetotermico, guardamotor, termico, diferencial, motor, transformador, variador, lampara, piloto -->
- **Alimentación**: red 3F+N+PE, monofásica, fuente de continua.
- **Mando**: pulsadores NA/NC, seta de emergencia, interruptores, conmutadores, cruzamiento, selector I-0-II, finales de carrera, captadores (flotador, presostato, termostato, detector).
- **Bobinas**: contactor, relé, temporizadores a la conexión y a la desconexión, intermitente, telerruptor.
- **Protecciones**: fusibles gG/aM, magnetotérmicos de 1 a 4 polos (curvas B, C, D), guardamotor, relé térmico (clase 10, 20, 30), diferencial.
- **Receptores**: lámpara, piloto, timbre, resistencia, electroválvula.
- **Motores**: trifásicos de 3 y 6 bornes, monofásico con condensador.
- **Transformadores y variador de frecuencia**, **PLC compacto**, voltímetro y amperímetro.

## PLC dentro del esquema
<!-- palabras: plc, automata, kf1, entradas, salidas, ladder, texto estructurado, 24v -->
El **PLC compacto** (`KF1`) lleva dentro la CPU de la pestaña PLC. Sus entradas I0.0-I0.7 miden la tensión real del borne respecto a M: se activan con 15 V o más (con más de 36 V se dañan). Sus salidas Q0.0-Q0.7 son relés con común 1L que cierran contactos de verdad en el circuito. Sin 24 V DC entre L+ y M, las salidas quedan abiertas.

El programa se escribe en Propiedades (texto estructurado, direcciones `%IX0.0` y `%QX0.0`) o se carga un `.ladder.json` guardado en la pestaña PLC. Los LED verdes del símbolo son las entradas y los ámbar, las salidas.

## Variador de frecuencia
<!-- palabras: variador, frecuencia, rampa, giro, stf, str, consigna, v/f -->
El variador sigue la ley V/f, con rampas de aceleración y deceleración e inversión de giro (STF/STR) pasando por 0 Hz. Falla por sobrecorriente o sobrecarga; clic sobre él para rearmarlo. Lo que cuelga de U-V-W trabaja a la frecuencia del variador. Si unes su salida a la red, el simulador te avisa.

## Aparatos reales y catálogos
<!-- palabras: catalogo, csv, schneider, siemens, abb, referencia, fabricante, excel, datos de placa -->
Al seleccionar un componente aparece **Aparato de catálogo** para aplicarle una referencia real (Schneider, Siemens, ABB, motores IE3…) con sus datos de placa.

Para añadir más referencias, **Archivo › Importar catálogo CSV…** con columnas `Fabricante;Referencia;Tipo;Descripción;Parámetros`. Sirve para tablas copiadas de catálogos o exportadas desde Excel.

## Símbolos de QElectroTech
<!-- palabras: qelectrotech, elmt, importar simbolo -->
**Archivo › Importar símbolos QElectroTech…** lee archivos `.elmt`. Se conserva su dibujo y sus bornes, y el comportamiento se deduce del propio archivo (bobina, contacto NA, NC o temporizado) o del nombre. Lo que no se reconoce queda como dibujo con bornes.

## ¿Por qué no arranca el motor?
<!-- palabras: motor no arranca, no gira, no funciona, falta fase -->
Revisa, en este orden:
1. Que la bobina del contactor tenga la misma designación que sus contactos principales (por ejemplo `KM1` en los dos).
2. Que el circuito de mando cierre: el contacto del térmico (95-96) y la seta deben estar cerrados.
3. Que no haya disparado una protección (clic para rearmar).
4. Que lleguen las tres fases: con una sola fase menos, el motor no arranca y se calienta.

## ¿Por qué no se queda enganchado el contactor?
<!-- palabras: autorretencion, enclavamiento, se suelta, retencion -->
Falta la autorretención: un contacto auxiliar NA del propio contactor (13-14) en paralelo con el pulsador de marcha. Mira el ejemplo «Arranque directo con autorretención».

## ¿Por qué dispara el magnetotérmico al arrancar?
<!-- palabras: dispara, salta, corto, cortocircuito, curva -->
Puede ser un cortocircuito real (los cortos dan kA) o la corriente de arranque del motor. Para motores usa curva C o D, o un guardamotor ajustado a la corriente del motor.

## ¿Qué ejemplos hay?
<!-- palabras: ejemplos, estrella triangulo, inversion de giro, conmutada, telerruptor, minutero, cascada, garaje -->
Arranque directo, inversión de giro con enclavamiento, estrella-triángulo, luz conmutada desde tres puntos, telerruptor y minutero, bomba con flotador y selector, cintas en cascada, puerta de garaje, cuadro de vivienda con diferencial, transformador de mando 230/24 V, variador con dos sentidos y marcha-paro con PLC.
