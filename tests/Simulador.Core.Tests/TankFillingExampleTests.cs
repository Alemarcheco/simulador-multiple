using Simulador.Core.CoSim;
using Simulador.Core.Signals;

namespace Simulador.Core.Tests;

/// <summary>
/// Ejemplo de extremo a extremo del banco de pruebas: PLC → contactor → motor → bomba → tanque,
/// con el nivel del tanque realimentado al PLC. Los modelos son simplificados y viven solo en las pruebas;
/// los reales los aportarán los módulos PLC, eléctrico e hidráulico con sus factorías.
/// </summary>
public class TankFillingExampleTests
{
    private static ComponentState State(string id, TimeSpan t, params (string, object)[] values) =>
        new(id, t, values.ToDictionary(v => v.Item1, v => v.Item2));

    /// <summary>Control todo/nada por nivel: arranca bajo 0,5 m y para sobre 1,8 m. Scan de 10 ms.</summary>
    private sealed class Plc : ISimComponent
    {
        public readonly SignalPort Level = new("PLC1.IW64", SignalKind.AI, SignalUnit.Meter);
        public readonly SignalPort Q0 = new("PLC1.Q0.0", SignalKind.DO, SignalUnit.Bool, 24);
        private TimeSpan _t;
        public string Id => "PLC1";
        public string ModuleId => "plc";
        public IReadOnlyList<ISignalPort> Signals => [Level, Q0];
        public IReadOnlyList<IPowerPort> PowerPorts => [];
        public int StepMultiple => 10;
        public SimPhase Phase => SimPhase.Logic;
        public void Initialize(SimContext context) { }
        public void DoStep(TimeSpan now, TimeSpan dt)
        {
            _t = now + dt;
            if (Level.Value < 0.5) Q0.Value = 1;
            else if (Level.Value > 1.8) Q0.Value = 0;
        }
        public ComponentState Snapshot() => State(Id, _t, ("Q0.0", Q0.Value > 0.5));
        public void Reset() => Q0.Value = 0;
    }

    /// <summary>Contactor: los polos cierran/abren 20 ms después de cambiar la bobina (evento discreto).</summary>
    private sealed class Contactor : ISimComponent
    {
        public readonly SignalPort Coil = new("KM1.A1", SignalKind.DI, SignalUnit.Bool, 24);
        public readonly PowerPort Line = new("KM1.L", PowerDomain.Electrical3Ph, Causality.ImposesFlow) { Effort = 400 };
        public readonly PowerPort Load = new("KM1.T", PowerDomain.Electrical3Ph, Causality.ImposesEffort);
        private ISimulationClock _clock = null!;
        private bool _coil;
        private TimeSpan? _switchAt;
        private TimeSpan _t;
        public bool Closed { get; private set; }
        public TimeSpan? ClosedAt { get; private set; }
        public string Id => "KM1";
        public string ModuleId => "electrico";
        public IReadOnlyList<ISignalPort> Signals => [Coil];
        public IReadOnlyList<IPowerPort> PowerPorts => [Line, Load];
        public int StepMultiple => 1;
        public SimPhase Phase => SimPhase.Logic;
        public void Initialize(SimContext context)
        {
            _clock = context.Clock;
            Coil.Changed += (_, v) =>
            {
                _coil = v > 0.5;
                _switchAt = _clock.Now + TimeSpan.FromMilliseconds(20);
                _clock.RequestEventAt(_switchAt.Value);
            };
        }
        public void DoStep(TimeSpan now, TimeSpan dt)
        {
            _t = now + dt;
            if (_switchAt is { } at && _t >= at)
            {
                Closed = _coil;
                if (Closed) ClosedAt ??= _t;
                _switchAt = null;
            }
            Load.Effort = Closed ? Line.Effort : 0;
            Line.Flow = Closed ? Load.Flow : 0;
        }
        public ComponentState Snapshot() => State(Id, _t, ("cerrado", Closed));
        public void Reset() { Closed = false; _switchAt = null; ClosedAt = null; }
    }

    /// <summary>Motor con curva par–velocidad lineal; lleva la inercia total del eje (la suya + la de la bomba).</summary>
    private sealed class Motor : ISimComponent
    {
        public readonly PowerPort Terminals = new("M1.Bornes", PowerDomain.Electrical3Ph, Causality.ImposesFlow);
        public readonly PowerPort Shaft = new("M1.Eje", PowerDomain.MechanicalRotational, Causality.ImposesFlow);
        private const double SyncSpeed = 157, StallTorque = 60, Inertia = 0.05 + 0.02;
        private TimeSpan _t;
        public double PeakCurrent { get; private set; }
        public string Id => "M1";
        public string ModuleId => "electrico";
        public IReadOnlyList<ISignalPort> Signals => [];
        public IReadOnlyList<IPowerPort> PowerPorts => [Terminals, Shaft];
        public int StepMultiple => 1;
        public SimPhase Phase => SimPhase.Physics;
        public void Initialize(SimContext context) { }
        public void DoStep(TimeSpan now, TimeSpan dt)
        {
            _t = now + dt;
            double w = Shaft.Flow;
            double torque = Terminals.Effort > 0 ? StallTorque * (1 - w / SyncSpeed) : 0;
            w += (torque - Shaft.Effort) / Inertia * dt.TotalSeconds;
            Shaft.Flow = Math.Max(0, w);
            Terminals.Flow = Terminals.Effort > 0 ? 3 + 0.8 * torque : 0;
            PeakCurrent = Math.Max(PeakCurrent, Terminals.Flow);
        }
        public ComponentState Snapshot() => State(Id, _t, ("velocidad", Shaft.Flow), ("corriente", Terminals.Flow));
        public void Reset() { Shaft.Flow = 0; Terminals.Flow = 0; PeakCurrent = 0; }
    }

    /// <summary>Bomba centrífuga: caudal proporcional a la velocidad y menor con la contrapresión; par ∝ w².</summary>
    private sealed class Pump : ISimComponent
    {
        public readonly PowerPort Shaft = new("P1.Eje", PowerDomain.MechanicalRotational, Causality.ImposesEffort);
        public readonly PowerPort Outlet = new("P1.Salida", PowerDomain.Hydraulic, Causality.ImposesFlow);
        private const double NominalSpeed = 150, MaxFlow = 0.01, ShutoffPressure = 3e5;
        private TimeSpan _t;
        public string Id => "P1";
        public string ModuleId => "hidraulica";
        public IReadOnlyList<ISignalPort> Signals => [];
        public IReadOnlyList<IPowerPort> PowerPorts => [Shaft, Outlet];
        public int StepMultiple => 1;
        public SimPhase Phase => SimPhase.Physics;
        public void Initialize(SimContext context) { }
        public void DoStep(TimeSpan now, TimeSpan dt)
        {
            _t = now + dt;
            double w = Shaft.Flow;
            Outlet.Flow = Math.Max(0, MaxFlow * (w / NominalSpeed) * (1 - Outlet.Effort / ShutoffPressure));
            Shaft.Effort = 0.0012 * w * w;
        }
        public ComponentState Snapshot() => State(Id, _t, ("caudal", Outlet.Flow));
        public void Reset() { Outlet.Flow = 0; Shaft.Effort = 0; }
    }

    /// <summary>Tanque de 1 m² con un consumo fijo de 2 L/s; impone la presión hidrostática en su entrada.</summary>
    private sealed class Tank : ISimComponent
    {
        public readonly PowerPort Inlet = new("TK1.Entrada", PowerDomain.Hydraulic, Causality.ImposesEffort);
        public readonly SignalPort LevelSensor = new("TK1.LT1", SignalKind.AO, SignalUnit.Meter);
        private const double Area = 1, Demand = 0.002, StartLevel = 0.2;
        private double _rho = 1000;
        private TimeSpan _t;
        public double Level { get; private set; } = StartLevel;
        public string Id => "TK1";
        public string ModuleId => "hidraulica";
        public IReadOnlyList<ISignalPort> Signals => [LevelSensor];
        public IReadOnlyList<IPowerPort> PowerPorts => [Inlet];
        public int StepMultiple => 10;
        public SimPhase Phase => SimPhase.Physics;
        public void Initialize(SimContext context)
        {
            if (context.Fluids.TryGetValue("agua", out var f)) _rho = f.Density;
            Publish();
        }
        public void DoStep(TimeSpan now, TimeSpan dt)
        {
            _t = now + dt;
            double outflow = Level > 0 ? Demand : 0;
            Level = Math.Max(0, Level + (Inlet.Flow - outflow) / Area * dt.TotalSeconds);
            Publish();
        }
        private void Publish()
        {
            Inlet.Effort = _rho * 9.81 * Level;
            LevelSensor.Value = Level;
        }
        public ComponentState Snapshot() => State(Id, _t, ("nivel", Level));
        public void Reset() { Level = StartLevel; Publish(); }
    }

    private sealed record Plant(CoSimScheduler S, Plc Plc, Contactor Km, Motor M, Pump P, Tank Tk);

    private static Plant Build()
    {
        var s = new CoSimScheduler(fluids: new Dictionary<string, FluidProperties>
        {
            ["agua"] = new(Density: 1000, Viscosity: 1e-3, BulkModulus: 2.2e9),
        });
        var plc = new Plc(); var km = new Contactor(); var m = new Motor(); var p = new Pump(); var tk = new Tank();
        s.Add(plc); s.Add(km); s.Add(m); s.Add(p); s.Add(tk);

        Assert.Empty(s.Connect("PLC1.Q0.0", "KM1.A1").Warnings);
        Assert.Empty(s.Connect("TK1.LT1", "PLC1.IW64").Warnings);
        Assert.True(s.Connect(km.Load, m.Terminals).Connected);
        Assert.True(s.Connect(m.Shaft, p.Shaft).Connected);
        Assert.True(s.Connect(p.Outlet, tk.Inlet).Connected);
        return new Plant(s, plc, km, m, p, tk);
    }

    [Fact]
    public void Plc_starts_pump_and_tank_fills_then_stops_at_high_level()
    {
        var plant = Build();

        plant.S.RunFor(TimeSpan.FromSeconds(1));
        Assert.True(plant.Km.Closed);
        // El PLC activa Q0.0 en su primer scan (que empieza en t = 0) y los polos cierran 20 ms después,
        // justo en el instante pedido con RequestEventAt aunque el PLC solo corra cada 10 ms.
        Assert.Equal(TimeSpan.FromMilliseconds(20), plant.Km.ClosedAt);
        Assert.True(plant.M.Shaft.Flow > 100, $"El motor debería estar cerca de su velocidad, va a {plant.M.Shaft.Flow:F1} rad/s");
        Assert.True(plant.M.PeakCurrent > 40, "Debe verse el pico de corriente de arranque.");

        double before = plant.Tk.Level;
        plant.S.RunFor(TimeSpan.FromSeconds(30));
        Assert.True(plant.Tk.Level > before, "El tanque debería estar llenándose.");

        // Llenado hasta 1,8 m: el PLC para la bomba.
        var limit = TimeSpan.FromMinutes(10);
        while (plant.Km.Closed && plant.S.Now < limit) plant.S.RunFor(TimeSpan.FromSeconds(1));
        Assert.False(plant.Km.Closed, "El PLC debería haber parado la bomba al llegar al nivel alto.");
        Assert.InRange(plant.Tk.Level, 1.75, 1.9);

        // Sin bomba, el consumo vacía el tanque.
        double high = plant.Tk.Level;
        plant.S.RunFor(TimeSpan.FromSeconds(30));
        Assert.True(plant.Tk.Level < high);
    }

    [Fact]
    public void Same_setup_gives_same_result()
    {
        var a = Build(); var b = Build();
        a.S.RunFor(TimeSpan.FromSeconds(20));
        b.S.RunFor(TimeSpan.FromSeconds(20));
        Assert.Equal(a.Tk.Level, b.Tk.Level);
        Assert.Equal(a.M.Shaft.Flow, b.M.Shaft.Flow);
    }

    [Fact]
    public void Snapshot_has_what_the_animated_view_needs()
    {
        var plant = Build();
        plant.S.RunFor(TimeSpan.FromSeconds(2));
        var frame = new SimFrame(plant.S.Now, plant.S.Snapshot());

        Assert.True(frame["KM1"]!.Get<bool>("cerrado"));
        Assert.True(frame["M1"]!.Get<double>("velocidad") > 0);
        Assert.True(frame["P1"]!.Get<double>("caudal") > 0);
        Assert.True(frame["TK1"]!.Get<double>("nivel") > 0);
    }
}
