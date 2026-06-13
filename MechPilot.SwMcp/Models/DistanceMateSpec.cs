using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding one distance mate between two components'
/// default reference planes (Front / Top / Right) at a given mm distance.
/// Sibling of <see cref="CoincidentMateSpec"/> — same selection plumbing,
/// same AddMate5 path, just <c>swMateType_e.swMateDISTANCE</c> instead of
/// <c>swMateCOINCIDENT</c> plus a meaningful Distance argument.
///
/// LLM use case: "the cylinder sits 25 mm above the base block" →
///   add_mate_distance(asm, cyl-1, top, block-1, top, distance=25, alignment=aligned)
/// distance mate is the v1 PR #20 main path — AddMate5 with the 4 magic
/// positions (gear ratio 0.001, angle limits π/6) non-zero is what unlocked
/// distance mates in v1.
/// </summary>
public sealed record DistanceMateSpec
{
    /// <summary>Absolute path to an existing .sldasm to mate within. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>First component's instance name (e.g. "asm_cyl_123-1"). Get from inspect_assembly.</summary>
    public required string Component1Name { get; init; }

    /// <summary>
    /// Reference plane of component 1: "front" / "top" / "right"
    /// (case-insensitive). Required unless <see cref="Face1Index"/> is set,
    /// which mates a specific planar model face instead.
    /// </summary>
    public string? Plane1 { get; init; }

    /// <summary>Second component's instance name.</summary>
    public required string Component2Name { get; init; }

    /// <summary>Reference plane of component 2 (or omit and use <see cref="Face2Index"/>).</summary>
    public string? Plane2 { get; init; }

    /// <summary>
    /// Optional inspect_topology planar-face index on component1's part to mate
    /// that EXACT face (M54) instead of a reference plane. ≥ 0; when set,
    /// <see cref="Plane1"/> is ignored.
    /// </summary>
    public int? Face1Index { get; init; }

    /// <summary>Optional inspect_topology planar-face index on component2's part.</summary>
    public int? Face2Index { get; init; }

    /// <summary>Mate distance in mm. Must be &gt; 0.</summary>
    public required double DistanceMm { get; init; }

    /// <summary>
    /// Mate alignment: "aligned" (default) / "anti-aligned" / "closest" (case-insensitive).
    /// For distance mates, alignment picks which side of plane1 plane2 sits on.
    /// </summary>
    public string Alignment { get; init; } = "aligned";

    /// <summary>
    /// Optional absolute .sldasm output path. When null or empty the input
    /// assembly is overwritten in place (the common case).
    /// </summary>
    public string? OutputPath { get; init; }

    private const double MinDistanceMm = 0.01;
    private const double MaxDistanceMm = 100_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateAssemblyPath(AssemblyPath);
        ValidateComponentName(Component1Name, "component1Name");
        CoincidentMateSpec.ValidateSide(Plane1, Face1Index, "plane1", "face1Index");
        ValidateComponentName(Component2Name, "component2Name");
        CoincidentMateSpec.ValidateSide(Plane2, Face2Index, "plane2", "face2Index");
        ValidateDistance(DistanceMm);
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

    private static void ValidateDistance(double distance)
    {
        if (double.IsNaN(distance) || double.IsInfinity(distance) || distance <= 0)
        {
            throw new McpToolException(
                $"distance must be > 0 mm (got {distance}). " +
                "Hint: pass millimeters, e.g. 25 for a 25 mm offset.");
        }
        if (distance < MinDistanceMm || distance > MaxDistanceMm)
        {
            throw new McpToolException(
                $"distance {distance} mm is outside the supported range " +
                $"[{MinDistanceMm}, {MaxDistanceMm}] mm.");
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
