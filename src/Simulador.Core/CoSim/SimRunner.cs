// Contrato: /mnt/project-files/contratos/co-simulacion.md
using System.Diagnostics;

namespace Simulador.Core.CoSim;

/// <summary>
/// Ejecuta un <see cref="CoSimScheduler"/> en un hilo propio, a velocidad controlada
/// (1× = tiempo real, 0,1× cámara lenta, 10× acelerado, o lo más rápido posible).
/// La interfaz nunca toca el planificador: lee <see cref="Latest"/>, la última instantánea publicada.
/// </summary>
public sealed class SimRunner : IDisposable
{
    private readonly CoSimScheduler _scheduler;
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _paused = true;
    private int _pendingSteps;
    private double _speed = 1.0;

    public SimRunner(CoSimScheduler scheduler, TimeSpan? publishEvery = null)
    {
        _scheduler = scheduler;
        PublishEvery = publishEvery ?? TimeSpan.FromMilliseconds(16);
        Latest = new SimFrame(TimeSpan.Zero, _scheduler.Snapshot());
    }

    /// <summary>Cada cuánto tiempo de simulación se publica una instantánea (por defecto ~60 por segundo simulado).</summary>
    public TimeSpan PublishEvery { get; }

    /// <summary>Factor de velocidad. <see cref="double.PositiveInfinity"/> = lo más rápido posible.</summary>
    public double Speed
    {
        get { lock (_gate) return _speed; }
        set
        {
            if (!(value > 0)) throw new ArgumentOutOfRangeException(nameof(value), "La velocidad debe ser positiva.");
            lock (_gate) { _speed = value; _rebase = true; }
        }
    }

    public bool IsPaused => _paused;

    /// <summary>Última instantánea publicada. Se puede leer desde el hilo de la interfaz.</summary>
    public SimFrame Latest { get; private set; }

    /// <summary>Se dispara en el hilo de simulación cada vez que se publica una instantánea.</summary>
    public event EventHandler<SimFrame>? FramePublished;

    /// <summary>Si algún componente lanza una excepción, la simulación se pausa y se publica aquí.</summary>
    public event EventHandler<Exception>? Faulted;

    private bool _rebase = true;

    public void Play()
    {
        EnsureThread();
        lock (_gate) { _paused = false; _rebase = true; Monitor.PulseAll(_gate); }
    }

    public void Pause()
    {
        lock (_gate) { _paused = true; Monitor.PulseAll(_gate); }
    }

    /// <summary>Estando en pausa, avanza <paramref name="steps"/> pasos maestros.</summary>
    public void StepOnce(int steps = 1)
    {
        EnsureThread();
        lock (_gate) { _pendingSteps += steps; Monitor.PulseAll(_gate); }
    }

    private void EnsureThread()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "CoSim" };
        _thread.Start();
    }

    private void Loop()
    {
        var wall = Stopwatch.StartNew();
        TimeSpan simBase = TimeSpan.Zero, wallBase = TimeSpan.Zero;
        TimeSpan lastPublish = _scheduler.Now;

        try
        {
            while (!_stop)
            {
                int manual;
                double speed;
                lock (_gate)
                {
                    while (!_stop && _paused && _pendingSteps == 0) Monitor.Wait(_gate);
                    if (_stop) break;
                    manual = _pendingSteps;
                    _pendingSteps = 0;
                    speed = _speed;
                    if (_rebase) { simBase = _scheduler.Now; wallBase = wall.Elapsed; _rebase = false; }
                }

                if (manual > 0)
                {
                    for (int i = 0; i < manual; i++) _scheduler.StepOnce();
                    Publish();
                    lastPublish = _scheduler.Now;
                    continue;
                }

                // Cuánto tiempo simulado debería haber pasado según el reloj de pared y la velocidad.
                TimeSpan budget = double.IsPositiveInfinity(speed)
                    ? _scheduler.Now + PublishEvery
                    : simBase + (wall.Elapsed - wallBase) * speed;

                if (_scheduler.Now + _scheduler.Step > budget)
                {
                    Thread.Sleep(1);
                    continue;
                }

                while (_scheduler.Now + _scheduler.Step <= budget && !_paused && !_stop)
                {
                    _scheduler.StepOnce();
                    if (_scheduler.Now - lastPublish >= PublishEvery)
                    {
                        Publish();
                        lastPublish = _scheduler.Now;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _paused = true;
            Publish();
            Faulted?.Invoke(this, ex);
        }
    }

    private void Publish()
    {
        var frame = new SimFrame(_scheduler.Now, _scheduler.Snapshot());
        Latest = frame;
        FramePublished?.Invoke(this, frame);
    }

    public void Dispose()
    {
        lock (_gate) { _stop = true; Monitor.PulseAll(_gate); }
        _thread?.Join();
    }
}

/// <summary>Estado de todo el banco de pruebas en un instante.</summary>
public sealed record SimFrame(TimeSpan Time, IReadOnlyList<ComponentState> Components)
{
    public ComponentState? this[string componentId] => Components.FirstOrDefault(c => c.ComponentId == componentId);
}
