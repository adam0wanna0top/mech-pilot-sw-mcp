using MechPilot.SwMcp.Tools.Internal;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 tests for <see cref="BuildInfo"/> — the build-identity reader behind
/// M57's richer ping. Reads against the main assembly (built with the
/// EmbedGitInfo target) and exercises the pure Describe formatter.
/// </summary>
public class BuildInfoTests
{
    [Fact]
    public void Read_main_assembly_returns_sha_and_build_time()
    {
        var asm = typeof(MechPilot.SwMcp.Tools.PingTool).Assembly;
        var (sha, _, buildTime) = BuildInfo.Read(asm);

        // The build embeds a real SHA; off-repo builds fall back to "unknown".
        Assert.False(string.IsNullOrWhiteSpace(sha));
        // The assembly exists on disk, so its build time is readable.
        Assert.NotNull(buildTime);
    }

    [Fact]
    public void Describe_clean_build_has_no_dirty_suffix()
    {
        var when = new DateTime(2026, 6, 14, 18, 30, 5, DateTimeKind.Utc);
        var s = BuildInfo.Describe("ccdeb6e", gitDirty: false, when);

        Assert.Equal("git ccdeb6e, built 2026-06-14 18:30:05 UTC", s);
    }

    [Fact]
    public void Describe_dirty_build_marks_sha()
    {
        var s = BuildInfo.Describe("ccdeb6e", gitDirty: true, buildTimeUtc: null);

        Assert.Contains("ccdeb6e-dirty", s);
        Assert.Contains("built unknown", s);
    }
}
