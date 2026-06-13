using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 tests for <see cref="DeleteComponentSpec"/> validation — an existing
/// .sldasm + a non-empty instance name. The assembly path check needs a real
/// temp file (Validate does File.Exists).
/// </summary>
public sealed class DeleteComponentSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _assembly;

    public DeleteComponentSpecTests()
    {
        _assembly = Path.Combine(TempDir, $"delcomp-asm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_assembly, "stub asm");
    }

    public void Dispose()
    {
        if (File.Exists(_assembly)) { File.Delete(_assembly); }
    }

    private DeleteComponentSpec Canonical() => new()
    {
        AssemblyPath = _assembly,
        ComponentName = "bolt-2",
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates() => Canonical().Validate();

    [Theory]
    [InlineData("bolt-2")]
    [InlineData("cyl_123-1")]
    [InlineData("凸台-拉伸2-3")]
    public void Various_instance_names_validate(string name)
    {
        (Canonical() with { ComponentName = name }).Validate();
    }

    // ── assembly path validation ──────────────────────────────────────────

    [Fact]
    public void Empty_assembly_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { AssemblyPath = "" }).Validate);
        Assert.Contains("assemblyPath", ex.Message);
    }

    [Fact]
    public void Relative_assembly_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { AssemblyPath = "asm.sldasm" }).Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_assembly_extension_throws()
    {
        var spec = Canonical() with { AssemblyPath = Path.Combine(TempDir, "asm.sldprt") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldasm", ex.Message);
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

    // ── component name validation ─────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_component_name_throws_with_inspect_hint(string name)
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { ComponentName = name }).Validate);
        Assert.Contains("componentName", ex.Message);
        Assert.Contains("inspect_assembly", ex.Message);
    }

    [Fact]
    public void Overlong_component_name_throws()
    {
        var spec = Canonical() with { ComponentName = new string('x', 600) };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("componentName", ex.Message);
    }
}
