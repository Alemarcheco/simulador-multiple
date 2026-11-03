using Simulador.Plc.Runtime.Ladder;

namespace Simulador.Plc;

/// <summary>Prácticas de ejemplo. Usan direcciones IEC (%IX0.0) para funcionar con cualquier marca.</summary>
internal static class Examples
{
    public sealed record StExample(string Name, string Source);

    public sealed record LadderExample(string Name, LadderProgram Program);

    public static IReadOnlyList<StExample> StructuredText { get; } =
    [
        new("Marcha-paro con enclavamiento", """
            PROGRAM MarchaParo
            VAR
                Marcha AT %IX0.0 : BOOL;   (* pulsador NA *)
                Paro   AT %IX0.1 : BOOL;   (* pulsador NC: activa la entrada en reposo *)
                Motor  AT %QX0.0 : BOOL;
            END_VAR

            Motor := (Marcha OR Motor) AND Paro;
            END_PROGRAM
            """),
        new("Semáforo", """
            PROGRAM Semaforo
            VAR
                Activar  AT %IX0.0 : BOOL;
                Rojo     AT %QX0.0 : BOOL;
                Amarillo AT %QX0.1 : BOOL;
                Verde    AT %QX0.2 : BOOL;
                Paso : INT := 0;
                T1 : TON;
            END_VAR

            T1(IN := Activar AND NOT T1.Q, PT := SEL(Paso = 2, T#5s, T#2s));
            IF NOT Activar THEN
                Paso := 0;
            ELSIF T1.Q THEN
                Paso := (Paso + 1) MOD 3;
            END_IF;

            Rojo := Activar AND Paso = 0;
            Verde := Activar AND Paso = 1;
            Amarillo := NOT Activar OR Paso = 2;
            END_PROGRAM
            """),
        new("Arranque estrella-triángulo", """
            PROGRAM EstrellaTriangulo
            VAR
                Marcha     AT %IX0.0 : BOOL;
                Paro       AT %IX0.1 : BOOL;   (* NC *)
                Termico    AT %IX0.2 : BOOL;   (* NC del relé térmico *)
                KM1_Linea  AT %QX0.0 : BOOL;
                KM2_Estrella AT %QX0.1 : BOOL;
                KM3_Triangulo AT %QX0.2 : BOOL;
                Tiempo : TON;
            END_VAR

            KM1_Linea := (Marcha OR KM1_Linea) AND Paro AND Termico;
            Tiempo(IN := KM1_Linea, PT := T#6s);
            KM2_Estrella := KM1_Linea AND NOT Tiempo.Q AND NOT KM3_Triangulo;
            KM3_Triangulo := KM1_Linea AND Tiempo.Q AND NOT KM2_Estrella;
            END_PROGRAM
            """),
        new("Contador de piezas en cinta", """
            PROGRAM ContadorPiezas
            VAR
                Sensor  AT %IX0.0 : BOOL;
                Reponer AT %IX0.1 : BOOL;
                Cinta   AT %QX0.0 : BOOL;
                Lleno   AT %QX0.1 : BOOL;
                Piezas : CTU;
            END_VAR

            Piezas(CU := Sensor, R := Reponer, PV := 10);
            Lleno := Piezas.Q;
            Cinta := NOT Piezas.Q;
            %MW0 := Piezas.CV;
            END_PROGRAM
            """),
        new("Depósito con nivel analógico", """
            PROGRAM Deposito
            VAR
                Nivel  AT %IW64 : INT;     (* 0-10 V = 0-27648 *)
                Bomba  AT %QX0.0 : BOOL;
                Alarma AT %QX0.1 : BOOL;
                Porcentaje : REAL;
            END_VAR

            Porcentaje := INT_TO_REAL(Nivel) * 100.0 / 27648.0;
            IF Porcentaje < 20.0 THEN
                Bomba := TRUE;
            ELSIF Porcentaje > 80.0 THEN
                Bomba := FALSE;
            END_IF;
            Alarma := Porcentaje > 95.0;
            END_PROGRAM
            """),
        new("Inversión de giro con enclavamiento", """
            PROGRAM InversionGiro
            VAR
                Derecha   AT %IX0.0 : BOOL;   (* pulsador NA *)
                Izquierda AT %IX0.1 : BOOL;   (* pulsador NA *)
                Paro      AT %IX0.2 : BOOL;   (* NC *)
                Termico   AT %IX0.3 : BOOL;   (* NC *)
                KM1 AT %QX0.0 : BOOL;         (* giro a derechas *)
                KM2 AT %QX0.1 : BOOL;         (* giro a izquierdas *)
            END_VAR

            (* Enclavamiento eléctrico: nunca los dos contactores a la vez *)
            KM1 := (Derecha OR KM1) AND Paro AND Termico AND NOT KM2 AND NOT Izquierda;
            KM2 := (Izquierda OR KM2) AND Paro AND Termico AND NOT KM1 AND NOT Derecha;
            END_PROGRAM
            """),
        new("Puerta de garaje", """
            PROGRAM PuertaGaraje
            VAR
                Mando      AT %IX0.0 : BOOL;   (* pulsador del mando *)
                FC_Abierta AT %IX0.1 : BOOL;   (* final de carrera arriba *)
                FC_Cerrada AT %IX0.2 : BOOL;   (* final de carrera abajo *)
                Fotocelula AT %IX0.3 : BOOL;   (* TRUE = haz cortado *)
                Subir      AT %QX0.0 : BOOL;
                Bajar      AT %QX0.1 : BOOL;
                Luz        AT %QX0.2 : BOOL;
                Estado : INT := 0;             (* 0 cerrada, 1 abriendo, 2 abierta, 3 cerrando *)
                Pulso : R_TRIG;
                Espera : TON;
                LuzOff : TOF;
            END_VAR

            Pulso(CLK := Mando);
            Espera(IN := Estado = 2, PT := T#10s);
            CASE Estado OF
                0: IF Pulso.Q THEN Estado := 1; END_IF;
                1: IF FC_Abierta THEN Estado := 2; END_IF;
                2: IF Pulso.Q OR Espera.Q THEN Estado := 3; END_IF;
                3: IF Fotocelula OR Pulso.Q THEN Estado := 1;
                   ELSIF FC_Cerrada THEN Estado := 0; END_IF;
            END_CASE;
            Subir := Estado = 1;
            Bajar := Estado = 3;
            LuzOff(IN := Estado <> 0, PT := T#30s);
            Luz := LuzOff.Q;
            END_PROGRAM
            """),
        new("Ascensor de 3 plantas", """
            PROGRAM Ascensor
            VAR
                Llamada0 AT %IX0.0 : BOOL;
                Llamada1 AT %IX0.1 : BOOL;
                Llamada2 AT %IX0.2 : BOOL;
                Sensor0  AT %IX0.3 : BOOL;   (* cabina en planta 0 *)
                Sensor1  AT %IX0.4 : BOOL;
                Sensor2  AT %IX0.5 : BOOL;
                Subir    AT %QX0.0 : BOOL;
                Bajar    AT %QX0.1 : BOOL;
                Destino : INT := 0;
                Actual : INT := 0;
            END_VAR

            IF Sensor0 THEN Actual := 0; ELSIF Sensor1 THEN Actual := 1; ELSIF Sensor2 THEN Actual := 2; END_IF;
            IF NOT Subir AND NOT Bajar THEN
                IF Llamada0 THEN Destino := 0; ELSIF Llamada1 THEN Destino := 1; ELSIF Llamada2 THEN Destino := 2; END_IF;
            END_IF;
            Subir := Destino > Actual;
            Bajar := Destino < Actual;
            %MW0 := Actual;   (* display de planta *)
            END_PROGRAM
            """),
        new("Alternancia de dos bombas", """
            PROGRAM DosBombas
            VAR
                Demanda AT %IX0.0 : BOOL;     (* presostato: pide agua *)
                FalloB1 AT %IX0.1 : BOOL;
                FalloB2 AT %IX0.2 : BOOL;
                Bomba1  AT %QX0.0 : BOOL;
                Bomba2  AT %QX0.1 : BOOL;
                Turno : BOOL := FALSE;        (* FALSE = le toca a la 1 *)
                Fin : F_TRIG;
            END_VAR

            (* Cada vez que termina un ciclo de demanda se cambia de bomba, para igualar el desgaste *)
            Fin(CLK := Demanda);
            IF Fin.Q THEN Turno := NOT Turno; END_IF;
            Bomba1 := Demanda AND NOT FalloB1 AND (NOT Turno OR FalloB2);
            Bomba2 := Demanda AND NOT FalloB2 AND (Turno OR FalloB1);
            END_PROGRAM
            """),
    ];

    public static IReadOnlyList<StExample> InstructionList { get; } =
    [
        new("Marcha-paro (IEC)", """
            (* Lista de instrucciones IEC 61131-3. El acumulador guarda el resultado actual. *)
            LD   %IX0.0     (* marcha *)
            OR   %QX0.0     (* realimentación *)
            AND  %IX0.1     (* paro NC *)
            ST   %QX0.0     (* motor *)
            """),
        new("Marcha-paro (estilo AWL de Siemens)", """
            // U = Y, O = O, UN = Y negado, = asigna
            U    %IX0.0
            O    %QX0.0
            U    %IX0.1
            =    %QX0.0
            """),
        new("Temporizador y contador", """
            VAR
                T1 : TON;
                C1 : CTU;
            END_VAR
            CAL  T1(IN := %IX0.0, PT := T#3s)   (* retardo a la conexión *)
            LD   T1.Q
            ST   %QX0.0
            CAL  C1(CU := %IX0.1, R := %IX0.2, PV := 5)
            LD   C1.Q
            ST   %QX0.1
            LD   C1.CV
            ST   %MW0
            """),
        new("Saltos: modo manual / automático", """
                     LD    %IX0.0      (* selector: TRUE = manual *)
                     JMPC  Manual
                     LD    %IX0.1      (* automático: sensor *)
                     ST    %QX0.0
                     JMP   Fin
            Manual:  LD    %IX0.2      (* manual: pulsador *)
                     ST    %QX0.0
            Fin:     LD    %IX0.0
                     ST    %QX0.1      (* piloto de modo manual *)
            """),
        new("Comparación con analógica", """
            LD   %IW64       (* 0-10 V = 0-27648 *)
            GT   13824       (* más de 5 V *)
            ST   %QX0.0
            LD   %IW64
            LT   2765        (* menos de 1 V *)
            ST   %QX0.1
            """),
    ];

    public static IReadOnlyList<StExample> Sfc { get; } =
    [
        new("Depósito: llenar, mezclar y vaciar", """
            PROGRAM Deposito
            VAR
                Marcha   AT %IX0.0 : BOOL;
                Lleno    AT %IX0.1 : BOOL;   (* boya de máximo *)
                Vacio    AT %IX0.2 : BOOL;   (* boya de mínimo *)
                Valvula  AT %QX0.0 : BOOL;
                Agitador AT %QX0.1 : BOOL;
                Desague  AT %QX0.2 : BOOL;
                Lotes : INT;
            END_VAR

            INITIAL_STEP Reposo: END_STEP
            STEP Llenar:  Valvula(N); END_STEP
            STEP Mezclar: Agitador(N); ContarLote(P); END_STEP
            STEP Vaciar:  Desague(N); END_STEP

            TRANSITION FROM Reposo  TO Llenar  := Marcha; END_TRANSITION
            TRANSITION FROM Llenar  TO Mezclar := Lleno; END_TRANSITION
            TRANSITION FROM Mezclar TO Vaciar  := Mezclar.T >= T#5s; END_TRANSITION
            TRANSITION FROM Vaciar  TO Reposo  := Vacio; END_TRANSITION

            ACTION ContarLote:
                Lotes := Lotes + 1;
            END_ACTION
            END_PROGRAM
            """),
        new("Taladro: bajar, taladrar y subir", """
            VAR
                Pieza  AT %IX0.0 : BOOL;   (* pieza colocada *)
                FC_Arriba AT %IX0.1 : BOOL;
                FC_Abajo  AT %IX0.2 : BOOL;
                Bajar  AT %QX0.0 : BOOL;
                Subir  AT %QX0.1 : BOOL;
                Broca  AT %QX0.2 : BOOL;
            END_VAR

            INITIAL_STEP E0: END_STEP
            STEP E1: Broca(S); Bajar(N); END_STEP
            STEP E2: Bajar(L, T#2s); END_STEP     (* taladra 2 s en el fondo *)
            STEP E3: Broca(R); Subir(N); END_STEP

            TRANSITION FROM E0 TO E1 := Pieza AND FC_Arriba; END_TRANSITION
            TRANSITION FROM E1 TO E2 := FC_Abajo; END_TRANSITION
            TRANSITION FROM E2 TO E3 := E2.T >= T#2s; END_TRANSITION
            TRANSITION FROM E3 TO E0 := FC_Arriba; END_TRANSITION
            """),
        new("Dos cilindros en paralelo (divergencia Y)", """
            VAR
                Inicio AT %IX0.0 : BOOL;
                A_Fuera AT %IX0.1 : BOOL;
                B_Fuera AT %IX0.2 : BOOL;
                A_Dentro AT %IX0.3 : BOOL;
                B_Dentro AT %IX0.4 : BOOL;
                CilA AT %QX0.0 : BOOL;
                CilB AT %QX0.1 : BOOL;
            END_VAR

            INITIAL_STEP Reposo: END_STEP
            STEP SaleA: CilA(N); END_STEP
            STEP SaleB: CilB(N); END_STEP
            STEP EsperaA: END_STEP
            STEP EsperaB: END_STEP
            STEP Recoger: END_STEP

            (* Los dos cilindros salen a la vez y se espera a que lleguen ambos *)
            TRANSITION FROM Reposo TO (SaleA, SaleB) := Inicio; END_TRANSITION
            TRANSITION FROM SaleA TO EsperaA := A_Fuera; END_TRANSITION
            TRANSITION FROM SaleB TO EsperaB := B_Fuera; END_TRANSITION
            TRANSITION FROM (EsperaA, EsperaB) TO Recoger := TRUE; END_TRANSITION
            TRANSITION FROM Recoger TO Reposo := A_Dentro AND B_Dentro; END_TRANSITION
            """),
        new("Selección de recetas (divergencia O)", """
            VAR
                RecetaA AT %IX0.0 : BOOL;
                RecetaB AT %IX0.1 : BOOL;
                Fin     AT %IX0.2 : BOOL;
                BombaA  AT %QX0.0 : BOOL;
                BombaB  AT %QX0.1 : BOOL;
                Listo   AT %QX0.2 : BOOL;
            END_VAR

            INITIAL_STEP Espera: END_STEP
            STEP DosificarA: BombaA(N); END_STEP
            STEP DosificarB: BombaB(N); END_STEP
            STEP Terminado: Listo(N); END_STEP

            (* Se elige un camino u otro: la receta A tiene prioridad si se pulsan las dos *)
            TRANSITION FROM Espera TO DosificarA := RecetaA; END_TRANSITION
            TRANSITION FROM Espera TO DosificarB := RecetaB AND NOT RecetaA; END_TRANSITION
            TRANSITION FROM DosificarA TO Terminado := DosificarA.T >= T#3s; END_TRANSITION
            TRANSITION FROM DosificarB TO Terminado := DosificarB.T >= T#5s; END_TRANSITION
            TRANSITION FROM Terminado TO Espera := Fin; END_TRANSITION
            """),
    ];

    public static IReadOnlyList<LadderExample> Ladder { get; } =
    [
        new("Marcha-paro con enclavamiento", new LadderProgram(
            new Rung(
                new Series(
                    new ParallelBranch(new Contact(ContactKind.NormallyOpen, "%IX0.0"), new Contact(ContactKind.NormallyOpen, "%QX0.0")),
                    new Contact(ContactKind.NormallyOpen, "%IX0.1")),
                [new Coil(CoilKind.Normal, "%QX0.0")], "Marcha (NA) y paro (NC) con realimentación del motor"))),
        new("Intermitente con dos temporizadores", new LadderProgram(
            new Rung(new Series(new Contact(ContactKind.NormallyOpen, "%IX0.0"), new Contact(ContactKind.NormallyClosed, "T2.Q")),
                [new TimerBox(TimerKind.TON, "T1", TimeSpan.FromSeconds(1))], "T1 cuenta el tiempo apagado"),
            new Rung(new Contact(ContactKind.NormallyOpen, "T1.Q"),
                [new TimerBox(TimerKind.TON, "T2", TimeSpan.FromSeconds(1))], "T2 cuenta el tiempo encendido"),
            new Rung(new Contact(ContactKind.NormallyOpen, "T1.Q"), [new Coil(CoilKind.Normal, "%QX0.0")]))),
        new("Contador con flanco y comparación", new LadderProgram(
            new Rung(new Contact(ContactKind.NormallyOpen, "%IX0.0"),
                [new CounterBox(CounterKind.CTU, "C1", 5, "%IX0.1")], "Cuenta pulsos de %IX0.0; %IX0.1 pone a cero"),
            new Rung(new CompareContact("C1.CV", ">=", "5"), [new Coil(CoilKind.Normal, "%QX0.0")]),
            new Rung(new Contact(ContactKind.RisingEdge, "%IX0.0"), [new Coil(CoilKind.Set, "%QX0.1")], "Se enclava con el primer pulso"),
            new Rung(new Contact(ContactKind.NormallyOpen, "%IX0.1"), [new Coil(CoilKind.Reset, "%QX0.1")]))),
        new("Arranque estrella-triángulo", new LadderProgram(
            new Rung(
                new Series(
                    new ParallelBranch(new Contact(ContactKind.NormallyOpen, "%IX0.0"), new Contact(ContactKind.NormallyOpen, "%QX0.0")),
                    new Contact(ContactKind.NormallyOpen, "%IX0.1"), new Contact(ContactKind.NormallyOpen, "%IX0.2")),
                [new Coil(CoilKind.Normal, "%QX0.0"), new TimerBox(TimerKind.TON, "T1", TimeSpan.FromSeconds(6))],
                "KM1 línea: marcha %IX0.0, paro NC %IX0.1, térmico NC %IX0.2; T1 tiempo de estrella"),
            new Rung(new Series(new Contact(ContactKind.NormallyOpen, "%QX0.0"), new Contact(ContactKind.NormallyClosed, "T1.Q"), new Contact(ContactKind.NormallyClosed, "%QX0.2")),
                [new Coil(CoilKind.Normal, "%QX0.1")], "KM2 estrella"),
            new Rung(new Series(new Contact(ContactKind.NormallyOpen, "%QX0.0"), new Contact(ContactKind.NormallyOpen, "T1.Q"), new Contact(ContactKind.NormallyClosed, "%QX0.1")),
                [new Coil(CoilKind.Normal, "%QX0.2")], "KM3 triángulo"))),
        new("Inversión de giro con enclavamiento", new LadderProgram(
            new Rung(
                new Series(
                    new ParallelBranch(new Contact(ContactKind.NormallyOpen, "%IX0.0"), new Contact(ContactKind.NormallyOpen, "%QX0.0")),
                    new Contact(ContactKind.NormallyOpen, "%IX0.2"), new Contact(ContactKind.NormallyClosed, "%IX0.1"), new Contact(ContactKind.NormallyClosed, "%QX0.1")),
                [new Coil(CoilKind.Normal, "%QX0.0")], "KM1 derechas (%IX0.0), paro NC %IX0.2, enclavamiento con KM2"),
            new Rung(
                new Series(
                    new ParallelBranch(new Contact(ContactKind.NormallyOpen, "%IX0.1"), new Contact(ContactKind.NormallyOpen, "%QX0.1")),
                    new Contact(ContactKind.NormallyOpen, "%IX0.2"), new Contact(ContactKind.NormallyClosed, "%IX0.0"), new Contact(ContactKind.NormallyClosed, "%QX0.0")),
                [new Coil(CoilKind.Normal, "%QX0.1")], "KM2 izquierdas (%IX0.1)"))),
        new("Semáforo con temporizadores", new LadderProgram(
            new Rung(new Series(new Contact(ContactKind.NormallyOpen, "%IX0.0"), new Contact(ContactKind.NormallyClosed, "T3.Q")),
                [new TimerBox(TimerKind.TON, "T1", TimeSpan.FromSeconds(5))], "Activar con %IX0.0. T1: rojo 5 s"),
            new Rung(new Contact(ContactKind.NormallyOpen, "T1.Q"), [new TimerBox(TimerKind.TON, "T2", TimeSpan.FromSeconds(5))], "T2: verde 5 s"),
            new Rung(new Contact(ContactKind.NormallyOpen, "T2.Q"), [new TimerBox(TimerKind.TON, "T3", TimeSpan.FromSeconds(2))], "T3: ámbar 2 s y vuelve a empezar"),
            new Rung(new Series(new Contact(ContactKind.NormallyOpen, "%IX0.0"), new Contact(ContactKind.NormallyClosed, "T1.Q")), [new Coil(CoilKind.Normal, "%QX0.0")], "Rojo"),
            new Rung(new Series(new Contact(ContactKind.NormallyOpen, "T1.Q"), new Contact(ContactKind.NormallyClosed, "T2.Q")), [new Coil(CoilKind.Normal, "%QX0.2")], "Verde"),
            new Rung(new Contact(ContactKind.NormallyOpen, "T2.Q"), [new Coil(CoilKind.Normal, "%QX0.1")], "Ámbar"))),
        new("Telerruptor (un pulsador enciende y apaga)", new LadderProgram(
            new Rung(new Series(new Contact(ContactKind.RisingEdge, "%IX0.0"), new Contact(ContactKind.NormallyClosed, "%QX0.0")),
                [new Coil(CoilKind.Set, "%MX0.0")], "Cada pulso de %IX0.0 cambia la luz %QX0.0"),
            new Rung(new Series(new Contact(ContactKind.RisingEdge, "%IX0.0"), new Contact(ContactKind.NormallyOpen, "%QX0.0")),
                [new Coil(CoilKind.Reset, "%MX0.0")]),
            new Rung(new Contact(ContactKind.NormallyOpen, "%MX0.0"), [new Coil(CoilKind.Normal, "%QX0.0")]))),
        new("Escalera con minutero", new LadderProgram(
            new Rung(new ParallelBranch(new Contact(ContactKind.NormallyOpen, "%IX0.0"), new Contact(ContactKind.NormallyOpen, "%IX0.1"), new Contact(ContactKind.NormallyOpen, "%IX0.2")),
                [new TimerBox(TimerKind.TOF, "T1", TimeSpan.FromSeconds(60))], "Pulsadores en cada planta; la luz queda 60 s encendida"),
            new Rung(new Contact(ContactKind.NormallyOpen, "T1.Q"), [new Coil(CoilKind.Normal, "%QX0.0")]))),
        new("Llenado de depósito con boyas", new LadderProgram(
            new Rung(new Series(new Contact(ContactKind.NormallyClosed, "%IX0.0"), new Contact(ContactKind.NormallyClosed, "%IX0.2")),
                [new Coil(CoilKind.Set, "%QX0.0")], "Boya de mínimo %IX0.0 sin agua → arranca la bomba (si no hay fallo %IX0.2)"),
            new Rung(new ParallelBranch(new Contact(ContactKind.NormallyOpen, "%IX0.1"), new Contact(ContactKind.NormallyOpen, "%IX0.2")),
                [new Coil(CoilKind.Reset, "%QX0.0")], "Boya de máximo %IX0.1 o fallo → para"),
            new Rung(new Contact(ContactKind.NormallyOpen, "%IX0.2"), [new Coil(CoilKind.Normal, "%QX0.1")], "Piloto de fallo"))),
    ];
}
