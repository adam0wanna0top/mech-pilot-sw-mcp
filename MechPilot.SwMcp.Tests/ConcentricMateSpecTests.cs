using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// ConcentricMateSpec is the smallest mate spec — no plane keywords or
/// distance value, just two component names + alignment. Mirrors the
/// path / component / alignment checks shared with M18/M19.
/// </summary>
public class ConcentricMateSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingAsm;

    public ConcentricMateSpecTests()
    {
        _existingAsm = Path.Combine(TempDir, $"cmate-asm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_existingAsm, "stub asm");
    }

    public void Dispose()
    {
        if (File.Exists(_existingAsm))
        {
            File.Delete(_existingAsm);
        }
    }

    private ConcentricMateSpec Canonical() => new()
    {
        AssemblyPath = _existingAsm,
        Component1Name = "cyl-1",
        Component2Name = "block-1",
    };

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
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

    [Theory]
    [InlineData("")]
    [InlineData("parallel")]
    [InlineData("perpendicular")]
    public void Unknown_alignment_throws(string bad)
    {
        var spec = Canonical() with { Alignment = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("alignment", ex.Message);
    }

    [Fact]
    public void Same_component_throws()
    {
        var spec = Canonical() with { Component2Name = "cyl-1" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("must differ", ex.Message);
    }

    [Fact]
    public void Empty_component1_throws()
    {
        var spec = Canonical() with { Component1Name = "" };
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

    [Fact]
    public void Wrong_output_extension_throws()
    {
        var spec = Canonical() with { OutputPath = Path.Combine(TempDir, "out.sldprt") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldasm", ex.Message);
    }
}
