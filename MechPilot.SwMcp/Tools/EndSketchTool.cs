using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Exit sketch mode on the active part and return the just-completed
/// sketch's auto-assigned name (e.g. "草图1" / "Sketch1") in the result
/// message. M30 — companion to <see cref="StartSketchTool"/>.
///
/// The returned name is what the LLM should pass to feature primitives
/// (extrude / revolve / loft / sweep) to reference this sketch.
/// </summary>
[McpServerToolType]
public static class EndSketchTool
{
    [McpServerTool(Name = "end_sketch")]
    [Description(
        "Exit sketch mode on the active part. Requires an active sketch " +
        "(call start_sketch first). The result message includes the sketch's " +
        "auto-assigned SW name (e.g. '草图1' or 'Sketch1') — pass this name " +
        "to extrude / revolve / loft / sweep to reference the sketch in a " +
        "feature primitive. After end_sketch, no sketch is active until the " +
        "next start_sketch call.")]
    public static ToolResult Run()
    {
        return RunWithSpec(new EndSketchSpec());
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(EndSketchSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return EndSketchInSw();
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"end_sketch failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "end_sketch requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult EndSketchInSw()
    {
        // Ensure there is something to exit — guard before toggling.
        _ = Internal.SketchSession.RequireActiveSketch();

        var model = Internal.SketchSession.RequireActiveDoc();
        model.SketchManager.InsertSketch(true);   // toggle off

        // The just-saved sketch is the most recent user feature on the part
        // (boot filter strips RefPlanes etc.). PartGeometryHelpers already
        // implements this walk — reuse it here so end_sketch matches what
        // inspect_part will report.
        var sketchFeature = Internal.PartGeometryHelpers.FindLastUserFeature(model)
            ?? throw new McpToolException(
                "end_sketch toggled out of sketch mode but no user feature was " +
                "found on the part. The sketch may have been empty and SW " +
                "discarded it silently.");

        var sketchName = sketchFeature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Exited sketch mode; sketch name='{sketchName}' (pass to extrude / revolve / loft / sweep)",
            path: null);
    }
#endif
}
