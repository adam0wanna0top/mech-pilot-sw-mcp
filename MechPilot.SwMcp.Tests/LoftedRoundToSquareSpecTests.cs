using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// LoftedRoundToSquareSpec validates 4 dimensions (bottom diameter, top
/// length, top width, height) + the save path. Same path validation shape
/// as other create_* specs.
/// </summary>
public class LoftedRoundToSquareSpecTests
{
    private static readonly string TempDir = Path.GetTempPath();

    private static LoftedRoundToSquareSpec Canonical() => new()
    {
        BottomDiameterMm = 60,
        TopLengthMm = 40,
        TopWidthMm = 40,
        HeightMm = 30,
        SavePath = Path.Combine(TempDir, $"loft-{Guid.NewGuid()}.sldprt"),
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData(0.1, 0.1, 0.1, 0.1)]
    [InlineData(60, 40, 40, 30)]
    [InlineData(200, 100, 80, 150)]
    [InlineData(10000, 10000, 10000, 10000)]
    public void Valid_dimensions_validate(double bottomD, double topL, double topW, double h)
    {
        var spec = Canonical() with
        {
            BottomDiameterMm = bottomD,
            TopLengthMm = topL,
            TopWidthMm = topW,
            HeightMm = h,
        };
        spec.Validate();
    }

    [Fact]
    public void Asymmetric_top_rectangle_validates()
    {
        // L != W is fine (rectangular not square top).
        var spec = Canonical() with { TopLengthMm = 100, TopWidthMm = 30 };
        spec.Validate();
    }

    // ── dimension rejections ──────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public void Non_positive_bottom_diameter_is_rejected(double d)
    {
        var spec = Canonical() with { BottomDiameterMm = d };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("bottomDiameter", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    public void Non_positive_top_length_is_rejected(double l)
    {
        var spec = Canonical() with { TopLengthMm = l };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("topLength", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    public void Non_positive_top_width_is_rejected(double w)
    {
        var spec = Canonical() with { TopWidthMm = w };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("topWidth", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Non_positive_height_is_rejected(double h)
    {
        var spec = Canonical() with { HeightMm = h };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("height", ex.Message);
    }

    [Theory]
    [InlineData(0.05)]   // below 0.1 min
    [InlineData(10001)]  // above 10 m max
    public void Bottom_diameter_outside_range_is_rejected(double d)
    {
        var spec = Canonical() with { BottomDiameterMm = d };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_dimensions_are_rejected(double d)
    {
        var spec = Canonical() with { HeightMm = d };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── path rejections ───────────────────────────────────────────────────

    [Fact]
    public void Empty_path_is_rejected()
    {
        var spec = Canonical() with { SavePath = string.Empty };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Fact]
    public void Relative_path_is_rejected()
    {
        var spec = Canonical() with { SavePath = "transition.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_extension_is_rejected()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"loft-{Guid.NewGuid()}.step"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Missing_parent_directory_is_rejected()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"no-such-dir-{Guid.NewGuid()}", "loft.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("parent directory does not exist", ex.Message);
    }
}
