using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// NewAssemblySpec is the smallest write-spec — only a SavePath to validate
/// with .sldasm extension.
/// </summary>
public class NewAssemblySpecTests
{
    private static readonly string TempDir = Path.GetTempPath();

    private static NewAssemblySpec Canonical() => new()
    {
        SavePath = Path.Combine(TempDir, "asm.sldasm"),
    };

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Fact]
    public void Empty_savePath_throws()
    {
        var spec = Canonical() with { SavePath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("savePath", ex.Message);
    }

    [Fact]
    public void Relative_savePath_throws()
    {
        var spec = Canonical() with { SavePath = "asm.sldasm" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Theory]
    [InlineData(".sldprt")]   // part extension, not assembly
    [InlineData(".step")]
    [InlineData(".asm")]      // close but not the SW extension
    public void Wrong_extension_throws(string ext)
    {
        var spec = Canonical() with { SavePath = Path.Combine(TempDir, $"asm{ext}") };
        var exVal = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldasm", exVal.Message);
    }

    [Fact]
    public void Nonexistent_savePath_parent_throws()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, "no-dir-" + Guid.NewGuid(), "asm.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("directory", ex.Message);
    }
}
