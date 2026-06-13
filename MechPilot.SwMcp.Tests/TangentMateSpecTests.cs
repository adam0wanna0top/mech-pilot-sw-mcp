using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 tests for <see cref="TangentMateSpec"/> — existing .sldasm + two distinct
/// component names + two non-negative face indexes. Path check needs a real
/// temp file (Validate does File.Exists).
/// </summary>
public sealed class TangentMateSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _asm;

    public TangentMateSpecTests()
    {
        _asm = Path.Combine(TempDir, $"tan-asm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_asm, "stub asm");
    }

    public void Dispose()
    {
        if (File.Exists(_asm)) { File.Delete(_asm); }
    }

    private TangentMateSpec Canonical() => new()
    {
        AssemblyPath = _asm,
        Component1Name = "housing-1",
        Component2Name = "pole-1",
        Face1Index = 0,
        Face2Index = 1,
    };

    [Fact]
    public void Canonical_validates() => Canonical().Validate();

    [Theory]
    [InlineData("closest")]
    [InlineData("aligned")]
    [InlineData("anti-aligned")]
    [InlineData("Closest")]
    public void Alignment_keywords_validate(string a)
        => (Canonical() with { Alignment = a }).Validate();

    [Fact]
    public void Default_alignment_is_closest()
        => Assert.Equal("closest", Canonical().Alignment);

    [Fact]
    public void Unknown_alignment_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { Alignment = "parallel" }).Validate);
        Assert.Contains("alignment", ex.Message);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 12)]
    public void Valid_face_indexes_validate(int f1, int f2)
        => (Canonical() with { Face1Index = f1, Face2Index = f2 }).Validate();

    [Fact]
    public void Negative_face1_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { Face1Index = -1 }).Validate);
        Assert.Contains("face1Index", ex.Message);
    }

    [Fact]
    public void Negative_face2_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { Face2Index = -3 }).Validate);
        Assert.Contains("face2Index", ex.Message);
    }

    [Fact]
    public void Same_component_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { Component2Name = "housing-1" }).Validate);
        Assert.Contains("must differ", ex.Message);
    }

    [Fact]
    public void Empty_component1_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { Component1Name = "" }).Validate);
        Assert.Contains("component1Name", ex.Message);
    }

    [Fact]
    public void Nonexistent_assembly_throws()
    {
        var spec = Canonical() with
        {
            AssemblyPath = Path.Combine(TempDir, $"no-such-{Guid.NewGuid()}.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }
}
