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
        cam.Fit(min, max);
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

    [Fact]
    public void All_templates_fit_inside_the_canvas_from_any_angle()
    {
        var r = BuildingRenderer.CreateRasterizer();
        r.Resize(640, 480);
        foreach (var (name, create) in BuildingTemplates.All)
        {
            var mesh = BuildingMesh.Build(create());
            var (min, max) = BuildingRenderer.Bounds(mesh);
            var cam = BuildingRenderer.CreateCamera();
            cam.Fit(min, max);
            for (double yaw = 0; yaw < 360; yaw += 30)
            for (double pitch = 5; pitch < 86; pitch += 20)
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
}
