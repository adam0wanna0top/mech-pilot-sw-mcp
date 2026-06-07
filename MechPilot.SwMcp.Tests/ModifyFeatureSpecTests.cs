using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 unit tests for ModifyFeatureSpec (M38) — the "mechanical Cursor" edit
/// primitive's input validation: a feature name + a positive new dimension.
/// </summary>
public class ModifyFeatureSpecTests
{
    [Fact]
    public void Canonical_validates()
    {
        new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = 25 }.Validate();
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(25)]
    [InlineData(360)]
    [InlineData(100000)]
    public void Valid_values_validate(double v)
    {
        new ModifyFeatureSpec { FeatureName = "旋转1", Value = v }.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_feature_name_is_rejected(string name)
    {
        var ex = Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = name, Value = 25 }.Validate());
        Assert.Contains("featureName", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public void Non_positive_value_is_rejected(double v)
    {
        var ex = Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = v }.Validate());
        Assert.Contains("> 0", ex.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_finite_value_is_rejected(double v)
    {
        Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = v }.Validate());
    }

    [Fact]
    public void Implausibly_large_value_is_rejected()
    {
        var ex = Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = 100001 }.Validate());
        Assert.Contains("large", ex.Message);
    }
}
