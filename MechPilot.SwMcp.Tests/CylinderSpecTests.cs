using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

public class CylinderSpecTests
{
    // Use an existing directory the CI runner always has, so the "parent exists"
    // check doesn't false-trigger on machines without C:/tmp.
    private static readonly string TempDir = Path.GetTempPath();
    private static string ValidPath() => Path.Combine(TempDir, "cyl.sldprt");

    [Fact]
    public void Valid_spec_passes()
    {
        var spec = new CylinderSpec { DiameterMm = 30, LengthMm = 50, SavePath = ValidPath() };
        spec.Validate(); // should not throw
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_or_nonfinite_diameter_throws(double bad)
    {
        var spec = new CylinderSpec { DiameterMm = bad, LengthMm = 50, SavePath = ValidPath() };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("diameter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0.05)]   // below MinDimMm
    [InlineData(20_000)] // above MaxDimMm
    public void Diameter_out_of_range_throws(double bad)
    {
        var spec = new CylinderSpec { DiameterMm = bad, LengthMm = 50, SavePath = ValidPath() };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    public void Non_positive_length_throws(double bad)
    {
        var spec = new CylinderSpec { DiameterMm = 30, LengthMm = bad, SavePath = ValidPath() };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_save_path_throws()
    {
        var spec = new CylinderSpec { DiameterMm = 30, LengthMm = 50, SavePath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Relative_save_path_throws()
    {
        var spec = new CylinderSpec { DiameterMm = 30, LengthMm = 50, SavePath = "cyl.sldprt" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wrong_extension_throws()
    {
        var spec = new CylinderSpec
        {
            DiameterMm = 30,
            LengthMm = 50,
            SavePath = Path.Combine(TempDir, "cyl.step"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldprt", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nonexistent_parent_directory_throws()
    {
        var spec = new CylinderSpec
        {
            DiameterMm = 30,
            LengthMm = 50,
            SavePath = Path.Combine(TempDir, "definitely-does-not-exist-" + Guid.NewGuid(), "cyl.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("parent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sldprt_extension_is_case_insensitive()
    {
        var spec = new CylinderSpec
        {
            DiameterMm = 30,
            LengthMm = 50,
            SavePath = Path.Combine(TempDir, "cyl.SLDPRT"),
        };
        spec.Validate(); // should not throw
    }
}
