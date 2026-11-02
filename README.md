# Simulador múltiple

Aplicación de escritorio para Windows 11 que reúne varios simuladores en una sola ventana, cada uno en su propia pestaña.

Hecha con C# (.NET 8) y [Avalonia](https://avaloniaui.net/). La simulación de circuitos usará [ngspice](https://ngspice.sourceforge.io/).

## Simuladores

| Pestaña | Proyecto | Estado |
|---|---|---|
| ⚡ Circuitos y PCB | `src/Simulador.Circuitos` | Importación de librerías lista; esquemático, simulación y PCB en construcción |

## Descargar y ejecutar

Cada cambio en GitHub compila la aplicación en Windows. En la pestaña **Actions** del repositorio, abre la última ejecución de "Compilar y probar" y descarga `SimuladorMultiple-win-x64`. Descomprímelo y ejecuta `SimuladorMultiple.exe` (no necesita instalar .NET).

## Compilar en tu equipo

Necesitas el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build
dotnet test
dotnet run --project src/Simulador.App
```

## Estructura

```
src/
  Simulador.Core/            Contratos comunes sin interfaz gráfica: módulos (IModule), señales entre
                             simuladores, co-simulación (ISimComponent) y modelo neutro de placas (Pcb).
  Simulador.Core.Libraries/  Importadores de librerías de componentes (KiCad .kicad_sym/.kicad_mod, SPICE .lib/.mod)
                             a un modelo neutro que puede usar cualquier simulador.
  Simulador.App/             Ventana principal: descubre los módulos y crea una pestaña por cada uno.
  Simulador.Circuitos/       Módulo de circuitos electrónicos y PCB.
tests/
  Simulador.Core.Tests/      Pruebas de los contratos e importadores.
docs/
  arquitectura.md            Cómo está organizado y cómo añadir un simulador nuevo.
```
