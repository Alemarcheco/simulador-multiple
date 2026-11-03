using System.Numerics;

namespace Simulador.Core.Rendering;

/// <summary>Triángulo con color ARGB (alfa &lt; 255 = semitransparente: se mezcla y no tapa lo de detrás).</summary>
public readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, uint Color);

/// <summary>
/// Cámara orbital común a las vistas 3D: gira alrededor de <see cref="Target"/>. Ángulos en grados, distancias en
/// metros, Z hacia arriba. Puede ser en perspectiva o ortográfica (<see cref="Orthographic"/>).
/// <see cref="Fit"/> encuadra con la esfera que envuelve la escena, que no cambia al girar: así la escala y el centro
/// se mantienen fijos mientras se rota.
/// </summary>
public class OrbitCamera
{
    public Vector3 Target { get; set; }
    public double Yaw { get; set; } = -60;
    public double Pitch { get; set; } = 28;
    public double Distance { get; set; } = 60;
    /// <summary>Campo de visión vertical (grados). En ortográfica fija el tamaño visible a la distancia actual.</summary>
    public double FieldOfView { get; set; } = 45;
    /// <summary>true = proyección paralela (planos y edificios: las verticales no se inclinan).</summary>
    public bool Orthographic { get; set; }
    public double MinPitch { get; set; } = -5;
    public double MaxPitch { get; set; } = 89;

    /// <summary>Posición del ojo y ejes de la cámara (derecha, arriba y hacia dónde mira).</summary>
    public (Vector3 Eye, Vector3 Right, Vector3 Up, Vector3 Forward) Frame()
    {
        double yaw = Yaw * Math.PI / 180, pitch = Pitch * Math.PI / 180;
        var dir = new Vector3((float)(Math.Cos(pitch) * Math.Cos(yaw)), (float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch));
        var eye = Target + dir * (float)Distance;
        var forward = Vector3.Normalize(-dir);
        var right = Vector3.Cross(forward, Vector3.UnitZ);
        right = right.LengthSquared() < 1e-9f ? Vector3.UnitX : Vector3.Normalize(right);
        var up = Vector3.Cross(right, forward);
        return (eye, right, up, forward);
    }

    /// <summary>
    /// Píxeles por metro en el plano del objetivo. En perspectiva depende del alto; en ortográfica del lado menor,
    /// para que lo encuadrado quepa también en lienzos verticales.
    /// </summary>
    public double PixelsPerMeter(int width, int height)
    {
        double span = 2 * Distance * Math.Tan(FieldOfView * Math.PI / 360);
        return (Orthographic ? Math.Min(width, height) : height) / Math.Max(span, 1e-9);
    }

    public void Orbit(double dxDeg, double dyDeg)
    {
        Yaw -= dxDeg;
        Pitch = Math.Clamp(Pitch + dyDeg, MinPitch, MaxPitch);
    }

    public void Pan(double dxPixels, double dyPixels, int viewportHeight) => Pan(dxPixels, dyPixels, viewportHeight, viewportHeight);

    public void Pan(double dxPixels, double dyPixels, int viewportWidth, int viewportHeight)
    {
        var (_, right, up, _) = Frame();
        double metersPerPixel = 1 / PixelsPerMeter(Math.Max(viewportWidth, 1), Math.Max(viewportHeight, 1));
        Target += (-right * (float)dxPixels + up * (float)dyPixels) * (float)metersPerPixel;
    }

    public void Zoom(double wheel) => Distance = Math.Clamp(Distance * Math.Pow(0.88, wheel), 2, 2000);

    /// <summary>Centra y aleja la cámara para que la caja <paramref name="min"/>–<paramref name="max"/> se vea entera.</summary>
    public void Fit(Vector3 min, Vector3 max)
    {
        if (min.X > max.X) return;
        Target = (min + max) / 2;
        float size = Math.Max((max - min).Length(), 0.5f);
        double tan = 2 * Math.Tan(FieldOfView * Math.PI / 360);
        // Ortográfica: la esfera entera cabe con un 8 % de margen desde cualquier ángulo.
        // Perspectiva: algo más cerca, como hasta ahora (las esquinas de la caja casi nunca quedan alineadas).
        Distance = Orthographic ? size / 0.92 / tan : Math.Max(5, size / tan * 0.8);
    }
}
