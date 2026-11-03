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
  fusible fundido para reponerlo. Durante la simulación puedes cambiar la carga del motor seleccionado.
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
| Motores | Motor de inducción trifásico de 3 bornes (estrella o triángulo interno) y de 6 bornes |
| Medida | Voltímetro, amperímetro, borne de conexión |

Ejemplos incluidos (menú **Ejemplos**): arranque directo con autorretención, inversión de giro con enclavamiento,
estrella-triángulo, luz conmutada desde tres puntos, telerruptor y minutero de escalera.

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

1. Importar símbolos de **QElectroTech** (`.elmt`, miles de símbolos libres) asociándolos a un comportamiento de la
   librería, y de **DXF**.
2. Bornes de E/S hacia el módulo PLC/Arduino (`ISignalPort`, contrato `bus-senales.md`): entradas que miden la tensión
   real del borne y salidas a relé o transistor como contactos del circuito.
3. Componente "red eléctrica" para el banco de pruebas combinado (`ISimComponent`, contrato `co-simulacion.md`), con los
   ejes de los motores como `IPowerPort`.
4. Más aparatos: motor monofásico con condensador, motor Dahlander, arrancador suave y variador de frecuencia básicos,
   transformador de mando, relé de control de fases, interruptor horario, crepuscular, detector de movimiento.
5. Vista de esquema multihoja con referencias cruzadas de contactos y exportación a PDF.
