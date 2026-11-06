using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Pcb;
using Simulador.Circuitos.Schematic;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Tests;

public class AutoRouterTests
{
    private readonly ComponentCatalog _catalog = new();

    private PcbDocument Board(Func<ComponentCatalog, SchematicDocument> example, double w = 100, double h = 70)
    {
        var pcb = new PcbDocument { BoardWidth = w, BoardHeight = h };
        var report = PcbSync.Update(pcb, example(_catalog), _catalog);
        Assert.Empty(report.Warnings);
        return pcb;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Routes_examples_without_rule_errors(int index)
    {
        var pcb = Board(Examples.All[index].Build);
        var report = AutoRouter.Route(pcb, allowJumpers: true);

        Assert.Equal(0, report.Failed);
        // Con los componentes en fila (sin colocar) a veces hacen falta puentes.
        Assert.InRange(report.Jumpers, 0, 6);
        Assert.Empty(PcbAnalysis.ComputeRatsnest(pcb));
        Assert.Empty(PcbAnalysis.CheckRules(pcb));
        Assert.All(pcb.Tracks, t => Assert.Contains(t.Layer, new[] { Layers.Bottom, Layers.Jumper }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(6)]
    public void Auto_placement_makes_a_compact_routable_board(int index)
    {
        var pcb = Board(Examples.All[index].Build, 150, 120);
        double rowArea = pcb.BoardWidth * pcb.BoardHeight;
        AutoPlacer.Place(pcb);
        var report = AutoRouter.Route(pcb, allowJumpers: true);
        AutoRouter.FitBoard(pcb);

        Assert.Empty(PcbAnalysis.ComputeRatsnest(pcb));
        Assert.Empty(PcbAnalysis.CheckRules(pcb));
        Assert.InRange(report.Jumpers, 0, 1);
        Assert.True(pcb.BoardWidth * pcb.BoardHeight < rowArea / 2);
        Console.WriteLine($"{Examples.All[index].Name}: {pcb.BoardWidth} x {pcb.BoardHeight} mm, {report.Jumpers} puentes");
    }

    [Fact]
    public void Well_placed_divider_needs_no_jumpers()
    {
        var pcb = Board(Examples.Divider);
        var report = AutoRouter.Route(pcb, allowJumpers: true);
        Assert.Equal(0, report.Jumpers);
        Assert.Empty(PcbAnalysis.ComputeRatsnest(pcb));
    }

    [Fact]
    public void Jumpers_go_to_the_silkscreen_not_the_copper()
    {
        var pcb = Board(Examples.LedTransistor);
        pcb.Tracks.Add(new PcbTrack { A = new Vec2(1, 1), B = new Vec2(9, 1), Layer = Layers.Jumper, Net = "X", Width = 0.6 });
        Assert.DoesNotContain(Simulador.Circuitos.Fabrication.Artwork.Layer(pcb, Simulador.Circuitos.Fabrication.ArtworkLayer.BottomCopper),
            s => s is Simulador.Circuitos.Fabrication.Stroke st && st.A == new Vec2(1, 1));
        Assert.Contains(Simulador.Circuitos.Fabrication.Artwork.Layer(pcb, Simulador.Circuitos.Fabrication.ArtworkLayer.TopSilk),
            s => s is Simulador.Circuitos.Fabrication.Stroke st && st.A == new Vec2(1, 1));
    }

    [Fact]
    public void Reports_connections_it_cannot_route()
    {
        // Placa diminuta: no cabe ninguna pista entre el borde y los pads.
        var pcb = Board(Examples.Divider, 100, 70);
        AutoRouter.FitBoard(pcb, 1.0);
        pcb.Rules.EdgeClearance = 1.0;
        pcb.Rules.Clearance = 3;
        var report = AutoRouter.Route(pcb);
        Assert.True(report.Failed > 0);
        // Lo que no cabe se deja sin rutear: nunca se crean pistas que incumplan las reglas.
        Assert.DoesNotContain(PcbAnalysis.CheckRules(pcb), v => v.Message.Contains("ista") || v.Message.Contains("Cortocircuito"));
    }

    [Fact]
    public void Fit_board_shrinks_to_content_and_keeps_rules()
    {
        var pcb = Board(Examples.LedTransistor, 150, 120);
        AutoRouter.Route(pcb, allowJumpers: true);
        double saved = AutoRouter.FitBoard(pcb);

        Assert.True(saved > 0);
        Assert.True(pcb.BoardWidth < 150 && pcb.BoardHeight < 120);
        Assert.Empty(PcbAnalysis.CheckRules(pcb));
        foreach (var f in pcb.Footprints)
        {
            var (min, max) = FootprintTransform.Bounds(f);
            Assert.True(min.X >= 2 - 1e-6 && min.Y >= 2 - 1e-6 && max.X <= pcb.BoardWidth - 2 + 0.5 && max.Y <= pcb.BoardHeight - 2 + 0.5);
        }
    }
}
