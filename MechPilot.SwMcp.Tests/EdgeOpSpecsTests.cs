using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>L1 tests for the M52 edge-operation specs (fillet_edges / chamfer_edges).</summary>
public sealed class EdgeOpSpecsTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _partPath;

    public EdgeOpSpecsTests()
    {
        _tmpDir = Path.Combine(
            Path.GetTempPath(), $"mech-pilot-edgeops-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _partPath = Path.Combine(_tmpDir, "part.sldprt");
        File.WriteAllText(_partPath, "stub");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── FilletEdgesSpec ─────────────────────────────────────────────────────

    [Fact]
    public void Fillet_active_mode_passes()
        => new FilletEdgesSpec { EdgeIndexes = new[] { 4, 7 }, RadiusMm = 3 }.Validate();

    [Fact]
    public void Fillet_file_mode_passes()
        => new FilletEdgesSpec
        {
            EdgeIndexes = new[] { 0 },
            RadiusMm = 2,
            PartPath = _partPath,
        }.Validate();

    [Fact]
    public void Fillet_empty_indexes_throws_with_inspect_hint()
    {
        var spec = new FilletEdgesSpec { EdgeIndexes = Array.Empty<int>(), RadiusMm = 3 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("inspect_topology", ex.Message);
    }

    [Fact]
    public void Fillet_negative_index_throws()
    {
        var spec = new FilletEdgesSpec { EdgeIndexes = new[] { 2, -1 }, RadiusMm = 3 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("negative", ex.Message);
    }

    [Fact]
    public void Fillet_duplicate_index_throws()
    {
        var spec = new FilletEdgesSpec { EdgeIndexes = new[] { 4, 4 }, RadiusMm = 3 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("more than once", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.005)]
    [InlineData(1001)]
    [InlineData(double.NaN)]
    public void Fillet_radius_out_of_bounds_throws(double bad)
    {
        var spec = new FilletEdgesSpec { EdgeIndexes = new[] { 0 }, RadiusMm = bad };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("radius", ex.Message);
    }

    [Fact]
    public void Fillet_output_without_part_path_throws()
    {
        var spec = new FilletEdgesSpec
        {
            EdgeIndexes = new[] { 0 },
            RadiusMm = 3,
            OutputPath = Path.Combine(_tmpDir, "copy.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("partPath", ex.Message);
    }

    // ── ChamferEdgesSpec ────────────────────────────────────────────────────

    [Fact]
    public void Chamfer_active_mode_passes()
        => new ChamferEdgesSpec { EdgeIndexes = new[] { 1 }, DistanceMm = 2 }.Validate();

    [Fact]
    public void Chamfer_file_mode_with_copy_passes()
        => new ChamferEdgesSpec
        {
            EdgeIndexes = new[] { 1, 2, 3 },
            DistanceMm = 1.5,
            PartPath = _partPath,
            OutputPath = Path.Combine(_tmpDir, "copy.sldprt"),
        }.Validate();

    [Fact]
    public void Chamfer_empty_indexes_throws()
    {
        var spec = new ChamferEdgesSpec { EdgeIndexes = Array.Empty<int>(), DistanceMm = 2 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("edgeIndexes", ex.Message);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(double.PositiveInfinity)]
    public void Chamfer_distance_out_of_bounds_throws(double bad)
    {
        var spec = new ChamferEdgesSpec { EdgeIndexes = new[] { 0 }, DistanceMm = bad };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("distance", ex.Message);
    }

    [Fact]
    public void Chamfer_missing_part_file_throws()
    {
        var spec = new ChamferEdgesSpec
        {
            EdgeIndexes = new[] { 0 },
            DistanceMm = 2,
            PartPath = Path.Combine(_tmpDir, "missing.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("does not exist", ex.Message);
    }
}
