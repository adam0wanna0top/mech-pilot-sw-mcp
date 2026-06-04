using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// AngleMateSpec validates the assembly path + 2 component names + 2 planes
/// + the angle (0, 180)° + alignment + optional output path + the
/// not-same-component cross-field constraint. Mirrors DistanceMateSpec
/// almost 1:1 — the only field-shape difference is angle (degrees, 0..180
/// exclusive) instead of distance (mm, > 0).
/// </summary>
public class AngleMateSpecTests : IDisposable
{
    private static readonly string TempDir = Path.GetTempPath();
    private readonly string _existingAsm;

    public AngleMateSpecTests()
    {
        _existingAsm = Path.Combine(TempDir, $"anglemate-asm-{Guid.NewGuid()}.sldasm");
        File.WriteAllText(_existingAsm, "stub asm");
    }

    public void Dispose()
    {
        if (File.Exists(_existingAsm))
        {
            File.Delete(_existingAsm);
        }
        GC.SuppressFinalize(this);
    }

    private AngleMateSpec Canonical() => new()
    {
        AssemblyPath = _existingAsm,
        Component1Name = "link1-1",
        Plane1 = "front",
        Component2Name = "link2-1",
        Plane2 = "front",
        AngleDeg = 30,
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_validates()
    {
        Canonical().Validate();
    }

    [Theory]
    [InlineData(0.01)]    // minimum
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(90)]      // right angle (the most common)
    [InlineData(135)]
    [InlineData(179.99)]  // maximum
    public void Valid_angles_validate(double angleDeg)
    {
        var spec = Canonical() with { AngleDeg = angleDeg };
        spec.Validate();
    }

    [Theory]
    [InlineData("front")]
    [InlineData("top")]
    [InlineData("right")]
    [InlineData("Front")]   // case-insensitive
    [InlineData("TOP")]
    public void Recognized_planes_validate(string plane)
    {
        var spec = Canonical() with { Plane1 = plane };
        spec.Validate();
    }

    [Theory]
    [InlineData("aligned")]
    [InlineData("anti-aligned")]
    [InlineData("closest")]
    public void Recognized_alignments_validate(string alignment)
    {
        var spec = Canonical() with { Alignment = alignment };
        spec.Validate();
    }

    [Fact]
    public void Default_alignment_is_aligned()
    {
        var spec = Canonical();
        Assert.Equal("aligned", spec.Alignment);
    }

    [Fact]
    public void Output_path_with_existing_parent_validates()
    {
        var outPath = Path.Combine(TempDir, $"anglemate-out-{Guid.NewGuid()}.sldasm");
        var spec = Canonical() with { OutputPath = outPath };
        spec.Validate();
    }

    // ── angle rejections ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Non_positive_angle_is_rejected(double angleDeg)
    {
        var spec = Canonical() with { AngleDeg = angleDeg };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("angle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0.005)]   // below 0.01 min
    [InlineData(180.0)]   // at degenerate parallel
    [InlineData(180.01)]
    [InlineData(360)]
    [InlineData(720)]
    public void Angles_outside_range_are_rejected(double angleDeg)
    {
        var spec = Canonical() with { AngleDeg = angleDeg };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("angle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_angles_are_rejected(double angleDeg)
    {
        var spec = Canonical() with { AngleDeg = angleDeg };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── plane rejections ──────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bottom")]
    [InlineData("back")]
    public void Invalid_plane_is_rejected(string plane)
    {
        var spec = Canonical() with { Plane1 = plane };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── alignment rejections ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("perpendicular")]
    public void Invalid_alignment_is_rejected(string alignment)
    {
        var spec = Canonical() with { Alignment = alignment };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── self-mate rejection ──────────────────────────────────────────────

    [Fact]
    public void Self_mate_is_rejected()
    {
        var spec = Canonical() with
        {
            Component1Name = "link1-1",
            Component2Name = "link1-1",
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("must differ", ex.Message);
    }

    [Fact]
    public void Self_mate_case_insensitive_is_rejected()
    {
        var spec = Canonical() with
        {
            Component1Name = "Link1-1",
            Component2Name = "link1-1",
        };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    // ── path rejections ───────────────────────────────────────────────────

    [Fact]
    public void Empty_assembly_path_is_rejected()
    {
        var spec = Canonical() with { AssemblyPath = string.Empty };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Fact]
    public void Relative_assembly_path_is_rejected()
    {
        var spec = Canonical() with { AssemblyPath = "asm.sldasm" };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Wrong_assembly_extension_is_rejected()
    {
        var p = Path.Combine(TempDir, $"anglemate-{Guid.NewGuid()}.sldprt");
        File.WriteAllText(p, "stub");
        try
        {
            var spec = Canonical() with { AssemblyPath = p };
            var ex = Assert.Throws<McpToolException>(() => spec.Validate());
            Assert.Contains(".sldasm", ex.Message);
        }
        finally
        {
            File.Delete(p);
        }
    }

    [Fact]
    public void Missing_assembly_is_rejected()
    {
        var spec = Canonical() with
        {
            AssemblyPath = Path.Combine(TempDir, $"missing-{Guid.NewGuid()}.sldasm"),
        };
        Assert.Throws<McpToolException>(() => spec.Validate());
    }

    [Fact]
    public void Missing_output_parent_directory_is_rejected()
    {
        var spec = Canonical() with
        {
            OutputPath = Path.Combine(TempDir, $"no-such-dir-{Guid.NewGuid()}", "out.sldasm"),
        };
        var ex = Assert.Throws<McpToolException>(() => spec.Validate());
        Assert.Contains("parent directory does not exist", ex.Message);
    }
}
