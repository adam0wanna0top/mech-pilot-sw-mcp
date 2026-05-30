using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for mirroring a feature across one of the three default
/// reference planes. Sibling of <see cref="AxialHoleSpec"/> in spirit — both
/// edit an existing part — but uses a different SW selection-mark layout
/// (plane=2, feature=1; opposite of pattern's edge=1/seed=4).
///
/// LLM use case: "mirror that hole across the Front plane" → one tool call.
/// </summary>
public sealed record MirrorSpec
{
    /// <summary>Absolute path to an existing .sldprt to edit. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Mirror plane keyword: "front" / "top" / "right" (case-insensitive).
    /// Mapped at runtime to SW's named reference planes (CN / EN both tried).
    /// </summary>
    public required string MirrorPlane { get; init; }

    /// <summary>
    /// Optional name of the feature to mirror (e.g. "Cut-Extrude1"). When
    /// null or empty the tool picks the last user-meaningful feature
    /// (same boot-filter as inspect_part), so the LLM-common "mirror that
    /// hole I just drilled" works without naming the feature.
    /// </summary>
    public string? FeatureName { get; init; }

    /// <summary>
    /// Optional absolute .sldprt output path. When null or empty the input
    /// file is overwritten in place; when given, its parent directory must
    /// exist.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>Accepted mirror-plane keywords (case-insensitive).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PlaneAliases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // SW 中文 UI 用中文名, EN 模式用英文 — 工具 runtime 两个都试。
            ["front"] = new[] { "前视基准面", "Front Plane" },
            ["top"] = new[] { "上视基准面", "Top Plane" },
            ["right"] = new[] { "右视基准面", "Right Plane" },
        };

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateMirrorPlane(MirrorPlane);
        ValidateInputPath(InputPath);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
    }

    private static void ValidateMirrorPlane(string plane)
    {
        if (string.IsNullOrWhiteSpace(plane))
        {
            throw new McpToolException(
                "mirrorPlane must not be empty. Use 'front', 'top', or 'right'.");
        }
        if (!PlaneAliases.ContainsKey(plane))
        {
            var supported = string.Join(", ", PlaneAliases.Keys);
            throw new McpToolException(
                $"mirrorPlane '{plane}' is not recognized. Supported: {supported}.");
        }
    }

    private static void ValidateInputPath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new McpToolException("inputPath must not be empty.");
        }
        if (!Path.IsPathRooted(inputPath))
        {
            throw new McpToolException(
                $"inputPath must be absolute (got '{inputPath}').");
        }
        if (!inputPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"inputPath must end in .sldprt (got '{inputPath}').");
        }
        if (!File.Exists(inputPath))
        {
            throw new McpToolException(
                $"inputPath does not exist: '{inputPath}'. " +
                "Create the part first (e.g. with create_cylinder / create_flange).");
        }
    }

    private static void ValidateOutputPath(string outputPath)
    {
        if (!Path.IsPathRooted(outputPath))
        {
            throw new McpToolException(
                $"outputPath must be absolute (got '{outputPath}').");
        }
        if (!outputPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"outputPath must end in .sldprt (got '{outputPath}').");
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
