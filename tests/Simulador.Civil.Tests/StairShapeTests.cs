using Simulador.Civil.Model;
using Simulador.Civil.Rendering;
using Simulador.Core.Building;

namespace Simulador.Civil.Tests;

public class StairShapeTests
{
    private static Stair Make(StairShape shape, bool left = true, double rise = 3.5, double width = 1.2) =>
        new("E1", "N0", new Point2(0, 0), Math.PI / 2, width, rise, shape, left); // sube hacia +Y

    [Theory]
    [InlineData(StairShape.Straight)]
    [InlineData(StairShape.L)]
    [InlineData(StairShape.U)]
    public void Steps_climb_one_riser_at_a_time_and_end_one_riser_below_the_top(StairShape shape)
    {
        var s = Make(shape);
        var d = s.Design;
        var tops = s.Steps.Select(x => x.Top).ToList();
        for (int i = 0; i < tops.Count; i++) Assert.Equal((i + 1) * d.Riser, tops[i], 6);
        // La última contrahuella llega al piso de arriba: Rise = último peldaño + una contrahuella.
        Assert.Equal(s.Rise, tops[^1] + d.Riser, 6);
        Assert.Equal(shape == StairShape.Straight ? 0 : 1, s.Steps.Count(x => x.IsLanding));
    }

    [Fact]
    public void L_stair_turns_to_the_chosen_side()
    {
        var left = Make(StairShape.L, left: true);
        var right = Make(StairShape.L, left: false);
        // Subiendo hacia +Y, la izquierda es -X.
        Assert.True(left.Arrival.X < -1);
        Assert.True(right.Arrival.X > 1);
        Assert.Equal(left.Arrival.Y, right.Arrival.Y, 6);
        // El segundo tramo sale del descanso en perpendicular: llega a la altura del descanso.
        var landing = left.Steps.Single(x => x.IsLanding);
        Assert.InRange(left.Arrival.Y, landing.Outline.Min(p => p.Y), landing.Outline.Max(p => p.Y));
    }

    [Fact]
    public void U_stair_comes_back_alongside_and_is_much_shorter_than_straight()
    {
        var u = Make(StairShape.U, left: false);
        var straight = Make(StairShape.Straight);
        Assert.Equal(u.Width + Stair.UGap, u.Arrival.X - u.Start.X, 6); // vuelve paralela, a la derecha
        Assert.Equal(u.Start.Y, u.Arrival.Y, 6);                      // y llega a la altura del arranque
        var (len, breadth) = u.Footprint;
        Assert.Equal(2 * u.Width + Stair.UGap, breadth, 6);
        Assert.True(len < straight.Footprint.Length * 0.7, $"{len} vs {straight.Footprint.Length}");
    }

    [Fact]
    public void Long_stair_warning_goes_away_with_a_landing()
    {
        Assert.Contains(Make(StairShape.Straight).Design.Warnings, w => w.Contains("descanso"));
        Assert.DoesNotContain(Make(StairShape.L).Design.Warnings, w => w.Contains("descanso"));
        Assert.DoesNotContain(Make(StairShape.U).Design.Warnings, w => w.Contains("descanso"));
    }

    [Fact]
    public void Treads_do_not_overlap_each_other_or_the_landing()
    {
        foreach (var shape in new[] { StairShape.L, StairShape.U })
        foreach (bool left in new[] { true, false })
        {
            var steps = Make(shape, left).Steps;
            for (int i = 0; i < steps.Count; i++)
            for (int j = i + 1; j < steps.Count; j++)
            {
                var a = Shrink(steps[i].Outline);
                Assert.False(a.Any(p => Geo.Contains(steps[j].Outline, p)), $"{shape}/{left}: {i} pisa {j}");
            }
        }
    }

    private static Point2[] Shrink(Point2[] pts)
    {
        var c = new Point2(pts.Average(p => p.X), pts.Average(p => p.Y));
        return pts.Select(p => c + (p - c) * 0.98).ToArray();
    }

    [Fact]
    public void Shape_survives_save_and_load_and_old_files_default_to_straight()
    {
        var doc = new BuildingDocument();
        doc.Edit(e =>
        {
            e.Clear();
            var n = e.AddLevel("Planta baja", 0, 3, "N0");
            e.AddStair(n.Id, new Point2(1, 1), 0, 1.0, 3.0, StairShape.U, turnLeft: false);
        });
        var copy = BuildingDocument.FromJson(doc.ToJson());
        Assert.Equal(StairShape.U, copy.Stairs[0].Shape);
        Assert.False(copy.Stairs[0].TurnLeft);

        // Un archivo anterior sin "Shape" ni "TurnLeft" se abre como escalera recta.
        var old = System.Text.RegularExpressions.Regex.Replace(doc.ToJson(), @",\s*""(Shape|TurnLeft)""\s*:\s*(""U""|false)", "");
        Assert.DoesNotContain("Shape", old);
        Assert.DoesNotContain("TurnLeft", old);
        copy = BuildingDocument.FromJson(old);
        Assert.Equal(StairShape.Straight, copy.Stairs[0].Shape);
    }

    [Fact]
    public void Hotel_uses_a_U_stair_without_warnings_and_its_mesh_has_the_landing()
    {
        var doc = BuildingTemplates.Hotel();
        var st = doc.Stairs.Single(s => s.LevelId == "N0");
        Assert.Equal(StairShape.U, st.Shape);
        Assert.DoesNotContain(BuildingChecks.Run(doc), c => c.ElementId == st.Id);
        // Todo el contorno queda dentro del edificio.
        Assert.All(st.Outline, p => Assert.True(p.X > 0 && p.X < 16 && p.Y > 0 && p.Y < 12, $"{p}"));
        var mesh = BuildingMesh.Build(doc);
        Assert.Contains(mesh.Faces, f => f.Argb == BuildingMesh.StairColor && f.Pts.All(p => Math.Abs(p.Z - 10 * st.Design.Riser) < 1e-6));
    }
}
