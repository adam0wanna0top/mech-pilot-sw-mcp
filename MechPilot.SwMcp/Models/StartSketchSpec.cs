using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for entering sketch mode on a named plane. M30 — second
/// step of the generic primitives layer (entry to sketch primitives).
///
/// The plane can be one of:
///   • "front" / "top" / "right" (case-insensitive) — SW's default
///     reference planes
///   • A face selector "+x" / "-x" / "+y" / "-y" / "+z" / "-z" (M37) —
///     sketch directly on the outermost planar body face whose outward
///     normal points that way (e.g. "+z" = the current top face). Lets the
///     LLM build onto an existing face without first creating a RefPlane at
///     that height. Requires an existing solid body.
///   • Any other string — interpreted as a literal SW plane name
///     (e.g. "基准面1" or "Plane1" for auto-created RefPlanes from
///     add_ref_plane, or a custom-named plane).
///
/// After start_sketch succeeds, SW enters sketch mode and subsequent
/// sketch_line / sketch_arc_* / sketch_circle / sketch_centerline /
/// sketch_rectangle_center calls add primitives to the active sketch.
/// Call end_sketch to exit sketch mode and obtain the sketch's name
/// (e.g. "草图1" / "Sketch1") for later feature primitives (extrude /
/// revolve / loft / sweep).
/// </summary>
public sealed record StartSketchSpec
{
    /// <summary>
    /// Plane name. Aliases "front" / "top" / "right" (case-insensitive)
    /// map to SW's CN/EN default reference plane names; any other value
    /// is used as a literal SW plane name.
    /// </summary>
    public required string Plane { get; init; }

    /// <summary>Recognized standard plane aliases (case-insensitive).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> StandardPlaneAliases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["front"] = new[] { "前视基准面", "Front Plane" },
            ["top"] = new[] { "上视基准面", "Top Plane" },
            ["right"] = new[] { "右视基准面", "Right Plane" },
        };

    /// <summary>Throws <see cref="McpToolException"/> if Plane is empty.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Plane))
        {
            throw new McpToolException(
                "plane must not be empty. Use 'front' / 'top' / 'right' for SW's " +
                "default reference planes, or a literal plane name like 'Plane1' " +
                "for a RefPlane created with add_ref_plane.");
        }
    }
}

/// <summary>
/// Specification for exiting sketch mode. M30 — companion to
/// <see cref="StartSketchSpec"/>. No parameters — end_sketch toggles SW
/// out of sketch mode and returns the just-completed sketch's name
/// (e.g. "草图1" / "Sketch1") in the result message.
/// </summary>
public sealed record EndSketchSpec
{
    /// <summary>No-op — end_sketch has no parameters to validate.</summary>
    public void Validate()
    {
        _ = this;
    }
}
