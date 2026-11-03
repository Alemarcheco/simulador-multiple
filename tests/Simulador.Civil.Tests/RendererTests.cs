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
        // Cámara mirando hacia +Y (yaw 0): la caja en y = 0 está delante de la de y = 5.
        var mesh = new BuildingMesh();
        void Near() => mesh.AddBox(Square(0, 0, 1), 0, 2, 0xFFFF0000);
        void Far() => mesh.AddBox(Square(0, 5, 2), 0, 4, 0xFF0000FF);
        if (nearFirst) { Near(); Far(); } else { Far(); Near(); }

        var r = new SoftwareRenderer();
        r.Resize(200, 200);
        r.Clear(0xFFFFFFFF);
        var cam = new OrbitCamera { Yaw = 0, Pitch = 0.05 };
        var proj = cam.CreateProjector(mesh.Bounds, 200, 200);
        r.Render(mesh, proj, edges: false);

        var (x, y, _) = proj.Project(new Point3(0, -1, 1));
        int px = r.Pixels[(int)y * 200 + (int)x];
        Assert.True(((px >> 16) & 0xFF) > 100 && (px & 0xFF) < 60, $"Se esperaba la caja roja delante, color {px:X8}");
    }

    [Fact]
    public void Framing_does_not_change_while_rotating()
    {
        var mesh = BuildingMesh.Build(BuildingTemplates.Hotel());
        var b = mesh.Bounds;
        var center = new Point3((b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2, (b.Min.Z + b.Max.Z) / 2);
        var cam = new OrbitCamera();
        double? scale = null;
        for (double yaw = 0; yaw < 2 * Math.PI; yaw += 0.3)
        {
            cam.Yaw = yaw;
            var p = cam.CreateProjector(b, 800, 600);
            var c = p.Project(center);
            Assert.Equal(400, c.X, 6);
            Assert.Equal(300, c.Y, 6);
            // Un segmento vertical de 1 m mide lo mismo en pantalla con cualquier giro.
            double len = p.Project(center).Y - p.Project(new Point3(center.X, center.Y, center.Z + 1)).Y;
            scale ??= len;
            Assert.Equal(scale.Value, len, 6);
        }
    }

    [Fact]
    public void All_templates_fit_inside_the_canvas_from_any_angle()
    {
        foreach (var (name, create) in BuildingTemplates.All)
        {
            var mesh = BuildingMesh.Build(create());
            var cam = new OrbitCamera();
            for (double yaw = 0; yaw < 2 * Math.PI; yaw += 0.5)
            for (double pitch = 0.1; pitch < 1.5; pitch += 0.4)
            {
                cam.Yaw = yaw; cam.Pitch = pitch;
                var p = cam.CreateProjector(mesh.Bounds, 640, 480);
                foreach (var pt in mesh.Faces.SelectMany(f => f.Pts))
                {
                    var s = p.Project(pt);
                    Assert.True(s.X >= 0 && s.X <= 640 && s.Y >= 0 && s.Y <= 480, $"{name}: se sale de la vista");
                }
            }
        }
    }
}
