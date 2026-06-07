using MechPilot.SwMcp.Tools.Internal;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 for <see cref="MateType"/> — the SW-free swMateType_e → name mapping and
/// "carries a value" check used by inspect_assembly's mates list (M41). The live
/// mate traversal in MateReader is covered by the M41 L2 integration test.
/// </summary>
public class MateTypeTests
{
    [Theory]
    [InlineData(0, "coincident")]
    [InlineData(1, "concentric")]
    [InlineData(3, "parallel")]
    [InlineData(5, "distance")]
    [InlineData(6, "angle")]
    [InlineData(11, "width")]
    [InlineData(99, "type99")]   // unmapped → stable fallback
    public void Name_maps_sw_mate_type(int swMateType, string expected) =>
        Assert.Equal(expected, MateType.Name(swMateType));

    [Theory]
    [InlineData(5, true)]    // distance
    [InlineData(6, true)]    // angle
    [InlineData(0, false)]   // coincident
    [InlineData(1, false)]   // concentric
    public void HasValue_only_distance_and_angle(int swMateType, bool expected) =>
        Assert.Equal(expected, MateType.HasValue(swMateType));
}
