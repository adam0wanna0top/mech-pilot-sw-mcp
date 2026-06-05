#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Interop;
using SolidWorks.Interop.sldworks;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Shared session-state helpers for the M30 generic sketch primitives.
/// Centralizes "is there an active part / active sketch?" checks so each
/// of the 8 sketch tools (start_sketch / end_sketch / sketch_line /
/// sketch_arc_3point / sketch_arc_center / sketch_circle / sketch_centerline /
/// sketch_rectangle_center) doesn't need to repeat the same null-guards.
///
/// All methods throw <see cref="McpToolException"/> with descriptive
/// messages on missing state — the LLM sees a clean error pointing at
/// the missing prerequisite (e.g. "call new_part first" / "call
/// start_sketch first").
/// </summary>
internal static class SketchSession
{
    /// <summary>
    /// Returns SW's currently active document as an <see cref="IModelDoc2"/>.
    /// Throws if no doc is active (typically: forgot to call new_part).
    /// </summary>
    public static IModelDoc2 RequireActiveDoc()
    {
        var swApp = SwConnection.Instance.GetApp();
        return swApp.ActiveDoc as IModelDoc2
            ?? throw new McpToolException(
                "No active SolidWorks document. Call new_part first to open a " +
                "blank part before invoking sketch primitives.");
    }

    /// <summary>
    /// Returns the active sketch on the active document. Throws if no doc
    /// is active OR no sketch is currently being edited (typically: forgot
    /// to call start_sketch, or already called end_sketch).
    /// </summary>
    public static ISketch RequireActiveSketch()
    {
        var model = RequireActiveDoc();
        var skMgr = model.SketchManager;
        return skMgr.ActiveSketch as ISketch
            ?? throw new McpToolException(
                "No active sketch. Call start_sketch first to enter sketch mode " +
                "on a named plane before invoking sketch primitives.");
    }

    /// <summary>
    /// Returns the active document's <see cref="ISketchManager"/>. Throws if
    /// no doc is active.
    /// </summary>
    public static ISketchManager RequireSketchManager()
    {
        var model = RequireActiveDoc();
        return model.SketchManager;
    }
}
#endif
