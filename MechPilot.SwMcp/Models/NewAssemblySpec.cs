using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for creating a fresh empty assembly document.
/// Sibling of <see cref="CylinderSpec"/>/<see cref="RectangularBlockSpec"/>
/// in spirit (create-from-scratch + save), but produces a <c>.sldasm</c>
/// instead of a <c>.sldprt</c>.
///
/// LLM use case: "新建一个装配体" — one tool call. Then add zero or more
/// components with add_component to populate it.
/// </summary>
public sealed record NewAssemblySpec
{
    /// <summary>
    /// Absolute output path with <c>.sldasm</c> extension. Parent directory
    /// must exist.
    /// </summary>
    public required string SavePath { get; init; }

    /// <summary>Throws <see cref="McpToolException"/> if the save path is invalid.</summary>
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
                "Hint: pass something like 'C:/tmp/asm.sldasm'.");
        }
        if (!SavePath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"savePath must end in .sldasm (got '{SavePath}').");
        }
        var dir = Path.GetDirectoryName(SavePath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new McpToolException($"savePath has no parent directory: '{SavePath}'.");
        }
        if (!Directory.Exists(dir))
        {
            throw new McpToolException(
                $"savePath parent directory does not exist: '{dir}'.");
        }
    }
}
