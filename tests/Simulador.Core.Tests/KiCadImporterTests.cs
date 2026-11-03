using Simulador.Core.Libraries.KiCad;
using Simulador.Core.Libraries.Model;

namespace Simulador.Core.Tests;

public class KiCadImporterTests
{
    private const string SymbolLib = """
        (kicad_symbol_lib (version 20231120) (generator "kicad_symbol_editor")
          (symbol "R" (pin_numbers hide) (pin_names (offset 0)) (in_bom yes) (on_board yes)
            (property "Reference" "R" (at 2.032 0 90) (effects (font (size 1.27 1.27))))
            (property "Value" "R" (at 0 0 90) (effects (font (size 1.27 1.27))))
            (property "Footprint" "" (at -1.778 0 90) (effects (font (size 1.27 1.27)) hide))
            (property "Description" "Resistor" (at 0 0 0) (effects (font (size 1.27 1.27)) hide))
            (symbol "R_0_1"
              (rectangle (start -1.016 -2.54) (end 1.016 2.54) (stroke (width 0.254) (type default)) (fill (type none))))
            (symbol "R_1_1"
              (pin passive line (at 0 3.81 270) (length 1.27) (name "~" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27)))))
              (pin passive line (at 0 -3.81 90) (length 1.27) (name "~" (effects (font (size 1.27 1.27)))) (number "2" (effects (font (size 1.27 1.27)))))))
          (symbol "R_Small" (extends "R")
            (property "Reference" "R" (at 0 0 0) (effects (font (size 1.27 1.27))))
            (property "Value" "R_Small" (at 0 0 0) (effects (font (size 1.27 1.27)))))
          (symbol "LM358" (in_bom yes) (on_board yes)
            (property "Reference" "U" (at 0 5.08 0) (effects (font (size 1.27 1.27))))
            (symbol "LM358_1_1" (pin output line (at 7.62 0 180) (length 2.54) (name "~" (effects (font (size 1.27 1.27)))) (number "1" (effects (font (size 1.27 1.27))))))
            (symbol "LM358_2_1" (pin output line (at 7.62 0 180) (length 2.54) (name "~" (effects (font (size 1.27 1.27)))) (number "7" (effects (font (size 1.27 1.27))))))
            (symbol "LM358_3_1" (pin power_in line (at -2.54 -7.62 90) (length 3.81) (name "V-" (effects (font (size 1.27 1.27)))) (number "4" (effects (font (size 1.27 1.27))))))))
        """;

    [Fact]
    public void Imports_symbols_pins_and_graphics()
    {
        var result = new KiCadSymbolImporter().Import(SymbolLib, "Device");
        var r = result.Symbols.Single(s => s.Name == "R");

        Assert.Equal("Device:R", r.FullName);
        Assert.Equal("Resistor", r.Description);
        Assert.Equal(2, r.Pins.Count);
        Assert.Equal(new Vec2(0, 3.81), r.Pins[0].Position);
        Assert.Equal(270, r.Pins[0].OrientationDeg);
        Assert.Equal(PinElectricalType.Passive, r.Pins[0].ElectricalType);
        Assert.IsType<RectangleGraphic>(Assert.Single(r.Graphics));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Resolves_extends()
    {
        var result = new KiCadSymbolImporter().Import(SymbolLib, "Device");
        var small = result.Symbols.Single(s => s.Name == "R_Small");

        Assert.Equal(2, small.Pins.Count);
        Assert.Equal("R_Small", small.Value);
        Assert.Equal("Resistor", small.Description);
    }

    [Fact]
    public void Counts_units()
    {
        var result = new KiCadSymbolImporter().Import(SymbolLib, "Amplifier");
        var opamp = result.Symbols.Single(s => s.Name == "LM358");

        Assert.Equal(3, opamp.UnitCount);
        Assert.Equal(PinElectricalType.PowerIn, opamp.Pins.Single(p => p.Number == "4").ElectricalType);
    }

    [Fact]
    public void Imports_footprint()
    {
        const string mod = """
            (footprint "R_Axial_DIN0207_L6.3mm_D2.5mm_P10.16mm_Horizontal" (version 20240108) (layer "F.Cu")
              (descr "Resistor, Axial_DIN0207")
              (tags "Resistor Axial_DIN0207")
              (attr through_hole)
              (fp_line (start 0.94 -1.37) (end 0.94 1.37) (stroke (width 0.12) (type solid)) (layer "F.SilkS"))
              (fp_circle (center 5 0) (end 6 0) (stroke (width 0.1) (type solid)) (fill none) (layer "F.Fab"))
              (pad "1" thru_hole circle (at 0 0) (size 1.6 1.6) (drill 0.8) (layers "*.Cu" "*.Mask"))
              (pad "2" thru_hole oval (at 10.16 0 90) (size 1.6 1.6) (drill oval 0.8 1.2) (layers "*.Cu" "*.Mask"))
              (model "${KICAD8_3DMODEL_DIR}/Resistor_THT.3dshapes/R_Axial_DIN0207.wrl" (offset (xyz 0 0 0)) (scale (xyz 1 1 1)) (rotate (xyz 0 0 0))))
            """;
        var fp = Assert.Single(new KiCadFootprintImporter().Import(mod, "Resistor_THT").Footprints);

        Assert.Equal("Resistor_THT:R_Axial_DIN0207_L6.3mm_D2.5mm_P10.16mm_Horizontal", fp.FullName);
        Assert.False(fp.IsSmd);
        Assert.Equal(2, fp.Pads.Count);
        Assert.Equal(0.8, fp.Pads[0].Drill);
        Assert.Equal(PadShape.Oval, fp.Pads[1].Shape);
        Assert.Equal(90, fp.Pads[1].RotationDeg);
        Assert.Equal(new Vec2(0.8, 1.2), fp.Pads[1].DrillSize);
        Assert.Equal(1.0, Assert.IsType<CircleGraphic>(fp.Graphics[1]).Radius, 6);
        Assert.Single(fp.Models3D);
    }

    [Fact]
    public void Rejects_non_kicad_text()
    {
        Assert.Throws<FormatException>(() => new KiCadSymbolImporter().Import("hola", "x"));
        Assert.Throws<FormatException>(() => new KiCadSymbolImporter().Import("(kicad_symbol_lib (symbol \"R\"", "x"));
    }
}
