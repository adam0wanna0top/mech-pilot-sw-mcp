using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;

namespace MechPilot.SwMcp.Tests;

public class FlangeSpecTests
{
    private static readonly string TempDir = Path.GetTempPath();
    private static string ValidPath() => Path.Combine(TempDir, "flange.sldprt");

    /// <summary>The reference flange we test most variations against (PR #35 canonical case).</summary>
    private static FlangeSpec Canonical() => new()
    {
        OuterDiameterMm = 80,
        ThicknessMm = 10,
        CenterHoleDiameterMm = 30,
        BoltCount = 4,
        BoltDiameterMm = 6,
        BoltCircleDiameterMm = 55,
        SavePath = ValidPath(),
    };

    // ── happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Canonical_PR35_flange_validates()
    {
        Canonical().Validate();
    }

    [Fact]
    public void Solid_disk_no_holes_validates()
    {
        var spec = Canonical() with
        {
            CenterHoleDiameterMm = 0,
            BoltCount = 0,
            BoltDiameterMm = 0,
            BoltCircleDiameterMm = 0,
        };
        spec.Validate();
    }

    [Fact]
    public void Center_hole_only_no_bolts_validates()
    {
        var spec = Canonical() with
        {
            BoltCount = 0,
            BoltDiameterMm = 0,
            BoltCircleDiameterMm = 0,
        };
        spec.Validate();
    }

    [Fact]
    public void Bolts_only_no_center_hole_validates()
    {
        var spec = Canonical() with { CenterHoleDiameterMm = 0 };
        spec.Validate();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    public void Various_bolt_counts_validate(int count)
    {
        var spec = Canonical() with { BoltCount = count };
        spec.Validate();
    }

    // ── basic field validation ────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    public void Non_positive_outer_throws(double bad)
    {
        var spec = Canonical() with { OuterDiameterMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("outerDiameter", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_thickness_throws(double bad)
    {
        var spec = Canonical() with { ThicknessMm = bad };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("thickness", ex.Message);
    }

    [Fact]
    public void Negative_center_hole_throws()
    {
        var spec = Canonical() with { CenterHoleDiameterMm = -1 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("centerHoleDiameter", ex.Message);
    }

    // ── geometric relations ──────────────────────────────────────────────

    [Fact]
    public void Center_hole_ge_outer_throws()
    {
        var spec = Canonical() with { CenterHoleDiameterMm = 80 }; // == outer
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("centerHole", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pcd_too_large_extends_past_outer_throws()
    {
        // bolt D6 on PCD78 → bolt edge at r=42 > outer/2=40 → overlap outer
        var spec = Canonical() with { BoltCircleDiameterMm = 78 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("PCD", ex.Message);
        Assert.Contains("outerDiameter", ex.Message);
    }

    [Fact]
    public void Pcd_too_small_overlaps_center_hole_throws()
    {
        // bolt D6 on PCD32, center D30 → bolt inner edge at r=13 < center/2=15 → overlap center
        var spec = Canonical() with { BoltCircleDiameterMm = 32 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("PCD", ex.Message);
        Assert.Contains("centerHole", ex.Message);
    }

    [Fact]
    public void Too_many_bolts_chord_overlap_throws()
    {
        // 20 bolts D6 on PCD55 → chord = 55 * sin(π/20) ≈ 8.6 mm > 6 mm OK
        // 60 bolts D6 on PCD55 → chord = 55 * sin(π/60) ≈ 2.88 mm < 6 mm FAIL
        var spec = Canonical() with { BoltCount = 60 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("overlap each other", ex.Message);
    }

    [Fact]
    public void Bolt_count_zero_but_bolt_geometry_set_throws()
    {
        // Catches the "user forgot to bump boltCount" footgun.
        var spec = Canonical() with { BoltCount = 0 };
        // BoltDiameterMm and BoltCircleDiameterMm are still 6 / 55 from Canonical.
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("boltCount is 0", ex.Message);
    }

    [Fact]
    public void Bolt_count_positive_but_no_diameter_throws()
    {
        var spec = Canonical() with { BoltDiameterMm = 0 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("boltDiameter", ex.Message);
    }

    [Fact]
    public void Bolt_count_positive_but_no_pcd_throws()
    {
        var spec = Canonical() with { BoltCircleDiameterMm = 0 };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("boltCircleDiameter", ex.Message);
    }

    // ── path validation (shared logic with CylinderSpec, but assert each path) ──

    [Fact]
    public void Empty_save_path_throws()
    {
        var spec = Canonical() with { SavePath = "" };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wrong_extension_throws()
    {
        var spec = Canonical() with { SavePath = Path.Combine(TempDir, "flange.step") };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains(".sldprt", ex.Message);
    }

    [Fact]
    public void Nonexistent_parent_directory_throws()
    {
        var spec = Canonical() with
        {
            SavePath = Path.Combine(TempDir, "does-not-exist-" + Guid.NewGuid(), "flange.sldprt"),
        };
        var ex = Assert.Throws<McpToolException>(spec.Validate);
        Assert.Contains("parent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
