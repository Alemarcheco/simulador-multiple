using System.Numerics;
using Simulador.Hidraulica.Model;
using Simulador.Hidraulica.Persistence;
using Simulador.Hidraulica.Solver;
using Simulador.Hidraulica.View3D;

namespace Simulador.Hidraulica.Tests;

public class SamplesAnd3DTests
{
    public static TheoryData<string> SampleNames()
    {
        var data = new TheoryData<string>();
        foreach (var s in Samples.All) data.Add(s.Name);
        return data;
    }

    private static SampleInfo Sample(string name) => Samples.All.Single(s => s.Name == name);

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Every_sample_is_valid_and_simulates_ten_minutes_cleanly(string name)
    {
        var net = Sample(name).Create();
        Assert.True(net.Validate().IsValid, string.Join("; ", net.Validate().Errors));
        Assert.False(string.IsNullOrWhiteSpace(net.Description));
        var sim = new HydraulicSimulation(net);
        sim.Step(TimeSpan.FromMinutes(10));
        Assert.True(sim.LastSolveConverged);
        Assert.Empty(sim.Warnings);
        foreach (var t in net.Nodes.OfType<Tank>()) Assert.InRange(t.Level, 0, t.MaxLevel);
        Assert.Contains(net.Links, l => Math.Abs(sim.FlowIn(l.Id)) > 1e-4);
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Every_sample_round_trips_and_builds_a_3D_scene(string name)
    {
        var net = Sample(name).Create();
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(net));
        Assert.Equal(net.Description, back.Description);

        var scene = SceneBuilder.Build(back, new HydraulicSimulation(back), 1.0);
        Assert.True(scene.Triangles.Count > 500);
        foreach (var t in back.Nodes.OfType<Tank>())
            Assert.Contains(scene.Labels, l => l.Text.StartsWith(t.Id));
        foreach (var p in back.Links.OfType<Pump>())
            Assert.Contains(scene.Labels, l => l.Text.StartsWith(p.Id));
    }

    [Fact]
    public void Second_parallel_pump_raises_flow_but_does_not_double_it()
    {
        var net = Samples.ParallelPumps();
        var sim = new HydraulicSimulation(net);
        double one = sim.FlowIn("P6");
        sim.SetPumpRunning("B2", true);
        sim.Refresh();
        double two = sim.FlowIn("P6");
        Assert.True(two > one * 1.2 && two < one * 2, $"una: {one}, dos: {two}");
        Assert.Equal(sim.FlowIn("B1"), sim.FlowIn("B2"), 6);
    }

    [Fact]
    public void Gravity_transfer_moves_water_from_high_to_low_tank()
    {
        var net = Samples.GravityTransfer();
        var sim = new HydraulicSimulation(net);
        var t1 = (Tank)net.FindNode("T1")!;
        var t2 = (Tank)net.FindNode("T2")!;
        double total = t1.Volume + t2.Volume;
        sim.Step(TimeSpan.FromMinutes(5));
        Assert.True(t1.Level < 2.8 && t2.Level > 0.2);
        Assert.Equal(total, t1.Volume + t2.Volume, 2); // el agua que sale de T1 llega a T2
    }

    [Fact]
    public void Pipe_path_starts_and_ends_at_its_nodes_with_vertical_riser()
    {
        var net = Samples.PumpingToTank();
        var path = SceneBuilder.PipePath(net, net.FindLink("P2")!);
        var n2 = net.FindNode("N2")!;
        var t1 = (Tank)net.FindNode("T1")!;
        Assert.Equal(SceneBuilder.PlanPosition(n2.X, n2.Y, n2.Elevation), path[0]);
        Assert.Equal(SceneBuilder.PlanPosition(t1.X, t1.Y, t1.Elevation), path[^1]);
        Assert.Equal(path[^2].X, path[^1].X);
        Assert.Equal(path[^2].Y, path[^1].Y);
        Assert.Equal(0f, path[^2].Z);
    }

    [Theory]
    [InlineData(-60, 28)]
    [InlineData(30, 10)]
    [InlineData(150, 60)]
    [InlineData(-170, 5)]
    public void Depth_buffer_shows_the_nearest_surface_from_any_angle(double yaw, double pitch)
    {
        // Dos cubos en línea con la cámara: siempre debe verse el más cercano, se dibuje en el orden que se dibuje.
        var cam = new OrbitCamera { Target = Vector3.Zero, Yaw = yaw, Pitch = pitch, Distance = 20 };
        var (eye, _, _, forward) = cam.Frame();
        var near = eye + forward * 8;
        var far = eye + forward * 14;
        const uint red = 0xFFFF0000, blue = 0xFF0000FF;
        foreach (bool farFirst in new[] { true, false })
        {
            var scene = new Scene();
            if (farFirst) SceneBuilder.Box(scene, far, new Vector3(3), blue);
            SceneBuilder.Box(scene, near, new Vector3(1), red);
            if (!farFirst) SceneBuilder.Box(scene, far, new Vector3(3), blue);
            var r = new Rasterizer();
            r.Resize(64, 48);
            r.Render(scene, cam);
            uint center = r.Pixels[24 * 64 + 32];
            Assert.True(((center >> 16) & 0xFF) > 0 && (center & 0xFF) == 0, $"Se ve {center:X8} en vez del cubo rojo cercano");
            Assert.InRange(r.Depth[24 * 64 + 32], 7f, 8f);
        }
    }

    [Fact]
    public void Triangles_crossing_the_camera_plane_are_clipped_not_lost()
    {
        var cam = new OrbitCamera { Target = Vector3.Zero, Yaw = 0, Pitch = 10, Distance = 10 };
        var scene = new Scene();
        // Suelo enorme que pasa por debajo y por detrás de la cámara.
        SceneBuilder.Quad(scene, new(-500, -500, -1), new(500, -500, -1), new(500, 500, -1), new(-500, 500, -1), 0xFF00FF00);
        var r = new Rasterizer();
        r.Resize(80, 60);
        r.Render(scene, cam);
        uint px = r.Pixels[59 * 80 + 40]; // la parte de abajo de la imagen es suelo (verde puro sombreado)
        Assert.True(((px >> 8) & 0xFF) > 0 && ((px >> 16) & 0xFF) == 0 && (px & 0xFF) == 0, $"{px:X8}");
    }
}
