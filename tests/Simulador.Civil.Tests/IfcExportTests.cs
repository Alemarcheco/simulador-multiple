using System.Text.RegularExpressions;
using Simulador.Civil.Export;
using Simulador.Civil.Model;

namespace Simulador.Civil.Tests;

public class IfcExportTests
{
    private static readonly DateTime Stamp = new(2026, 11, 7);

    private static int Count(string ifc, string entity) => Regex.Matches(ifc, $@"^#\d+={entity}\(", RegexOptions.Multiline).Count;

    [Fact]
    public void File_is_ifc4_step_with_header_and_data()
    {
        var ifc = IfcExport.ToIfc(BuildingTemplates.Casa(), Stamp);
        Assert.StartsWith("ISO-10303-21;\nHEADER;", ifc);
        Assert.Contains("FILE_SCHEMA(('IFC4'));", ifc);
        Assert.EndsWith("END-ISO-10303-21;\n", ifc);
        Assert.Equal(1, Count(ifc, "IFCPROJECT"));
        Assert.Equal(1, Count(ifc, "IFCSITE"));
        Assert.Equal(1, Count(ifc, "IFCBUILDING"));
        Assert.Contains(".METRE.", ifc);
        // Todo el archivo es ASCII (las tildes van codificadas).
        Assert.All(ifc, c => Assert.True(c < 128));
    }

    [Fact]
    public void Every_level_wall_opening_and_space_is_exported()
    {
        var doc = BuildingTemplates.Hotel();
        var ifc = IfcExport.ToIfc(doc, Stamp);
        Assert.Equal(doc.Levels.Count, Count(ifc, "IFCBUILDINGSTOREY"));
        Assert.Equal(doc.Walls.Count, Count(ifc, "IFCWALL"));
        Assert.Equal(doc.Openings.Count, Count(ifc, "IFCOPENINGELEMENT"));
        Assert.Equal(doc.Openings.Count, Count(ifc, "IFCRELVOIDSELEMENT"));
        Assert.Equal(doc.Openings.Count, Count(ifc, "IFCRELFILLSELEMENT"));
        Assert.Equal(doc.Openings.Count(o => o.Kind == Simulador.Core.Building.OpeningKind.Door), Count(ifc, "IFCDOOR"));
        Assert.Equal(doc.Spaces.Count, Count(ifc, "IFCSPACE"));
        Assert.Equal(doc.Stairs.Count, Count(ifc, "IFCSTAIR"));
        Assert.Contains(".HALF_TURN_STAIR.", ifc); // el hotel tiene escalera en U
    }

    [Fact]
    public void Installations_become_proxies_named_like_the_test_bench()
    {
        var doc = BuildingTemplates.CasaConInstalaciones();
        var ifc = IfcExport.ToIfc(doc, Stamp);
        Assert.Equal(doc.Fixtures.Count, Count(ifc, "IFCBUILDINGELEMENTPROXY"));
        var first = doc.Fixtures[0];
        Assert.Contains($"IFCBUILDINGELEMENTPROXY('", ifc);
        Assert.Contains($",'{first.ComponentId}',", ifc);
    }

    [Fact]
    public void Global_ids_are_valid_unique_and_stable()
    {
        var doc = BuildingTemplates.Hotel();
        var a = IfcExport.ToIfc(doc, Stamp);
        var b = IfcExport.ToIfc(doc, Stamp);
        Assert.Equal(a, b);
        var ids = Regex.Matches(a, @"^#\d+=IFC[A-Z]+\('([^']{22})'", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.True(ids.Count > 100);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches("^[0-3][0-9A-Za-z_$]{21}$", id));
    }

    [Fact]
    public void Compress_guid_matches_the_ifc_encoding()
    {
        Assert.Equal("0000000000000000000000", IfcExport.CompressGuid(new byte[16]));
        Assert.Equal("3$$$$$$$$$$$$$$$$$$$$$", IfcExport.CompressGuid(Enumerable.Repeat((byte)0xFF, 16).ToArray()));
        var one = new byte[16]; one[15] = 1;
        Assert.Equal("0000000000000000000001", IfcExport.CompressGuid(one));
    }

    [Fact]
    public void Accents_are_encoded_as_x2_and_references_resolve()
    {
        var doc = BuildingTemplates.Hotel();
        var ifc = IfcExport.ToIfc(doc, Stamp);
        Assert.Contains(@"Recepci\X2\00F3\X0\n", ifc);
        var defined = Regex.Matches(ifc, @"^#(\d+)=", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();
        var used = Regex.Matches(ifc, @"(?<!^)#(\d+)", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value));
        Assert.All(used, r => Assert.Contains(r, defined));
    }
}
