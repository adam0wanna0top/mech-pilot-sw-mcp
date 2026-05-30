using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// LinearPatternSpec validates axis keywords + count/spacing per direction +
/// the dir2-coupled fields (must be set together) + input/output paths.
/// </summary>
public class LinearPatternSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public LinearPatternSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"linpat-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private LinearPatternSpec Canonical() => new()
    {
        InputPath = _existingPart,
        Direction1Axis = "x",
        CountDir1 = 3,
        SpacingDir1Mm = 10,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Single_direction_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("z")]
    [InlineData("X")]   // case-insensitive
    [InlineData("Y")]
    public void Recognized_axes_validate(string axis)
    {
        var spec = Canonical() with { Direction1Axis = axis };
        spec.Validate();
    }

    [Fact]
    public void Two_direction_validates()
    {
        var spec = Canonical() with
        {
            Direction2Axis = "y",
            CountDir2 = 5,
            SpacingDir2Mm = 15,
        };
        spec.Validate();
    }

    [Fact]
    public void With_explicit_feature_name_validates()
    {
        var spec = Canonical() with { FeatureName = "Cut-Extrude1" };
        spec.Validate();
    }

    [Fact]
    public void Explicit_output_validates()
    {
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "linpat-out.sldprt") };
        spec.Validate();
    }

    // ── axis validation ───────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_axis_throws(string bad)
    {
        var spec = Canonical() with { Direction1Axis = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("direction1Axis", ex.Message);
    }

    [Theory]
    [InlineData("w")]
    [InlineData("xy")]
    [InlineData("axis1")]
    public void Unrecognized_axis_throws(string bad)
    {
        var spec = Canonical() with { Direction1Axis = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("not recognized", ex.Message);
    }

    [Fact]
    public void Dir2_same_as_dir1_throws()
    {
        var spec = Canonical() with
        {
            Direction2Axis = "x",   // same as dir1
            CountDir2 = 3,
            SpacingDir2Mm = 10,
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("direction2Axis must differ", ex.Message);
    }

    // ── count validation ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]   // 1 means "seed only" — pattern is a no-op, reject
    [InlineData(-5)]
    public void CountDir1_below_min_throws(int bad)
    {
        var spec = Canonical() with { CountDir1 = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("countDir1", ex.Message);
    }

    [Fact]
    public void CountDir1_above_cap_throws()
    {
        var spec = Canonical() with { CountDir1 = 5000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("exceeds", ex.Message);
    }

    [Fact]
    public void CountDir2_set_without_axis2_throws()
    {
        var spec = Canonical() with { CountDir2 = 3 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("no direction2Axis", ex.Message);
    }

    [Fact]
    public void CountDir2_below_min_when_axis2_set_throws()
    {
        var spec = Canonical() with
        {
            Direction2Axis = "y",
            CountDir2 = 1,        // pattern with second direction needs >= 2
            SpacingDir2Mm = 10,
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("countDir2", ex.Message);
    }

    // ── spacing validation ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_spacing1_throws(double bad)
    {
        var spec = Canonical() with { SpacingDir1Mm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("spacingDir1", ex.Message);
    }

    [Fact]
    public void Spacing1_above_max_throws()
    {
        var spec = Canonical() with { SpacingDir1Mm = 20_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    [Fact]
    public void Spacing2_zero_when_axis2_set_throws()
    {
        var spec = Canonical() with
        {
            Direction2Axis = "y",
            CountDir2 = 3,
            SpacingDir2Mm = 0,
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("spacingDir2", ex.Message);
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
    public void Nonexistent_input_throws()
    {
        var spec = Canonical() with
        {
            InputPath = Path.Combine(TempDir, $"no-such-part-{Guid.NewGuid()}.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Wrong_input_extension_throws()
    {
        var spec = Canonical() with { InputPath = Path.Combine(TempDir, "part.step") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldprt", ex.Message);
    }

    // ── output path validation ────────────────────────────────────────────

    [Fact]
    public void Relative_output_throws()
    {
        var spec = Canonical() with { OutputPath = "out.sldprt" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }
}
