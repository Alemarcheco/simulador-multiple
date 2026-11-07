# Arquitectura

## Idea general

Una sola aplicación (`Simulador.App`) con una pestaña por simulador. Cada simulador es un proyecto independiente
(`Simulador.<Nombre>`) que implementa `Simulador.Core.Modules.IModule`. La aplicación busca al arrancar todos los
`Simulador.*.dll` que hay junto al ejecutable y crea una pestaña por cada `IModule` que encuentra, ordenadas por `Order`.
La vista de cada pestaña se crea la primera vez que se abre, para que el arranque sea rápido.

`Simulador.Core` no depende de Avalonia: así los contratos y la co-simulación se pueden probar sin ventana.
Por eso `IModule.CreateRootView` devuelve `object`; en la práctica debe ser un `Avalonia.Controls.Control`.

## Añadir un simulador

1. Crea `src/Simulador.<Nombre>/Simulador.<Nombre>.csproj` con referencias a `Avalonia` (sin versión: se gestiona en
   `Directory.Packages.props`) y a `Simulador.Core`.
2. Implementa `IModule` en una clase pública con constructor sin parámetros. Usa un `Id` en minúsculas y un `Order`
   libre (Circuitos usa 10).
3. Añade `<ProjectReference>` a tu proyecto en `src/Simulador.App/Simulador.App.csproj` y `dotnet sln add` tu proyecto.
4. Las pruebas van en `tests/`.

## Contratos compartidos

Los contratos entre simuladores se acuerdan entre los responsables de cada módulo antes de cambiarlos:

| Espacio de nombres | Qué contiene | Responsable |
|---|---|---|
| `Simulador.Core.Modules` | `IModule`, `IModuleHost`, `ModuleLoader` | Circuitos y PCB |
| `Simulador.Core.Signals` | `ISignalPort`, `IPowerPort`, `ISignalBus`, `ISimulationClock` | PLC y Arduino |
| `Simulador.Core.CoSim` | `ISimComponent`, `ISimComponentFactory`, `SimContext`, `CoSimScheduler` (reloj común y planificador), `SimRunner` (ejecución a velocidad controlada), `PowerPort` | Entorno combinado |
| `Simulador.Core.Rendering` | `OrbitCamera` (perspectiva u ortográfica, encuadre fijo al girar), `ZBufferRasterizer` (búfer de profundidad por software, transparencias, líneas, supermuestreo opcional) | Edificación e Hidráulica |
| `Simulador.Core.Pcb` | `BoardExport` (contorno, taladros, componentes de una placa) | Circuitos y PCB |
| `Simulador.Core.Libraries` | Importadores KiCad/SPICE y modelo neutro de símbolos, huellas y modelos | Circuitos y PCB |
| `Simulador.Core.Building` | `IBuildingModel`: niveles, muros, huecos, losas, habitaciones, elementos colocados (`PlacedFixture`) y rutas (`Route`) | Edificación |
| `Simulador.Core.Model3D` | `AnimatedModel` (piezas móviles y conectores para la vista animada) | Diseño 3D |

## Unidades

Unidades SI en simulación. Geometría de símbolos, huellas y placas en milímetros.

## Módulo Circuitos y PCB (plan)

1. **Librerías** (hecho): importar símbolos y huellas de KiCad y modelos SPICE de fabricantes.
2. **Esquemático** (hecho): colocar símbolos, cablear, etiquetas de red, girar/espejo, deshacer, guardar (.esquema) y netlist SPICE. Pendiente: comprobación de reglas eléctricas.
3. **Simulación** (hecho): netlist hacia ngspice (biblioteca compartida `ngspice.dll`, `Simulation/NgSpice.cs`),
   punto de trabajo con tensiones sobre el esquema, transitorio (osciloscopio) y respuesta en frecuencia (Bode).
4. **PCB** (hecho): actualizar desde el esquema (huellas y redes), colocar y girar, ratsnest, ruteo manual con
   esquinas a 45°, reglas para placa casera (pista 0,8 mm, separación 0,5 mm) y DRC. Archivo `.placa` (JSON).
   Huellas básicas incluidas en `Resources/Basicos.pretty` con pads grandes. Pendiente: planos de masa, dos caras con vías.
5. **Fabricación** (`Fabrication/`): PDF 1:1 para transferencia térmica (el cobre inferior sin espejo, las capas superiores en espejo; regla de 50 mm para comprobar la escala), Gerber RS-274X X2 y taladros Excellon en un .zip. `Artwork` genera las formas de cada capa, que comparten las tres salidas.
6. **Más adelante**: autorruteo, vista 3D, simulación de microcontroladores.

## Co-simulación: cómo avanza el tiempo

`CoSimScheduler` implementa `ISimulationClock`. En cada paso maestro (1 ms):

1. Si hay un evento pedido con `RequestEventAt` antes del final del paso, el paso se acorta para caer justo en él y en ese instante avanzan todos los componentes.
2. Si no, avanza cada componente cuyo turno toca (`StepMultiple`), con `dt` = tiempo desde su último paso.
3. Orden: fase (`Inputs`, `Logic`, `Physics`, `Outputs`) y, dentro de la fase, orden de alta.
4. Tras avanzar un componente, sus `IPowerPort` enlazados se copian al otro lado: el que impone esfuerzo copia el esfuerzo y el que impone flujo copia el flujo.

Mientras los componentes avanzan, `Now` es todavía el inicio del paso. `SimRunner` lo ejecuta en un hilo propio a 0,1×, 1×, 10× o lo más rápido posible, con pausa y paso a paso, y publica `SimFrame` (instantáneas de todos los componentes) para que la vista anime sin tocar la simulación. El ejemplo completo PLC → contactor → motor → bomba → tanque está en `tests/Simulador.Core.Tests/TankFillingExampleTests.cs`.

## Módulo Edificación (`Simulador.Civil`, Order 15)

Diseño civil básico (sin cálculo estructural) para ubicar sobre él las instalaciones de los demás módulos.
Contrato: `Simulador.Core.Building` (ver `contratos/edificio.md` en los archivos del proyecto).

- **Hecho (fase 1)**: niveles con cota y altura; muros con espesor, altura y material; puertas y ventanas alojadas en
  muros; losas y cubiertas; escaleras rectas, en L y en U con descanso y cálculo de peldaños (2C + H ≈ 63 cm); detección automática de
  habitaciones con nombre, uso, área y perímetro; copiar planta encima (planta tipo); planta 2D editable con imán y
  ortogonal; vista 3D ortográfica con el rasterizador común de `Simulador.Core.Rendering` (`Rendering/BuildingRenderer.cs`); plantillas (casa, casa de dos
  plantas, hotel, nave industrial, casa con instalación eléctrica e hidráulica, edificio de apartamentos, oficina); colocar
  tomacorrientes, interruptores, luminarias, tableros, puntos de agua, desagües y tanques; rutas por el techo con
  longitud y desnivel reales; comprobaciones (huecos que no caben, elementos dentro de un hueco o tapados por una
  puerta, escaleras incómodas, habitaciones pequeñas); deshacer/rehacer; archivo `.edif.json`.
- **Asistente** (`Model/Advisor.cs`, pestaña Asistente): reglas editables y guardadas con el proyecto (tomas por
  perímetro y mínimos por uso, luminarias, interruptores, ventanas ≥ 1/8 del piso, agua por persona y tanque elevado,
  presión mínima, distancia de evacuación); botón Aplicar que coloca lo que falta (tomas, luces, interruptores,
  tanque, puntos de agua, tablero y rutas); materiales y presupuesto con precios editables y copia para Excel.
- **Calco** (`Import/DxfReader.cs`, `Model/Underlay.cs`, `Import/WallDetector.cs`): planos de referencia por nivel,
  DXF de texto (líneas, polilíneas con arcos, círculos, arcos, elipses y bloques; unidad de `$INSUNITS` o deducida
  del tamaño) o imagen escaneada; opacidad, capas, escalar con una medida conocida, mover y girar; imán a los
  extremos de sus líneas al dibujar; crear muros desde las parejas de líneas paralelas del DXF (prefiere las capas
  con nombre de muro). Los calcos se guardan en el proyecto (las imágenes, por ruta) y no entran en deshacer.
- **Exportar plano** (`Export/PlanDrawing.cs`, `Export/PlanExport.cs`, `Export/PdfCanvas.cs`): cada nivel se convierte
  en primitivas 2D en metros por capas (MUROS, HUECOS, ESCALERAS, HABITACIONES, COTAS, ELECTRICIDAD, HIDRAULICA) con
  cotas totales y de fachada. Sale a DXF R12 en metros (ANSI 1252, un nivel al lado de otro) o a PDF vectorial a
  escala normalizada (automática o elegida) con cajetín y barra de escala, una hoja por nivel, sin dependencias.
- **IFC** (`Export/IfcExport.cs`): el edificio entero a IFC4 (STEP de texto, en metros) para programas BIM: plantas,
  muros con material, huecos (IfcOpeningElement) con su puerta o ventana, forjados y cubierta, escaleras (recta, L, U),
  habitaciones con superficie (Qto_SpaceBaseQuantities) e instalaciones como IfcBuildingElementProxy con la designación
  del banco de pruebas. GlobalId estables derivados del id de cada elemento. Comprobado con IfcOpenShell (0 errores).
- **Recorrido en primera persona** (`Model/Walkthrough.cs`, botón «Recorrer por dentro» de la vista 3D): se empieza
  delante de la puerta de entrada; W A S D o flechas para andar, ratón para mirar, Esc para salir. `Walker` choca con
  muros y ventanas (cuerpo de 25 cm de radio, deslizando), pasa por las puertas, sube y baja escaleras peldaño a
  peldaño y cambia de planta. Los forjados se dibujan con el hueco de la escalera que llega desde abajo
  (`BuildingMesh.StairWellCut`, `Geo.SubtractConvex`).
- **Soleamiento** (`Model/SunStudy.cs`, `Rendering/SunShadows.cs`, botón «Sol y sombras» de la vista 3D): posición
  del sol con el algoritmo de la NOAA (comprobado contra el SPA del NREL con pvlib), salida, puesta y horas de sol.
  El lugar (ciudad o coordenadas, UTC) y el norte de la planta se guardan con el edificio (`BuildingDocument.Site`).
  Sombras con mapa de profundidad visto desde el sol, suavizado 3×3 y separación según la normal; el rasterizador
  común tiene para ello `PixelLight` (punto y normal de cada píxel) y `DepthAt`. Terreno alrededor que recibe las sombras.
- **Siguiente**: lo que proponga el asistente o pida el usuario.
- **Más adelante**: IFC (xBIM), recorrido en primera persona, soleamiento, simulación de evacuación.

## Módulo Diseño 3D

Proyectos: `Simulador.Cad3D.Geometry` (sin interfaz: documento paramétrico, motor de sólidos, STL/3MF, plantillas)
y `Simulador.Cad3D` (pestaña, visor 3D, panel de propiedades). Pruebas en `tests/Simulador.Cad3D.Tests`.

1. **Prototipo** (hecho): piezas con historial de operaciones (caja, caja redondeada, cilindro/cono, esfera,
   extrusión de perfil) que suman, restan o intersecan; visor 3D por software; proyecto `.cad3d` (JSON);
   exportación STL binario y 3MF (una pieza por objeto, con color); plantilla de caja con tapa y caja a medida
   de una `BoardExport`. El motor actual (`MeshKernel`, árboles BSP en C#) da mallas cerradas.
2. **Motor exacto** (hecho): `OcctKernel` (`IExactKernel`) sobre OpenCASCADE mediante el envoltorio nativo
   `native/simcad_occt`: geometría exacta, redondeos, chaflanes, vaciados, revoluciones e importación/exportación
   STEP. Si falta la biblioteca nativa, la app usa `MeshKernel`. Tras redondeos y chaflanes se repara la forma
   (`heal`: costura y `ShapeFix_Solid`) y el teselado pasa por `MeshRepair.FixTJunctions`.
3. **Fase 2** (hecho):
   - Visor OpenGL (`GlViewport3D`, `OpenGlControlBase`) con cámara compartida (`OrbitCamera`) y respaldo por
     software (`Viewport3D`) si OpenGL no arranca en 3 s o con `CAD3D_SOFTWARE_VIEW=1`; lo elige `ViewportHost`.
   - Editor de bocetos 2D (`SketchDialog`, lógica en `Sketch2D`): rejilla con ajuste, añadir/mover/borrar puntos,
     cotas de cada tramo editables, rectángulo/polígono/círculo y validación (cruces, área, radio negativo).
   - Arcos en los bocetos: cada tramo del perfil lleva un abombamiento (`Bulges`, en el archivo `bulges`, como el *bulge* de DXF:
     tan(ángulo/4), 0 = recto). Se convierte en arco arrastrando el cuadradito central del tramo o escribiendo su
     radio; «Redondear esquina» mete un arco tangente; rectángulo con esquinas redondeadas y círculo exacto.
     El motor recibe el perfil dividido con `Sketch2D.Tessellate` (error de cuerda 0,01 mm). Los `.cad3d` sin
     `Bulges` se abren con todos los tramos rectos.
   - Repeticiones y simetría (`Model/PatternFeatures.cs`): `PatternFeature` repite las últimas N operaciones con
     forma propia (en fila, en rejilla o en círculo alrededor de Z) y cada copia suma o resta como su original;
     no repite copias de otra repetición. `MirrorFeature` (modificador, funciona con los dos motores) une la pieza
     con su imagen en un espejo perpendicular a X, Y o Z.
   - Ejemplos prediseñados (`Templates/Examples.cs`): cajas, soporte en L, engranaje, maceta, separador, atril.
   - «Caja para placa…»: abre un `.placa`, lo convierte con `PcbBoardExport.ToBoardExport` (Simulador.Circuitos,
     Y hacia arriba) y crea la caja con `EnclosureTemplate.FromBoard` (postes en los taladros de montaje,
     huecos para conectores). Si el componente trae su huella (`BoardComponent.BoundsMin/BoundsMax`), el hueco
     mide lo que la huella más 0,5 mm de holgura por lado y va en la pared más cercana; si no, 12 mm genéricos.
   - Texto en relieve o grabado (`Model/TextFeature.cs`, tipo `texto`): contornos de la fuente del sistema con
     SkiaSharp (`TextOutline`), altura de mayúscula en mm, negrita y alineación; las letras con agujeros (o, A, 8)
     se extruyen con `IGeometryKernel.ExtrudeRegion`. Con el motor de malla, grabar varias letras puede dejar
     grietas; con OpenCASCADE sale cerrado.
4. **Pendiente**: restricciones de boceto (paralelo, perpendicular, igual), arcos exactos en OpenCASCADE (ahora
   llegan divididos en tramos),
   texto sobre caras curvas, ver los modelos STEP de los componentes
   dentro de la caja y exportar equipos animables `.sm3d` (contrato `Simulador.Core.Model3D`).

## Banco de pruebas (`Simulador.Banco`)

Pestaña que junta piezas de varios módulos en una sola simulación con el reloj común (`CoSimScheduler` + `SimRunner`):

- **Montajes** (`Scenarios/`): llenado de tanque (PLC real en texto estructurado → contactor con térmico → motor → bomba y red hidráulica real del módulo Hidráulica → tanque con transmisor de nivel), turbogenerador en isla con regulador PI y turbocompresor con depósito de aire regulado por el PLC.
- **Equipos genéricos** (`Generic/`): red trifásica, contactor con relé térmico, motor asíncrono (Kloss), generador síncrono en isla, carga eléctrica, turbina (vapor, gas o hidráulica) con limitador y disparo por sobrevelocidad, compresor, depósito de aire, transmisores de señal y regulador PI. Son modelos sencillos con parámetros de placa; cuando cada módulo publique su `ISimComponentFactory` se usan los suyos.
- **Vista** (`Ui/`): animación 2D, marcha/pausa/paso/velocidad, mandos y fallos, valores, lista de conexiones (bornes, cables, ejes, tuberías), programa del PLC y tendencias. Los cambios desde la interfaz pasan por `SimRunner.Post`, que los ejecuta en el hilo de simulación.
