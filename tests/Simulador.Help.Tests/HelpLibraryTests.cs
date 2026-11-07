using Simulador.Help;

namespace Simulador.Help.Tests;

public class HelpLibraryTests
{
    private static readonly HelpLibrary Library = HelpLibrary.Default;

    // Los Id de las pestañas de la aplicación (IModule.Id). Si se añade un módulo, añade aquí su guía.
    public static TheoryData<string> ModuleIds => new()
    {
        "general", "circuitos", "edificacion", "electrico", "plc", "hidraulica", "neumatica", "cad3d", "banco",
    };

    [Theory]
    [MemberData(nameof(ModuleIds))]
    public void CadaPestanaTieneSuGuia(string moduleId)
    {
        var guide = Library.FindGuide(moduleId);
        Assert.NotNull(guide);
        Assert.False(string.IsNullOrWhiteSpace(guide!.Overview.Body));
        Assert.True(guide.Topics.Count(t => !t.IsOverview && !t.IsQuestion) >= 3, "La guía necesita al menos 3 temas.");
        Assert.Contains(guide.Topics, t => t.IsQuestion);
    }

    [Fact]
    public void LaGuiaGeneralVaPrimero() => Assert.Equal("general", Library.Guides[0].ModuleId);

    [Fact]
    public void LosIdsDeLosTemasSonUnicos()
    {
        var ids = Library.AllTopics.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void LosEnlacesEntreTemasLlevanAUnTemaQueExiste()
    {
        var links = Library.AllTopics
            .SelectMany(t => System.Text.RegularExpressions.Regex.Matches(t.Body, @"\]\(#(.+?)\)").Select(m => (t.Id, Target: m.Groups[1].Value)))
            .ToList();
        Assert.NotEmpty(links);
        foreach (var (from, target) in links)
            Assert.True(Library.FindTopic(target) is not null || Library.FindGuide(target) is not null, $"{from} enlaza a «{target}», que no existe.");
    }

    [Theory]
    [InlineData("¿Cómo simulo un circuito?", "circuitos/simular-el-circuito")]
    [InlineData("exportar stl", "cad3d/exportar-para-imprimir-stl-3mf-y-step")]
    [InlineData("gerber", "circuitos/fabricar-la-placa")]
    [InlineData("el motor no arranca", "electrico/por-que-no-arranca-el-motor")]
    [InlineData("golpe de ariete", "hidraulica/golpe-de-ariete")]
    [InlineData("recorrer el edificio en primera persona", "edificacion/vista-3d-recorrido-y-sol")]
    [InlineData("LADDER", "plc/ladder-ld")]
    [InlineData("osci", "circuitos/simular-el-circuito")]
    public void ElBuscadorEncuentraElTemaAdecuado(string query, string expectedId)
    {
        var results = Library.Search(query);
        Assert.NotEmpty(results);
        var top = results.Take(3).Select(r => r.Topic.Id).ToList();
        Assert.Contains(expectedId, top);
    }

    [Fact]
    public void ElBuscadorNoDistingueAcentos()
    {
        var con = Library.Search("simulación").Select(r => r.Topic.Id).ToList();
        var sin = Library.Search("simulacion").Select(r => r.Topic.Id).ToList();
        Assert.Equal(con, sin);
    }

    [Fact]
    public void ElBuscadorDaPreferenciaALaPestanaActiva()
    {
        var top = Library.Search("vista 3d", currentModuleId: "hidraulica")[0];
        Assert.Equal("hidraulica", top.Topic.Guide.ModuleId);
    }

    [Fact]
    public void UnaBusquedaVaciaNoDevuelveNada()
    {
        Assert.Empty(Library.Search(""));
        Assert.Empty(Library.Search("   "));
    }

    [Fact]
    public void UnaBusquedaSinSentidoNoDevuelveNada() => Assert.Empty(Library.Search("zzqxw"));

    [Fact]
    public void F1AbreElTemaDeLaSubPestana()
    {
        Assert.Equal("circuitos/disenar-la-placa-pcb", Library.ForContext("circuitos", ["PCB", "Circuitos y PCB"]).Id);
        Assert.Equal("edificacion/vista-3d-recorrido-y-sol", Library.ForContext("edificacion", ["Vista 3D"]).Id);
        // Sub-pestaña sin tema propio: la introducción de la guía.
        Assert.Equal("circuitos", Library.ForContext("circuitos", ["Mensajes de ngspice"]).Id);
        // Pestaña sin guía (o ninguna): la guía general.
        Assert.Equal("general", Library.ForContext("desconocido").Id);
        Assert.Equal("general", Library.ForContext(null).Id);
    }

    [Fact]
    public void ElContextoIgnoraIconosYAcentos()
    {
        Assert.Equal("circuitos/dibujar-el-esquema", Library.ForContext("circuitos", ["✏ esquematico"]).Id);
    }

    [Fact]
    public void ParseLeeMetadatosYSecciones()
    {
        var guide = HelpLibrary.Parse("""
            <!-- modulo: prueba -->
            <!-- orden: 7 -->
            # Guía de prueba
            Introducción.

            ## Primer tema
            <!-- contexto: Pestaña A -->
            <!-- palabras: uno, dos -->
            Texto.

            ## ¿Una pregunta?
            Respuesta.

            ## Primer tema
            Repetido.
            """, "x");
        Assert.Equal("prueba", guide.ModuleId);
        Assert.Equal(7, guide.Order);
        Assert.Equal("Guía de prueba", guide.Title);
        Assert.Equal(["prueba", "prueba/primer-tema", "prueba/una-pregunta", "prueba/primer-tema-2"], guide.Topics.Select(t => t.Id));
        Assert.Equal("Introducción.", guide.Overview.Body);
        Assert.Equal(["Pestaña A"], guide.Topics[1].Contexts);
        Assert.Equal(["uno", "dos"], guide.Topics[1].Keywords);
        Assert.True(guide.Topics[2].IsQuestion);
        Assert.DoesNotContain("<!--", guide.Topics[1].Body);
    }
}

public class HelpSearchTailTests
{
    [Fact]
    public void NoSeLlenaDeTemasQueSoloTienenUnaPalabra()
    {
        var results = HelpLibrary.Default.Search("¿cómo simulo un circuito?");
        Assert.InRange(results.Count, 1, 12);
    }
}
