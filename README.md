# Simulador múltiple

Aplicación de escritorio para Windows 11 que reúne varios simuladores en una sola ventana, cada uno en su propia pestaña.

Hecha con C# (.NET 8) y [Avalonia](https://avaloniaui.net/). La simulación de circuitos usará [ngspice](https://ngspice.sourceforge.io/).

## Simuladores

| Pestaña | Proyecto | Estado |
|---|---|---|
| ⚡ Circuitos y PCB | `src/Simulador.Circuitos` | Librerías, esquemáticos, simulación (ngspice) y diseño de PCB y salidas de fabricación (PDF para transferencia térmica, Gerber y taladros) |
| 🔌 Instalaciones eléctricas | `src/Simulador.Electrico` | Editor y simulación de mando y potencia: contactores, relés, temporizadores, protecciones, motores e iluminación. Ver [docs/modulo-electrico.md](docs/modulo-electrico.md) |

## Descargar y ejecutar

Cada cambio en GitHub compila la aplicación en Windows. En la pestaña **Actions** del repositorio, abre la última ejecución de "Compilar y probar" y descarga `SimuladorMultiple-win-x64`. Descomprímelo y ejecuta `SimuladorMultiple.exe` (no necesita instalar .NET).

La versión descargable ya incluye `ngspice.dll` (motor de simulación [ngspice](https://ngspice.sourceforge.io/), licencia BSD modificada).

## Compilar en tu equipo

Necesitas el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build
dotnet test
dotnet run --project src/Simulador.App
```

Para simular desde una compilación propia, copia `ngspice.dll` (del paquete `ngspice-XX_dll_64.7z` de
[ngspice](https://sourceforge.net/projects/ngspice/files/ng-spice-rework/), carpeta `Spice64_dll/dll-vs`) junto al
ejecutable, o indica su ruta en la variable de entorno `NGSPICE_LIBRARY_PATH`. En Linux basta con `apt install libngspice0`.

## Estructura

```
src/
  Simulador.Core/            Contratos comunes sin interfaz gráfica: módulos (IModule), señales entre
                             simuladores, co-simulación (ISimComponent) y modelo neutro de placas (Pcb).
  Simulador.Core.Libraries/  Importadores de librerías de componentes (KiCad .kicad_sym/.kicad_mod, SPICE .lib/.mod)
                             a un modelo neutro que puede usar cualquier simulador.
  Simulador.App/             Ventana principal: descubre los módulos y crea una pestaña por cada uno.
  Simulador.Circuitos/       Módulo de circuitos electrónicos y PCB.
  Simulador.Electrico.Engine/ Motor del simulador eléctrico (sin interfaz): librería de componentes, red por fasores,
                             bobinas, temporizadores, protecciones y motor de inducción.
  Simulador.Electrico/       Pestaña de instalaciones eléctricas: editor de esquemas y simulación en tiempo real.
tests/
  Simulador.Core.Tests/      Pruebas de los contratos e importadores.
  Simulador.Electrico.Tests/ Pruebas del simulador eléctrico sobre los esquemas de ejemplo.
docs/
  arquitectura.md            Cómo está organizado y cómo añadir un simulador nuevo.
```
