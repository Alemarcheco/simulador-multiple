using Simulador.Core.Libraries.Spice;

namespace Simulador.Core.Tests;

public class SpiceImporterTests
{
    private const string Lib = """
        * Modelos de ejemplo
        .model 1N4148 D(Is=2.52n Rs=.568 N=1.752 Cjo=4p M=.4 tt=20n Iave=200m Vpk=75 mfg=OnSemi type=silicon)
        .MODEL 2N2222A NPN (IS=14.34F XTI=3 EG=1.11 VAF=74.03 BF=255.9
        + NE=1.307 ISE=14.34F IKF=.2847 ; comentario
        + XTB=1.5 BR=6.092)
        *
        .subckt LM358 1 2 3 4 5 params: GAIN=100k
        Q1 6 1 7 QX
        .model QX PNP(BF=100)
        .ends LM358
        .SUBCKT TL431 K A REF
        R1 K A 1meg
        .ENDS
        """;

    [Fact]
    public void Reads_models_with_continuations()
    {
        var r = new SpiceLibraryImporter().Import(Lib, "demo");

        Assert.Equal(["1N4148", "2N2222A"], r.SpiceModels.Select(m => m.Name));
        var diode = r.SpiceModels[0];
        Assert.Equal("D", diode.Type);
        Assert.Equal("2.52n", diode.Parameters["is"]);
        var bjt = r.SpiceModels[1];
        Assert.Equal("NPN", bjt.Type);
        Assert.Equal("6.092", bjt.Parameters["BR"]);
        Assert.Equal("1.307", bjt.Parameters["NE"]);
        Assert.Contains("+ XTB=1.5", bjt.SourceText);
    }

    [Fact]
    public void Reads_subcircuits_with_pins_and_params()
    {
        var r = new SpiceLibraryImporter().Import(Lib, "demo");

        Assert.Equal(2, r.SpiceSubcircuits.Count);
        var lm = r.SpiceSubcircuits[0];
        Assert.Equal("LM358", lm.Name);
        Assert.Equal(["1", "2", "3", "4", "5"], lm.Pins);
        Assert.Equal("100k", lm.Parameters["GAIN"]);
        Assert.EndsWith(".ends LM358", lm.SourceText);
        Assert.Equal(["K", "A", "REF"], r.SpiceSubcircuits[1].Pins);
        Assert.Empty(r.Warnings);
    }
}
