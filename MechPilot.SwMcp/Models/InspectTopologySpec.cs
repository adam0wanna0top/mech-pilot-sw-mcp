using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Spec for the M51 deep-inspection tool: per-face / per-edge topology of a
/// part (type, normal/axis, center, area, radius, length) — the "addresses"
/// future precise solid operations (fillet THIS edge / cut THIS face) need.
/// PartPath null/empty = the ACTIVE part; set = open that .sldprt read-only.
/// </summary>
public sealed record InspectTopologySpec
{
    /// <summary>Optional absolute .sldprt. Null/empty = inspect the ACTIVE part.</summary>
    public string? PartPath { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PartPath))
        {
            return;
        }
        if (!Path.IsPathRooted(PartPath))
        {
            throw new McpToolException($"partPath must be absolute (got '{PartPath}').");
        }
        if (!PartPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException($"partPath must end in .sldprt (got '{PartPath}').");
        }
        if (!File.Exists(PartPath))
        {
            throw new McpToolException($"partPath does not exist: '{PartPath}'.");
        }
    }
}
