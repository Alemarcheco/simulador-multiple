using Simulador.Civil.Model;
using Simulador.Core.Building;

namespace Simulador.Civil.Tests;

public class AdvisorTests
{
    private static void ApplyAll(BuildingDocument doc)
    {
        var actions = BuildingAdvisor.Suggest(doc).Where(s => s.Apply is not null).ToList();
        doc.Edit(e => { foreach (var s in actions) s.Apply!(e); });
    }

    [Fact]
    public void Outlets_follow_perimeter_with_minimum_per_use()
    {
        var s = new AdvisorSettings();
        Space Room(string use, double w, double h) => new("R", "N0", "x", use, [new(0, 0), new(w, 0), new(w, h), new(0, h)], w * h, 2 * (w + h));
        Assert.Equal(3, BuildingAdvisor.RequiredOutlets(s, Room("dormitorio", 3, 3)));   // 12 m → 3, mínimo 3
        Assert.Equal(5, BuildingAdvisor.RequiredOutlets(s, Room("estar", 5, 5)));        // 20 m → 5
        Assert.Equal(1, BuildingAdvisor.RequiredOutlets(s, Room("baño", 3.8, 2)));
    }

    [Fact]
    public void Empty_house_gets_electrical_suggestions_and_applying_them_clears_them()
    {
        var doc = BuildingTemplates.Casa();
        var before = BuildingAdvisor.Suggest(doc);
        Assert.Contains(before, s => s.Kind == SuggestionKind.Electricidad && s.Message.Contains("Dormitorio 1") && s.Apply is not null);
        Assert.Contains(before, s => s.Kind == SuggestionKind.Agua && s.Message.Contains("tanque"));

        ApplyAll(doc);

        var after = BuildingAdvisor.Suggest(doc);
        Assert.DoesNotContain(after, s => s.Kind == SuggestionKind.Electricidad && s.Severity == CheckSeverity.Warning && !s.Message.Contains("tablero"));
        Assert.DoesNotContain(after, s => s.Message.Contains("debería guardar"));
        Assert.True(doc.Fixtures.Count(f => f.Kind == "tomacorriente") >= 15);
        // Lo que coloca el asistente no choca con puertas ni ventanas.
        Assert.DoesNotContain(BuildingChecks.Run(doc), c => doc.Fixtures.Any(f => f.Id == c.ElementId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Applying_suggestions_never_creates_check_errors(int template)
    {
        var doc = BuildingTemplates.All[template].Create();
        ApplyAll(doc);
        var errors = BuildingChecks.Run(doc).Where(c => c.Severity == CheckSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Message)));
    }

    [Fact]
    public void Water_is_sized_from_bedrooms()
    {
        var w = BuildingAdvisor.Water(BuildingTemplates.Casa());
        Assert.Equal(4, w.Persons);
        Assert.Equal(600, w.DailyLiters, 6);
        Assert.Equal(200, w.TankLitersNeeded, 6);
        Assert.Equal(0, w.TankLitersInstalled);
    }

    [Fact]
    public void Tank_too_low_for_shower_pressure_is_reported()
    {
        var doc = BuildingTemplates.CasaConInstalaciones();
        var w = BuildingAdvisor.Water(doc);
        Assert.True(w.TankLitersInstalled > w.TankLitersNeeded);
        Assert.Equal(1.9 + 2.0, w.MinTankBaseElevation, 6);
        Assert.Contains(BuildingAdvisor.Suggest(doc), s => s.Kind == SuggestionKind.Agua && s.Message.Contains("subirlo"));
    }

    [Fact]
    public void Upper_floor_without_stair_cannot_evacuate()
    {
        var doc = BuildingTemplates.Casa();
        doc.Edit(e => e.DuplicateLevel("N0", "Planta alta"));
        Assert.Contains(BuildingAdvisor.Suggest(doc), s => s.Kind == SuggestionKind.Evacuacion && s.Severity == CheckSeverity.Error);
        Assert.DoesNotContain(BuildingAdvisor.Suggest(BuildingTemplates.CasaDosPlantas()), s => s.Kind == SuggestionKind.Evacuacion && s.Severity == CheckSeverity.Error);
    }

    [Fact]
    public void Ground_floor_exterior_doors_are_found()
    {
        var doc = BuildingTemplates.Casa();
        var exits = BuildingAdvisor.ExteriorDoors(doc, "N0").ToList();
        Assert.Single(exits);
    }

    [Fact]
    public void Budget_counts_blocks_and_uses_editable_prices()
    {
        var doc = BuildingTemplates.Casa();
        var lines = BuildingAdvisor.Budget(doc);
        var blocks = lines.Single(l => l.PriceKey == "bloque");
        double area = doc.Walls.Sum(w => w.Length * w.Height) - doc.Openings.Sum(o => o.Width * o.Height);
        Assert.Equal(Math.Ceiling(area * 12.5 * 1.05), blocks.Quantity);
        doc.Advisor.UnitPrices["bloque"] = 2;
        Assert.Equal(blocks.Quantity * 2, BuildingAdvisor.Budget(doc).Single(l => l.PriceKey == "bloque").Total, 6);
    }

    [Fact]
    public void Advisor_settings_are_saved_with_the_project()
    {
        var doc = BuildingTemplates.Casa();
        doc.Advisor.WaterPerPersonDay = 200;
        doc.Advisor.UnitPrices["bloque"] = 1.7;
        var copy = BuildingDocument.FromJson(doc.ToJson());
        Assert.Equal(200, copy.Advisor.WaterPerPersonDay);
        Assert.Equal(1.7, copy.Advisor.UnitPrices["bloque"]);
    }
}

public class AdvisorWiringTests
{
    [Fact]
    public void Applying_twice_wires_everything_to_a_board_and_the_tank()
    {
        var doc = BuildingTemplates.Casa();
        for (int pass = 0; pass < 2; pass++)
        {
            var actions = BuildingAdvisor.Suggest(doc).Where(s => s.Apply is not null).ToList();
            doc.Edit(e => { foreach (var s in actions) s.Apply!(e); });
        }
        var board = Assert.Single(doc.Fixtures, f => f.Kind == "tablero");
        var routed = doc.Routes.Select(r => r.ToFixtureId).ToHashSet();
        Assert.All(doc.Fixtures.Where(f => f.Kind is "tomacorriente" or "interruptor" or "luminaria" or "punto_agua"), f => Assert.Contains(f.Id, routed));
        Assert.Contains(BuildingAdvisor.Budget(doc), l => l.PriceKey == "cable_m" && l.Quantity > 0);
        Assert.DoesNotContain(BuildingChecks.Run(doc), c => c.Severity == CheckSeverity.Error);
        Assert.NotNull(board.HostId);
    }
}
