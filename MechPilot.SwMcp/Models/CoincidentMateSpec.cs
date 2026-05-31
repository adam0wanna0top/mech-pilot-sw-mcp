using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding one coincident mate between two components'
/// default reference planes (Front / Top / Right). Sibling of
/// <see cref="MirrorSpec"/> in plane-keyword design — both map "front" /
/// "top" / "right" to SW's CN-or-EN named reference planes.
///
/// LLM use case: after add_component places parts at positions, the LLM
/// uses inspect_assembly to learn the component instance names, then
/// add_mate_coincident to constrain "block-1's Top Plane to base-2's
/// Top Plane" (底面贴合 / 端面对齐 — the most common mate type, ~80%
/// of LLM assembly requests).
///
/// **Scope**: only coincident-of-planes (the simplest reliable case).
/// Concentric (cylindrical-face-to-cylindrical-face) and distance mates
/// are future PRs — v1 PR #20 has the AddMate5 magic-position recipe
/// for distance and the CreateMate path for concentric.
/// </summary>
public sealed record CoincidentMateSpec
{
    /// <summary>Absolute path to an existing .sldasm to mate within. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// First component's instance name (e.g. "asm_cyl_123-1"). Get this from
    /// <c>inspect_assembly</c>'s components[].name field — must match exactly.
    /// </summary>
    public required string Component1Name { get; init; }

    /// <summary>Reference plane of component 1: "front" / "top" / "right" (case-insensitive).</summary>
    public required string Plane1 { get; init; }

    /// <summary>Second component's instance name.</summary>
    public required string Component2Name { get; init; }

    /// <summary>Reference plane of component 2.</summary>
    public required string Plane2 { get; init; }

    /// <summary>
    /// Mate alignment: "aligned" (default) / "anti-aligned" / "closest".
    /// "aligned" matches normal directions; "anti-aligned" reverses one;
    /// "closest" lets SW decide. Case-insensitive.
    /// </summary>
    public string Alignment { get; init; } = "aligned";

    /// <summary>
    /// Optional absolute .sldasm output path. When null or empty the input
    /// assembly is overwritten in place (the common case).
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>Accepted reference-plane keywords (case-insensitive). Same dictionary as MirrorSpec.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PlaneAliases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["front"] = new[] { "前视基准面", "Front Plane" },
            ["top"] = new[] { "上视基准面", "Top Plane" },
            ["right"] = new[] { "右视基准面", "Right Plane" },
        };

    /// <summary>Accepted alignment keywords.</summary>
    public static readonly IReadOnlySet<string> AlignmentKeywords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aligned",
            "anti-aligned",
            "closest",
        };

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateAssemblyPath(AssemblyPath);
        ValidateComponentName(Component1Name, "component1Name");
        ValidatePlane(Plane1, "plane1");
        ValidateComponentName(Component2Name, "component2Name");
        ValidatePlane(Plane2, "plane2");
        ValidateAlignment(Alignment);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
        // Same component on both sides is a no-op or self-mate; reject.
        if (string.Equals(Component1Name, Component2Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"component1Name and component2Name must differ (both got '{Component1Name}'). " +
                "Mating a component to itself is not meaningful here.");
        }
    }

    private static void ValidateAssemblyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpToolException("assemblyPath must not be empty.");
        }
        if (!Path.IsPathRooted(path))
        {
            throw new McpToolException($"assemblyPath must be absolute (got '{path}').");
        }
        if (!path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"assemblyPath must end in .sldasm (got '{path}').");
        }
        if (!File.Exists(path))
        {
            throw new McpToolException(
                $"assemblyPath does not exist: '{path}'. " +
                "Create the assembly first with new_assembly + add_component.");
        }
    }

    private static void ValidateComponentName(string name, string field)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new McpToolException(
                $"{field} must not be empty. " +
                "Use inspect_assembly to learn the component instance names first.");
        }
    }

    private static void ValidatePlane(string plane, string field)
    {
        if (string.IsNullOrWhiteSpace(plane))
        {
            throw new McpToolException(
                $"{field} must not be empty. Use 'front', 'top', or 'right'.");
        }
        if (!PlaneAliases.ContainsKey(plane))
        {
            throw new McpToolException(
                $"{field} '{plane}' is not recognized. Supported: " +
                $"{string.Join(", ", PlaneAliases.Keys)}.");
        }
    }

    private static void ValidateAlignment(string alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
        {
            throw new McpToolException(
                "alignment must not be empty. Use 'aligned', 'anti-aligned', or 'closest'.");
        }
        if (!AlignmentKeywords.Contains(alignment))
        {
            throw new McpToolException(
                $"alignment '{alignment}' is not recognized. Supported: " +
                $"{string.Join(", ", AlignmentKeywords)}.");
        }
    }

    private static void ValidateOutputPath(string outputPath)
    {
        if (!Path.IsPathRooted(outputPath))
        {
            throw new McpToolException(
                $"outputPath must be absolute (got '{outputPath}').");
        }
        if (!outputPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"outputPath must end in .sldasm (got '{outputPath}').");
        }
        var dir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new McpToolException($"outputPath has no parent directory: '{outputPath}'.");
        }
        if (!Directory.Exists(dir))
        {
            throw new McpToolException(
                $"outputPath parent directory does not exist: '{dir}'.");
        }
    }
}
