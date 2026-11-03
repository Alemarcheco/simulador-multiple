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
| `Simulador.Core.CoSim` | `ISimComponent`, `ISimComponentFactory`, `SimContext` | Entorno combinado |
| `Simulador.Core.Pcb` | `BoardExport` (contorno, taladros, componentes de una placa) | Circuitos y PCB |
| `Simulador.Core.Libraries` | Importadores KiCad/SPICE y modelo neutro de símbolos, huellas y modelos | Circuitos y PCB |
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

## Módulo Diseño 3D (plan)

Proyectos: `Simulador.Cad3D.Geometry` (sin interfaz: documento paramétrico, motor de sólidos, STL/3MF, plantillas)
y `Simulador.Cad3D` (pestaña, visor 3D, panel de propiedades). Pruebas en `tests/Simulador.Cad3D.Tests`.

1. **Prototipo** (hecho): piezas con historial de operaciones (caja, caja redondeada, cilindro/cono, esfera,
   extrusión de perfil) que suman, restan o intersecan; visor 3D por software; proyecto `.cad3d` (JSON);
   exportación STL binario y 3MF (una pieza por objeto, con color); plantilla de caja con tapa y caja a medida
   de una `BoardExport`. El motor actual (`MeshKernel`, árboles BSP en C#) da mallas cerradas.
2. **Motor exacto**: `IGeometryKernel` con OpenCASCADE (envoltorio nativo) para redondeos, chaflanes, vaciados
   e importación/exportación STEP; visor OpenGL.
3. **Bocetos 2D** con restricciones y cotas, revolución, patrones y simetrías.
4. **Integración**: caja desde la placa del módulo de circuitos con un clic; exportar equipos animables `.sm3d`
   para el banco de pruebas (contrato `Simulador.Core.Model3D`).
