namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Pure (SolidWorks-free) mapping of a swMateType_e int to a short lowercase
/// name for the inspect_assembly mates list, plus which types carry a single
/// editable value (distance / angle). Kept SW-free so it is L1-testable.
/// (Values reflected from swconst on SW 2026.)
/// </summary>
internal static class MateType
{
    public static string Name(int swMateType) => swMateType switch
    {
        0 => "coincident",
        1 => "concentric",
        2 => "perpendicular",
        3 => "parallel",
        4 => "tangent",
        5 => "distance",
        6 => "angle",
        8 => "symmetric",
        9 => "cam",
        10 => "gear",
        11 => "width",
        16 => "lock",
        21 => "slot",
        _ => $"type{swMateType}",
    };

    /// <summary>
    /// True for mate types whose value is a single editable number reachable via
    /// the mate's display dimension: distance (5, mm) and angle (6, deg).
    /// </summary>
    public static bool HasValue(int swMateType) => swMateType is 5 or 6;

    /// <summary>True for an angle mate (swMateANGLE = 6): its value is degrees, not mm.</summary>
    public static bool IsAngle(int swMateType) => swMateType == 6;
}
