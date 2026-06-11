using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 tests for the M48 feature-management specs (DeleteFeatureSpec /
/// SuppressFeatureSpec). FILE-mode path checks need real files (Validate
/// does File.Exists), so tests run against a throwaway temp dir.
/// </summary>
public sealed class FeatureManageSpecTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _partPath;

    public FeatureManageSpecTests()
    {
        _tmpDir = Path.Combine(
            Path.GetTempPath(), $"mech-pilot-fmgmt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _partPath = Path.Combine(_tmpDir, "part.sldprt");
        File.WriteAllText(_partPath, "stub");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── DeleteFeatureSpec ───────────────────────────────────────────────────

    [Fact]
    public void Delete_active_mode_passes()
        => new DeleteFeatureSpec { FeatureName = "凸台-拉伸2" }.Validate();

    [Fact]
    public void Delete_file_mode_passes()
        => new DeleteFeatureSpec { FeatureName = "凸台-拉伸2", PartPath = _partPath }.Validate();

    [Fact]
    public void Delete_file_mode_with_output_copy_passes()
        => new DeleteFeatureSpec
        {
            FeatureName = "凸台-拉伸2",
            PartPath = _partPath,
            OutputPath = Path.Combine(_tmpDir, "copy.sldprt"),
        }.Validate();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Delete_empty_feature_name_throws(string bad)
    {
        var spec = new DeleteFeatureSpec { FeatureName = bad };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("featureName", ex.Message);
    }

    [Fact]
    public void Delete_relative_part_path_throws()
    {
        var spec = new DeleteFeatureSpec { FeatureName = "f", PartPath = "rel/part.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Delete_wrong_part_extension_throws()
    {
        var spec = new DeleteFeatureSpec
        {
            FeatureName = "f",
            PartPath = Path.Combine(_tmpDir, "asm.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Delete_missing_part_file_throws()
    {
        var spec = new DeleteFeatureSpec
        {
            FeatureName = "f",
            PartPath = Path.Combine(_tmpDir, "missing.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Delete_output_without_part_path_throws()
    {
        var spec = new DeleteFeatureSpec
        {
            FeatureName = "f",
            OutputPath = Path.Combine(_tmpDir, "copy.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("partPath", ex.Message);
    }

    [Fact]
    public void Delete_output_wrong_extension_throws()
    {
        var spec = new DeleteFeatureSpec
        {
            FeatureName = "f",
            PartPath = _partPath,
            OutputPath = Path.Combine(_tmpDir, "copy.step"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("outputPath", ex.Message);
    }

    // ── SuppressFeatureSpec ─────────────────────────────────────────────────

    [Fact]
    public void Suppress_defaults_to_suppress_true_and_passes()
    {
        var spec = new SuppressFeatureSpec { FeatureName = "圆角1" };
        Assert.True(spec.Suppress);
        spec.Validate();
    }

    [Fact]
    public void Unsuppress_active_mode_passes()
        => new SuppressFeatureSpec { FeatureName = "圆角1", Suppress = false }.Validate();

    [Fact]
    public void Suppress_file_mode_passes()
        => new SuppressFeatureSpec { FeatureName = "圆角1", PartPath = _partPath }.Validate();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Suppress_empty_feature_name_throws(string bad)
    {
        var spec = new SuppressFeatureSpec { FeatureName = bad };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("featureName", ex.Message);
    }

    [Fact]
    public void Suppress_missing_part_file_throws()
    {
        var spec = new SuppressFeatureSpec
        {
            FeatureName = "f",
            PartPath = Path.Combine(_tmpDir, "missing.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Suppress_output_without_part_path_throws()
    {
        var spec = new SuppressFeatureSpec
        {
            FeatureName = "f",
            OutputPath = Path.Combine(_tmpDir, "copy.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("partPath", ex.Message);
    }
}
