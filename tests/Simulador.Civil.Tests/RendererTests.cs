using Simulador.Civil.Model;
using Simulador.Civil.Rendering;
using Simulador.Core.Building;

namespace Simulador.Civil.Tests;

public class RendererTests
{
    private static Point2[] Square(double cx, double cy, double half) =>
        [new(cx - half, cy - half), new(cx + half, cy - half), new(cx + half, cy + half), new(cx - half, cy + half)];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nearer_box_hides_farther_box_whatever_the_drawing_order(bool nearFirst)
    {
        // Cámara en -Y mirando hacia +Y: la caja en y = 0 está delante de la de y = 5.
        var mesh = new BuildingMesh();
        void Near() => mesh.AddBox(Square(0, 0, 1), 0, 2, 0xFFFF0000);
        void Far() => mesh.AddBox(Square(0, 5, 2), 0, 4, 0xFF0000FF);
        if (nearFirst) { Near(); Far(); } else { Far(); Near(); }

        var r = BuildingRenderer.CreateRasterizer();
        r.Resize(200, 200);
        var cam = BuildingRenderer.CreateCamera();
        cam.Yaw = -90; cam.Pitch = 3;
        var (min, max) = BuildingRenderer.Bounds(mesh);
        cam.Fit(min, max);
        BuildingRenderer.Render(r, mesh, cam, edges: false);

        var p = r.Project(new(0, -1, 1))!.Value;
        uint px = r.Pixels[(int)p.Y * 200 + (int)p.X];
        Assert.True(((px >> 16) & 0xFF) > 100 && (px & 0xFF) < 60, $"Se esperaba la caja roja delante, color {px:X8}");
    }

    [Fact]
    public void Framing_does_not_change_while_rotating()
    {
        var mesh = BuildingMesh.Build(BuildingTemplates.Hotel());
        var (min, max) = BuildingRenderer.Bounds(mesh);
        var center = (min + max) / 2;
        var cam = BuildingRenderer.CreateCamera();
        BuildingRenderer.Fit(cam, min, max);
        var r = BuildingRenderer.CreateRasterizer();
        r.Resize(800, 600);
        double? scale = null;
        for (double yaw = 0; yaw < 360; yaw += 17)
        {
            cam.Yaw = yaw;
            r.Begin(cam);
            var c = r.Project(center)!.Value;
            Assert.Equal(400, c.X, 3);
            Assert.Equal(300, c.Y, 3);
            // Un segmento vertical de 1 m mide lo mismo en pantalla con cualquier giro.
            double len = c.Y - r.Project(center + System.Numerics.Vector3.UnitZ)!.Value.Y;
            scale ??= len;
            Assert.Equal(scale.Value, len, 3);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void All_templates_fit_inside_the_canvas_from_any_angle(bool perspective)
    {
        var r = BuildingRenderer.CreateRasterizer();
        r.Resize(640, 480);
        foreach (var (name, create) in BuildingTemplates.All)
        {
            var mesh = BuildingMesh.Build(create());
            var (min, max) = BuildingRenderer.Bounds(mesh);
            var cam = BuildingRenderer.CreateCamera(perspective);
            BuildingRenderer.Fit(cam, min, max);
            for (double yaw = 0; yaw < 360; yaw += 30)
            for (double pitch = -85; pitch < 90; pitch += 20)
            {
                cam.Yaw = yaw; cam.Pitch = pitch;
                r.Begin(cam);
                foreach (var pt in mesh.Faces.SelectMany(f => f.Pts))
                {
                    var s = r.Project(BuildingRenderer.ToVector(pt))!.Value;
                    Assert.True(s.X >= 0 && s.X <= 640 && s.Y >= 0 && s.Y <= 480, $"{name}: se sale de la vista");
                }
            }
        }
    }

    [Fact]
    public void Orbit_goes_below_the_ground_to_show_the_underside()
    {
        var cam = BuildingRenderer.CreateCamera();
        cam.Orbit(0, -200);
        Assert.True(cam.Pitch < -80);

        // Desde abajo se ve la losa de la planta baja (no queda el fondo vacío).
        var mesh = BuildingMesh.Build(BuildingTemplates.All.First().Create());
        var (min, max) = BuildingRenderer.Bounds(mesh);
        BuildingRenderer.Fit(cam, min, max);
        cam.Pitch = -60;
        var r = BuildingRenderer.CreateRasterizer();
        r.Resize(200, 200);
        BuildingRenderer.Render(r, mesh, cam, edges: false);
        Assert.NotEqual(BuildingRenderer.Background, r.Pixels[100 * 200 + 100]);
    }

    [Fact]
    public void Perspective_draws_the_far_side_shorter_than_the_near_side()
    {
        // Mirando de frente y desde arriba, el borde trasero de una losa no debe verse más ancho que el delantero.
        var cam = BuildingRenderer.CreateCamera();
        cam.Yaw = -90; cam.Pitch = 35;
        BuildingRenderer.Fit(cam, new(-5, -5, 0), new(5, 5, 3));
        var r = BuildingRenderer.CreateRasterizer();
        r.Resize(400, 300);
        r.Begin(cam);
        double Width(float y) => r.Project(new(5, y, 3))!.Value.X - r.Project(new(-5, y, 3))!.Value.X;
        Assert.True(Width(5) < Width(-5) * 0.95, $"trasero {Width(5):0.0} px, delantero {Width(-5):0.0} px");
    }
}
