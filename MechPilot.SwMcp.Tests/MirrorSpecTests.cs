using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// MirrorSpec validates input path + mirrorPlane keyword + optional output.
/// Path checks mirror FilletSpec one-for-one (single shared validation
/// surface); the mirror-plane keyword check is unique to this spec.
/// </summary>
public class MirrorSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public MirrorSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"mirror-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private MirrorSpec Canonical(string plane = "front") => new()
    {
        InputPath = _existingPart,
        MirrorPlane = plane,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("front")]
    [InlineData("top")]
    [InlineData("right")]
    [InlineData("FRONT")]   // case-insensitive
    [InlineData("Top")]
    public void Recognized_planes_validate(string plane)
    {
        Canonical(plane).Validate();
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
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "mirror-out.sldprt") };
        spec.Validate();
    }

    // ── mirror plane validation ───────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_plane_throws(string bad)
    {
        var spec = Canonical(bad);
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("mirrorPlane", ex.Message);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("xy")]
    [InlineData("left")]    // common LLM typo, not a default SW plane
    [InlineData("frontplane")]
    public void Unrecognized_plane_throws(string bad)
    {
        var spec = Canonical(bad);
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("not recognized", ex.Message);
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
