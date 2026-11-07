using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>
/// Dónde está el edificio: lugar, coordenadas (grados, norte y este positivos), diferencia con la hora universal
/// (horas, la oficial del lugar) y hacia dónde apunta el norte en la planta.
/// <paramref name="NorthAngleDeg"/> = 0: el norte es +Y de la planta (hacia arriba del plano); positivo gira el norte
/// en sentido horario, como la flecha de norte de un plano.
/// </summary>
public sealed record SiteLocation(string Place, double Latitude, double Longitude, double UtcOffsetHours, double NorthAngleDeg = 0)
{
    /// <summary>Ciudades de partida (hora oficial de invierno; el horario de verano se suma a mano).</summary>
    public static readonly SiteLocation[] Presets =
    [
        new("Madrid", 40.4168, -3.7038, 1),
        new("Barcelona", 41.3874, 2.1686, 1),
        new("Sevilla", 37.3891, -5.9845, 1),
        new("Ciudad de México", 19.4326, -99.1332, -6),
        new("Guadalajara (México)", 20.6597, -103.3496, -6),
        new("Bogotá", 4.7110, -74.0721, -5),
        new("Medellín", 6.2442, -75.5812, -5),
        new("Lima", -12.0464, -77.0428, -5),
        new("Quito", -0.1807, -78.4678, -5),
        new("Caracas", 10.4806, -66.9036, -4),
        new("La Paz", -16.4897, -68.1193, -4),
        new("Santiago de Chile", -33.4489, -70.6693, -4),
        new("Buenos Aires", -34.6037, -58.3816, -3),
        new("Montevideo", -34.9011, -56.1645, -3),
    ];

    public static SiteLocation Default => Presets[0];
}

/// <summary>Posición del sol: altura sobre el horizonte y acimut desde el norte en sentido horario (grados).</summary>
public readonly record struct SunPosition(double AltitudeDeg, double AzimuthDeg)
{
    public bool IsUp => AltitudeDeg > 0;

    /// <summary>Punto cardinal del acimut (N, NE, E…).</summary>
    public string Cardinal => Cardinals[(int)Math.Round(((AzimuthDeg % 360) + 360) % 360 / 45) % 8];
    private static readonly string[] Cardinals = ["N", "NE", "E", "SE", "S", "SO", "O", "NO"];
}

/// <summary>
/// Posición del sol con el algoritmo de la NOAA (precisión de unos pocos minutos de arco entre 1800 y 2100), salida y
/// puesta, y la dirección hacia el sol en coordenadas de la planta para dibujar las sombras.
/// </summary>
public static class Solar
{
    private const double Deg = Math.PI / 180;

    /// <summary>Posición del sol a una hora oficial del lugar.</summary>
    public static SunPosition Position(SiteLocation site, DateTime localTime)
    {
        var g = Geometric(site, localTime);
        return g with { AltitudeDeg = g.AltitudeDeg + Refraction(g.AltitudeDeg) };
    }

    /// <summary>Posición geométrica, sin la refracción de la atmósfera.</summary>
    private static SunPosition Geometric(SiteLocation site, DateTime localTime)
    {
        var utc = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified) - TimeSpan.FromHours(site.UtcOffsetHours);
        double jd = utc.ToOADate() + 2415018.5;
        double t = (jd - 2451545.0) / 36525.0;

        double l0 = Mod(280.46646 + t * (36000.76983 + t * 0.0003032), 360);
        double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        double c = Math.Sin(m * Deg) * (1.914602 - t * (0.004817 + 0.000014 * t))
                   + Math.Sin(2 * m * Deg) * (0.019993 - 0.000101 * t) + Math.Sin(3 * m * Deg) * 0.000289;
        double omega = 125.04 - 1934.136 * t;
        double lambda = l0 + c - 0.00569 - 0.00478 * Math.Sin(omega * Deg);
        double eps0 = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60;
        double eps = eps0 + 0.00256 * Math.Cos(omega * Deg);
        double decl = Math.Asin(Math.Sin(eps * Deg) * Math.Sin(lambda * Deg));

        double y = Math.Pow(Math.Tan(eps * Deg / 2), 2);
        double eqTime = 4 / Deg * (y * Math.Sin(2 * l0 * Deg) - 2 * e * Math.Sin(m * Deg)
                                   + 4 * e * y * Math.Sin(m * Deg) * Math.Cos(2 * l0 * Deg)
                                   - 0.5 * y * y * Math.Sin(4 * l0 * Deg) - 1.25 * e * e * Math.Sin(2 * m * Deg));

        double minutesUtc = utc.TimeOfDay.TotalMinutes;
        double trueSolar = Mod(minutesUtc + eqTime + 4 * site.Longitude, 1440);
        double hourAngle = trueSolar / 4 - 180;

        double lat = site.Latitude * Deg, ha = hourAngle * Deg;
        double cosZen = Math.Clamp(Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(ha), -1, 1);
        double altitude = 90 - Math.Acos(cosZen) / Deg;
        double az = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat)) / Deg + 180;
        return new SunPosition(altitude, Mod(az, 360));
    }

    /// <summary>Refracción atmosférica aproximada (el sol se ve un poco más alto cerca del horizonte), en grados.</summary>
    private static double Refraction(double altitude)
    {
        if (altitude > 85) return 0;
        double te = Math.Tan(altitude * Deg);
        double arcsec = altitude > 5 ? 58.1 / te - 0.07 / (te * te * te) + 0.000086 / Math.Pow(te, 5)
            : altitude > -0.575 ? 1735 + altitude * (-518.2 + altitude * (103.4 + altitude * (-12.79 + altitude * 0.711)))
            : -20.772 / te;
        return arcsec / 3600;
    }

    /// <summary>
    /// Salida y puesta del sol (hora oficial) del día de <paramref name="date"/>; null si ese día no sale o no se pone
    /// (cerca de los polos). Se busca minuto a minuto el cruce del borde superior del sol con el horizonte.
    /// </summary>
    public static (DateTime? Sunrise, DateTime? Sunset) RiseSet(SiteLocation site, DateTime date)
    {
        var day = date.Date;
        DateTime? rise = null, set = null;
        // Salida y puesta oficiales: el centro del sol a 0,833° bajo el horizonte geométrico (refracción y radio del disco).
        double prev = Geometric(site, day).AltitudeDeg + 0.833;
        for (int min = 1; min <= 1440; min++)
        {
            var at = day.AddMinutes(min);
            double alt = Geometric(site, at).AltitudeDeg + 0.833;
            if (prev < 0 && alt >= 0 && rise is null) rise = at.AddMinutes(-alt / (alt - prev));
            if (prev >= 0 && alt < 0 && set is null) set = at.AddMinutes(-alt / (alt - prev));
            prev = alt;
        }
        return (rise, set);
    }

    /// <summary>Horas de sol del día (entre salida y puesta).</summary>
    public static TimeSpan DayLength(SiteLocation site, DateTime date)
    {
        var (rise, set) = RiseSet(site, date);
        if (rise is { } r && set is { } s && s > r) return s - r;
        return Position(site, date.Date.AddHours(12)).IsUp ? TimeSpan.FromHours(24) : TimeSpan.Zero;
    }

    /// <summary>
    /// Vector unitario hacia el sol en coordenadas de la planta (X, Y del plano y Z hacia arriba), teniendo en cuenta
    /// hacia dónde apunta el norte del edificio.
    /// </summary>
    public static (double X, double Y, double Z) DirectionInPlan(SunPosition sun, SiteLocation site)
    {
        double n = site.NorthAngleDeg * Deg;
        var north = new Point2(Math.Sin(n), Math.Cos(n));
        var east = new Point2(Math.Cos(n), -Math.Sin(n));
        double alt = sun.AltitudeDeg * Deg, az = sun.AzimuthDeg * Deg;
        var h = (east * Math.Sin(az) + north * Math.Cos(az)) * Math.Cos(alt);
        return (h.X, h.Y, Math.Sin(alt));
    }

    private static double Mod(double v, double m) => ((v % m) + m) % m;
}
