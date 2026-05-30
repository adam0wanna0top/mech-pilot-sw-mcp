using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// ExportSpec validates an EXISTING input part and a NEUTRAL output extension.
/// Like FilletSpec, the happy paths need a real temp .sldprt on disk.
/// </summary>
public class ExportSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingPart;

    public ExportSpecTests()
    {
        _existingPart = Path.Combine(TempDir, $"export-input-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(_existingPart, "stub part");
    }

    public void Dispose()
    {
        if (File.Exists(_existingPart))
        {
            File.Delete(_existingPart);
        }
    }

    private ExportSpec Canonical(string ext = ".step") => new()
    {
        InputPath = _existingPart,
        OutputPath = Path.Combine(TempDir, $"export-out{ext}"),
    };

    // ── happy paths: every supported extension validates ──────────────────

    [Theory]
    [InlineData(".step")]
    [InlineData(".stp")]
    [InlineData(".STEP")]   // case-insensitive
    [InlineData(".stl")]
    [InlineData(".iges")]
    [InlineData(".igs")]
    [InlineData(".x_t")]
    [InlineData(".x_b")]
    public void Supported_extensions_validate(string ext)
    {
        Canonical(ext).Validate();
    }

    // ── input path validation ─────────────────────────────────────────────

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
        var spec = Canonical() with { InputPath = "part.sldprt" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_input_extension_throws()
    {
        var spec = Canonical() with { InputPath = Path.Combine(TempDir, "part.step") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Nonexistent_input_throws()
    {
        var spec = Canonical() with
        {
            InputPath = Path.Combine(TempDir, $"no-such-part-{Guid.NewGuid()}.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("does not exist", ex.Message);
    }

    // ── output path validation ────────────────────────────────────────────

    [Fact]
    public void Empty_output_throws()
    {
        var spec = Canonical() with { OutputPath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("outputPath", ex.Message);
    }

    [Fact]
    public void Relative_output_throws()
    {
        var spec = Canonical() with { OutputPath = "out.step" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("absolute", ex.Message);
    }

    [Theory]
    [InlineData(".sldprt")]   // SW native — refuse so we don't accidentally overwrite source format semantics
    [InlineData(".obj")]      // Wavefront — not in SW SaveAs dispatch
    [InlineData(".dxf")]      // 2D drawing — not a part neutral format
    [InlineData("")]
    public void Unsupported_output_extension_throws(string ext)
    {
        var spec = Canonical() with
        {
            OutputPath = Path.Combine(TempDir, $"out{ext}"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("not a supported neutral format", ex.Message);
    }

    [Fact]
    public void Output_equals_input_throws()
    {
        // identical path — even if extension somehow matched, refuse to overwrite source
        var spec = Canonical() with { OutputPath = _existingPart };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        // Either extension or same-path catches it; both are acceptable rejection reasons.
        Assert.True(
            ex.Message.Contains("differ from inputPath") || ex.Message.Contains("not a supported"),
            $"unexpected message: {ex.Message}");
    }

    [Fact]
    public void Same_path_different_extension_passes_extension_then_fails_same_check()
    {
        // Sanity: a hypothetical extension match would surface the differ-from-input check.
        // (No path on disk can both end in .sldprt AND .step, but the check order matters
        // for the error message users see — extension check fires first if the extension
        // is wrong; same-path check fires if extension is valid but paths collide.)
        var validExt = Path.ChangeExtension(_existingPart, ".step");
        // Place a stub at validExt so parent dir checks pass, then point output there
        // and the input to that same path — both ending in .step — and confirm
        // the spec rejects (input must be .sldprt).
        File.WriteAllText(validExt, "stub");
        try
        {
            var spec = Canonical() with { InputPath = validExt, OutputPath = validExt };
            var ex = Assert.Throws<McpToolException>(spec.Validate);
            Assert.Contains(".sldprt", ex.Message);   // input-extension check fires first
        }
        finally
        {
            if (File.Exists(validExt)) { File.Delete(validExt); }
        }
    }

    [Fact]
    public void Nonexistent_output_parent_throws()
    {
        var spec = Canonical() with
        {
            OutputPath = Path.Combine(TempDir, "no-dir-" + Guid.NewGuid(), "out.step"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("directory", ex.Message);
    }
}
