using Simulador.Civil.Model;

namespace Simulador.Civil.Tests;

/// <summary>Valores de referencia calculados con pvlib (algoritmo SPA del NREL), independiente de este código.</summary>
public class SunStudyTests
{
    private static SiteLocation Site(string place) => SiteLocation.Presets.First(p => p.Place == place);

    [Theory]
    [InlineData("Madrid", "2026-06-21 14:00", 70.731, 211.514)]
    [InlineData("Madrid", "2026-12-21 10:30", 15.819, 141.555)]
    [InlineData("Bogotá", "2026-03-20 12:00", 85.238, 168.901)]
    [InlineData("Buenos Aires", "2026-07-15 16:45", 12.610, 307.445)]
    public void Position_matches_the_nrel_reference(string place, string local, double altitude, double azimuth)
    {
        var sun = Solar.Position(Site(place), DateTime.Parse(local, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(altitude, sun.AltitudeDeg, 0.1);
        // Con el sol casi en el cénit el acimut es muy sensible: se compara con más margen.
        Assert.Equal(azimuth, sun.AzimuthDeg, altitude > 80 ? 1.5 : 0.15);
    }

    [Theory]
    [InlineData("Madrid", "2026-06-21", "05:44:45", "20:48:31")]
    [InlineData("Madrid", "2026-12-21", "08:34:20", "17:51:26")]
    [InlineData("Bogotá", "2026-03-20", "06:00:25", "18:06:59")]
    [InlineData("Buenos Aires", "2026-07-15", "07:58:02", "18:01:18")]
    public void Sunrise_and_sunset_within_two_minutes(string place, string date, string rise, string set)
    {
        var d = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        var (r, s) = Solar.RiseSet(Site(place), d);
        Assert.NotNull(r);
        Assert.NotNull(s);
        Assert.True(Math.Abs((r!.Value - d.Add(TimeSpan.Parse(rise))).TotalMinutes) < 2, $"salida {r}");
        Assert.True(Math.Abs((s!.Value - d.Add(TimeSpan.Parse(set))).TotalMinutes) < 2, $"puesta {s}");
    }

    [Fact]
    public void Summer_days_are_longer_than_winter_days_in_the_north_and_the_opposite_in_the_south()
    {
        var jun = new DateTime(2026, 6, 21);
        var dec = new DateTime(2026, 12, 21);
        Assert.True(Solar.DayLength(Site("Madrid"), jun) > Solar.DayLength(Site("Madrid"), dec) + TimeSpan.FromHours(5));
        Assert.True(Solar.DayLength(Site("Buenos Aires"), dec) > Solar.DayLength(Site("Buenos Aires"), jun) + TimeSpan.FromHours(3));
    }

    [Fact]
    public void Direction_in_plan_follows_the_north_arrow()
    {
        var south = new SunPosition(30, 180); // sol al sur
        var (x, y, z) = Solar.DirectionInPlan(south, Site("Madrid"));
        Assert.Equal(0, x, 6);
        Assert.True(y < -0.8); // norte = +Y, así que el sur es −Y
        Assert.Equal(0.5, z, 6);
        // Con el norte girado 90° (apunta a +X), el sur queda hacia −X.
        var (x2, y2, _) = Solar.DirectionInPlan(south, Site("Madrid") with { NorthAngleDeg = 90 });
        Assert.True(x2 < -0.8);
        Assert.Equal(0, y2, 6);
    }

    [Fact]
    public void Site_is_saved_with_the_building()
    {
        var doc = BuildingTemplates.Casa();
        doc.Site = Site("Lima") with { NorthAngleDeg = 25 };
        var back = BuildingDocument.FromJson(doc.ToJson());
        Assert.Equal(doc.Site, back.Site);
        Assert.Equal(SiteLocation.Default, BuildingDocument.FromJson(BuildingTemplates.Casa().ToJson()).Site);
    }
}

public class SunShadowTests
{
    private static System.Numerics.Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);

    private static Simulador.Civil.Rendering.SunShadows ShadowsAt(BuildingDocument doc, DateTime t)
    {
        var mesh = Simulador.Civil.Rendering.BuildingMesh.Build(doc);
        mesh.AddGround(10);
        var (x, y, z) = Solar.DirectionInPlan(Solar.Position(doc.Site, t), doc.Site);
        var sh = new Simulador.Civil.Rendering.SunShadows();
        sh.Build(mesh, V(x, y, z));
        return sh;
    }

    [Fact]
    public void In_madrid_winter_noon_the_shadow_falls_north_of_the_house()
    {
        // Casa de 10 × 8 m y 2,85 m de alto; el 21 de diciembre a mediodía el sol está a unos 26° al sur.
        var doc = BuildingTemplates.Casa();
        var sh = ShadowsAt(doc, new DateTime(2026, 12, 21, 12, 45, 0));
        var up = System.Numerics.Vector3.UnitZ;
        Assert.False(sh.IsLit(V(5, 9.0, -0.02), up));  // a 1 m al norte: sombra
        Assert.False(sh.IsLit(V(5, 13.0, -0.02), up)); // la sombra mide unos 5,8 m
        Assert.True(sh.IsLit(V(5, 15.5, -0.02), up));  // más allá: sol
        Assert.True(sh.IsLit(V(5, -1.0, -0.02), up));  // al sur: sol
        // La fachada sur da al sol y la norte está a la sombra.
        Assert.True(sh.IsLit(V(5, -0.1, 1.5), -System.Numerics.Vector3.UnitY));
        Assert.False(sh.IsLit(V(5, 8.1, 1.5), System.Numerics.Vector3.UnitY));
    }

    [Fact]
    public void In_the_southern_hemisphere_the_shadow_falls_south()
    {
        var doc = BuildingTemplates.Casa();
        doc.Site = SiteLocation.Presets.First(p => p.Place == "Buenos Aires");
        var sh = ShadowsAt(doc, new DateTime(2026, 6, 21, 12, 50, 0));
        var up = System.Numerics.Vector3.UnitZ;
        Assert.False(sh.IsLit(V(5, -1.0, -0.02), up));
        Assert.True(sh.IsLit(V(5, 9.0, -0.02), up));
    }

    [Fact]
    public void Turning_the_north_arrow_turns_the_shadow()
    {
        var doc = BuildingTemplates.Casa();
        doc.Site = doc.Site with { NorthAngleDeg = 90 }; // el norte apunta a +X
        var sh = ShadowsAt(doc, new DateTime(2026, 12, 21, 12, 45, 0));
        var up = System.Numerics.Vector3.UnitZ;
        Assert.False(sh.IsLit(V(11.0, 4, -0.02), up)); // la sombra va hacia +X (el norte)
        Assert.True(sh.IsLit(V(-1.0, 4, -0.02), up));
    }
}
