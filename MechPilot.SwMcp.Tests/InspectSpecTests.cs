using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// InspectSpec is the smallest spec we have — only an input path to validate.
/// The path rules mirror FilletSpec exactly so a regression in one fails the
/// other.
/// </summary>
public class InspectSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public InspectSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"inspect-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private InspectSpec Canonical() => new() { InputPath = _existingPart };

    // ── happy path ────────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
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
    public void Whitespace_input_throws()
    {
        var spec = Canonical() with { InputPath = "   " };
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

    // ── InspectActiveSpec (M36) — no parameters, reads the active doc ──────

    [Fact]
    public void InspectActiveSpec_validates_with_no_parameters()
    {
        // Empty spec: Validate is a no-op and must never throw.
        new InspectActiveSpec().Validate();
    }
}
