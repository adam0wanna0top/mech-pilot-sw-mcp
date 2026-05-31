using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// InspectAssemblySpec mirrors InspectSpec's path rules with .sldasm
/// extension instead of .sldprt.
/// </summary>
public class InspectAssemblySpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingAsm;

    public InspectAssemblySpecTests()
    {
        _existingAsm = Path.Combine(TempDir, $"inspect-asm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_existingAsm, "stub asm");
    }

    public void Dispose()
    {
        if (File.Exists(_existingAsm))
        {
            File.Delete(_existingAsm);
        }
    }

    private InspectAssemblySpec Canonical() => new() { InputPath = _existingAsm };

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Fact]
    public void Empty_input_throws()
    {
        var spec = Canonical() with { InputPath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("inputPath", ex.Message);
    }

    [Fact]
    public void Relative_input_throws()
    {
        var spec = Canonical() with { InputPath = "asm.sldasm" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Theory]
    [InlineData(".sldprt")]   // part, not assembly — hint should redirect to inspect_part
    [InlineData(".step")]
    [InlineData(".asm")]
    public void Wrong_extension_throws(string ext)
    {
        var spec = Canonical() with { InputPath = Path.Combine(TempDir, $"asm{ext}") };
        var exVal = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldasm", exVal.Message);
    }

    [Fact]
    public void Wrong_extension_sldprt_hints_inspect_part()
    {
        var spec = Canonical() with { InputPath = Path.Combine(TempDir, "asm.sldprt") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        // Error message should redirect LLM to the part-version of the tool.
        Assert.Contains("inspect_part", ex.Message);
    }

    [Fact]
    public void Nonexistent_input_throws()
    {
        var spec = Canonical() with
        {
            InputPath = Path.Combine(TempDir, $"no-such-asm-{Guid.NewGuid()}.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }
}
