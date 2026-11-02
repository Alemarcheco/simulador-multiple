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
5. **Fabricación**: Gerber RS-274X, taladros Excellon, e impresión 1:1 en espejo (PDF) para transferencia térmica.
6. **Más adelante**: autorruteo, vista 3D, simulación de microcontroladores.
