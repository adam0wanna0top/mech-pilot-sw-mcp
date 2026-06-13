using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 tests for <see cref="ToolboxFastenerSpec"/> validation. Path checks
/// need real files (Validate does File.Exists), so each test runs against a
/// throwaway temp dir with a stub .sldasm + .sldprt.
/// </summary>
public sealed class ToolboxFastenerSpecTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _asmPath;
    private readonly string _partPath;

    public ToolboxFastenerSpecTests()
    {
        _tmpDir = Path.Combine(
            Path.GetTempPath(), $"mech-pilot-tbf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _asmPath = Path.Combine(_tmpDir, "asm.sldasm");
        _partPath = Path.Combine(_tmpDir, "bolt.sldprt");
        File.WriteAllText(_asmPath, "stub");
        File.WriteAllText(_partPath, "stub");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    private ToolboxFastenerSpec MakeValid(
        string? config = "M6X30",
        double x = 0, double y = 0, double z = 0) => new()
        {
            AssemblyPath = _asmPath,
            PartPath = _partPath,
            ConfigName = config,
            PositionXMm = x,
            PositionYMm = y,
            PositionZMm = z,
        };

    // ── happy paths ─────────────────────────────────────────────────────────

    [Fact]
    public void Valid_spec_with_config_passes()
        => MakeValid().Validate();

    [Fact]
    public void Null_config_means_default_size_and_passes()
        => MakeValid(config: null).Validate();

    [Fact]
    public void Empty_config_means_default_size_and_passes()
        => MakeValid(config: "").Validate();

    [Fact]
    public void Position_at_sanity_bound_passes()
        => MakeValid(x: 100_000, y: -100_000, z: 100_000).Validate();

    // ── assemblyPath ────────────────────────────────────────────────────────

    [Fact]
    public void Empty_assembly_path_throws()
    {
        var spec = MakeValid() with { AssemblyPath = "" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("assemblyPath", ex.Message);
    }

    [Fact]
    public void Relative_assembly_path_throws()
    {
        var spec = MakeValid() with { AssemblyPath = "rel/asm.sldasm" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Assembly_path_with_part_extension_throws()
    {
        var spec = MakeValid() with { AssemblyPath = _partPath };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldasm", ex.Message);
    }

    [Fact]
    public void Missing_assembly_file_throws_with_hint()
    {
        var spec = MakeValid() with
        {
            AssemblyPath = Path.Combine(_tmpDir, "missing.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("new_assembly", ex.Message);
    }

    // ── partPath ────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_part_path_throws()
    {
        var spec = MakeValid() with { PartPath = "" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("partPath", ex.Message);
    }

    [Fact]
    public void Relative_part_path_throws()
    {
        var spec = MakeValid() with { PartPath = "rel/bolt.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Part_path_with_assembly_extension_throws()
    {
        // Toolbox standard parts are .sldprt — a .sldasm is rejected.
        var spec = MakeValid() with { PartPath = _asmPath };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Missing_part_file_throws_with_toolbox_hint()
    {
        var spec = MakeValid() with
        {
            PartPath = Path.Combine(_tmpDir, "missing.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("Toolbox", ex.Message);
    }

    // ── configName ──────────────────────────────────────────────────────────

    [Fact]
    public void Config_name_at_max_length_passes()
        => MakeValid(config: new string('x', 256)).Validate();

    [Fact]
    public void Overlong_config_name_throws()
    {
        var spec = MakeValid(config: new string('x', 300));
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("configName", ex.Message);
    }

    // ── position ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_position_throws(double bad)
    {
        var spec = MakeValid(x: bad);
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("finite", ex.Message);
    }

    [Fact]
    public void Position_beyond_sanity_bound_throws()
    {
        var spec = MakeValid(y: 100_001);
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("sanity", ex.Message);
    }

    // ── rotation (M53-①) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(90, 0, 0)]
    [InlineData(0, 90, 0)]
    [InlineData(-45.5, 270, 3600)]
    public void Various_rotations_pass(double rx, double ry, double rz)
    {
        var spec = MakeValid() with
        {
            RotationXDeg = rx,
            RotationYDeg = ry,
            RotationZDeg = rz,
        };
        spec.Validate();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_finite_rotation_throws(double bad)
    {
        var spec = MakeValid() with { RotationYDeg = bad };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("finite", ex.Message);
    }

    [Fact]
    public void Rotation_beyond_sanity_bound_throws_with_degrees_hint()
    {
        var spec = MakeValid() with { RotationXDeg = -4_000 };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("RotationX", ex.Message);
        Assert.Contains("DEGREES", ex.Message);
    }
}
