using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// CoincidentMateSpec validates the assembly path + 2 component names + 2
/// plane keywords + 1 alignment keyword. Component names are free-form
/// strings (validated only as non-empty + not-identical), per LLM usage
/// where they come from inspect_assembly.
/// </summary>
public class CoincidentMateSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingAsm;

    public CoincidentMateSpecTests()
    {
        _existingAsm = Path.Combine(TempDir, $"mate-asm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_existingAsm, "stub asm");
    }

    public void Dispose()
    {
        if (File.Exists(_existingAsm))
        {
            File.Delete(_existingAsm);
        }
    }

    private CoincidentMateSpec Canonical() => new()
    {
        AssemblyPath = _existingAsm,
        Component1Name = "cyl-1",
        Plane1 = "front",
        Component2Name = "block-1",
        Plane2 = "top",
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData("front", "top")]
    [InlineData("top", "right")]
    [InlineData("right", "front")]
    [InlineData("FRONT", "Top")]   // case-insensitive
    public void Plane_combinations_validate(string p1, string p2)
    {
        var spec = Canonical() with { Plane1 = p1, Plane2 = p2 };
        spec.Validate();
    }

    [Theory]
    [InlineData("aligned")]
    [InlineData("anti-aligned")]
    [InlineData("closest")]
    [InlineData("Aligned")]   // case-insensitive
    public void Alignment_keywords_validate(string keyword)
    {
        var spec = Canonical() with { Alignment = keyword };
        spec.Validate();
    }

    [Fact]
    public void Explicit_output_validates()
    {
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "out.sldasm") };
        spec.Validate();
    }

    // ── assembly path validation ──────────────────────────────────────────

    [Fact]
    public void Empty_asm_throws()
    {
        var spec = Canonical() with { AssemblyPath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("assemblyPath", ex.Message);
    }

    [Fact]
    public void Wrong_asm_extension_throws()
    {
        var spec = Canonical() with { AssemblyPath = Path.Combine(TempDir, "asm.sldprt") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldasm", ex.Message);
    }

    [Fact]
    public void Nonexistent_asm_throws()
    {
        var spec = Canonical() with
        {
            AssemblyPath = Path.Combine(TempDir, $"no-such-{Guid.NewGuid()}.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }

    // ── component name validation ─────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_component1_throws(string bad)
    {
        var spec = Canonical() with { Component1Name = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("component1Name", ex.Message);
    }

    [Fact]
    public void Empty_component2_throws()
    {
        var spec = Canonical() with { Component2Name = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("component2Name", ex.Message);
    }

    [Fact]
    public void Same_component_for_both_sides_throws()
    {
        var spec = Canonical() with { Component2Name = "cyl-1" };  // same as Component1Name
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("must differ", ex.Message);
    }

    // ── plane validation ──────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("bottom")]    // common LLM typo
    [InlineData("front plane")]
    public void Unrecognized_plane1_throws(string bad)
    {
        var spec = Canonical() with { Plane1 = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("plane1", ex.Message);
    }

    [Fact]
    public void Unrecognized_plane2_throws()
    {
        var spec = Canonical() with { Plane2 = "xy" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("plane2", ex.Message);
    }

    // ── alignment validation ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("opposite")]
    [InlineData("parallel")]
    public void Unrecognized_alignment_throws(string bad)
    {
        var spec = Canonical() with { Alignment = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("alignment", ex.Message);
    }

    // ── output path validation ────────────────────────────────────────────

    [Fact]
    public void Wrong_output_extension_throws()
    {
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "out.sldprt") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldasm", ex.Message);
    }
}
