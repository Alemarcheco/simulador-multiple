using Simulador.Plc.Runtime;
using Simulador.Plc.Runtime.Ladder;
using Simulador.Plc.Runtime.Profiles;

namespace Simulador.Plc.Tests;

public class LadderEditingTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMilliseconds(10);

    /// <summary>Construye marcha-paro con las operaciones del editor, como lo haría el usuario.</summary>
    private static LadderDocument BuildStartStop()
    {
        var doc = new LadderDocument();
        var rung = doc.AddRung();
        var start = new EditContact(ContactKind.NormallyOpen, "I0.0");
        doc.Append(rung, start);
        doc.InsertAfter(start, new EditContact(ContactKind.NormallyOpen, "I0.1"));
        doc.WrapInParallel(start, new EditContact(ContactKind.NormallyOpen, "Q0.0"));
        doc.AddOutput(rung, new Coil(CoilKind.Normal, "Q0.0"));
        return doc;
    }

    [Fact]
    public void Editor_operations_build_a_working_program()
    {
        var doc = BuildStartStop();
        var cpu = new PlcCpu("PLC1", new SiemensS71200Profile());
        cpu.LoadLadder(doc.ToProgram());
        cpu.Start();
        var i0 = cpu.Signals.Single(s => s.Id == "PLC1.I0.0");
        var i1 = cpu.Signals.Single(s => s.Id == "PLC1.I0.1");
        var q0 = cpu.Signals.Single(s => s.Id == "PLC1.Q0.0");

        i1.Value = 24;
        i0.Value = 24;
        cpu.Scan(Cycle);
        i0.Value = 0;
        cpu.Scan(2 * Cycle);
        Assert.Equal(1, q0.Value);
        i1.Value = 0;
        cpu.Scan(3 * Cycle);
        Assert.Equal(0, q0.Value);
    }

    [Fact]
    public void Removing_a_branch_unwraps_the_parallel()
    {
        var doc = BuildStartStop();
        var rung = doc.Rungs[0];
        var parallel = Assert.IsType<EditParallel>(rung.Condition.Items[0]);
        var seal = parallel.Branches[1].Items[0];

        doc.Remove(seal);

        Assert.Equal(2, rung.Condition.Items.Count);
        var first = Assert.IsType<EditContact>(rung.Condition.Items[0]);
        Assert.Equal("I0.0", first.Operand);
        Assert.Same(rung.Condition, first.Parent);
        Assert.Same(rung, LadderDocument.RungOf(first));
    }

    [Fact]
    public void Wrapping_an_existing_parallel_adds_a_branch()
    {
        var doc = BuildStartStop();
        var p = (EditParallel)doc.Rungs[0].Condition.Items[0];
        doc.WrapInParallel(p, new EditContact(ContactKind.NormallyOpen, "M0.0"));
        Assert.Equal(3, p.Branches.Count);
        Assert.Same(doc.Rungs[0], LadderDocument.RungOf(p.Branches[2].Items[0]));
    }

    [Fact]
    public void Rungs_can_be_moved_and_removed()
    {
        var doc = BuildStartStop();
        var second = doc.AddRung();
        doc.MoveRung(second, -1);
        Assert.Same(second, doc.Rungs[0]);
        doc.RemoveRung(second);
        Assert.Single(doc.Rungs);
    }

    [Fact]
    public void Program_survives_document_and_file_round_trip()
    {
        var original = new LadderProgram(
            new Rung(new Series(new Contact(ContactKind.RisingEdge, "X0"), new CompareContact("C1.CV", "<", "10")),
                [new CounterBox(CounterKind.CTU, "C1", 10, "X1"), new TimerBox(TimerKind.TOF, "T1", TimeSpan.FromMilliseconds(1500))], "Contador"),
            new Rung(new ParallelBranch(new Contact(ContactKind.NormallyClosed, "X2"), new Series(new Contact(ContactKind.FallingEdge, "X3"), new Contact(ContactKind.NormallyOpen, "M5"))),
                new Coil(CoilKind.Set, "Y0"), new MoveBox("C1.CV", "D0")),
            new Rung(null, new Coil(CoilKind.Negated, "Y1")));

        var viaDoc = LadderDocument.FromProgram(original).ToProgram();
        var json = LadderFile.Save(viaDoc, "mitsubishi-fx5u");
        var loaded = LadderFile.Load(json);

        Assert.Equal("mitsubishi-fx5u", loaded.ProfileId);
        Assert.Equal(LadderFile.Save(original, "mitsubishi-fx5u"), LadderFile.Save(loaded.Program, loaded.ProfileId));
    }

    [Fact]
    public void Invalid_files_give_clear_errors()
    {
        Assert.Throws<FormatException>(() => LadderFile.Load("no es json"));
        Assert.Throws<FormatException>(() => LadderFile.Load("""{"version":1,"rungs":[{"outputs":[{"type":"bobina"}]}]}"""));
        Assert.Throws<FormatException>(() => LadderFile.Load("""{"version":99}"""));
    }
}

public class LadderTranslationTests
{
    [Fact]
    public void Operands_are_translated_between_brands()
    {
        var siemens = new SiemensS71200Profile();
        var mitsubishi = new MitsubishiFx5uProfile();
        var doc = LadderDocument.FromProgram(new LadderProgram(
            new Rung(new Series(new Contact(ContactKind.NormallyOpen, "I1.0"), new Contact(ContactKind.NormallyClosed, "T1.Q")),
                new Coil(CoilKind.Normal, "Q0.3"), new TimerBox(TimerKind.TON, "T1", TimeSpan.FromSeconds(1)))));

        doc.TranslateOperands(op => siemens.TryParse(op, out var a) ? mitsubishi.Format(a) : op);

        var rung = doc.ToProgram().Rungs[0];
        var series = Assert.IsType<Series>(rung.Condition);
        Assert.Equal("X10", ((Contact)series.Items[0]).Operand);
        Assert.Equal("T1.Q", ((Contact)series.Items[1]).Operand);
        Assert.Equal("Y3", ((Coil)rung.Outputs[0]).Operand);
    }
}
