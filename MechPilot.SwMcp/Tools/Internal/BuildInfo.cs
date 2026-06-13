using System.Reflection;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Reads which build is running (M57) so <c>ping</c> can report it — the cheap
/// fix for the recurring "is the long-lived MCP server my latest exe?" guessing
/// game. The git commit + dirty flag are embedded as AssemblyMetadata at build
/// time (see the EmbedGitInfo target in the csproj); the build time is just the
/// exe file's last-write time, so nothing has to be embedded for it. Pure (no
/// SolidWorks), so it is L1-testable.
/// </summary>
internal static class BuildInfo
{
    /// <summary>
    /// Returns (gitSha, gitDirty, buildTimeUtc) for the given assembly. gitSha
    /// is "unknown" when the build couldn't read git (off-repo / no git on the
    /// build machine); buildTimeUtc is null if the assembly has no file on disk.
    /// </summary>
    public static (string GitSha, bool GitDirty, DateTime? BuildTimeUtc) Read(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var sha = Lookup(metadata, "GitSha") ?? "unknown";
        var dirty = string.Equals(Lookup(metadata, "GitDirty"), "true", StringComparison.OrdinalIgnoreCase);

        DateTime? buildTime = null;
        try
        {
            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                buildTime = File.GetLastWriteTimeUtc(location);
            }
        }
        catch
        {
            // best-effort — leave buildTime null
        }

        return (sha, dirty, buildTime);
    }

    /// <summary>
    /// A one-line human summary, e.g. "git ccdeb6e, built 2026-06-14 18:30:05
    /// UTC" (a "-dirty" suffix on the sha when the build had uncommitted
    /// changes). This is what makes a stale server obvious at a glance.
    /// </summary>
    public static string Describe(string gitSha, bool gitDirty, DateTime? buildTimeUtc)
    {
        var sha = gitDirty ? $"{gitSha}-dirty" : gitSha;
        var built = buildTimeUtc is { } t
            ? t.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            : "unknown";
        return $"git {sha}, built {built}";
    }

    private static string? Lookup(IEnumerable<AssemblyMetadataAttribute> metadata, string key) =>
        metadata.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.Ordinal))?.Value;
}
