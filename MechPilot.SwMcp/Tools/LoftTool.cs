using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Loft (blend) over 2+ named sketches into a solid body. M32 generic-layer
/// equivalent of M28's <c>create_lofted_round_to_square</c> — accepts any
/// 2+ sketches the LLM has built.
///
/// Wraps SW's <c>InsertProtrusionBlend</c> (17 args, reflected in M28).
/// Selection: all profile sketches selected with mark=1 in order (v1 PR #27
/// + M28 confirmed).
///
/// Pipeline:
///   1. RequireActiveDoc.
///   2. ClearSelection2.
///   3. For each sketch name: SelectByID2(name, "SKETCH", mark=1,
///      append=(i > 0)).
///   4. InsertProtrusionBlend(17 args educated defaults — same as M28).
/// </summary>
[McpServerToolType]
public static class LoftTool
{
    [McpServerTool(Name = "loft")]
    [Description(
        "Loft (blend) over 2+ named sketches into a solid body on the active " +
        "part. sketchNames is the ordered list of sketch names (each from " +
        "end_sketch). The sketches should sit on different planes (use " +
        "add_ref_plane to create offset planes). closed=true treats the " +
        "list as a closed loop. Use this for round-to-square transitions, " +
        "tapers, blends between arbitrary profile shapes — anything beyond " +
        "what create_lofted_round_to_square covers.")]
    public static ToolResult Run(
        [Description("Ordered list of sketch names to loft between (2+ items).")]
        string[] sketchNames,
        [Description("If true, treat as a closed loop. Default false (open loft).")]
        bool closed = false)
    {
        return RunWithSpec(new LoftSpec
        {
            SketchNames = sketchNames,
            Closed = closed,
        });
    }

    public static ToolResult RunWithSpec(LoftSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"loft failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("loft requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(LoftSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var ext = model.Extension;
        var fm = model.FeatureManager;

        // ── 1. Select all profile sketches in order, mark=1 ─────────────────
        model.ClearSelection2(true);
        for (int i = 0; i < spec.SketchNames.Count; i++)
        {
            var name = spec.SketchNames[i];
            if (!ext.SelectByID2(
                Name: name, Type: "SKETCH",
                X: 0.0, Y: 0.0, Z: 0.0,
                Append: i > 0, Mark: 1,
                Callout: null, SelectOption: 0))
            {
                throw new McpToolException(
                    $"Cannot select sketch '{name}' (index {i}). " +
                    "Verify the name returned by end_sketch.");
            }
        }

        // ── 2. InsertProtrusionBlend — same 17 args educated defaults as M28 ──
        var feature = fm.InsertProtrusionBlend(
            Closed: spec.Closed,
            KeepTangency: false,
            ForceNonRational: false,
            TessToleranceFactor: 0.0,
            StartMatchingType: 0,
            EndMatchingType: 0,
            StartTangentLength: 1.0,
            EndTangentLength: 1.0,
            StartTangentDir: false,
            EndTangentDir: false,
            IsThinBody: false,
            Thickness1: 0.0,
            Thickness2: 0.0,
            ThinType: 0,
            Merge: true,
            UseFeatScope: true,
            UseAutoSelect: true);

        if (feature == null)
        {
            throw new McpToolException(
                $"InsertProtrusionBlend returned null for {spec.SketchNames.Count} sketches " +
                $"[{string.Join(", ", spec.SketchNames)}]. Common causes: one of the " +
                "sketches is open / self-intersecting / zero-area, or sketches were " +
                "selected in the wrong order (loft order matters).");
        }

        var featureName = feature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Lofted {spec.SketchNames.Count} sketches " +
                     $"[{string.Join(", ", spec.SketchNames)}] → feature '{featureName}'" +
                     (spec.Closed ? " (closed loop)" : ""),
            path: null);
    }
#endif
}
