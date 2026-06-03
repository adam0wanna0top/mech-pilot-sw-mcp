using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// CircularPatternSpec validates count (≥2) + totalAngleDeg (1..360) + input
/// path (exists, .sldprt) + optional output path (parent must exist, .sldprt).
/// </summary>
public class CircularPatternSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public CircularPatternSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"cirpat-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
        GC.SuppressFinalize(this);
    }

    private CircularPatternSpec Canonical() => new()
    {
        InputPath = _existingPart,
        Count = 6,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData(2)]      // minimum
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(360)]    // maximum (1°/instance ceiling)
    public void Valid_counts_validate(int count)
    {
        var spec = Canonical() with { Count = count };
        spec.Validate();
    }

    [Theory]
    [InlineData(1.0)]    // minimum
    [InlineData(90.0)]
    [InlineData(180.0)]
    [InlineData(270.0)]
    [InlineData(360.0)]  // default / full circle
    public void Valid_angles_validate(double angleDeg)
    {
        var spec = Canonical() with { TotalAngleDeg = angleDeg };
        spec.Validate();
    }

    [Fact]
    public void Default_angle_is_360()
    {
        var spec = Canonical();
        Assert.Equal(360.0, spec.TotalAngleDeg);
    }

    [Fact]
    public void Output_path_with_existing_parent_validates()
    {
        var outPath = Path.Combine(TempDir, $"cirpat-out-{Guid.NewGuid()}.sldprt");
        var spec = Canonical() with { OutputPath = outPath };
        spec.Validate();   // does not need to exist; parent must exist
    }

    [Fact]
    public void Feature_name_optional()
    {
        var spec = Canonical() with { FeatureName = "Cut-Extrude2" };
        spec.Validate();
    }

    // ── count rejections ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]      // seed-only is a no-op
    [InlineData(-3)]
    public void Counts_below_min_are_rejected(int count)
    {
        var spec = Canonical() with { Count = count };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Count_above_max_is_rejected()
    {
        var spec = Canonical() with { Count = 361 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("361", ex.Message);
    }

    // ── angle rejections ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(-90.0)]
    [InlineData(0.5)]    // below 1° min
    [InlineData(360.01)] // above 360° max
    [InlineData(720.0)]
    public void Angles_outside_range_are_rejected(double angleDeg)
    {
        var spec = Canonical() with { TotalAngleDeg = angleDeg };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("totalAngleDeg", ex.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_angles_are_rejected(double angleDeg)
    {
        var spec = Canonical() with { TotalAngleDeg = angleDeg };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("finite", ex.Message);
    }

    // ── input-path rejections ─────────────────────────────────────────────

    [Fact]
    public void Empty_input_is_rejected()
    {
        var spec = Canonical() with { InputPath = string.Empty };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Relative_input_is_rejected()
    {
        var spec = Canonical() with { InputPath = "relative.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_input_extension_is_rejected()
    {
        var p = Path.Combine(TempDir, $"cirpat-{Guid.NewGuid()}.step");
        File.WriteAllText(p, "stub");
        try
        {
            var spec = Canonical() with { InputPath = p };
            var ex = Assert.Throws<McpToolException>(() => spec.Validate());
            Assert.Contains(".sldprt", ex.Message);
        }
        finally
        {
            File.Delete(p);
        }
    }

    [Fact]
    public void Missing_input_is_rejected()
    {
        var spec = Canonical() with
        {
            InputPath = Path.Combine(TempDir, $"missing-{Guid.NewGuid()}.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("does not exist", ex.Message);
    }

    // ── output-path rejections ────────────────────────────────────────────

    [Fact]
    public void Relative_output_is_rejected()
    {
        var spec = Canonical() with { OutputPath = "relative.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_output_extension_is_rejected()
    {
        var spec = Canonical() with
        {
            OutputPath = Path.Combine(TempDir, "wrong.step"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Missing_output_parent_is_rejected()
    {
        var spec = Canonical() with
        {
            OutputPath = Path.Combine(TempDir, $"no-such-dir-{Guid.NewGuid()}", "out.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("parent directory does not exist", ex.Message);
    }
}
