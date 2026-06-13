using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// DistanceMateSpec mirrors CoincidentMateSpec's validation surface (same
/// component-names + plane-keyword + alignment + asm path checks) plus an
/// additional positive distance field. PlaneAliases / AlignmentKeywords are
/// shared via CoincidentMateSpec's static members — no duplicated tables.
/// </summary>
public class DistanceMateSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingAsm;

    public DistanceMateSpecTests()
    {
        _existingAsm = Path.Combine(TempDir, $"dmate-asm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_existingAsm, "stub asm");
    }

    public void Dispose()
    {
        if (File.Exists(_existingAsm))
        {
            File.Delete(_existingAsm);
        }
    }

    private DistanceMateSpec Canonical() => new()
    {
        AssemblyPath = _existingAsm,
        Component1Name = "cyl-1",
        Plane1 = "top",
        Component2Name = "block-1",
        Plane2 = "top",
        DistanceMm = 25,
    };

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(25)]
    [InlineData(1000)]
    [InlineData(50000)]
    public void Various_distances_validate(double mm)
    {
        var spec = Canonical() with { DistanceMm = mm };
        spec.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_positive_distance_throws(double bad)
    {
        var spec = Canonical() with { DistanceMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("distance", ex.Message);
    }

    [Fact]
    public void Distance_above_max_throws()
    {
        var spec = Canonical() with { DistanceMm = 200_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("range", ex.Message);
    }

    [Fact]
    public void Same_component_throws()
    {
        var spec = Canonical() with { Component2Name = "cyl-1" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("must differ", ex.Message);
    }

    [Fact]
    public void Unknown_plane_throws()
    {
        var spec = Canonical() with { Plane1 = "bottom" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("plane1", ex.Message);
    }

    [Fact]
    public void Unknown_alignment_throws()
    {
        var spec = Canonical() with { Alignment = "parallel" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("alignment", ex.Message);
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
    public void Empty_component1_throws()
    {
        var spec = Canonical() with { Component1Name = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("component1Name", ex.Message);
    }

    // ── topology face indexing (M54) ──────────────────────────────────────

    [Fact]
    public void Face_index_replaces_plane_and_validates()
    {
        var spec = Canonical() with { Plane2 = null, Face2Index = 4 };
        spec.Validate();
    }

    [Fact]
    public void Negative_face2_index_throws()
    {
        var spec = Canonical() with { Plane2 = null, Face2Index = -3 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("face2Index", ex.Message);
    }

    [Fact]
    public void Neither_plane_nor_face_on_a_side_throws()
    {
        var spec = Canonical() with { Plane2 = null, Face2Index = null };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("plane2", ex.Message);
    }
}
