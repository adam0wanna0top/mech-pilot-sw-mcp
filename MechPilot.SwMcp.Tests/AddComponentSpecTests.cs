using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// AddComponentSpec validates an existing assembly + existing component +
/// finite XYZ position. The happy paths need real temp files on disk.
/// </summary>
public class AddComponentSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _assembly;
    private readonly string _part;
    private readonly string _subAsm;

    public AddComponentSpecTests()
    {
        _assembly = Path.Combine(TempDir, $"addcomp-asm-{Guid.NewGuid()}.sldasm");
        _part = Path.Combine(TempDir, $"addcomp-part-{Guid.NewGuid()}.sldprt");
        _subAsm = Path.Combine(TempDir, $"addcomp-subasm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_assembly, "stub asm");
        File.WriteAllText(_part, "stub part");
        File.WriteAllText(_subAsm, "stub sub-asm");
    }

    public void Dispose()
    {
        foreach (var f in new[] { _assembly, _part, _subAsm })
        {
            if (File.Exists(f)) { File.Delete(f); }
        }
    }

    private AddComponentSpec Canonical() => new()
    {
        AssemblyPath = _assembly,
        ComponentPath = _part,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Fact]
    public void Sub_assembly_as_component_validates()
    {
        var spec = Canonical() with { ComponentPath = _subAsm };
        spec.Validate();
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(50, 0, 0)]
    [InlineData(-25.5, 100.3, -10)]
    [InlineData(1000, 1000, 1000)]
    public void Various_positions_validate(double x, double y, double z)
    {
        var spec = Canonical() with
        {
            PositionXMm = x,
            PositionYMm = y,
            PositionZMm = z,
        };
        spec.Validate();
    }

    // ── assembly path validation ──────────────────────────────────────────

    [Fact]
    public void Empty_assembly_throws()
    {
        var spec = Canonical() with { AssemblyPath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("assemblyPath", ex.Message);
    }

    [Fact]
    public void Relative_assembly_throws()
    {
        var spec = Canonical() with { AssemblyPath = "asm.sldasm" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_assembly_extension_throws()
    {
        var spec = Canonical() with
        {
            AssemblyPath = Path.Combine(TempDir, "asm.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldasm", ex.Message);
    }

    [Fact]
    public void Nonexistent_assembly_throws()
    {
        var spec = Canonical() with
        {
            AssemblyPath = Path.Combine(TempDir, $"no-such-asm-{Guid.NewGuid()}.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }

    // ── component path validation ─────────────────────────────────────────

    [Fact]
    public void Empty_component_throws()
    {
        var spec = Canonical() with { ComponentPath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("componentPath", ex.Message);
    }

    [Theory]
    [InlineData(".step")]
    [InlineData(".obj")]
    [InlineData(".prt")]
    public void Wrong_component_extension_throws(string ext)
    {
        var spec = Canonical() with
        {
            ComponentPath = Path.Combine(TempDir, $"comp{ext}"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldprt or .sldasm", ex.Message);
    }

    [Fact]
    public void Nonexistent_component_throws()
    {
        var spec = Canonical() with
        {
            ComponentPath = Path.Combine(TempDir, $"no-such-comp-{Guid.NewGuid()}.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }

    // ── position validation ───────────────────────────────────────────────

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFinite_X_throws(double bad)
    {
        var spec = Canonical() with { PositionXMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("PositionX", ex.Message);
    }

    [Fact]
    public void Position_above_sanity_throws()
    {
        var spec = Canonical() with { PositionYMm = 200_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("PositionY", ex.Message);
    }

    // ── rotation validation (M53-①) ───────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(90, 0, 0)]
    [InlineData(0, -90, 0)]
    [InlineData(45.5, 180, -270)]
    [InlineData(3600, -3600, 3600)]
    public void Various_rotations_validate(double rx, double ry, double rz)
    {
        var spec = Canonical() with
        {
            RotationXDeg = rx,
            RotationYDeg = ry,
            RotationZDeg = rz,
        };
        spec.Validate();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void NonFinite_rotation_throws(double bad)
    {
        var spec = Canonical() with { RotationXDeg = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("RotationX", ex.Message);
    }

    [Fact]
    public void Rotation_above_sanity_throws_with_degrees_hint()
    {
        var spec = Canonical() with { RotationZDeg = 5_000 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("RotationZ", ex.Message);
        Assert.Contains("DEGREES", ex.Message);
    }
}
