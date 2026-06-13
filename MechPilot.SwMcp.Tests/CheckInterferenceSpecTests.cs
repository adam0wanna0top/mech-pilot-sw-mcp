using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 tests for <see cref="CheckInterferenceSpec"/> — an existing .sldasm path.
/// The path check needs a real temp file (Validate does File.Exists).
/// </summary>
public sealed class CheckInterferenceSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _asm;

    public CheckInterferenceSpecTests()
    {
        _asm = Path.Combine(TempDir, $"clash-asm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_asm, "stub asm");
    }

    public void Dispose()
    {
        if (File.Exists(_asm)) { File.Delete(_asm); }
    }

    private CheckInterferenceSpec Canonical() => new() { AssemblyPath = _asm };

    [Fact]
    public void Canonical_validates() => Canonical().Validate();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TreatCoincident_does_not_affect_validation(bool flag)
        => (Canonical() with { TreatCoincidentAsInterference = flag }).Validate();

    [Fact]
    public void Empty_path_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { AssemblyPath = "" }).Validate);
        Assert.Contains("assemblyPath", ex.Message);
    }

    [Fact]
    public void Relative_path_throws()
    {
        var ex = Assert.Throws<McpToolException>((Canonical() with { AssemblyPath = "asm.sldasm" }).Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_extension_hints_inspect_part()
    {
        var spec = Canonical() with { AssemblyPath = Path.Combine(TempDir, "part.sldprt") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldasm", ex.Message);
        Assert.Contains("inspect_part", ex.Message);
    }

    [Fact]
    public void Nonexistent_file_throws()
    {
        var spec = Canonical() with
        {
            AssemblyPath = Path.Combine(TempDir, $"no-such-{Guid.NewGuid()}.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }
}
