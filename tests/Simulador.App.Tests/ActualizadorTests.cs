using Simulador.App;

namespace Simulador.App.Tests;

public class ActualizadorTests
{
    [Theory]
    [InlineData("v0.1.154", "0.1.154")]
    [InlineData("0.1.7", "0.1.7")]
    [InlineData("V1.2", "1.2.0")]
    public void LeeLaEtiquetaDelRelease(string etiqueta, string esperada) =>
        Assert.Equal(Version.Parse(esperada), Actualizador.LeerEtiqueta(etiqueta));

    [Theory]
    [InlineData("")]
    [InlineData("ultima")]
    [InlineData(null)]
    public void EtiquetaInvalidaNoEsVersion(string? etiqueta) => Assert.Null(Actualizador.LeerEtiqueta(etiqueta));

    [Fact]
    public void ComparaVersionesPorNumeroNoPorTexto()
    {
        Assert.True(Actualizador.EsMasNueva(new Version(0, 1, 155), new Version(0, 1, 99)));
        Assert.False(Actualizador.EsMasNueva(new Version(0, 1, 155), new Version(0, 1, 155, 0)));
        Assert.False(Actualizador.EsMasNueva(new Version(0, 1, 154), new Version(0, 1, 155)));
    }

    [Fact]
    public void EncuentraElInstaladorEnLaRespuestaDeGitHub()
    {
        const string json = """
        {
          "tag_name": "v0.1.160",
          "html_url": "https://github.com/Alemarcheco/simulador-multiple/releases/tag/v0.1.160",
          "assets": [
            { "name": "SimuladorMultiple-portable.zip", "size": 10, "browser_download_url": "https://x/zip" },
            { "name": "SimuladorMultiple-Setup.exe", "size": 50000000, "browser_download_url": "https://x/setup.exe" }
          ]
        }
        """;
        var v = Actualizador.LeerRespuesta(json);
        Assert.NotNull(v);
        Assert.Equal(new Version(0, 1, 160), v.Version);
        Assert.Equal("https://x/setup.exe", v.Instalador);
        Assert.Equal(50000000, v.Bytes);
        Assert.EndsWith("v0.1.160", v.Pagina);
    }

    [Fact]
    public void SinInstaladorLoDiceSinFallar()
    {
        var v = Actualizador.LeerRespuesta("""{ "tag_name": "v0.1.161", "assets": [] }""");
        Assert.NotNull(v);
        Assert.Null(v.Instalador);
        Assert.Equal(Actualizador.PaginaDescargas, v.Pagina);
    }

    [Fact]
    public void LasCompilacionesLocalesTienenVersion()
    {
        // Sin -p:Version la versión es 0.1.0 (no la 1.0.0 por defecto, que taparía todas las actualizaciones).
        Assert.True(Actualizador.VersionActual >= new Version(0, 1, 0));
        Assert.True(Actualizador.VersionActual < new Version(1, 0, 0));
    }
}
