using System.Numerics;

namespace Simulador.Electrico.Engine.Sim;

/// <summary>
/// Motor de inducción trifásico de jaula con su circuito equivalente por fase (R1, X1, Xm, R2/s, X2).
/// Se resuelve por componentes simétricas: la secuencia directa ve el deslizamiento s y la inversa 2 − s.
/// Así salen solos la inversión de giro al cruzar dos fases, el funcionamiento con una fase perdida
/// (sin par de arranque y corriente alta) y la corriente de arranque.
/// </summary>
public sealed class InductionMotor
{
    private static readonly Complex A = Complex.FromPolarCoordinates(1, 2 * Math.PI / 3);

    public double RatedPowerW { get; }
    public double WindingVoltage { get; }
    public double WindingCurrent { get; }
    public double RatedFrequency { get; }
    public int PolePairs { get; }
    public double RatedSpeedRadS { get; }
    public double RatedTorque { get; }
    public double Inertia { get; }
    /// <summary>Inercia de la máquina accionada en el banco de pruebas (kg·m²), que se suma a la del motor.</summary>
    public double ExternalInertia { get; set; }
    /// <summary>Par resistente que devuelve la carga del banco de pruebas (N·m, positivo = frena).</summary>
    public double ExternalLoadTorque { get; set; }
    private double TotalInertia => Inertia + Math.Max(ExternalInertia, 0);
    public double LoadFraction { get; set; }
    public bool QuadraticLoad { get; set; }

    // Parámetros del circuito equivalente en ohmios a la frecuencia asignada.
    private readonly double _r1, _x1, _x2, _xm;
    private double _r2;

    /// <summary>Velocidad mecánica (rad/s). Positiva = giro en sentido directo (secuencia L1-L2-L3 en U-V-W).</summary>
    public double Omega { get; private set; }
    public double Torque { get; private set; }
    public double LoadTorque { get; private set; }
    public double Frequency { get; set; }

    public double Rpm => Omega * 60 / (2 * Math.PI);
    public double SyncOmega => 2 * Math.PI * Frequency / PolePairs;
    public double Slip => SyncOmega <= 0 ? 1 : (SyncOmega - Omega) / SyncOmega;

    public InductionMotor(double powerKw, double windingVoltage, double rpm, double cosPhi, double efficiency,
        double startCurrentRatio, double frequency, double inertia, double loadPercent, bool quadraticLoad)
    {
        RatedPowerW = powerKw * 1000;
        WindingVoltage = windingVoltage;
        RatedFrequency = frequency;
        Frequency = frequency;
        Inertia = Math.Max(inertia, 1e-5);
        LoadFraction = loadPercent / 100;
        QuadraticLoad = quadraticLoad;
        PolePairs = Math.Max(1, (int)Math.Floor(60 * frequency / rpm));
        var syncRpm = 60 * frequency / PolePairs;
        var sn = Math.Clamp((syncRpm - rpm) / syncRpm, 0.005, 0.2);
        RatedSpeedRadS = rpm * 2 * Math.PI / 60;
        RatedTorque = RatedPowerW / RatedSpeedRadS;
        WindingCurrent = RatedPowerW / (3 * windingVoltage * efficiency * cosPhi);

        var zb = windingVoltage / WindingCurrent;
        _r1 = 0.03 * zb;
        _xm = 3.0 * zb;
        // R2 se ajusta para que el par en el deslizamiento asignado sea el par asignado;
        // X1 + X2 se ajusta para dar la corriente de arranque pedida.
        _r2 = sn * zb;
        double xsum = 0.15 * zb;
        for (var iter = 0; iter < 4; iter++)
        {
            var rsum = _r1 + _r2;
            var zStart = zb / startCurrentRatio;
            xsum = zStart > rsum ? Math.Sqrt(zStart * zStart - rsum * rsum) : 0.05 * zb;
            _x1 = xsum / 2;
            _x2 = xsum / 2;
            double lo = 0.05 * _r2, hi = 20 * _r2;
            for (var k = 0; k < 60; k++)
            {
                _r2 = Math.Sqrt(lo * hi);
                var t = SequenceTorque(windingVoltage, sn, frequency);
                // A deslizamiento pequeño el par baja al subir R2.
                if (t > RatedTorque) lo = _r2; else hi = _r2;
            }
        }
    }

    private double Scale(double f) => f / RatedFrequency;

    private Complex Zseq(double slip, double f)
    {
        var s = Math.Abs(slip) < 1e-5 ? (slip < 0 ? -1e-5 : 1e-5) : slip;
        var kx = Scale(f);
        var zs = new Complex(_r1, _x1 * kx);
        var zr = new Complex(_r2 / s, _x2 * kx);
        var zm = new Complex(0, _xm * kx);
        return zs + zm * zr / (zm + zr);
    }

    /// <summary>Par (N·m) que produce una tensión de secuencia (fase de devanado) con deslizamiento dado.</summary>
    private double SequenceTorque(Complex v, double slip, double f)
    {
        if (f < 0.05) return 0;
        var s = Math.Abs(slip) < 1e-5 ? (slip < 0 ? -1e-5 : 1e-5) : slip;
        var kx = Scale(f);
        var zs = new Complex(_r1, _x1 * kx);
        var zr = new Complex(_r2 / s, _x2 * kx);
        var zm = new Complex(0, _xm * kx);
        var i = v / (zs + zm * zr / (zm + zr));
        var e = v - i * zs;
        var ir = e / zr;
        var ws = 2 * Math.PI * f / PolePairs;
        return 3 * ir.Magnitude * ir.Magnitude * _r2 / (s * ws);
    }

    /// <summary>Matriz de admitancias 3×3 entre los tres devanados (corriente k = Σ Y[k,j]·Vdevanado j).</summary>
    internal Complex[,] WindingAdmittance()
    {
        var s = Slip;
        var y1 = 1 / Zseq(s, Frequency);
        var y2 = 1 / Zseq(2 - s, Frequency);
        var y0 = 1 / new Complex(_r1, _x1 * Scale(Frequency));
        // Y_abc = T · diag(y0, y1, y2) · T⁻¹ con T de Fortescue.
        var t = new Complex[3, 3]
        {
            { 1, 1, 1 },
            { 1, A * A, A },
            { 1, A, A * A },
        };
        var d = new[] { y0, y1, y2 };
        var y = new Complex[3, 3];
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
            {
                Complex sum = 0;
                for (var k = 0; k < 3; k++)
                    sum += t[r, k] * d[k] * Complex.Conjugate(t[c, k]) / 3; // T⁻¹ = conj(T)ᵀ / 3
                y[r, c] = sum;
            }
        return y;
    }

    internal double WindingResistance => _r1;

    /// <summary>
    /// Admitancia vista por la red en un motor monofásico de condensador permanente. Se modela como el motor
    /// trifásico equivalente alimentado solo en secuencia directa (o inversa si gira a izquierdas): el condensador
    /// crea el campo giratorio. Con la misma potencia, la corriente de línea es tres veces la de un devanado.
    /// </summary>
    internal Complex SinglePhaseAdmittance(bool reverse) =>
        3 / Zseq(reverse ? 2 - Slip : Slip, Frequency);

    /// <summary>Tensión de alimentación de un motor monofásico (ver <see cref="SinglePhaseAdmittance"/>).</summary>
    internal void SetSinglePhaseVoltage(Complex v, double dc, bool reverse)
    {
        _v1 = reverse ? 0 : v;
        _v2 = reverse ? v : 0;
        _vdc = Math.Abs(dc);
    }

    private Complex _v1, _v2;
    private double _vdc;

    /// <summary>Guarda las tensiones de devanado de la última resolución de la red.</summary>
    internal void SetWindingVoltages(Complex va, Complex vb, Complex vc, double dcMax)
    {
        _v1 = (va + A * vb + A * A * vc) / 3;
        _v2 = (va + A * A * vb + A * vc) / 3;
        _vdc = dcMax;
    }

    public double PositiveSequenceVoltage => _v1.Magnitude;
    public double NegativeSequenceVoltage => _v2.Magnitude;
    public double DcVoltage => _vdc;

    /// <summary>Avanza la mecánica del eje. Devuelve el deslizamiento para que la red decida si recalcular.</summary>
    internal void Step(double dt)
    {
        var s = Slip;
        var te = SequenceTorque(_v1, s, Frequency) - SequenceTorque(_v2, 2 - s, Frequency);
        Torque = te;

        var load = RatedTorque * LoadFraction * (QuadraticLoad ? Math.Pow(Omega / RatedSpeedRadS, 2) : 1) + Math.Max(ExternalLoadTorque, 0);
        var friction = 0.01 * RatedTorque * Omega / RatedSpeedRadS;
        LoadTorque = load;

        if (Math.Abs(Omega) < 1e-3)
        {
            // Parado: la carga solo frena, no empuja. Si el par motor no la vence, el eje no se mueve.
            if (Math.Abs(te) <= load) { Omega = 0; return; }
            var acc0 = (te - Math.Sign(te) * load) / TotalInertia;
            Omega += acc0 * dt;
            return;
        }

        var dir = Math.Sign(Omega);
        var acc = (te - dir * load - friction) / TotalInertia;
        var next = Omega + acc * dt;
        // Si la carga y el rozamiento lo frenan hasta cero, se queda parado en vez de invertir el giro.
        if (Math.Sign(next) != dir && Math.Abs(te) <= load) next = 0;
        Omega = next;
    }

    internal void Reset() { Omega = 0; Torque = 0; _v1 = _v2 = 0; _vdc = 0; }
}
