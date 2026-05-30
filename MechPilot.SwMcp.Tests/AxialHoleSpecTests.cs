using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// AxialHoleSpec covers: diameter, depth (nullable through-all vs blind),
/// XY position bounds, input-path existence, and optional output-path
/// directory. Like FilletSpec the happy paths need a real temp .sldprt.
/// </summary>
public class AxialHoleSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public AxialHoleSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"axhole-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private AxialHoleSpec Canonical() => new()
    {
        InputPath = _existingPart,
        DiameterMm = 5,
        DepthMm = null,
        PositionXMm = 0,
        PositionYMm = 0,
        OutputPath = null,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Through_hole_centered_validates()
    {
        Canonical().Validate();
    }

    [Fact]
    public void Blind_hole_with_depth_validates()
    {
        var spec = Canonical() with { DepthMm = 8 };
        spec.Validate();
    }

    [Fact]
    public void Offset_position_validates()
    {
        var spec = Canonical() with { PositionXMm = 5, PositionYMm = -3 };
        spec.Validate();
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(6.6)]   // M6 clearance (LLM-common)
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Various_diameters_validate(double diameter)
    {
        var spec = Canonical() with { DiameterMm = diameter };
        spec.Validate();
    }

    [Theory]
    [InlineData(null)]    // through-all
    [InlineData(0.5)]
    [InlineData(10.0)]    // 10.0 not 10 — xUnit InlineData(int) won't bind to double?
    [InlineData(1000.0)]
    public void Various_depths_validate(double? depth)
    {
        var spec = Canonical() with { DepthMm = depth };
        spec.Validate();
    }

    [Fact]
    public void Explicit_output_validates()
    {
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "axhole-out.sldprt") };
        spec.Validate();
    }

    // ── diameter validation ───────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_diameter_throws(double bad)
    {
        var spec = Canonical() with { DiameterMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("diameter", ex.Message);
    }

    [Fact]
    public void Diameter_above_max_throws()
    {
        var spec = Canonical() with { DiameterMm = 20_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    // ── depth validation ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_depth_throws(double bad)
    {
        var spec = Canonical() with { DepthMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("depth", ex.Message);
    }

    [Fact]
    public void Depth_above_max_throws()
    {
        var spec = Canonical() with { DepthMm = 20_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    // ── position validation ───────────────────────────────────────────────

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFinite_positionX_throws(double bad)
    {
        var spec = Canonical() with { PositionXMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("PositionX", ex.Message);
    }

    [Fact]
    public void PositionX_above_sanity_throws()
    {
        var spec = Canonical() with { PositionXMm = 20_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("PositionX", ex.Message);
    }

    [Fact]
    public void PositionY_below_sanity_throws()
    {
        var spec = Canonical() with { PositionYMm = -20_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("PositionY", ex.Message);
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

    // ── output path validation ────────────────────────────────────────────

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
