using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 unit tests for ModifyFeatureSpec — the edit primitive's input validation:
/// a feature name + positive new dimension (M38), plus the optional FILE-mode
/// partPath / outputPath (M44). A real temp .sldprt backs the File.Exists check.
/// </summary>
public class ModifyFeatureSpecTests : IDisposable
{
    private readonly string _part;

    public ModifyFeatureSpecTests()
    {
        _part = Path.Combine(Path.GetTempPath(), $"m44_spec_{Guid.NewGuid():N}.sldprt");
        File.WriteAllText(_part, "dummy");
    }

    public void Dispose()
    {
        if (File.Exists(_part)) { File.Delete(_part); }
    }

    [Fact]
    public void Canonical_validates() =>
        new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = 25 }.Validate();

    [Theory]
    [InlineData(0.001)]
    [InlineData(25)]
    [InlineData(360)]
    [InlineData(100000)]
    public void Valid_values_validate(double v) =>
        new ModifyFeatureSpec { FeatureName = "旋转1", Value = v }.Validate();

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
    public void Non_finite_value_is_rejected(double v) =>
        Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = v }.Validate());

    [Fact]
    public void Implausibly_large_value_is_rejected()
    {
        var ex = Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = 100001 }.Validate());
        Assert.Contains("large", ex.Message);
    }

    // ── M44 FILE mode (partPath / outputPath) ──────────────────────────────

    [Fact]
    public void Valid_partPath_validates() =>
        new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = 90, PartPath = _part }.Validate();

    [Fact]
    public void Valid_partPath_with_output_validates() =>
        new ModifyFeatureSpec
        {
            FeatureName = "凸台-拉伸1",
            Value = 90,
            PartPath = _part,
            OutputPath = "C:/tmp/out.sldprt",
        }.Validate();

    [Fact]
    public void Relative_partPath_is_rejected() =>
        Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = 90, PartPath = "rel.sldprt" }.Validate());

    [Fact]
    public void Wrong_extension_partPath_is_rejected() =>
        Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = 90, PartPath = "C:/tmp/asm.sldasm" }.Validate());

    [Fact]
    public void Missing_partPath_is_rejected() =>
        Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec { FeatureName = "凸台-拉伸1", Value = 90, PartPath = "C:/nope/x.sldprt" }.Validate());

    [Fact]
    public void Wrong_extension_outputPath_is_rejected() =>
        Assert.Throws<McpToolException>(() =>
            new ModifyFeatureSpec
            {
                FeatureName = "凸台-拉伸1",
                Value = 90,
                PartPath = _part,
                OutputPath = "C:/tmp/out.step",
            }.Validate());
}
