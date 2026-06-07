using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Spec for editing an existing mate's value (distance in mm, angle in degrees)
/// in an assembly and rebuilding — the mate counterpart of
/// <see cref="ModifyFeatureSpec"/> (M42). The write primitive assembly resize
/// needs: scale a distance mate as the parts grow.
///
/// LLM workflow:
///   inspect_assembly (read mate names + types) →
///   modify_mate("距离1", 40) → inspect_assembly (see the new value)
/// </summary>
public sealed record ModifyMateSpec
{
    /// <summary>Absolute path to an existing .sldasm to edit.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>Exact mate name from inspect_assembly's mates list (e.g. "距离1").</summary>
    public required string MateName { get; init; }

    /// <summary>New value — mm for a distance mate, degrees for an angle mate. Finite &gt; 0.</summary>
    public required double Value { get; init; }

    /// <summary>Optional output .sldasm path. Empty/null = overwrite the input in place.</summary>
    public string? OutputPath { get; init; }

    private const double MaxValue = 100_000.0;

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
            throw new McpToolException($"assemblyPath must end in .sldasm (got '{AssemblyPath}').");
        }
        if (!File.Exists(AssemblyPath))
        {
            throw new McpToolException($"assemblyPath does not exist: '{AssemblyPath}'.");
        }
        if (string.IsNullOrWhiteSpace(MateName))
        {
            throw new McpToolException(
                "mateName must not be empty. Pass a mate name from inspect_assembly's " +
                "mates list (e.g. '距离1').");
        }
        if (double.IsNaN(Value) || double.IsInfinity(Value) || Value <= 0)
        {
            throw new McpToolException(
                $"value must be a finite number > 0 (got {Value}). It is the new distance " +
                "(mm) or angle (degrees) depending on the mate type.");
        }
        if (Value > MaxValue)
        {
            throw new McpToolException($"value {Value} is implausibly large (> {MaxValue}).");
        }
        if (!string.IsNullOrWhiteSpace(OutputPath) &&
            !OutputPath!.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException($"outputPath must end in .sldasm (got '{OutputPath}').");
        }
    }
}
