using System.Numerics;
using Simulador.Banco.Mesa;
using Simulador.Banco.Mesa.Ui;
using Simulador.Banco.Mesa.View3D;
using Simulador.Core.CoSim;

namespace Simulador.Banco.Tests;

public class Mesa3DTests
{
    private static int Count(BenchScene s, string id)
    {
        var (min, max) = s.Parts[id];
        return s.Scene.Triangles.Count(t => Inside(t.A, min, max) && Inside(t.B, min, max) && Inside(t.C, min, max));
    }

    private static bool Inside(Vector3 p, Vector3 min, Vector3 max) =>
        p.X >= min.X - 1e-4 && p.Y >= min.Y - 1e-4 && p.Z >= min.Z - 1e-4 && p.X <= max.X + 1e-4 && p.Y <= max.Y + 1e-4 && p.Z <= max.Z + 1e-4;

    [Fact]
    public void Every_catalog_part_has_a_3d_model_on_its_plate_with_all_its_terminals()
    {
        foreach (var t in BenchCatalog.All)
        {
            var project = new BenchProject();
            var part = t.NewPart(t.Prefix + "1", 100, 200);
            project.Parts.Add(part);
            var scene = BenchScene3D.Build(project, new BenchLive());

            Assert.True(scene.Parts.ContainsKey(part.Id), t.TypeId);
            var (min, max) = scene.Parts[part.Id];
            // Algo más que la placa y los bornes: un modelo con volumen.
            Assert.True(max.Z > 0.25f, $"{t.TypeId}: el modelo apenas sobresale de la placa ({max.Z:0.00} m)");
            // Dentro de la huella del bloque (con el margen de los bornes).
            var box = BenchCanvas.Layout(part).Box;
            Assert.True(min.X >= (float)box.X * BenchScene3D.Scale - 0.1f && max.X <= (float)box.Right * BenchScene3D.Scale + 0.1f,
                $"{t.TypeId}: se sale de su placa ({min.X:0.00}…{max.X:0.00} m frente a {box.X * BenchScene3D.Scale:0.00}…{box.Right * BenchScene3D.Scale:0.00} m)");
            foreach (var p in t.PortsFor(part)) Assert.True(scene.Terminals.ContainsKey((part.Id, p.Name)), $"{t.TypeId}.{p.Name}");
        }
    }

    [Fact]
    public void Every_example_draws_every_wire_between_its_terminals()
    {
        foreach (var ex in BenchExamples.All)
        {
            var project = ex.Create();
            var bare = new BenchProject { Name = project.Name };
            bare.Parts.AddRange(project.Parts);
            int without = BenchScene3D.Build(bare, new BenchLive()).Scene.Triangles.Count;
            var scene = BenchScene3D.Build(project, new BenchLive());
            Assert.True(scene.Scene.Triangles.Count > without + project.Wires.Count * 50, ex.Name);
            foreach (var w in project.Wires)
            {
                Assert.True(scene.Terminals.ContainsKey((w.FromPart, w.FromPort)), $"{ex.Name}: {w.FromPart}.{w.FromPort}");
                Assert.True(scene.Terminals.ContainsKey((w.ToPart, w.ToPort)), $"{ex.Name}: {w.ToPart}.{w.ToPort}");
            }
        }
    }

    [Fact]
    public void The_3d_view_follows_the_simulation()
    {
        var project = BenchExamples.TankFill();
        var built = BenchBuilder.Build(project);
        Assert.DoesNotContain(built.Issues, i => i.Level == IssueLevel.Error);
        built.Scheduler.RunFor(TimeSpan.FromSeconds(20));
        var frame = new SimFrame(built.Scheduler.Now, built.Scheduler.Snapshot());
        PartLook? Look(string id) => built.Parts.TryGetValue(id, out var bp) && bp.Look is { } l ? l(built.Reader(frame, id)) : null;

        var still = BenchScene3D.Build(project, new BenchLive());
        var live = BenchScene3D.Build(project, new BenchLive { Running = true, LookOf = Look, WireActive = _ => true, Phase = 1.3 });

        // Las etiquetas muestran lo que mide cada pieza y el agua del tanque tiene la altura de su nivel.
        Assert.Contains(live.Scene.Labels, l => l.Text.StartsWith("TK1 · ") && l.Text.Contains(" m de "));
        Assert.Contains(still.Scene.Labels, l => l.Text == "TK1");
        // Los cables activos llevan cuentas que avanzan: hay más triángulos en marcha.
        Assert.True(live.Scene.Triangles.Count > still.Scene.Triangles.Count);
    }

    [Fact]
    public void Wires_rest_on_the_table_between_terminals()
    {
        var a = new Vector3(0, 0, 0.1f);
        var b = new Vector3(3, 1, 0.1f);
        var pts = BenchScene3D.WirePath(a, Vector3.UnitX, b, -Vector3.UnitX);
        Assert.True(Vector3.Distance(a, pts[0]) < 1e-4f);
        Assert.True(Vector3.Distance(b, pts[^1]) < 1e-4f);
        Assert.True(pts[pts.Count / 2].Z < 0.05f);
        Assert.All(pts, p => Assert.True(p.Z >= 0));
    }

    [Fact]
    public void Selected_and_faulty_parts_change_their_plate_colour()
    {
        var project = BenchExamples.MotorStartStop();
        uint PlateOf(BenchScene s) => s.Scene.Triangles.First(t => Inside(t.A, s.Parts["M1"].Min, s.Parts["M1"].Max) && t.A.Z < 0.05f).Color;
        var normal = PlateOf(BenchScene3D.Build(project, new BenchLive()));
        var sel = PlateOf(BenchScene3D.Build(project, new BenchLive { SelectedId = "M1" }));
        var err = PlateOf(BenchScene3D.Build(project, new BenchLive { IssueOf = id => id == "M1" ? IssueLevel.Error : null }));
        Assert.NotEqual(normal, sel);
        Assert.NotEqual(normal, err);
        Assert.NotEqual(sel, err);
        Assert.True(Count(BenchScene3D.Build(project, new BenchLive()), "M1") > 100);
    }
}
