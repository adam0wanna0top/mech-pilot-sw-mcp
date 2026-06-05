using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for opening a new blank SolidWorks part document. M29 —
/// first tool in the project's generic primitives layer (vs. the existing
/// 7 parametric helpers like create_cylinder / create_hemisphere etc.).
///
/// The generic layer lets the LLM build arbitrary geometry by composing
/// sketch primitives + feature primitives, rather than picking from a
/// predefined catalog of shapes. Both layers coexist (Plan B from the
/// design review): parametric helpers stay for simple cases (1 call),
/// generic primitives unlock arbitrary cases (multiple calls).
///
/// LLM workflow:
///   1. new_part()                      ← M29, this tool
///   2. start_sketch("front")           ← M30
///   3. sketch_circle / sketch_line / ... ← M30
///   4. end_sketch()                    ← M30
///   5. extrude / revolve / loft / sweep ← M31 / M32
///   6. save_part("path.sldprt")        ← M29, SavePartSpec
///
/// This spec has no required fields — new_part takes no parameters and
/// uses the SW default part template (configured in SW UI → Tools → Options
/// → Default Templates → Part).
/// </summary>
public sealed record NewPartSpec
{
    // No fields — new_part takes no parameters. Reserved for future:
    // optional template path override (currently uses SW default).

    /// <summary>No-op — new_part has no parameters to validate.</summary>
    public void Validate()
    {
        // Reserved.
        _ = this;
    }
}

/// <summary>
/// Specification for saving the currently active SolidWorks part document.
/// M29 — companion to <see cref="NewPartSpec"/>: save the active part to
/// disk and close it.
///
/// LLM workflow continues from new_part:
///   ... (sketch + feature primitive calls) ...
///   save_part("C:/tmp/my_part.sldprt")     ← this tool
///
/// Validation mirrors CylinderSpec.SavePath: absolute path, .sldprt
/// extension, parent directory must exist.
/// </summary>
public sealed record SavePartSpec
{
    /// <summary>
    /// Absolute output path with .sldprt extension. Parent directory must exist.
    /// </summary>
    public required string SavePath { get; init; }

    /// <summary>Throws <see cref="McpToolException"/> if SavePath is invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SavePath))
        {
            throw new McpToolException("savePath must not be empty.");
        }
        if (!Path.IsPathRooted(SavePath))
        {
            throw new McpToolException(
                $"savePath must be absolute (got '{SavePath}'). " +
                "Hint: pass something like 'C:/tmp/part.sldprt'.");
        }
        if (!SavePath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"savePath must end in .sldprt (got '{SavePath}').");
        }

        var dir = Path.GetDirectoryName(SavePath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new McpToolException($"savePath has no parent directory: '{SavePath}'.");
        }
        if (!Directory.Exists(dir))
        {
            throw new McpToolException(
                $"savePath parent directory does not exist: '{dir}'. " +
                "Create it first or pick an existing folder.");
        }
    }
}
