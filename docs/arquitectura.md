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
| `Simulador.Core.Pcb` | `BoardExport` (contorno, taladros, componentes de una placa) | Circuitos y PCB |
| `Simulador.Core.Libraries` | Importadores KiCad/SPICE y modelo neutro de símbolos, huellas y modelos | Circuitos y PCB |

## Unidades

Unidades SI en simulación. Geometría de símbolos, huellas y placas en milímetros.

## Módulo Circuitos y PCB (plan)

1. **Librerías** (hecho): importar símbolos y huellas de KiCad y modelos SPICE de fabricantes.
2. **Esquemático**: colocar símbolos, cablear, etiquetas de red, anotación y comprobación eléctrica.
3. **Simulación**: netlist hacia ngspice (biblioteca compartida `ngspice.dll`), análisis DC/AC/transitorio,
   instrumentos virtuales.
4. **PCB**: importar la netlist, colocar huellas, ratsnest, ruteo manual, planos de masa y comprobación de reglas.
5. **Fabricación**: Gerber RS-274X, taladros Excellon, e impresión 1:1 en espejo (PDF) para transferencia térmica.
6. **Más adelante**: autorruteo, vista 3D, simulación de microcontroladores.

## Co-simulación: cómo avanza el tiempo

`CoSimScheduler` implementa `ISimulationClock`. En cada paso maestro (1 ms):

1. Si hay un evento pedido con `RequestEventAt` antes del final del paso, el paso se acorta para caer justo en él y en ese instante avanzan todos los componentes.
2. Si no, avanza cada componente cuyo turno toca (`StepMultiple`), con `dt` = tiempo desde su último paso.
3. Orden: fase (`Inputs`, `Logic`, `Physics`, `Outputs`) y, dentro de la fase, orden de alta.
4. Tras avanzar un componente, sus `IPowerPort` enlazados se copian al otro lado: el que impone esfuerzo copia el esfuerzo y el que impone flujo copia el flujo.

Mientras los componentes avanzan, `Now` es todavía el inicio del paso. `SimRunner` lo ejecuta en un hilo propio a 0,1×, 1×, 10× o lo más rápido posible, con pausa y paso a paso, y publica `SimFrame` (instantáneas de todos los componentes) para que la vista anime sin tocar la simulación. El ejemplo completo PLC → contactor → motor → bomba → tanque está en `tests/Simulador.Core.Tests/TankFillingExampleTests.cs`.
