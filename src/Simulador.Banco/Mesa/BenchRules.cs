using Simulador.Core.Signals;

namespace Simulador.Banco.Mesa;

/// <summary>Qué se puede unir con qué en la mesa, con el motivo en palabras sencillas cuando no se puede.</summary>
public static class BenchRules
{
    public static string KindName(PortDef p) => p.Kind switch
    {
        PortKind.SignalIn => "una entrada de señal",
        PortKind.SignalOut => "una salida de señal",
        PortKind.Pipe => "una boca de agua",
        PortKind.Tap => "la toma de un transmisor",
        PortKind.Probe => "una punta de medida",
        _ => p.Domain switch
        {
            PowerDomain.Electrical3Ph or PowerDomain.ElectricalDC => "un borne de potencia eléctrica",
            PowerDomain.MechanicalRotational => "un eje",
            PowerDomain.MechanicalLinear => "un vástago",
            PowerDomain.Pneumatic => "una conexión de aire comprimido",
            PowerDomain.Hydraulic => "una brida hidráulica",
            _ => "un borne de potencia",
        },
    };

    /// <summary>Lo que hace un borne de potencia, para explicar los choques de causalidad.</summary>
    private static string Role(PortDef p) => (p.Domain, p.Causality) switch
    {
        (PowerDomain.Electrical3Ph or PowerDomain.ElectricalDC, Causality.ImposesEffort) => "da tensión",
        (PowerDomain.Electrical3Ph or PowerDomain.ElectricalDC, _) => "consume corriente",
        (PowerDomain.MechanicalRotational or PowerDomain.MechanicalLinear, Causality.ImposesFlow) => "mueve el eje",
        (PowerDomain.MechanicalRotational or PowerDomain.MechanicalLinear, _) => "frena el eje (es una carga)",
        (_, Causality.ImposesEffort) => "fija la presión",
        _ => "da caudal",
    };

    /// <summary>
    /// null si se puede unir el borne A con el B; si no, el motivo. Tiene en cuenta lo que ya hay conectado.
    /// </summary>
    public static string? CanConnect(BenchProject project, string partA, string portA, string partB, string portB)
    {
        if (partA == partB && portA == portB) return "No puedes unir un borne consigo mismo.";
        var pa = project.Part(partA);
        var pb = project.Part(partB);
        if (pa is null || pb is null) return "Esa pieza ya no está en la mesa.";
        var ta = BenchCatalog.Get(pa.Type);
        var tb = BenchCatalog.Get(pb.Type);
        if (ta is null || tb is null) return "Pieza de un tipo desconocido.";
        var a = ta.Port(pa, portA);
        var b = tb.Port(pb, portB);
        if (a is null || b is null) return "Ese borne no existe.";
        if (project.Wires.Any(w => (w.Touches(partA, portA) && w.Other(partA, portA) == (partB, portB))))
            return "Esos dos bornes ya están unidos.";
        return Pair(a, $"{partA}.{a.Text}", b, $"{partB}.{b.Text}") ?? FanIn(project, partA, a, partB, b);
    }

    /// <summary>Compatibilidad de dos bornes, sin mirar el resto del montaje.</summary>
    public static string? Pair(PortDef a, string nameA, PortDef b, string nameB)
    {
        if (a.Kind == PortKind.Probe && b.Kind == PortKind.Probe) return "Las puntas de medida se ponen en un borne de una pieza, no en otra punta.";
        if (a.Kind == PortKind.Probe || b.Kind == PortKind.Probe)
        {
            var other = a.Kind == PortKind.Probe ? b : a;
            return other.Kind == PortKind.Tap ? "Mide en la salida «Medida» del transmisor o en la pieza misma, no en su toma." : null;
        }
        if (a.Kind == PortKind.Tap || b.Kind == PortKind.Tap)
        {
            var other = a.Kind == PortKind.Tap ? b : a;
            return other.Kind == PortKind.Pipe ? null : "La toma de un transmisor hidráulico se une a una boca de agua (tanque, depósito, tubería…).";
        }
        if (a.Kind == PortKind.Pipe && b.Kind == PortKind.Pipe) return null;
        bool sa = a.Kind is PortKind.SignalIn or PortKind.SignalOut, sb = b.Kind is PortKind.SignalIn or PortKind.SignalOut;
        if (sa && sb)
        {
            if (a.Kind == b.Kind && a.Kind == PortKind.SignalOut)
                return $"Has unido dos salidas ({nameA} y {nameB}): las dos quieren mandar la señal y se pelearían. Une una salida con una entrada.";
            if (a.Kind == b.Kind)
                return $"Has unido dos entradas ({nameA} y {nameB}): ninguna de las dos da señal. Une cada entrada a una salida (un pulsador, un contacto, una salida del PLC…).";
            return null;
        }
        if (a.Kind == PortKind.Power && b.Kind == PortKind.Power)
        {
            if (a.Domain != b.Domain)
                return $"No se puede unir {KindName(a)} ({nameA}) con {KindName(b)} ({nameB}).";
            if (a.Causality == b.Causality)
                return a.Domain switch
                {
                    PowerDomain.Electrical3Ph or PowerDomain.ElectricalDC when a.Causality == Causality.ImposesEffort =>
                        $"{nameA} y {nameB} dan tensión los dos: dos fuentes unidas sin sincronizar provocarían un cortocircuito. Une cada fuente a sus cargas.",
                    PowerDomain.Electrical3Ph or PowerDomain.ElectricalDC =>
                        $"{nameA} y {nameB} consumen los dos: falta la fuente. Pon entre ellos una red, un generador, un contactor o un embarrado.",
                    PowerDomain.MechanicalRotational or PowerDomain.MechanicalLinear when a.Causality == Causality.ImposesFlow =>
                        $"{nameA} y {nameB} quieren mover el mismo eje los dos (dos motores o motor y turbina). Un eje lo mueve una sola máquina.",
                    PowerDomain.MechanicalRotational or PowerDomain.MechanicalLinear =>
                        $"{nameA} y {nameB} son dos cargas en el mismo eje y nadie lo mueve. Acopla un motor o una turbina.",
                    _ => $"{nameA} y {nameB} {Role(a)} los dos: une uno que {Role(a)} con otro que {(a.Causality == Causality.ImposesEffort ? "da caudal" : "fije la presión")}.",
                };
            return null;
        }
        return $"No se puede unir {KindName(a)} ({nameA}) con {KindName(b)} ({nameB}).";
    }

    private static string? FanIn(BenchProject project, string partA, PortDef a, string partB, PortDef b)
    {
        string? Busy(string part, PortDef p)
        {
            var existing = project.WiresAt(part, p.Name).Select(w => w.Other(part, p.Name))
                .Where(o => PortOf(project, o.Part, o.Port)?.Kind != PortKind.Probe).ToList();
            if (existing.Count == 0) return null;
            var (op, oport) = existing[0];
            return p.Kind switch
            {
                PortKind.SignalIn => $"La entrada {part}.{p.Text} ya recibe señal de {op}.{oport}. Una entrada tiene una sola fuente; para combinar señales usa un PLC o un Arduino.",
                PortKind.Power => $"{part}.{p.Text} ya está unido a {op}.{oport}. " + (p.Domain is PowerDomain.Electrical3Ph or PowerDomain.ElectricalDC
                    ? "Para alimentar varios equipos desde el mismo punto usa un Embarrado."
                    : "Un eje o una conexión de aire une solo dos máquinas."),
                PortKind.Tap => $"La toma de {part} ya está puesta en {op}. Un transmisor mide en un solo sitio.",
                PortKind.Probe => $"La punta {part}.{p.Text} ya está en {op}.{oport}.",
                _ => null,
            };
        }
        // Las puntas de medida se pueden poner en cualquier borne aunque ya esté ocupado.
        if (a.Kind == PortKind.Probe) return Busy(partA, a);
        if (b.Kind == PortKind.Probe) return Busy(partB, b);
        return Busy(partA, a) ?? Busy(partB, b);
    }

    public static PortDef? PortOf(BenchProject project, string partId, string port)
    {
        var p = project.Part(partId);
        return p is null ? null : BenchCatalog.Get(p.Type)?.Port(p, port);
    }
}
