using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Tests;

public class WalkthroughTests
{
    private static Point2 P(double x, double y) => new(x, y);

    /// <summary>Camina hacia un punto en pasos cortos (como lo haría el teclado).</summary>
    private static void WalkTowards(Walker w, Point2 target, double maxMeters = 30)
    {
        for (double done = 0; done < maxMeters && w.Position.DistanceTo(target) > 0.1; done += 0.1)
        {
            var d = target - w.Position;
            w.Heading = Math.Atan2(d.Y, d.X);
            w.Move(Math.Min(0.1, d.Length), 0);
        }
    }

    [Fact]
    public void Starts_outside_in_front_of_the_entrance_looking_at_it()
    {
        var doc = BuildingTemplates.Casa();
        var w = Walker.AtEntrance(doc);
        // La puerta de la casa está en el muro sur (y = 0) en x = 2,5: se empieza fuera (y < 0) mirando al norte.
        Assert.True(w.Position.Y < -1, $"y = {w.Position.Y}");
        Assert.InRange(w.Position.X, 2.0, 3.0);
        Assert.InRange(Math.Sin(w.Heading), 0.99, 1.0);
        Assert.Equal(0, w.FootZ, 3);
        Assert.Equal(Walker.EyeHeight, w.EyeZ, 3);
    }

    [Fact]
    public void Walls_stop_you_but_doors_let_you_through()
    {
        var doc = BuildingTemplates.Casa();
        // Contra el muro sur, lejos de la puerta: no se entra.
        var blocked = new Walker(doc, P(4.2, -1.5), Math.PI / 2);
        blocked.Move(3, 0);
        Assert.True(blocked.Position.Y < -0.1 - Walker.BodyRadius + 0.01, $"atravesó el muro: y = {blocked.Position.Y}");
        // Por la puerta (x = 2,5) se entra a la sala.
        var w = Walker.AtEntrance(doc);
        WalkTowards(w, P(2.5, 2.5));
        Assert.True(w.Position.DistanceTo(P(2.5, 2.5)) < 0.2, $"se quedó en {w.Position}");
    }

    [Fact]
    public void Sliding_along_a_wall_keeps_the_body_radius()
    {
        var doc = BuildingTemplates.Casa();
        var w = new Walker(doc, P(2.5, 2.5), Math.PI); // hacia el muro oeste (x = 0, 20 cm)
        w.Move(5, 0);
        Assert.InRange(w.Position.X, 0.1 + Walker.BodyRadius - 0.01, 0.1 + Walker.BodyRadius + 0.05);
        // Andando en diagonal contra él, se desliza hacia el norte.
        double y0 = w.Position.Y;
        w.Heading = Math.PI * 0.75;
        w.Move(1, 0);
        Assert.True(w.Position.Y > y0 + 0.5);
        Assert.True(w.Position.X >= 0.1 + Walker.BodyRadius - 0.01);
    }

    [Fact]
    public void Windows_do_not_let_you_out()
    {
        var doc = BuildingTemplates.Casa();
        // Ventana del muro oeste en y = 5,5: se queda dentro.
        var w = new Walker(doc, P(1.5, 5.5), Math.PI);
        w.Move(4, 0);
        Assert.True(w.Position.X > 0.1);
    }

    [Fact]
    public void Climbing_the_stair_reaches_the_upper_floor_and_coming_down_returns()
    {
        var doc = BuildingTemplates.CasaDosPlantas();
        var st = doc.Stairs.Single();
        var upper = doc.Levels.OrderBy(l => l.Elevation).Last();
        // Al pie de la escalera, mirando en su dirección de subida.
        var start = st.Start - st.Direction * 0.4;
        var w = new Walker(doc, start, st.Angle);
        Assert.Equal(0, w.FootZ, 3);
        double last = w.FootZ;
        for (int i = 0; i < 80 && w.FootZ < upper.Elevation - 1e-6; i++)
        {
            w.Move(0.1, 0);
            Assert.True(w.FootZ >= last - 1e-9, "bajó mientras subía");
            Assert.True(w.FootZ - last <= Walker.MaxStepUp + 1e-9, "subió de golpe");
            last = w.FootZ;
        }
        Assert.Equal(upper.Elevation, w.FootZ, 3);
        Assert.Equal(upper.Id, w.Level!.Id);

        // Media vuelta y bajar: se vuelve a la planta baja.
        w.Heading += Math.PI;
        for (int i = 0; i < 80 && w.FootZ > 1e-6; i++) w.Move(0.1, 0);
        Assert.Equal(0, w.FootZ, 3);
        Assert.Equal(doc.Levels.OrderBy(l => l.Elevation).First().Id, w.Level!.Id);
    }

    [Fact]
    public void Upstairs_the_floor_holds_you_except_over_the_stairwell()
    {
        var doc = BuildingTemplates.CasaDosPlantas();
        var w = new Walker(doc, P(3, 4), 0);
        var upper = doc.Levels.OrderBy(l => l.Elevation).Last();
        Assert.Equal(upper.Elevation, w.FloorAt(P(3, 4), upper.Elevation + 0.1), 3);
        var st = doc.Stairs.Single();
        var mid = st.Steps[st.Steps.Count / 2].Outline;
        var c = mid.Aggregate(P(0, 0), (a, b) => a + b) * (1.0 / mid.Length);
        Assert.True(w.FloorAt(c, upper.Elevation + 0.1) < upper.Elevation - 0.5);
    }

    [Fact]
    public void Looking_up_and_down_is_limited()
    {
        var w = new Walker(BuildingTemplates.Casa(), P(2.5, 2.5), 0);
        w.Turn(0, 10);
        Assert.True(w.Pitch < Math.PI / 2);
        var (x, y, z) = w.LookDirection;
        Assert.Equal(1, Math.Sqrt(x * x + y * y + z * z), 6);
    }

    [Fact]
    public void Subtracting_a_convex_hole_keeps_the_rest_of_the_area()
    {
        Point2[] slab = [P(0, 0), P(10, 0), P(10, 8), P(0, 8)];
        Point2[] hole = [P(1, 1), P(2, 1), P(2, 4), P(1, 4)];
        var pieces = Geo.SubtractConvex(slab, hole);
        Assert.Equal(80 - 3, pieces.Sum(p => Math.Abs(Geo.SignedArea(p))), 6);
        Assert.DoesNotContain(pieces, p => Geo.Contains(p, P(1.5, 2)));
        // Agujero fuera: queda el forjado entero. Forjado dentro del agujero: no queda nada.
        Assert.Equal(80, Geo.SubtractConvex(slab, [P(20, 0), P(21, 0), P(21, 1), P(20, 1)]).Sum(p => Math.Abs(Geo.SignedArea(p))), 6);
        Assert.Empty(Geo.SubtractConvex(hole, slab));
    }

    [Fact]
    public void The_upper_slab_has_a_hole_over_the_stair()
    {
        var doc = BuildingTemplates.CasaDosPlantas();
        var upper = doc.Levels.OrderBy(l => l.Elevation).Last();
        var mesh = Simulador.Civil.Rendering.BuildingMesh.Build(doc);
        var st = doc.Stairs.Single();
        var mid = st.Steps[st.Steps.Count / 2].Outline;
        var c = mid.Aggregate(P(0, 0), (a, b) => a + b) * (1.0 / mid.Length);
        // Ninguna cara horizontal a la altura del forjado de arriba cubre el centro de la escalera.
        var topFaces = mesh.Faces.Where(f => f.Pts.All(p => Math.Abs(p.Z - upper.Elevation) < 1e-6) && f.Argb == Simulador.Civil.Rendering.BuildingMesh.FloorColor);
        Assert.NotEmpty(topFaces);
        Assert.DoesNotContain(topFaces, f => Geo.Contains(f.Pts.Select(p => p.Xy).ToArray(), c));
    }
}
