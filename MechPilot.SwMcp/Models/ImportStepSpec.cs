using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Spec for importing a neutral CAD file (STEP / IGES / Parasolid) as a
/// SolidWorks part (.sldprt) — M43. The imported part is a DUMB body (no
/// parametric feature tree; carries an MBimport node), which inspect_assembly
/// classifies as "imported" — a fixed anchor the resize orchestration must not
/// edit. Enables bringing a vendor/external part into an assembly.
/// </summary>
public sealed record ImportStepSpec
{
    /// <summary>Absolute path to an existing neutral CAD file to import.</summary>
    public required string InputPath { get; init; }

    /// <summary>Absolute output path ending in .sldprt.</summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Neutral solid formats SW imports via LoadFile4 (mirrors export_part's
    /// solid formats; STL is a mesh and is intentionally excluded).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedInputExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".step"] = "STEP",
            [".stp"] = "STEP",
            [".iges"] = "IGES",
            [".igs"] = "IGES",
            [".x_t"] = "Parasolid (text)",
            [".x_b"] = "Parasolid (binary)",
        };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            throw new McpToolException("inputPath must not be empty.");
        }
        if (!Path.IsPathRooted(InputPath))
        {
            throw new McpToolException($"inputPath must be absolute (got '{InputPath}').");
        }
        var ext = Path.GetExtension(InputPath);
        if (!AllowedInputExtensions.ContainsKey(ext))
        {
            var supported = string.Join(", ", AllowedInputExtensions.Keys);
            throw new McpToolException(
                $"inputPath must be a neutral CAD file ({supported}); got '{ext}'. " +
                "(STL meshes are not supported.)");
        }
        if (!File.Exists(InputPath))
        {
            throw new McpToolException($"inputPath does not exist: '{InputPath}'.");
        }
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            throw new McpToolException("outputPath must not be empty.");
        }
        if (!Path.IsPathRooted(OutputPath))
        {
            throw new McpToolException($"outputPath must be absolute (got '{OutputPath}').");
        }
        if (!OutputPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException($"outputPath must end in .sldprt (got '{OutputPath}').");
        }
    }
}
