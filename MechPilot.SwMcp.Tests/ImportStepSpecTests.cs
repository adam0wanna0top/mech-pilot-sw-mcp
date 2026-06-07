using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 for <see cref="ImportStepSpec"/> validation (M43). The live import
/// (LoadFile4 → SaveAs) is covered by the M43 L2 integration test. A real temp
/// .step backs the File.Exists check.
/// </summary>
public class ImportStepSpecTests : IDisposable
{
    private readonly string _step;

    public ImportStepSpecTests()
    {
        _step = Path.Combine(Path.GetTempPath(), $"m43_in_{Guid.NewGuid():N}.step");
        File.WriteAllText(_step, "dummy");
    }

    public void Dispose()
    {
        if (File.Exists(_step)) { File.Delete(_step); }
    }

    private ImportStepSpec Valid() => new() { InputPath = _step, OutputPath = "C:/tmp/out.sldprt" };

    [Fact]
    public void Valid_does_not_throw() => Valid().Validate();

    [Theory]
    [InlineData(".step")]
    [InlineData(".stp")]
    [InlineData(".iges")]
    [InlineData(".igs")]
    [InlineData(".x_t")]
    [InlineData(".x_b")]
    public void AllowedInputExtensions_includes_neutral_solid_formats(string ext) =>
        Assert.True(ImportStepSpec.AllowedInputExtensions.ContainsKey(ext));

    [Fact]
    public void Empty_input_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { InputPath = "" }).Validate());

    [Fact]
    public void Relative_input_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { InputPath = "rel.step" }).Validate());

    [Fact]
    public void Wrong_input_ext_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { InputPath = "C:/tmp/part.sldprt" }).Validate());

    [Fact]
    public void Stl_input_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { InputPath = "C:/tmp/mesh.stl" }).Validate());

    [Fact]
    public void Missing_input_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { InputPath = "C:/nope/x.step" }).Validate());

    [Fact]
    public void Empty_output_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { OutputPath = "" }).Validate());

    [Fact]
    public void Relative_output_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { OutputPath = "out.sldprt" }).Validate());

    [Fact]
    public void Wrong_output_ext_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { OutputPath = "C:/tmp/out.step" }).Validate());
}
