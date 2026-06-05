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
/// Save the currently active SolidWorks part document to disk and close it.
/// M29 — companion to <see cref="NewPartTool"/>. Bracket-closes a generic
/// primitives layer build session:
///   new_part → start_sketch → sketch_* → end_sketch → extrude → save_part
///
/// Uses <c>Extension.SaveAs</c> (not <c>Save3</c>) since the part is brand
/// new and has no existing on-disk path. <c>CloseDoc</c> after to free
/// resources (matches the create_* pattern).
/// </summary>
[McpServerToolType]
public static class SavePartTool
{
    [McpServerTool(Name = "save_part")]
    [Description(
        "Save the currently active SolidWorks part (the one most recently " +
        "opened with new_part) to disk as a .sldprt file, then close it. " +
        "Use this to bracket-close a generic primitives layer build session " +
        "(new_part → sketches → features → save_part). savePath must be an " +
        "absolute path ending in .sldprt; the parent directory must already " +
        "exist. After save_part, no part is active — call new_part again to " +
        "start a new build.")]
    public static ToolResult Run(
        [Description("Absolute output path with .sldprt extension, e.g. C:/tmp/part.sldprt.")]
        string savePath)
    {
        var spec = new SavePartSpec { SavePath = savePath };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(SavePartSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return SaveActivePartInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"save_part failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "save_part requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult SaveActivePartInSw(SavePartSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Grab the active doc ──────────────────────────────────────────
        var model = swApp.ActiveDoc as IModelDoc2
            ?? throw new McpToolException(
                "No active SolidWorks document to save. Call new_part first to " +
                "open a blank part.");

        // ── 2. Verify it's actually a part (not assembly / drawing) ────────
        if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new McpToolException(
                $"Active doc is not a part (type={model.GetType()}); save_part " +
                "only saves parts. Use a future save_assembly / save_drawing tool " +
                "for other types.");
        }

        var ext = model.Extension;

        // ── 3. SaveAs (Extension API, not IModelDoc2 / Save3 — the part is
        //   brand new with no existing on-disk path) ──────────────────────
        int saveErrors = 0;
        int saveWarnings = 0;
        var savedOk = ext.SaveAs(
            Name: spec.SavePath,
            Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
            Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            ExportData: null,
            Errors: ref saveErrors,
            Warnings: ref saveWarnings);

        if (!savedOk || !File.Exists(spec.SavePath))
        {
            throw new McpToolException(
                $"SaveAs failed for '{spec.SavePath}'. errors=0x{saveErrors:X} " +
                $"warnings=0x{saveWarnings:X}.");
        }

        // ── 4. Close the doc to free SW resources ──────────────────────────
        var title = model.GetTitle();
        swApp.CloseDoc(title);

        return ToolResult.Ok(
            message: $"Saved active part as '{spec.SavePath}' and closed it",
            path: spec.SavePath);
    }
#endif
}
