using MechPilot.SwMcp.Tools.Internal;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 for <see cref="DimensionFormat"/> — the SW-free unit/value conversion the
/// inspect tools use for each feature's editable dimensions (M39). Guards the
/// angular-type set and the SI → display math (the live COM dimension walk in
/// PartMetadata is covered by the M39 L2 integration test).
/// </summary>
public class DimensionFormatTests
{
    [Theory]
    [InlineData(3, true)]    // swAngularDimension
    [InlineData(16, true)]   // swAngularOrdinateDimension
    [InlineData(2, false)]   // swLinearDimension
    [InlineData(5, false)]   // swRadialDimension
    [InlineData(6, false)]   // swDiameterDimension (a length, not an angle)
    [InlineData(0, false)]   // swDimensionTypeUnknown
    public void IsAngular_classifies_by_sw_dimension_type(int type, bool expected) =>
        Assert.Equal(expected, DimensionFormat.IsAngular(type));

    [Fact]
    public void ToDisplay_length_converts_metres_to_mm()
    {
        var (value, unit) = DimensionFormat.ToDisplay(2, 0.03);
        Assert.Equal(30.0, value, 6);
        Assert.Equal("mm", unit);
    }

    [Fact]
    public void ToDisplay_diameter_is_a_length_in_mm()
    {
        var (value, unit) = DimensionFormat.ToDisplay(6, 0.012);
        Assert.Equal(12.0, value, 6);
        Assert.Equal("mm", unit);
    }

    [Fact]
    public void ToDisplay_angle_converts_radians_to_degrees()
    {
        var (value, unit) = DimensionFormat.ToDisplay(3, Math.PI);
        Assert.Equal(180.0, value, 6);
        Assert.Equal("deg", unit);
    }

    [Fact]
    public void ToDisplay_rounds_double_precision_noise()
    {
        // 0.03 m is not exactly representable; the result must still read 30 mm.
        var (value, _) = DimensionFormat.ToDisplay(2, 0.03);
        Assert.Equal(30.0, value);
    }
}
