using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// ShellSpec validates the thickness (positive, within practical (0.01, 100]
/// mm sanity range — real-world shells are 0.5-10 mm; > 100 mm is almost
/// certainly a unit-confusion bug) + the input path (exists, .sldprt) +
/// optional output path.
/// </summary>
public class ShellSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public ShellSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"shell-input-{Guid.NewGuid()}.sldprt");
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

    private ShellSpec Canonical() => new()
    {
        InputPath = _existingPart,
        ThicknessMm = 2,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData(0.01)]    // minimum
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]     // typical
    [InlineData(5.0)]
    [InlineData(100.0)]   // maximum
    public void Valid_thicknesses_validate(double thicknessMm)
    {
        var spec = Canonical() with { ThicknessMm = thicknessMm };
        spec.Validate();
    }

    [Fact]
    public void Default_outward_is_false()
    {
        var spec = Canonical();
        Assert.False(spec.Outward);
    }

    [Fact]
    public void Outward_true_validates()
    {
        var spec = Canonical() with { Outward = true };
        spec.Validate();
    }

    [Fact]
    public void Output_path_with_existing_parent_validates()
    {
        var outPath = Path.Combine(TempDir, $"shell-out-{Guid.NewGuid()}.sldprt");
        var spec = Canonical() with { OutputPath = outPath };
        spec.Validate();
    }

    // ── thickness rejections ──────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void Non_positive_thickness_is_rejected(double thicknessMm)
    {
        var spec = Canonical() with { ThicknessMm = thicknessMm };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("thickness", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0.005)]   // below 0.01
    [InlineData(101)]     // above 100
    [InlineData(1000)]    // mm → m unit-confusion
    [InlineData(1_000_000)]
    public void Thicknesses_outside_range_are_rejected(double thicknessMm)
    {
        var spec = Canonical() with { ThicknessMm = thicknessMm };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("range", ex.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_thickness_is_rejected(double thicknessMm)
    {
        var spec = Canonical() with { ThicknessMm = thicknessMm };
        Assert.Throws<McpToolException>(() => spec.Validate());
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
        var p = Path.Combine(TempDir, $"shell-{Guid.NewGuid()}.step");
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
        Assert.Throws<McpToolException>(() => spec.Validate());
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
