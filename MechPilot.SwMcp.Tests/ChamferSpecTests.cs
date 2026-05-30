using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// ChamferSpec mirrors FilletSpec's validation surface (same input-existence,
/// extension, and unit-bounds checks); the only field that differs is
/// DistanceMm vs RadiusMm. Tests parallel FilletSpecTests one-for-one so a
/// regression in the shared validation logic is caught on both specs.
/// </summary>
public class ChamferSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public ChamferSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"chamfer-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private ChamferSpec Canonical() => new()
    {
        InputPath = _existingPart,
        DistanceMm = 2,
        OutputPath = null,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_in_place_validates()
    {
        Canonical().Validate();
    }

    [Fact]
    public void Empty_output_is_in_place_and_validates()
    {
        var spec = Canonical() with { OutputPath = "" };
        spec.Validate();
    }

    [Fact]
    public void Explicit_output_validates()
    {
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "chamfer-out.sldprt") };
        spec.Validate();
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(1000)]
    public void Various_distances_validate(double distance)
    {
        var spec = Canonical() with { DistanceMm = distance };
        spec.Validate();
    }

    // ── distance validation ───────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_distance_throws(double bad)
    {
        var spec = Canonical() with { DistanceMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("distance", ex.Message);
    }

    [Fact]
    public void Distance_above_max_throws()
    {
        var spec = Canonical() with { DistanceMm = 2000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    [Fact]
    public void Distance_below_min_throws()
    {
        var spec = Canonical() with { DistanceMm = 0.001 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    // ── input path validation ─────────────────────────────────────────────

    [Fact]
    public void Empty_input_throws()
    {
        var spec = Canonical() with { InputPath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("inputPath", ex.Message);
    }

    [Fact]
    public void Relative_input_throws()
    {
        var spec = Canonical() with { InputPath = "part.sldprt" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_input_extension_throws()
    {
        var spec = Canonical() with { InputPath = Path.Combine(TempDir, "part.step") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Nonexistent_input_throws()
    {
        var spec = Canonical() with
        {
            InputPath = Path.Combine(TempDir, $"no-such-part-{Guid.NewGuid()}.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }

    // ── output path validation (only enforced when an output is given) ──────

    [Fact]
    public void Relative_output_throws()
    {
        var spec = Canonical() with { OutputPath = "out.sldprt" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_output_extension_throws()
    {
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "out.step") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Nonexistent_output_parent_throws()
    {
        var spec = Canonical() with
        {
            OutputPath = Path.Combine(TempDir, "no-dir-" + Guid.NewGuid(), "out.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("directory", ex.Message);
    }
}
