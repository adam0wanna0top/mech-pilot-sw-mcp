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
/// Creates a fresh empty assembly document — first tool of the assembly
/// family (M16 milestone: project from "single-part modeling" to "assembly
/// composition").
///
/// Sibling of CreateCylinderTool / CreateRectangularBlockTool in pipeline:
/// NewDocument → SaveAs → CloseDoc. Only difference is the template (asmdot
/// vs prtdot) located via <c>swDefaultTemplateAssembly</c> user preference.
///
/// v1 PR #9 lesson: user's SW default part template was once misset to
/// <c>.asmdot</c> — we explicitly request the **assembly** template here,
/// not "default", to avoid that trap.
/// </summary>
[McpServerToolType]
public static class NewAssemblyTool
{
    [McpServerTool(Name = "new_assembly")]
    [Description(
        "Create a fresh empty SolidWorks assembly (.sldasm) and save it to " +
        "disk. The assembly starts empty; add components with add_component. " +
        "savePath must be an absolute path ending in .sldasm; the parent " +
        "directory must already exist.")]
    public static ToolResult Run(
        [Description("Absolute output path with .sldasm extension, e.g. C:/tmp/asm.sldasm.")]
        string savePath)
    {
        var spec = new NewAssemblySpec { SavePath = savePath };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(NewAssemblySpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return NewAssemblyInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"new_assembly failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "new_assembly requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult NewAssemblyInSw(NewAssemblySpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Locate the default assembly template ─────────────────────────
        var template = swApp.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
        {
            throw new McpToolException(
                $"Default assembly template not found (resolved to '{template}'). " +
                "Open SW once and set Tools → Options → Default Templates → Assembly.");
        }

        // ── 2. New assembly document ────────────────────────────────────────
        var model = swApp.NewDocument(template, 0, 0.0, 0.0) as IModelDoc2
            ?? throw new McpToolException(
                $"swApp.NewDocument returned null for assembly template '{template}'.");

        // ── 3. Save as .sldasm ──────────────────────────────────────────────
        var ext = model.Extension;
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

        // ── 4. Close to free resources ──────────────────────────────────────
        swApp.CloseDoc(model.GetTitle());

        return ToolResult.Ok(
            message: "Created empty assembly (0 components)",
            path: spec.SavePath);
    }
#endif
}
