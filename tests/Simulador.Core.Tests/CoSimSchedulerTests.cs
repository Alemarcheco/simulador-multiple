using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Core.Tests;

public class CoSimSchedulerTests
{
    private sealed class Probe(string id, SimPhase phase, int stepMultiple, List<string> log) : ISimComponent
    {
        public string Id { get; } = id;
        public string ModuleId => "prueba";
        public IReadOnlyList<ISignalPort> Signals { get; init; } = [];
        public IReadOnlyList<IPowerPort> PowerPorts { get; init; } = [];
        public int StepMultiple { get; } = stepMultiple;
        public SimPhase Phase { get; } = phase;
        public List<(TimeSpan Now, TimeSpan Dt)> Steps { get; } = [];
        public int Initialized { get; private set; }
        public Action<TimeSpan, TimeSpan>? OnStep { get; init; }

        public void Initialize(SimContext context) => Initialized++;
        public void DoStep(TimeSpan now, TimeSpan dt)
        {
            Steps.Add((now, dt));
            log.Add(Id);
            OnStep?.Invoke(now, dt);
        }
        public ComponentState Snapshot() => new(Id, TimeSpan.Zero, new Dictionary<string, object> { ["pasos"] = Steps.Count });
        public void Reset() => Steps.Clear();
    }

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Runs_components_in_phase_order_then_registration_order()
    {
        var log = new List<string>();
        var s = new CoSimScheduler();
        s.Add(new Probe("fisica", SimPhase.Physics, 1, log));
        s.Add(new Probe("salidas", SimPhase.Outputs, 1, log));
        s.Add(new Probe("entradas", SimPhase.Inputs, 1, log));
        s.Add(new Probe("logica", SimPhase.Logic, 1, log));
        s.Add(new Probe("fisica2", SimPhase.Physics, 1, log));

        s.StepOnce();

        Assert.Equal(["entradas", "logica", "fisica", "fisica2", "salidas"], log);
    }

    [Fact]
    public void Step_multiple_runs_slow_components_every_n_ticks_with_full_dt()
    {
        var log = new List<string>();
        var s = new CoSimScheduler();
        var fast = new Probe("rapido", SimPhase.Physics, 1, log);
        var slow = new Probe("lento", SimPhase.Physics, 10, log);
        s.Add(fast);
        s.Add(slow);

        s.RunFor(Ms(30));

        Assert.Equal(30, fast.Steps.Count);
        Assert.Equal(3, slow.Steps.Count);
        Assert.Equal((Ms(1), Ms(10)), slow.Steps[1]);
        Assert.Equal(Ms(30), s.Now);
        Assert.Equal(1, fast.Initialized);
    }

    [Fact]
    public void Requested_event_lands_exactly_and_syncs_everyone()
    {
        var log = new List<string>();
        var s = new CoSimScheduler();
        var slow = new Probe("lento", SimPhase.Physics, 10, log);
        s.Add(slow);
        s.StepOnce();                       // t = 1 ms
        s.RequestEventAt(Ms(3.25));

        s.StepOnce();                       // 2 ms
        s.StepOnce();                       // 3 ms
        s.StepOnce();                       // 3,25 ms: evento

        Assert.Equal(Ms(3.25), s.Now);
        Assert.Equal((Ms(1), Ms(2.25)), slow.Steps[^1]);
    }

    [Fact]
    public void Power_ports_exchange_effort_and_flow()
    {
        var log = new List<string>();
        var s = new CoSimScheduler();
        var red = new PowerPort("Q1.Salida", PowerDomain.Electrical3Ph, Causality.ImposesEffort) { Effort = 400 };
        var motor = new PowerPort("M1.Bornes", PowerDomain.Electrical3Ph, Causality.ImposesFlow);

        var result = s.Connect(red, motor);
        Assert.True(result.Connected);
        Assert.Equal(400, motor.Effort);

        s.Add(new Probe("M1", SimPhase.Physics, 1, log) { PowerPorts = [motor], OnStep = (_, _) => motor.Flow = 7.5 });
        s.StepOnce();

        Assert.Equal(7.5, red.Flow);
    }

    [Fact]
    public void Rejects_power_links_with_wrong_domain_or_causality()
    {
        var s = new CoSimScheduler();
        var eje = new PowerPort("M1.Eje", PowerDomain.MechanicalRotational, Causality.ImposesFlow);
        var otro = new PowerPort("P1.Eje", PowerDomain.MechanicalRotational, Causality.ImposesFlow);
        var tubo = new PowerPort("T1.Brida", PowerDomain.Hydraulic, Causality.ImposesEffort);

        Assert.False(s.Connect(eje, otro).Connected);
        Assert.False(s.Connect(eje, tubo).Connected);
    }

    [Fact]
    public void Reset_returns_to_zero_and_reinitializes()
    {
        var log = new List<string>();
        var s = new CoSimScheduler();
        var p = new Probe("p", SimPhase.Logic, 1, log);
        s.Add(p);
        s.RunFor(Ms(5));

        s.Reset();
        s.StepOnce();

        Assert.Equal(Ms(1), s.Now);
        Assert.Equal(2, p.Initialized);
        Assert.Single(p.Steps);
    }

    [Fact]
    public void Runner_single_steps_while_paused()
    {
        var s = new CoSimScheduler();
        s.Add(new Probe("p", SimPhase.Logic, 1, []));
        using var runner = new SimRunner(s);
        var done = new ManualResetEventSlim();
        runner.FramePublished += (_, f) => { if (f.Time >= Ms(5)) done.Set(); };

        runner.StepOnce(5);

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(Ms(5), runner.Latest.Time);
        Assert.True(runner.IsPaused);
    }

    [Fact]
    public void Runner_runs_fast_as_possible_and_pauses()
    {
        var s = new CoSimScheduler();
        s.Add(new Probe("p", SimPhase.Logic, 1, []));
        using var runner = new SimRunner(s) { Speed = double.PositiveInfinity };
        var done = new ManualResetEventSlim();
        runner.FramePublished += (_, f) => { if (f.Time >= TimeSpan.FromSeconds(1)) done.Set(); };

        runner.Play();
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        runner.Pause();

        Assert.True(runner.Latest.Time >= TimeSpan.FromSeconds(1));
    }
}
