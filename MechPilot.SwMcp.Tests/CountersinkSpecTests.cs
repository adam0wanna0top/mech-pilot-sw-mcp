using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// CountersinkSpec validates M6-M12 (M3/M4/M5 explicitly rejected — SW's
/// internal GB countersink database is missing those, v1 PR #25 finding).
/// </summary>
public class CountersinkSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public CountersinkSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"cs-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private CountersinkSpec Canonical(string thread = "M8") => new()
    {
        InputPath = _existingPart,
        ThreadSize = thread,
    };

    [Theory]
    [InlineData("M6")]
    [InlineData("M8")]
    [InlineData("M10")]
    [InlineData("M12")]
    [InlineData("m10")]
    public void Supported_thread_sizes_validate(string thread)
    {
        Canonical(thread).Validate();
    }

    [Theory]
    [InlineData("M6", 6.6, 12.4)]
    [InlineData("M8", 9.0, 16.4)]
    [InlineData("M10", 11.0, 20.4)]
    [InlineData("M12", 13.5, 24.4)]
    public void GbTable_matches_GBT_152_2(string thread, double cl, double cs)
    {
        Assert.True(CountersinkSpec.GbTable.TryGetValue(thread, out var entry));
        Assert.Equal(cl, entry.ClearanceMm);
        Assert.Equal(cs, entry.CsDiameterMm);
    }

    [Fact]
    public void GbTable_has_exactly_4_entries()
    {
        Assert.Equal(4, CountersinkSpec.GbTable.Count);
    }

    [Theory]
    [InlineData("M3")]   // SW DB doesn't have M3 GB CSK
    [InlineData("M4")]
    [InlineData("M5")]
    public void Small_sizes_rejected_with_hint(string bad)
    {
        var spec = Canonical(bad);
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("not in the GB", ex.Message);
        Assert.Contains("not supported", ex.Message);   // explanation of WHY M3/M4/M5 excluded
    }

    [Theory]
    [InlineData("")]
    [InlineData("M14")]
    [InlineData("M16")]
    public void Other_unsupported_throws(string bad)
    {
        var spec = Canonical(bad);
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.True(
            ex.Message.Contains("not in the GB") || ex.Message.Contains("threadSize"),
            $"unexpected message: {ex.Message}");
    }

    [Fact]
    public void Blind_depth_validates()
    {
        var spec = Canonical() with { DepthMm = 8 };
        spec.Validate();
    }

    [Fact]
    public void Negative_depth_throws()
    {
        var spec = Canonical() with { DepthMm = -1 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("depth", ex.Message);
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
}
