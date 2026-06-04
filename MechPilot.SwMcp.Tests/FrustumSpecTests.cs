using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// FrustumSpec validates 3 diameter/height dimensions (positive, within
/// sanity bounds) + the top &lt; base constraint + the save path. Mirrors
/// HemisphereSpec / CylinderSpec, plus the cross-field constraint.
/// </summary>
public class FrustumSpecTests
{
    private static readonly string TempDir = Path.GetTempPath();

    private static FrustumSpec Canonical() => new()
    {
        BaseDiameterMm = 60,
        TopDiameterMm = 30,
        HeightMm = 40,
        SavePath = Path.Combine(TempDir, $"frustum-{Guid.NewGuid()}.sldprt"),
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    // (base, top, height)
    [InlineData(60, 30, 40)]      // typical
    [InlineData(100, 1, 80)]      // near-cone (top -> 0)
    [InlineData(100, 99, 50)]     // near-cylinder (top -> base)
    [InlineData(0.5, 0.2, 0.3)]   // tiny
    [InlineData(10000, 5000, 8000)]  // max-ish
    public void Valid_dimensions_validate(double baseD, double topD, double h)
    {
        var spec = Canonical() with
        {
            BaseDiameterMm = baseD,
            TopDiameterMm = topD,
            HeightMm = h,
        };
        spec.Validate();
    }

    [Fact]
    public void Save_path_with_uppercase_extension_validates()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"frustum-{Guid.NewGuid()}.SLDPRT"),
        };
        spec.Validate();
    }

    // ── individual dimension rejections ───────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_base_is_rejected(double baseD)
    {
        var spec = Canonical() with { BaseDiameterMm = baseD };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("baseDiameter", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_top_is_rejected(double topD)
    {
        var spec = Canonical() with { TopDiameterMm = topD };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("topDiameter", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Non_positive_height_is_rejected(double h)
    {
        var spec = Canonical() with { HeightMm = h };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("height", ex.Message);
    }

    [Theory]
    [InlineData(0.05)]    // below 0.1 mm
    [InlineData(10001)]   // above 10 m
    public void Base_outside_range_is_rejected(double baseD)
    {
        var spec = Canonical() with { BaseDiameterMm = baseD };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("range", ex.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_finite_dimensions_are_rejected(double v)
    {
        var spec = Canonical() with { BaseDiameterMm = v };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── cross-field: top < base ──────────────────────────────────────────

    [Fact]
    public void Top_equal_to_base_is_rejected_pointing_to_cylinder()
    {
        var spec = Canonical() with
        {
            BaseDiameterMm = 60,
            TopDiameterMm = 60,
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("create_cylinder", ex.Message);
    }

    [Fact]
    public void Top_greater_than_base_is_rejected_as_inverted()
    {
        var spec = Canonical() with
        {
            BaseDiameterMm = 30,
            TopDiameterMm = 60,
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("strictly less than baseDiameter", ex.Message);
    }

    [Fact]
    public void Top_strictly_less_than_base_validates_even_when_close()
    {
        var spec = Canonical() with
        {
            BaseDiameterMm = 60,
            TopDiameterMm = 59.999,
        };
        spec.Validate();   // strict less-than: 59.999 < 60 is OK
    }

    // ── path rejections ───────────────────────────────────────────────────

    [Fact]
    public void Empty_path_is_rejected()
    {
        var spec = Canonical() with { SavePath = string.Empty };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Relative_path_is_rejected()
    {
        var spec = Canonical() with { SavePath = "frustum.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_extension_is_rejected()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"frustum-{Guid.NewGuid()}.step"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Missing_parent_directory_is_rejected()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, $"no-such-dir-{Guid.NewGuid()}", "frustum.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("parent directory does not exist", ex.Message);
    }
}
