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
    ];
}
