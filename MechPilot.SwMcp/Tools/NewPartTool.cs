using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Interop;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Open a new blank SolidWorks part document. M29 — first tool in the
/// generic primitives layer. The opened part becomes SW's active doc;
/// subsequent generic-primitive calls (start_sketch / sketch_line /
/// extrude / etc.) operate on the active doc.
///
/// Pairs with <see cref="SavePartTool"/> to bracket a generic-layer
/// build session:
///   new_part → start_sketch → sketch_* → end_sketch → extrude → save_part
///
/// Existing parametric helpers (create_cylinder etc.) continue to bundle
/// new_part + sketch + extrude + save_part into a single call. The
/// generic layer simply unbundles them.
/// </summary>
[McpServerToolType]
public static class NewPartTool
{
    [McpServerTool(Name = "new_part")]
    [Description(
        "Open a new blank SolidWorks part document using the default part " +
        "template configured in SW (Tools → Options → Default Templates → " +
        "Part). The opened part becomes SW's active doc; subsequent generic " +
        "primitive calls (start_sketch / sketch_* / extrude / revolve / " +
        "loft / sweep) operate on this active doc. Save the result with " +
        "save_part once geometry is complete. " +
        "This is the entry point of the GENERIC PRIMITIVES LAYER — use it " +
        "to build arbitrary parts the parametric helpers (create_cylinder / " +
        "create_hemisphere / create_sphere / create_frustum / create_flange / " +
        "create_rectangular_block / create_lofted_round_to_square) don't " +
        "cover. For those standard shapes, the parametric helpers are still " +
        "the simpler choice (1 call vs. ~8).")]
    public static ToolResult Run()
    {
        var spec = new NewPartSpec();
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(NewPartSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return NewPartInSw();
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"new_part failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "new_part requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult NewPartInSw()
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Default part template ────────────────────────────────────────
        var template = swApp.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
        {
            throw new McpToolException(
                $"Default part template not found (resolved to '{template}'). " +
                "Open SW once and set Tools → Options → Default Templates → Part.");
        }

        // ── 2. New part document — becomes active ──────────────────────────
        var model = swApp.NewDocument(template, 0, 0.0, 0.0) as IModelDoc2
            ?? throw new McpToolException(
                $"swApp.NewDocument returned null for template '{template}'.");

        // ── 3. Verify the new doc is a part (and is active) ────────────────
        if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new McpToolException(
                $"NewDocument returned a non-part doc (type={model.GetType()}). " +
                $"Verify the default part template at '{template}' is actually a .prtdot.");
        }

        var title = model.GetTitle();
        return ToolResult.Ok(
            message: $"Opened new blank part (title='{title}'); now SW's active doc",
            path: null);
    }
#endif
}
