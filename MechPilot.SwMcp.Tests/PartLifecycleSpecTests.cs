using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 unit tests for the M29 part lifecycle specs (NewPartSpec /
/// SavePartSpec). NewPartSpec has no fields → only a smoke test.
/// SavePartSpec mirrors CylinderSpec.SavePath validation.
/// </summary>
public class PartLifecycleSpecTests
{
    private static readonly string TempDir = Path.GetTempPath();

    // ── NewPartSpec ──────────────────────────────────────────────────────

    [Fact]
    public void NewPartSpec_validates_with_no_fields()
    {
        new NewPartSpec().Validate();
    }

    // ── SavePartSpec happy paths ─────────────────────────────────────────

    private static SavePartSpec CanonicalSave() => new()
    {
        SavePath = Path.Combine(TempDir, $"part-{Guid.NewGuid()}.sldprt"),
    };

    [Fact]
    public void SavePartSpec_canonical_validates()
    {
        CanonicalSave().Validate();
    }

    [Fact]
    public void SavePartSpec_uppercase_extension_validates()
    {
        var spec = CanonicalSave() with
        {
            SavePath = Path.Combine(TempDir, $"part-{Guid.NewGuid()}.SLDPRT"),
        };
        spec.Validate();
    }

    // ── SavePartSpec rejections ──────────────────────────────────────────

    [Fact]
    public void SavePartSpec_empty_path_is_rejected()
    {
        var spec = CanonicalSave() with { SavePath = string.Empty };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavePartSpec_whitespace_path_is_rejected()
    {
        var spec = CanonicalSave() with { SavePath = "   " };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Fact]
    public void SavePartSpec_relative_path_is_rejected()
    {
        var spec = CanonicalSave() with { SavePath = "part.sldprt" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void SavePartSpec_wrong_extension_is_rejected()
    {
        var spec = CanonicalSave() with
        {
            SavePath = Path.Combine(TempDir, $"part-{Guid.NewGuid()}.step"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void SavePartSpec_no_extension_is_rejected()
    {
        var spec = CanonicalSave() with
        {
            SavePath = Path.Combine(TempDir, $"part-{Guid.NewGuid()}"),
        };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Fact]
    public void SavePartSpec_missing_parent_directory_is_rejected()
    {
        var spec = CanonicalSave() with
        {
            SavePath = Path.Combine(TempDir, $"no-such-dir-{Guid.NewGuid()}", "part.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("parent directory does not exist", ex.Message);
    }
}
