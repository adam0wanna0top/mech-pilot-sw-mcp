using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// ThreadedHoleSpec validates the 7 GB metric-coarse keywords (M3..M12),
/// optional depth (null = through-all), input/output paths, and exposes
/// the GbTapTable for the tool's drill+pitch lookup.
/// </summary>
public class ThreadedHoleSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public ThreadedHoleSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"thread-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private ThreadedHoleSpec Canonical(string thread = "M6") => new()
    {
        InputPath = _existingPart,
        ThreadSize = thread,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("M3")]
    [InlineData("M4")]
    [InlineData("M5")]
    [InlineData("M6")]
    [InlineData("M8")]
    [InlineData("M10")]
    [InlineData("M12")]
    [InlineData("m6")]   // case-insensitive
    public void Supported_thread_sizes_validate(string thread)
    {
        Canonical(thread).Validate();
    }

    [Fact]
    public void Blind_depth_validates()
    {
        var spec = Canonical() with { DepthMm = 8 };
        spec.Validate();
    }

    [Fact]
    public void Explicit_output_validates()
    {
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "thread-out.sldprt") };
        spec.Validate();
    }

    // ── GbTapTable contents ───────────────────────────────────────────────

    [Theory]
    [InlineData("M3", 2.5, 0.5)]
    [InlineData("M4", 3.3, 0.7)]
    [InlineData("M5", 4.2, 0.8)]
    [InlineData("M6", 5.0, 1.0)]
    [InlineData("M8", 6.8, 1.25)]
    [InlineData("M10", 8.5, 1.5)]
    [InlineData("M12", 10.2, 1.75)]
    public void GbTapTable_matches_GBT_196(string thread, double expectedDrill, double expectedPitch)
    {
        Assert.True(ThreadedHoleSpec.GbTapTable.TryGetValue(thread, out var entry));
        Assert.Equal(expectedDrill, entry.DrillDiameterMm);
        Assert.Equal(expectedPitch, entry.PitchMm);
    }

    [Fact]
    public void GbTapTable_has_exactly_7_entries()
    {
        Assert.Equal(7, ThreadedHoleSpec.GbTapTable.Count);
    }

    // ── thread-size validation ────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_thread_throws(string bad)
    {
        var spec = Canonical(bad);
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("threadSize", ex.Message);
    }

    [Theory]
    [InlineData("M2")]   // below smallest
    [InlineData("M14")]  // above largest
    [InlineData("M7")]   // skipped odd size (GB metric-coarse skips M7)
    [InlineData("1/4")]  // imperial — not on GB path
    [InlineData("M")]
    public void Unsupported_thread_throws(string bad)
    {
        var spec = Canonical(bad);
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("not in the GB metric-coarse table", ex.Message);
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
