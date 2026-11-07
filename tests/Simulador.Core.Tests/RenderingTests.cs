using System.Numerics;
using Simulador.Core.Rendering;

namespace Simulador.Core.Tests;

public class RenderingTests
{
    private static void Quad(List<Triangle> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, uint color)
    {
        t.Add(new(a, b, c, color));
        t.Add(new(a, c, d, color));
    }

    /// <summary>Cuadrado frente a la cámara (que mira hacia +X desde -X) a la distancia x.</summary>
    private static void Wall(List<Triangle> t, float x, float half, uint color) =>
        Quad(t, new(x, -half, -half), new(x, half, -half), new(x, half, half), new(x, -half, half), color);

    private static OrbitCamera Camera(bool ortho) => new() { Target = Vector3.Zero, Yaw = 180, Pitch = 0, Distance = 10, Orthographic = ortho };

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void Nearest_opaque_surface_wins_in_both_projections(bool ortho, int ss)
    {
        foreach (bool nearFirst in new[] { true, false })
        {
            var tris = new List<Triangle>();
            if (nearFirst) Wall(tris, -2, 1, 0xFFFF0000);
            Wall(tris, 3, 4, 0xFF0000FF);
            if (!nearFirst) Wall(tris, -2, 1, 0xFFFF0000);
            var r = new ZBufferRasterizer { Supersampling = ss };
            r.Resize(64, 48);
            r.Render(tris, Camera(ortho));
            Assert.Equal(64 * 48, r.Pixels.Length);
            uint c = r.Pixels[24 * 64 + 32];
            Assert.True(((c >> 16) & 0xFF) > 0 && (c & 0xFF) == 0, $"{c:X8}");
        }
    }

    [Fact]
    public void Transparent_triangles_blend_and_do_not_hide_what_is_behind()
    {
        var tris = new List<Triangle>();
        Wall(tris, -2, 1, 0x80FFFFFF); // vidrio delante
        Wall(tris, 3, 4, 0xFF0000FF);
        var r = new ZBufferRasterizer();
        r.Resize(64, 48);
        r.Render(tris, Camera(false));
        uint c = r.Pixels[24 * 64 + 32];
        Assert.True((c & 0xFF) > 100 && ((c >> 16) & 0xFF) > 60, $"{c:X8}");
        // La profundidad es la de la pared opaca (13 m), no la del vidrio.
        Assert.InRange(r.Depth[24 * 64 + 32], 12.5f, 13.5f);
    }

    [Fact]
    public void Project_and_IsVisible_tell_hidden_points_apart()
    {
        var tris = new List<Triangle>();
        Wall(tris, -2, 1, 0xFFFF0000);
        var r = new ZBufferRasterizer { Supersampling = 2 };
        r.Resize(80, 60);
        r.Render(tris, Camera(false));
        var front = r.Project(new(-3, 0, 0))!.Value;
        var behind = r.Project(new(2, 0, 0))!.Value;
        Assert.Equal(40, front.X, 2);
        Assert.Equal(30, front.Y, 2);
        Assert.True(r.IsVisible(front.X, front.Y, front.Depth));
        Assert.False(r.IsVisible(behind.X, behind.Y, behind.Depth));
        Assert.Null(r.Project(new(-20, 0, 0))); // detrás de la cámara
    }

    [Fact]
    public void Back_faces_can_be_culled()
    {
        var r = new ZBufferRasterizer();
        r.Resize(40, 40);
        r.Begin(Camera(true), 0xFFFFFFFF, 0xFFFFFFFF);
        // Cuadrado orientado hacia +X (de espaldas a la cámara, que está en -X).
        Vector3[] away = [new(0, -1, -1), new(0, 1, -1), new(0, 1, 1), new(0, -1, 1)];
        r.DrawPolygon(away, 0xFF000000, cullBackFaces: true);
        r.End();
        Assert.Equal(0xFFFFFFFFu, r.Pixels[20 * 40 + 20]);
        r.Begin(Camera(true), 0xFFFFFFFF, 0xFFFFFFFF);
        r.DrawPolygon(away.Reverse().ToArray(), 0xFF000000, cullBackFaces: true);
        r.End();
        Assert.NotEqual(0xFFFFFFFFu, r.Pixels[20 * 40 + 20]);
    }

    [Fact]
    public void Fit_keeps_the_whole_box_in_view_from_any_angle_in_orthographic()
    {
        var min = new Vector3(-10, -3, 0);
        var max = new Vector3(14, 6, 9);
        var cam = new OrbitCamera { Orthographic = true };
        cam.Fit(min, max);
        var r = new ZBufferRasterizer();
        r.Resize(400, 300);
        for (double yaw = 0; yaw < 360; yaw += 20)
        for (double pitch = -5; pitch <= 89; pitch += 15)
        {
            cam.Yaw = yaw; cam.Pitch = pitch;
            r.Begin(cam);
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
                var s = r.Project(p)!.Value;
                Assert.InRange(s.X, 0, 400);
                Assert.InRange(s.Y, 0, 300);
            }
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void Pixel_light_receives_the_world_point_and_normal_of_each_pixel(bool ortho, int ss)
    {
        var tris = new List<Triangle>();
        Wall(tris, 3, 12, 0xFFFFFFFF); // pared grande en x = 3, de cara a la cámara (que mira hacia +X)
        var r = new ZBufferRasterizer { Supersampling = ss, Ambient = 1 };
        r.Resize(40, 30);
        var seen = new List<(Vector3 P, Vector3 N)>();
        // Mitad izquierda de la imagen (Y del mundo > 0, porque la cámara mira hacia +X) a la sombra.
        r.PixelLight = (p, n) => { seen.Add((p, n)); return p.Y > 0 ? 0.5f : 1f; };
        r.Render(tris, Camera(ortho));
        Assert.NotEmpty(seen);
        Assert.All(seen, s => Assert.Equal(3, s.P.X, 2));
        Assert.All(seen, s => Assert.True(s.N.X < -0.99f, $"normal {s.N}")); // hacia la cámara
        uint left = r.Pixels[15 * 40 + 12], right = r.Pixels[15 * 40 + 28];
        Assert.True(((left >> 16) & 0xFF) is >= 120 and <= 135, $"izquierda {left:X8}, derecha {right:X8}");
        Assert.Equal(255u, (right >> 16) & 0xFF);
    }
}
