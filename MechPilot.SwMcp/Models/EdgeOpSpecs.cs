using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specs for the M52 precise edge operations (fillet_edges / chamfer_edges).
/// Edges are addressed by their <c>inspect_topology</c> index (SW body
/// enumeration order — re-inspect after any edit). Both specs follow the
/// modify_feature two-mode shape: PartPath null/empty = ACTIVE part (no
/// save); set = open the .sldprt, edit, save, close.
/// </summary>
internal static class EdgeOpValidation
{
    private const double MinSizeMm = 0.01;
    private const double MaxSizeMm = 1_000.0;

    public static void ValidateIndexes(IReadOnlyList<int>? indexes)
    {
        if (indexes is null || indexes.Count == 0)
        {
            throw new McpToolException(
                "edgeIndexes must not be empty. Call inspect_topology first and " +
                "pass the index values of the edges to operate on.");
        }
        var seen = new HashSet<int>();
        foreach (var i in indexes)
        {
            if (i < 0)
            {
                throw new McpToolException($"edge index {i} is negative — indexes start at 0.");
            }
            if (!seen.Add(i))
            {
                throw new McpToolException($"edge index {i} appears more than once.");
            }
        }
    }

    public static void ValidateSize(double valueMm, string name)
    {
        if (double.IsNaN(valueMm) || double.IsInfinity(valueMm) ||
            valueMm < MinSizeMm || valueMm > MaxSizeMm)
        {
            throw new McpToolException(
                $"{name} must be a finite number in [{MinSizeMm}, {MaxSizeMm}] mm (got {valueMm}).");
        }
    }
}

/// <summary>Round specific edges (by inspect_topology index) to a constant radius.</summary>
public sealed record FilletEdgesSpec
{
    /// <summary>Edge indexes from inspect_topology. Non-empty, distinct, ≥ 0.</summary>
    public required IReadOnlyList<int> EdgeIndexes { get; init; }

    /// <summary>Fillet radius in mm, [0.01, 1000].</summary>
    public required double RadiusMm { get; init; }

    /// <summary>Optional absolute .sldprt — FILE mode. Null/empty = active part.</summary>
    public string? PartPath { get; init; }

    /// <summary>Optional output .sldprt (FILE mode only). Null/empty = in place.</summary>
    public string? OutputPath { get; init; }

    public void Validate()
    {
        EdgeOpValidation.ValidateIndexes(EdgeIndexes);
        EdgeOpValidation.ValidateSize(RadiusMm, "radius");
        FeatureManageValidation.ValidatePaths(PartPath, OutputPath);
    }
}

/// <summary>Chamfer specific edges (by inspect_topology index) at equal distance (45°).</summary>
public sealed record ChamferEdgesSpec
{
    /// <summary>Edge indexes from inspect_topology. Non-empty, distinct, ≥ 0.</summary>
    public required IReadOnlyList<int> EdgeIndexes { get; init; }

    /// <summary>Equal chamfer distance in mm, [0.01, 1000].</summary>
    public required double DistanceMm { get; init; }

    /// <summary>Optional absolute .sldprt — FILE mode. Null/empty = active part.</summary>
    public string? PartPath { get; init; }

    /// <summary>Optional output .sldprt (FILE mode only). Null/empty = in place.</summary>
    public string? OutputPath { get; init; }

    public void Validate()
    {
        EdgeOpValidation.ValidateIndexes(EdgeIndexes);
        EdgeOpValidation.ValidateSize(DistanceMm, "distance");
        FeatureManageValidation.ValidatePaths(PartPath, OutputPath);
    }
}
