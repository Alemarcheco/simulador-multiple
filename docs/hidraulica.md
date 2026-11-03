# Módulo Hidráulica

Pestaña para diseñar gráficamente procesos con depósitos, tanques, tuberías, bombas, válvulas e instrumentos, y
simularlos en el tiempo (caudales, presiones y niveles).

## Uso

1. Elige una herramienta en la paleta y haz clic en el lienzo. Tuberías, bombas y válvulas se trazan con dos clics
   (origen → destino = sentido positivo del caudal); un clic en vacío crea una unión. Clic derecho o Esc: seleccionar.
2. Edita las propiedades a la derecha (mm, L/s, %, rpm). 1 cuadro del lienzo = 1 m al crear una tubería.
3. ▶ Simular. Durante la simulación puedes arrancar o parar bombas, cambiar su velocidad (variador) y mover válvulas.
4. Guarda el proyecto (`.hidx`, JSON) y exporta el informe (`.md`) con lista de materiales y resultados.

## Cálculo

- Red resuelta en cada instante con el método del gradiente (Todini–Pilati, Newton global).
- Tuberías: Darcy–Weisbach con Swamee–Jain (turbulento), Hagen–Poiseuille (laminar) y pérdidas locales ΣK.
- Bombas: curva H = H0 − r·Q² escalada con las leyes de semejanza; antirretorno incorporado; par = ρ·g·Q·H / (η·ω).
- Válvulas: K / apertura²; tiempo de recorrido; antirretorno.
- Tanques: el nivel se integra con subpasos que no cambian el nivel más del 1 %; rebose al llenarse; un tanque vacío
  no entrega agua.
- Pendiente (fase 2): golpe de ariete, tanques presurizados, neumática, cilindros, descarga libre por orificio.

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
