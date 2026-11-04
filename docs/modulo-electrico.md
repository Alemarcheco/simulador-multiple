# Módulo de instalaciones eléctricas y automatismos

Pestaña 🔌 **Instalaciones eléctricas**. Dibujas el esquema de mando y el de potencia en la misma hoja y lo simulas en
tiempo real: pulsas con el ratón y ves cómo actúan contactores, relés y temporizadores, arrancan los motores y disparan
las protecciones.

## Uso

- **Colocar**: elige un componente en la librería (izquierda) y haz clic en la hoja. Clic derecho o Esc para terminar.
- **Cablear**: clic en un borne y clic en otro. Con Mayús se sigue cableando desde el último borne.
- **Editar**: arrastra para mover, **R** gira 90°, **Supr** borra. A la derecha cambias designación, parámetros,
  numeración de bornes y el aparato de catálogo.
- **Designaciones** (IEC 81346): todo lo que tiene la misma designación es un mismo aparato. La bobina `KM1` mueve
  todos los contactos `KM1`; el relé térmico `F2` abre su contacto `F2` (95-96); un pulsador doble son dos contactos `S1`.
- **Simular** (▶): clic en pulsadores, interruptores, setas, finales de carrera y captadores. Clic en un magnetotérmico,
  guardamotor o diferencial para abrirlo o rearmarlo; Mayús+clic en un diferencial pulsa su botón de prueba. Clic en un
  fusible fundido para reponerlo. Durante la simulación puedes cambiar la carga del motor seleccionado y la consigna
  del variador.
- **Corriente y tensiones** (botones de la barra, como en Falstad): los cables se colorean con su tensión instantánea
  (verde positiva, rojo negativa, gris sin tensión) y unos puntos amarillos recorren los cables con la corriente. En
  continua avanzan; en alterna van y vienen, ralentizados para poder seguirlos. Cuanto más rápidos, más corriente.
- **Archivos**: los esquemas se guardan como `.selx` (JSON legible).

## Librería incluida

| Grupo | Componentes |
|---|---|
| Alimentación | Red 3F+N+PE (TN-S), alimentación monofásica, fuente DC |
| Mando y captadores | Pulsador NA/NC, seta de emergencia, interruptor/selector, conmutador, cruzamiento, selector I-0-II, finales de carrera NA/NC, captadores NA/NC (flotador, presostato, termostato, detector) |
| Contactos | Auxiliar NA/NC, temporizado NA/NC, contactos principales 3P |
| Bobinas | Contactor, relé auxiliar, temporizador a la conexión y a la desconexión, intermitente, telerruptor |
| Protecciones | Fusible gG/aM (1P y 3P), magnetotérmico 1P/2P/3P/4P curvas B/C/D, guardamotor, relé térmico clase 10/20/30, diferencial 2P/4P |
| Receptores | Lámpara, piloto de color, timbre, resistencia/calefactor, electroválvula |
| Motores | Motor de inducción trifásico de 3 bornes (estrella o triángulo interno) y de 6 bornes, motor monofásico de condensador permanente |
| Transformadores y variadores | Transformador monofásico (mando o separación), transformador trifásico Dyn con neutro, variador de frecuencia con rampas, marcha a derechas/izquierdas (STF/STR) y consigna ajustable |
| Automatización | PLC compacto de 24 V DC con 8 entradas y 8 salidas a relé, programado en texto estructurado o con un Ladder del módulo PLC |
| Importados | Símbolos de QElectroTech (`.elmt`) |
| Medida | Voltímetro, amperímetro, borne de conexión |

Cada componente de la librería muestra su símbolo en miniatura; al pasar el ratón por encima aparece abajo una vista
ampliada con los bornes numerados, la descripción y las referencias de catálogo disponibles, para verlo antes de colocarlo.

Ejemplos incluidos (menú **Ejemplos**):

- Arranque directo con autorretención.
- Inversión de giro con enclavamiento.
- Arranque estrella-triángulo.
- Luz conmutada desde tres puntos.
- Telerruptor y minutero de escalera.
- Bomba con flotador y selector Manual-0-Automático.
- Cintas transportadoras en cascada (la segunda arranca con retardo).
- Puerta de garaje con finales de carrera.
- Cuadro de vivienda con diferencial y fuga a tierra.
- Bomba monofásica con transformador de mando 230/24 V.
- Variador de frecuencia: rampa y dos sentidos de giro.
- Marcha-paro de motor con PLC.

## PLC en el esquema

El **PLC compacto** (`KF1`) lleva dentro la CPU del módulo PLC (perfil genérico IEC). Sus entradas I0.0-I0.7 miden la
tensión real del borne respecto a M (se activan con 15 V o más; con más de 36 V se dañan), y sus salidas Q0.0-Q0.7 son
relés con común 1L que cierran contactos de verdad en el circuito. Sin 24 V DC entre L+ y M, las salidas quedan abiertas.
El programa se escribe en Propiedades (texto estructurado, direcciones `%IX0.0` y `%QX0.0`) o se carga un
`.ladder.json` guardado en la pestaña PLC. Los LED verdes del símbolo son las entradas y los ámbar las salidas.

## Variador de frecuencia

Ley V/f con refuerzo a baja frecuencia, rampas de aceleración y deceleración, inversión de giro pasando por 0 Hz y
fallos por sobrecorriente (más de 2,2 In durante 50 ms) o sobrecarga (más de 1,5 In durante 60 s); clic sobre él para
rearmar. Lo que cuelga de U-V-W (térmico, contactor, motor) trabaja a la frecuencia del variador. Si la salida queda
unida a la red, el simulador lo avisa.

## Símbolos de QElectroTech

**Importar símbolos QElectroTech…** lee archivos `.elmt` (la colección libre de QElectroTech tiene miles de aparatos de
Schneider, Siemens, ABB, Legrand, Phoenix…). Se conserva su dibujo y sus bornes (redondeados a la rejilla), y se copian a
la carpeta de datos para tenerlos la próxima vez. El comportamiento se deduce del propio archivo: las bobinas
("maestro") excitan los contactos con su designación y los contactos ("esclavo") son NA, NC o temporizados. Si no lo
indica, se deduce por el nombre (lámpara, pulsador, fusible, motor…). Lo que no se reconoce queda como dibujo con bornes.
Un piloto verde junto al símbolo indica que el contacto está cerrado.

## Banco de pruebas combinado

`ElectricalComponentFactory` ofrece al banco de pruebas la "red eléctrica": un esquema `.selx` entero (o uno de los
ejemplos) como un único componente (`ElectricalNetwork`). Hacia fuera expone:

- `RED1.KM1.bobina` (DI, Bool): excita la bobina desde una salida de PLC del banco.
- `RED1.KM1.13-14` (DO, 24 V): estado de cada contacto auxiliar, también el 95-96 o 97-98 del térmico.
- `RED1.KF1.I0.0` / `RED1.KF1.Q0.0`: los bornes de los PLC dibujados en el esquema.
- `RED1.M1.eje` (MechanicalRotational, impone flujo): el motor da la velocidad en rad/s y lee en Effort el par
  resistente de la carga. La inercia de la carga se pasa con el parámetro `Inercia carga M1`.

## Cómo simula

- **Red**: análisis nodal con fasores a la frecuencia de red. Si hay fuentes de continua, se resuelve también la parte
  DC y se suman (superposición). Contactos y protecciones cerrados son resistencias de miliohmios, y cada fuente tiene su
  impedancia de cortocircuito, así que un corto da una corriente realista (kA) y dispara el magnético.
- **Bobinas**: entran con el 85 % de su tensión y caen por debajo del 50 %, con retardos de cierre y apertura
  (contactor 20/12 ms, relé 10/8 ms). Avisa si una bobina recibe más tensión de la suya.
- **Protecciones**: modelo térmico de primer orden (magnetotérmico, guardamotor y relé térmico según su clase) y disparo
  magnético instantáneo (B 5 In, C 10 In, D 20 In, guardamotor 13 Ir). Los fusibles funden según una curva
  tiempo-corriente gG o aM. El diferencial suma las corrientes de sus polos y dispara si la fuga supera su sensibilidad.
- **Motor de inducción**: circuito equivalente por fase ajustado con los datos de placa (potencia, tensión, velocidad,
  cos φ, rendimiento, corriente de arranque) y resuelto por componentes simétricas. Por eso salen solos la corriente de
  arranque, la inversión de giro al cruzar dos fases, el arranque estrella-triángulo con un tercio de corriente y el
  motor que no arranca y se calienta si le falta una fase. La mecánica del eje integra par motor, carga (constante o
  cuadrática) e inercia.

El motor de simulación está en `Simulador.Electrico.Engine`, sin interfaz gráfica, y se prueba en
`tests/Simulador.Electrico.Tests` con los mismos esquemas de ejemplo.

## Aparatos reales de fabricante

Cada componente de la librería admite referencias reales con sus datos de placa. Se cargan desde archivos CSV:

```
Fabricante;Referencia;Tipo;Descripción;Parámetros
Schneider Electric;LC1D09P7;contactos_principales;TeSys D 9 A AC-3 (4 kW 400 V);In=9
Siemens;3RV2011-1JA10;guardamotor;SIRIUS 3RV2 7-10 A;Ir=9 Im=13
```

- *Tipo* es el identificador del componente en la librería (`bobina_contactor`, `guardamotor`, `rele_termico`,
  `motor_3f`, `magnetotermico_3p`…). *Parámetros* son `clave=valor` con las claves de ese componente (los errores dicen
  cuáles son válidas).
- Se incluye `src/Simulador.Electrico/Catalogos/fabricantes-ejemplo.csv` con referencias habituales de Schneider,
  Siemens, ABB y motores IE3 típicos. Son valores de catálogo para practicar: compruébalos en la hoja de datos.
- **Importar catálogo CSV…** añade más archivos; se copian a la carpeta de datos del usuario y se cargan al arrancar.
  Sirve para pasar tablas copiadas de catálogos o exportadas desde Excel.
- Al seleccionar un componente aparece **Aparato de catálogo** para aplicar una referencia.

## Próximos pasos

1. Importar símbolos **DXF** y bloques de EPLAN.
2. Más aparatos: motor Dahlander, arrancador suave, relé de control de fases, interruptor horario, crepuscular,
   detector de movimiento, generador.
3. Vista de esquema multihoja con referencias cruzadas de contactos y exportación a PDF.
4. Corriente en lazos de cables: hoy, si los cables de una misma red forman un lazo, el cable que lo cierra no muestra
   puntos.
