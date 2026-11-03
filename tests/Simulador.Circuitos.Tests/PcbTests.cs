using Simulador.Circuitos.Catalog;
using Simulador.Circuitos.Pcb;
using Simulador.Circuitos.Schematic;
using Simulador.Core.Libraries.Model;

namespace Simulador.Circuitos.Tests;

public class PcbTests
{
    private readonly ComponentCatalog _catalog = new();

    private (PcbDocument Pcb, PcbSync.SyncReport Report) FromLedExample()
    {
        var pcb = new PcbDocument { BoardWidth = 80, BoardHeight = 50 };
        var report = PcbSync.Update(pcb, Examples.LedTransistor(_catalog), _catalog);
        return (pcb, report);
    }

    private static PlacedPad Pad(PcbDocument pcb, string reference, string number) =>
        pcb.Footprints.Single(f => f.Reference == reference).Let(FootprintTransform.Pads).Single(p => p.Pad.Number == number);

    [Fact]
    public void Builtin_footprints_load()
    {
        Assert.NotNull(_catalog.FindFootprint("Basicos:R_Axial_P10.16mm"));
        Assert.Equal(3, _catalog.FindFootprint("TO-92_Inline")!.Pads.Count);
    }

    [Fact]
    public void Sync_places_all_parts_with_nets()
    {
        var (pcb, report) = FromLedExample();

        Assert.Empty(report.Warnings);
        Assert.Equal(5, report.Added);
        Assert.Equal(["D1", "Q1", "R1", "R2", "V1"], pcb.Footprints.Select(f => f.Reference).Order());
        // El emisor (pad 1 del TO-92) va a masa, igual que el - de la fuente.
        Assert.Equal("0", Pad(pcb, "Q1", "1").Net);
        Assert.Equal("0", Pad(pcb, "V1", "2").Net);
        // Todo dentro de la placa.
        Assert.DoesNotContain(PcbAnalysis.CheckRules(pcb), v => v.Message.Contains("se sale"));
    }

    [Fact]
    public void Sync_keeps_positions_and_removes_deleted_parts()
    {
        var (pcb, _) = FromLedExample();
        var r1 = pcb.Footprints.Single(f => f.Reference == "R1");
        r1.Position = new Vec2(30, 30);

        var schematic = Examples.LedTransistor(_catalog);
        schematic.Symbols.RemoveAll(s => s.Reference == "R2");
        var report = PcbSync.Update(pcb, schematic, _catalog);

        Assert.Equal(1, report.Removed);
        Assert.Equal(new Vec2(30, 30), pcb.Footprints.Single(f => f.Reference == "R1").Position);
    }

    [Fact]
    public void Ratsnest_shrinks_when_a_track_is_routed()
    {
        var (pcb, _) = FromLedExample();
        int before = PcbAnalysis.ComputeRatsnest(pcb).Count;
        Assert.Equal(6, before);

        var a = Pad(pcb, "R1", "2");
        var b = Pad(pcb, "D1", "2");
        Assert.Equal(a.Net, b.Net);
        pcb.Tracks.Add(new PcbTrack { A = a.Center, B = b.Center, Net = a.Net });

        Assert.Equal(before - 1, PcbAnalysis.ComputeRatsnest(pcb).Count);
    }

    [Fact]
    public void Drc_detects_short_between_nets()
    {
        var (pcb, _) = FromLedExample();
        var a = Pad(pcb, "V1", "1");
        var b = Pad(pcb, "V1", "2");   // otra red: cortocircuito directo
        pcb.Tracks.Add(new PcbTrack { A = a.Center, B = b.Center, Net = a.Net });

        Assert.Contains(PcbAnalysis.CheckRules(pcb), v => v.Message.Contains("toca el pad V1.2"));
    }

    [Fact]
    public void Rotation_moves_pads_counterclockwise_on_screen()
    {
        var fp = new PcbFootprint { Definition = _catalog.FindFootprint("R_Axial_P10.16mm"), Position = new Vec2(10, 10), Rotation = 90 };
        var pad2 = FootprintTransform.Pads(fp).Single(p => p.Pad.Number == "2");
        Assert.Equal(10, pad2.Center.X, 6);
        Assert.Equal(10 - 10.16, pad2.Center.Y, 6);   // hacia arriba en pantalla
    }

    [Fact]
    public void Geometry_pad_distance()
    {
        var fp = new PcbFootprint { Definition = _catalog.FindFootprint("R_Axial_P10.16mm"), Position = new Vec2(0, 0) };
        var pad = FootprintTransform.Pads(fp).First();   // círculo de 2,2 mm en (0,0)
        Assert.Equal(2.0 - 1.1, Geometry2D.PadSegment(pad, new Vec2(-5, 2), new Vec2(5, 2)), 6);
        Assert.Equal(0, Geometry2D.PadSegment(pad, new Vec2(-5, 0.5), new Vec2(5, 0.5)), 6);
    }

    [Fact]
    public void Save_and_load()
    {
        var (pcb, _) = FromLedExample();
        pcb.Tracks.Add(new PcbTrack { A = new Vec2(1, 1), B = new Vec2(5, 1), Net = "N1" });
        var loaded = PcbDocument.FromJson(pcb.ToJson());
        Assert.Empty(loaded.ResolveFootprints(_catalog.FindFootprint));
        Assert.Equal(pcb.Footprints.Count, loaded.Footprints.Count);
        Assert.Equal(PcbAnalysis.ComputeRatsnest(pcb).Count, PcbAnalysis.ComputeRatsnest(loaded).Count);
    }
}

internal static class FunctionalExtensions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> f) => f(value);
}
