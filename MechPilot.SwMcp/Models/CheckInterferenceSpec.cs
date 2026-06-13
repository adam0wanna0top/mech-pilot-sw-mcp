using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for running SolidWorks interference detection on an assembly
/// (M55) — the tool-ified version of the manual pairwise envelope audit. Born
/// from the fan dogfooding pain: confirming "no parts clash" meant computing
/// every component's world box by hand and checking each pair. SW already has
/// a real solid-intersection check; this surfaces it.
///
/// LLM use case: after assembling, "does anything interfere?" →
/// check_interference(asm) → a list of clashing component pairs + overlap
/// volume, or "no interference". Read-only — the assembly is not modified.
/// </summary>
public sealed record CheckInterferenceSpec
{
    /// <summary>Absolute path to an existing .sldasm to check. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// When true, two faces merely touching (coincident, zero-volume) count as
    /// an interference. Default false so intentional contacts (a part resting
    /// on another, a shaft seated in a bore) are NOT flagged — only real
    /// solid overlap is.
    /// </summary>
    public bool TreatCoincidentAsInterference { get; init; }

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AssemblyPath))
        {
            throw new McpToolException("assemblyPath must not be empty.");
        }
        if (!Path.IsPathRooted(AssemblyPath))
        {
            throw new McpToolException($"assemblyPath must be absolute (got '{AssemblyPath}').");
        }
        if (!AssemblyPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"assemblyPath must end in .sldasm (got '{AssemblyPath}'). " +
                "For parts use inspect_part / inspect_topology.");
        }
        if (!File.Exists(AssemblyPath))
        {
            throw new McpToolException(
                $"assemblyPath does not exist: '{AssemblyPath}'. " +
                "Create the assembly first with new_assembly + add_component.");
        }
    }
}
