namespace Simulador.Plc.Runtime.Ladder;

// Modelo editable de un programa Ladder (para el editor gráfico). Los registros de LadderModel.cs son inmutables;
// aquí cada elemento es un objeto con identidad que se puede seleccionar, mover y modificar, y se convierte de nuevo
// a LadderProgram para compilarlo.

/// <summary>Elemento editable de la zona de condiciones.</summary>
public abstract class EditNode
{
    /// <summary>Serie que contiene este elemento.</summary>
    public EditSeries? Parent { get; internal set; }
}

public sealed class EditContact(ContactKind kind, string operand) : EditNode
{
    public ContactKind Kind { get; set; } = kind;
    public string Operand { get; set; } = operand;
}

public sealed class EditCompare(string left, string op, string right) : EditNode
{
    public string Left { get; set; } = left;
    public string Op { get; set; } = op;
    public string Right { get; set; } = right;
}

/// <summary>Ramas en paralelo; cada rama es una serie.</summary>
public sealed class EditParallel : EditNode
{
    public List<EditSeries> Branches { get; } = [];

    internal EditSeries NewBranch()
    {
        var s = new EditSeries { Owner = this };
        Branches.Add(s);
        return s;
    }
}

/// <summary>Elementos en serie. Es la condición de un segmento o una rama de un paralelo.</summary>
public sealed class EditSeries
{
    public List<EditNode> Items { get; } = [];
    /// <summary>Paralelo al que pertenece, o null si es la condición principal del segmento.</summary>
    public EditParallel? Owner { get; internal set; }
    public EditRung? Rung { get; internal set; }

    internal void Insert(int index, EditNode node)
    {
        node.Parent = this;
        Items.Insert(index, node);
    }
}

/// <summary>Salida editable (bobina o caja).</summary>
public sealed class EditOutput(LadderOutput value)
{
    public LadderOutput Value { get; set; } = value;
    public EditRung? Rung { get; internal set; }
}

public sealed class EditRung
{
    public EditRung()
    {
        Condition = new EditSeries { Rung = this };
    }

    public EditSeries Condition { get; }
    public List<EditOutput> Outputs { get; } = [];
    public string? Comment { get; set; }
}

/// <summary>Programa Ladder editable con las operaciones del editor.</summary>
public sealed class LadderDocument
{
    public List<EditRung> Rungs { get; } = [];

    public event EventHandler? Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // ---- Segmentos ----

    public EditRung AddRung(int? index = null)
    {
        var r = new EditRung();
        Rungs.Insert(index ?? Rungs.Count, r);
        NotifyChanged();
        return r;
    }

    public void RemoveRung(EditRung rung)
    {
        Rungs.Remove(rung);
        NotifyChanged();
    }

    public void MoveRung(EditRung rung, int delta)
    {
        int i = Rungs.IndexOf(rung);
        int j = Math.Clamp(i + delta, 0, Rungs.Count - 1);
        if (i < 0 || i == j) return;
        Rungs.RemoveAt(i);
        Rungs.Insert(j, rung);
        NotifyChanged();
    }

    public static EditRung RungOf(EditNode node)
    {
        var s = node.Parent ?? throw new InvalidOperationException("El elemento no está en ningún segmento.");
        while (s.Owner is { } p) s = p.Parent ?? throw new InvalidOperationException("Paralelo sin serie.");
        return s.Rung ?? throw new InvalidOperationException("Serie sin segmento.");
    }

    // ---- Condiciones ----

    /// <summary>Añade un elemento al final de la condición principal del segmento.</summary>
    public void Append(EditRung rung, EditNode node)
    {
        rung.Condition.Insert(rung.Condition.Items.Count, node);
        NotifyChanged();
    }

    /// <summary>Inserta un elemento justo después de <paramref name="anchor"/>, en serie con él.</summary>
    public void InsertAfter(EditNode anchor, EditNode node)
    {
        var s = anchor.Parent!;
        s.Insert(s.Items.IndexOf(anchor) + 1, node);
        NotifyChanged();
    }

    /// <summary>Pone <paramref name="newBranch"/> en paralelo con <paramref name="node"/>.</summary>
    public EditParallel WrapInParallel(EditNode node, EditNode newBranch)
    {
        if (node is EditParallel existing)
        {
            AddBranch(existing, newBranch);
            return existing;
        }
        var s = node.Parent!;
        int i = s.Items.IndexOf(node);
        s.Items.RemoveAt(i);
        var p = new EditParallel();
        p.NewBranch().Insert(0, node);
        p.NewBranch().Insert(0, newBranch);
        s.Insert(i, p);
        NotifyChanged();
        return p;
    }

    public void AddBranch(EditParallel parallel, EditNode node)
    {
        parallel.NewBranch().Insert(0, node);
        NotifyChanged();
    }

    /// <summary>Quita un elemento. Las ramas que quedan vacías desaparecen y un paralelo de una sola rama se deshace.</summary>
    public void Remove(EditNode node)
    {
        var s = node.Parent;
        if (s is null) return;
        s.Items.Remove(node);
        node.Parent = null;
        if (s.Items.Count == 0 && s.Owner is { } p)
        {
            p.Branches.Remove(s);
            if (p.Branches.Count == 0) Remove(p);
        }
        if (s.Owner is { Branches.Count: 1 } single && single.Parent is { } outer)
        {
            int i = outer.Items.IndexOf(single);
            outer.Items.RemoveAt(i);
            single.Parent = null;
            foreach (var item in single.Branches[0].Items.ToList()) outer.Insert(i++, item);
        }
        NotifyChanged();
    }

    // ---- Salidas ----

    public EditOutput AddOutput(EditRung rung, LadderOutput value)
    {
        var o = new EditOutput(value) { Rung = rung };
        rung.Outputs.Add(o);
        NotifyChanged();
        return o;
    }

    public void RemoveOutput(EditOutput output)
    {
        output.Rung?.Outputs.Remove(output);
        output.Rung = null;
        NotifyChanged();
    }

    /// <summary>
    /// Reescribe los operandos (p. ej. al cambiar de marca: I0.0 de Siemens → X0 de Mitsubishi). <paramref name="map"/>
    /// devuelve el operando nuevo o el mismo si no hay que cambiarlo.
    /// </summary>
    public void TranslateOperands(Func<string, string> map)
    {
        void Series(EditSeries s)
        {
            foreach (var n in s.Items)
            {
                switch (n)
                {
                    case EditContact c: c.Operand = map(c.Operand); break;
                    case EditCompare c: c.Left = map(c.Left); c.Right = map(c.Right); break;
                    case EditParallel p: p.Branches.ForEach(Series); break;
                }
            }
        }

        foreach (var r in Rungs)
        {
            Series(r.Condition);
            foreach (var o in r.Outputs)
            {
                o.Value = o.Value switch
                {
                    Coil c => c with { Operand = map(c.Operand) },
                    CounterBox k when k.ResetOperand is not null => k with { ResetOperand = map(k.ResetOperand) },
                    MoveBox m => m with { Source = map(m.Source), Destination = map(m.Destination) },
                    var other => other,
                };
            }
        }
        NotifyChanged();
    }

    // ---- Conversión ----

    public static LadderDocument FromProgram(LadderProgram program)
    {
        var doc = new LadderDocument();
        foreach (var r in program.Rungs)
        {
            var er = new EditRung { Comment = r.Comment };
            if (r.Condition is not null) Fill(er.Condition, r.Condition);
            foreach (var o in r.Outputs) er.Outputs.Add(new EditOutput(o) { Rung = er });
            doc.Rungs.Add(er);
        }
        return doc;
    }

    private static void Fill(EditSeries target, LadderNode node)
    {
        switch (node)
        {
            case Series s:
                foreach (var item in s.Items) Fill(target, item);
                break;
            case ParallelBranch p:
                var ep = new EditParallel();
                foreach (var b in p.Branches) Fill(ep.NewBranch(), b);
                target.Insert(target.Items.Count, ep);
                break;
            case Contact c:
                target.Insert(target.Items.Count, new EditContact(c.Kind, c.Operand));
                break;
            case CompareContact cc:
                target.Insert(target.Items.Count, new EditCompare(cc.Left, cc.Op, cc.Right));
                break;
        }
    }

    public LadderProgram ToProgram() => new([], Rungs.Select(r => new Rung(
        r.Condition.Items.Count == 0 ? null : ToNode(r.Condition),
        r.Outputs.Select(o => o.Value).ToList(),
        r.Comment)).ToList());

    private static LadderNode ToNode(EditSeries s) =>
        s.Items.Count == 1 ? ToNode(s.Items[0]) : new Series(s.Items.Select(ToNode).ToList());

    private static LadderNode ToNode(EditNode n) => n switch
    {
        EditContact c => new Contact(c.Kind, c.Operand),
        EditCompare c => new CompareContact(c.Left, c.Op, c.Right),
        EditParallel p => new ParallelBranch(p.Branches.Select(ToNode).ToList()),
        _ => throw new InvalidOperationException(),
    };
}
