using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding one concentric mate between two components'
/// **first cylindrical faces** (axis ±Z, auto-picked). Third member of the
/// mate family — sibling of <see cref="CoincidentMateSpec"/> and
/// <see cref="DistanceMateSpec"/> but selects cylindrical faces (e.g. the
/// outer surface of a pin or the inner surface of a hole) rather than
/// reference planes.
///
/// LLM use case: "make the pin concentric with the hole" — one tool call.
/// The tool internally walks each component's body, finds the first face
/// whose surface is cylindrical with its axis aligned with ±Z (the
/// extrusion direction of all our create_* tools), selects both, then
/// AddMate5(swMateCONCENTRIC). LLM only supplies the two component
/// instance names — no SW-internal face IDs needed.
///
/// **Auto-pick scope**: works for parts created by create_cylinder /
/// create_flange (have a clear axial cylindrical face) and parts edited
/// by add_axial_hole / add_threaded_hole etc. (the hole's inner surface
/// is also a Z-axial cylindrical face). For parts with multiple cylindrical
/// faces in the same direction, the **first one found wins**.
///
/// **Topology addressing (M53-③)**: to target a SPECIFIC cylindrical face —
/// e.g. "the flange's 3rd bolt hole" rather than whichever cylinder is found
/// first — supply <see cref="Face1Index"/> / <see cref="Face2Index"/>, the
/// face index from running inspect_topology on that component's underlying
/// .sldprt. The part's face order equals the component's in-assembly face
/// order (single-body parts), so the index bridges directly. Omit an index to
/// keep the auto-pick for that side; the two sides are independent.
/// </summary>
public sealed record ConcentricMateSpec
{
    /// <summary>Absolute path to an existing .sldasm to mate within. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>First component's instance name (e.g. "cyl-1"). Get from inspect_assembly.</summary>
    public required string Component1Name { get; init; }

    /// <summary>Second component's instance name.</summary>
    public required string Component2Name { get; init; }

    /// <summary>
    /// Optional inspect_topology face index on component1's part to mate that
    /// EXACT cylindrical face (M53-③). Null = auto-pick the first axial-Z
    /// cylinder. Must be ≥ 0 and point at a cylindrical face.
    /// </summary>
    public int? Face1Index { get; init; }

    /// <summary>Optional inspect_topology face index on component2's part (see
    /// <see cref="Face1Index"/>).</summary>
    public int? Face2Index { get; init; }

    /// <summary>
    /// Mate alignment: "aligned" (default) / "anti-aligned" / "closest".
    /// For concentric mates, alignment picks whether the cylinders share
    /// the same +Z direction (aligned) or face opposite (anti-aligned).
    /// </summary>
    public string Alignment { get; init; } = "aligned";

    /// <summary>
    /// Optional absolute .sldasm output path. When null or empty the input
    /// assembly is overwritten in place (the common case).
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
                $"{field} must not be empty. Use inspect_assembly to learn component instance names.");
        }
    }

    private static void ValidateFaceIndex(int? index, string field)
    {
        if (index is < 0)
        {
            throw new McpToolException(
                $"{field} must be ≥ 0 (got {index}). It is the inspect_topology " +
                "face index of the component's part; omit it to auto-pick the " +
                "first axial-Z cylindrical face.");
        }
    }

    private static void ValidateAlignment(string alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
        {
            throw new McpToolException(
                "alignment must not be empty. Use 'aligned', 'anti-aligned', or 'closest'.");
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
