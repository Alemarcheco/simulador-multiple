# Módulo Hidráulica

Pestaña para diseñar gráficamente procesos con depósitos, tanques, tuberías, bombas, válvulas e instrumentos, y
simularlos en el tiempo (caudales, presiones y niveles).

## Uso

1. Elige una herramienta en la paleta y haz clic en el lienzo. Tuberías, bombas y válvulas se trazan con dos clics
   (origen → destino = sentido positivo del caudal); un clic en vacío crea una unión. Clic derecho o Esc: seleccionar.
2. Edita las propiedades a la derecha (mm, L/s, %, rpm). 1 cuadro del lienzo = 1 m al crear una tubería.
3. ▶ Simular. Durante la simulación puedes arrancar o parar bombas, cambiar su velocidad (variador) y mover válvulas.
4. «Ejemplos ▾» abre diseños ya hechos: bombeo a tanque elevado, trasvase por gravedad, dos bombas en paralelo,
   red de distribución en malla y control de nivel con electroválvula (preparado para el PLC).
5. La pestaña «Vista 3D» muestra la instalación en 3D (cotas reales, planta según el esquema) y se anima durante la
   simulación: nivel de los tanques, bandas que avanzan con el caudal, ventiladores de las bombas y color de las
   válvulas según su apertura. Ratón: izquierdo gira, derecho desplaza, rueda acerca, doble clic encuadra.
6. Guarda el proyecto (`.hidx`, JSON) y exporta el informe (`.md`) con lista de materiales y resultados.

## Cálculo

- Red resuelta en cada instante con el método del gradiente (Todini–Pilati, Newton global).
- Tuberías: Darcy–Weisbach con Swamee–Jain (turbulento), Hagen–Poiseuille (laminar) y pérdidas locales ΣK.
- Bombas: curva H = H0 − r·Q² escalada con las leyes de semejanza; antirretorno incorporado; par = ρ·g·Q·H / (η·ω).
- Válvulas: K / apertura²; tiempo de recorrido; antirretorno.
- Tanques: el nivel se integra con subpasos que no cambian el nivel más del 1 %; rebose al llenarse; un tanque vacío
  no entrega agua.
- Pendiente (fase 2): golpe de ariete, tanques presurizados, neumática, cilindros, descarga libre por orificio.

## Vista 3D

`View3D/` es un rasterizador por software con búfer de profundidad (cada píxel muestra la superficie más cercana), así
que la imagen no se deforma al girar, como pasaría al ordenar triángulos. `SceneBuilder` genera la escena desde el
modelo y el estado de la simulación; las tuberías van en horizontal a la cota de salida y suben o bajan en vertical al
llegar a su destino.

## Banco de pruebas combinado

`CoSim/HydraulicCoSim.cs` implementa el contrato de co-simulación:

- `RedHidraulica` (ISimComponent, fase Physics, cada 10 ms) resuelve una red entera; `HydraulicPart` es cada pieza,
  con su `Snapshot()` (claves en `HydraulicKeys`).
- Eje de bomba `B1.Eje` (MechanicalRotational, ImposesEffort): el motor impone la velocidad (Flow, rad/s) y la bomba
  devuelve el par (Effort, N·m). Si `ShaftConnected` es false, manda la señal `B1.Marcha` (DI Bool).
- Válvula de control `V1.Consigna` (AI Percent), electroválvula `EV1.Bobina` (DI Bool), instrumentos `LT1.Medida`
  (AO en Pascal, Meter o CubicMeterPerSecond).
- `HydraulicComponentFactory` ofrece el catálogo; `RedHidraulica.Assemble` agrupa piezas sueltas unidas por sus bocas.
- `FluidProperties.Viscosity` del contexto se interpreta como viscosidad dinámica (Pa·s).
