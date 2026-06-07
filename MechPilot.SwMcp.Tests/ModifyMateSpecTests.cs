using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 for <see cref="ModifyMateSpec"/> validation (M42). The live edit
/// (find mate → set SystemValue → rebuild → save) is covered by the M42 L2
/// integration test. A real temp .sldasm backs the File.Exists check.
/// </summary>
public class ModifyMateSpecTests : IDisposable
{
    private readonly string _asm;

    public ModifyMateSpecTests()
    {
        _asm = Path.Combine(Path.GetTempPath(), $"m42_spec_{Guid.NewGuid():N}.sldasm");
        File.WriteAllText(_asm, "dummy");
    }

    public void Dispose()
    {
        if (File.Exists(_asm)) { File.Delete(_asm); }
    }

    private ModifyMateSpec Valid() => new() { AssemblyPath = _asm, MateName = "Distance1", Value = 25 };

    [Fact]
    public void Valid_spec_does_not_throw() => Valid().Validate();

    [Fact]
    public void Valid_with_output_path_does_not_throw() =>
        (Valid() with { OutputPath = "C:/tmp/copy.sldasm" }).Validate();

    [Fact]
    public void Empty_assemblyPath_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { AssemblyPath = "" }).Validate());

    [Fact]
    public void Relative_assemblyPath_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { AssemblyPath = "rel.sldasm" }).Validate());

    [Fact]
    public void Wrong_extension_assemblyPath_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { AssemblyPath = "C:/tmp/part.sldprt" }).Validate());

    [Fact]
    public void Missing_assemblyPath_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { AssemblyPath = "C:/nope/missing.sldasm" }).Validate());

    [Fact]
    public void Empty_mateName_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { MateName = "  " }).Validate());

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(200000)]
    public void Bad_value_throws(double value) =>
        Assert.Throws<McpToolException>(() => (Valid() with { Value = value }).Validate());

    [Fact]
    public void Wrong_extension_outputPath_throws() =>
        Assert.Throws<McpToolException>(() => (Valid() with { OutputPath = "C:/tmp/out.sldprt" }).Validate());
}
