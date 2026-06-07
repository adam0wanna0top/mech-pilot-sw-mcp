using MechPilot.SwMcp.Tools.Internal;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 for <see cref="StandardPartNames"/> — the name-based "looks like a standard
/// fastener/bearing" hint (M40). A true result means the resize orchestrator
/// treats the component as off-limits; false is not a guarantee it is custom.
/// </summary>
public class StandardPartNamesTests
{
    [Theory]
    // Standard-org designations (separators incl. GB/T-style "t").
    [InlineData("ISO 4762 M6x20.sldprt", true)]
    [InlineData("ISO4762.sldprt", true)]
    [InlineData("GB_T_70.1_M8x30.sldprt", true)]
    [InlineData("DIN912-M5.sldprt", true)]
    // Fastener / bearing keywords (English + Chinese).
    [InlineData("hex bolt M6.sldprt", true)]
    [InlineData("6204 bearing.sldprt", true)]
    [InlineData("flat washer.sldprt", true)]
    [InlineData("螺栓M6.sldprt", true)]
    [InlineData("轴承6204.sldprt", true)]
    // Custom parts — must NOT be flagged (no false positives on plain words/versions).
    [InlineData("my_bracket.sldprt", false)]
    [InlineData("motor_housing.sldprt", false)]
    [InlineData("base_plate_v2.sldprt", false)]
    [InlineData("isometric_view.sldprt", false)]
    [InlineData("din_bracket.sldprt", false)]
    [InlineData("", false)]
    public void IsStandardCandidate_flags_fasteners_and_standards(string fileName, bool expected) =>
        Assert.Equal(expected, StandardPartNames.IsStandardCandidate(fileName));
}
