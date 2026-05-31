using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for reading an existing assembly's component list (read-only).
/// Sibling of <see cref="InspectSpec"/> — same open-with-ReadOnly + close-
/// without-save pipeline, but reads a <c>.sldasm</c> and walks its component
/// tree instead of a <c>.sldprt</c>'s feature tree.
///
/// LLM use case: before calling add_mate (future tool) the LLM doesn't know
/// what component instance names are in the assembly. inspect_assembly
/// returns them ("asm_cyl_123-1", "asm_block_456-1") plus each component's
/// world position, so the LLM can wire up a mate spec confidently.
/// </summary>
public sealed record InspectAssemblySpec
{
    /// <summary>Absolute path to an existing .sldasm to read. Must exist.</summary>
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
        if (!InputPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"inputPath must end in .sldasm (got '{InputPath}'). " +
                "For parts (.sldprt) use inspect_part instead.");
        }
        if (!File.Exists(InputPath))
        {
            throw new McpToolException(
                $"inputPath does not exist: '{InputPath}'. " +
                "Create the assembly first with new_assembly + add_component.");
        }
    }
}
