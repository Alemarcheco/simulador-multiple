using Simulador.Electrico.Engine.Catalog;
using Simulador.Electrico.Engine.Model;
using Simulador.Electrico.Engine.Sim;

namespace Simulador.Electrico.Tests;

public class QetImporterTests
{
    private const string NoContact = """
        <definition width="20" height="50" hotspot_x="10" hotspot_y="25" type="element" link_type="slave" version="0.80">
          <names><name lang="fr">Contact NO</name><name lang="en">NO contact</name></names>
          <kindInformations><kindInformation name="type">simple</kindInformation><kindInformation name="state">NO</kindInformation></kindInformations>
          <description>
            <line x1="0" y1="-20" x2="0" y2="-8" style="line-style:normal;line-weight:normal;filling:none;color:black"/>
            <line x1="0" y1="8" x2="-8" y2="-6" style=""/>
            <line x1="0" y1="8" x2="0" y2="20" style=""/>
            <terminal x="0" y="-20" orientation="n"/>
            <terminal x="0" y="20" orientation="s"/>
          </description>
        </definition>
        """;

    private const string Coil = """
        <definition width="30" height="50" hotspot_x="15" hotspot_y="25" type="element" link_type="master">
          <names><name lang="es">Bobina de contactor</name></names>
          <kindInformations><kindInformation name="type">coil</kindInformation></kindInformations>
          <description>
            <rect x="-10" y="-8" width="20" height="16" style="filling:none"/>
            <line x1="0" y1="-20" x2="0" y2="-8"/>
            <line x1="0" y1="8" x2="0" y2="20"/>
            <arc x="-5" y="-5" width="10" height="10" start="0" angle="180"/>
            <text x="12" y="0" size="9" text="KM"/>
            <terminal x="0" y="-20" orientation="n"/>
            <terminal x="0" y="20" orientation="s"/>
          </description>
        </definition>
        """;

    [Fact]
    public void Slave_NO_contact_becomes_a_follower_contact_with_its_drawing()
    {
        var r = QetImporter.Parse(NoContact, "contact_no.elmt", ElementCatalog.Default);
        Assert.Equal(Behavior.ContactNO, r.Type.Behavior);
        Assert.Equal("NO contact", r.Type.Name); // sin español, cae en inglés
        Assert.Equal(["1", "2"], r.Type.Terminals.Select(t => t.Id));
        Assert.Equal((0, 0), (r.Type.Terminals[0].X, r.Type.Terminals[0].Y));
        Assert.Equal((0, 4), (r.Type.Terminals[1].X, r.Type.Terminals[1].Y));
        Assert.NotNull(r.Type.Graphic);
        Assert.Equal(3, r.Type.Graphic!.Primitives.Count);
        Assert.StartsWith("qet:", r.Type.Id);
    }

    [Fact]
    public void Imported_coil_and_contact_work_together_in_a_circuit()
    {
        var catalog = new ElementCatalog(ElementCatalog.Default.Types);
        var coil = QetImporter.Parse(Coil, "bobine.elmt", catalog).Type;
        var contact = QetImporter.Parse(NoContact, "contact_no.elmt", catalog).Type;
        catalog.Register(coil);
        catalog.Register(contact);
        Assert.Equal(Behavior.CoilContactor, coil.Behavior);
        Assert.Equal(["A1", "A2"], coil.Terminals.Select(t => t.Id));

        var b = new SchematicBuilder("qet");
        var src = b.Add("fuente_ac1", "G", 0, 0);
        var s1 = b.Add("interruptor", "S1", 0, 4);
        var k = b.Add(coil.Id, "KA1", 0, 10);
        var c = b.Add(contact.Id, "KA1", 6, 4);
        var h = b.Add("lampara", "E1", 6, 10);
        b.Wire(src, "L", s1, "1").Wire(s1, "2", k, "A1").Wire(k, "A2", src, "N");
        b.Wire(src, "L", c, "1").Wire(c, "2", h, "1").Wire(h, "2", src, "N");
        var sim = new Simulator(b.Schematic, catalog);
        sim.Run(0.1);
        Assert.True(sim.Status(h.Id).Brightness < 0.1);
        sim.PointerDown(s1.Id); sim.PointerUp(s1.Id);
        sim.Run(0.1);
        Assert.True(sim.Status(h.Id).Brightness > 0.9);
    }

    [Fact]
    public void Unknown_symbol_is_kept_as_drawing_only_and_bad_files_are_reported()
    {
        const string box = """
            <definition width="40" height="40" hotspot_x="20" hotspot_y="20" link_type="simple">
              <names><name lang="fr">Boîte de jonction</name></names>
              <description>
                <rect x="-20" y="-20" width="40" height="40"/>
                <terminal x="-20" y="0" orientation="w"/><terminal x="20" y="0" orientation="e"/><terminal x="0" y="20" orientation="s"/>
              </description>
            </definition>
            """;
        var r = QetImporter.Parse(box, "boite.elmt", ElementCatalog.Default);
        Assert.Equal(Behavior.Graphic, r.Type.Behavior);
        Assert.Equal(3, r.Type.Terminals.Count);
        Assert.Throws<FormatException>(() => QetImporter.Parse("<hola/>", "x.elmt", ElementCatalog.Default));
        Assert.Throws<FormatException>(() => QetImporter.Parse("no es xml", "x.elmt", ElementCatalog.Default));
    }
}
