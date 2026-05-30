using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for reading an existing part's metadata (bounding box,
/// feature list, face/edge counts). Pure read-only: the underlying tool
/// opens the .sldprt with the ReadOnly flag and never writes back, so
/// there's no OutputPath — only an InputPath to validate.
/// </summary>
public sealed record InspectSpec
{
    /// <summary>Absolute path to an existing .sldprt to read. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>Throws <see cref="McpToolException"/> if the input path is invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            throw new McpToolException("inputPath must not be empty.");
        }
        if (!Path.IsPathRooted(InputPath))
        {
            throw new McpToolException(
                $"inputPath must be absolute (got '{InputPath}').");
        }
        if (!InputPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"inputPath must end in .sldprt (got '{InputPath}').");
        }
        if (!File.Exists(InputPath))
        {
            throw new McpToolException(
                $"inputPath does not exist: '{InputPath}'. " +
                "Create the part first (e.g. with create_cylinder / create_flange).");
        }
    }
}
