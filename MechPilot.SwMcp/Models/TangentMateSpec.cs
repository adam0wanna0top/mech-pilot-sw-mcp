using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a tangent mate between two components (M56) — a curved
/// face (cylinder / sphere / cone) touching a plane or another curved face.
/// The mate the other four couldn't express: a cylinder resting on a flat, or
/// two cylinders touching along a line. Born from the fan dogfooding — the
/// motor housing (a horizontal cylinder) sits on the pole top (a flat),
/// a perpendicular junction that coincident / concentric can't constrain.
///
/// Unlike coincident / distance, there is no reference-plane shorthand and no
/// auto-pick: tangent is inherently about two SPECIFIC faces, so both are
/// addressed by their inspect_topology face index. At least one face must be
/// curved (a tangent of two planes is meaningless — use coincident).
///
/// LLM use case: "rest the motor housing on top of the pole" → tangent(asm,
/// housing, pole, face1Index=&lt;housing cylinder&gt;, face2Index=&lt;pole top plane&gt;).
/// </summary>
public sealed record TangentMateSpec
{
    /// <summary>Absolute path to an existing .sldasm to mate within. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>First component's instance name (from inspect_assembly).</summary>
    public required string Component1Name { get; init; }

    /// <summary>Second component's instance name.</summary>
    public required string Component2Name { get; init; }

    /// <summary>inspect_topology face index on component1's part. Required (≥ 0).</summary>
    public required int Face1Index { get; init; }

    /// <summary>inspect_topology face index on component2's part. Required (≥ 0).</summary>
    public required int Face2Index { get; init; }

    /// <summary>
    /// Mate alignment: "closest" (default — let SW pick which side touches),
    /// "aligned", or "anti-aligned" (case-insensitive).
    /// </summary>
    public string Alignment { get; init; } = "closest";

    /// <summary>
    /// Optional absolute .sldasm output path. Null / empty = overwrite the
    /// input assembly in place (the common case).
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateAssemblyPath(AssemblyPath);
        ValidateComponentName(Component1Name, "component1Name");
        ValidateComponentName(Component2Name, "component2Name");
        ValidateFaceIndex(Face1Index, "face1Index");
        ValidateFaceIndex(Face2Index, "face2Index");
        ValidateAlignment(Alignment);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
        if (string.Equals(Component1Name, Component2Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"component1Name and component2Name must differ (both got '{Component1Name}').");
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
                $"{field} must not be empty. Use inspect_assembly to learn component instance names.");
        }
    }

    private static void ValidateFaceIndex(int index, string field)
    {
        if (index < 0)
        {
            throw new McpToolException(
                $"{field} must be ≥ 0 (got {index}). It is the inspect_topology face " +
                "index of the component's part — tangent needs a specific face on each side.");
        }
    }

    private static void ValidateAlignment(string alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
        {
            throw new McpToolException(
                "alignment must not be empty. Use 'closest', 'aligned', or 'anti-aligned'.");
        }
        if (!CoincidentMateSpec.AlignmentKeywords.Contains(alignment))
        {
            throw new McpToolException(
                $"alignment '{alignment}' is not recognized. Supported: " +
                $"{string.Join(", ", CoincidentMateSpec.AlignmentKeywords)}.");
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
