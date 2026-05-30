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
/// Exports an existing part to a neutral CAD format (STEP / STL / IGES /
/// Parasolid). The output extension drives SW's internal format dispatch in
/// <c>IModelDocExtension.SaveAs</c>; no special enum / ExportData object is
/// needed because these are SW-builtin exporters.
///
/// Pipeline:
///   1. OpenDoc6 the input .sldprt (silent).
///   2. Extension.SaveAs to the neutral output path (SW reads the extension
///      and routes to STEP / STL / IGES / Parasolid exporter).
///   3. CloseDoc (in finally).
///
/// Why this is M5-bug-safe: the output extension is always different from
/// <c>.sldprt</c>, so the path can never equal the active doc's own path,
/// and the long-lived-SW "SaveAs(samepath)" failure (M5) is structurally
/// impossible here. The SaveAs branch is the only save path.
/// </summary>
[McpServerToolType]
public static class ExportPartTool
{
    [McpServerTool(Name = "export_part")]
    [Description(
        "Export an existing SolidWorks part (.sldprt) to a neutral CAD format. " +
        "The output extension picks the format: .step / .stp (STEP AP214), " +
        ".stl (STL mesh for 3D printing), .iges / .igs (IGES), .x_t / .x_b " +
        "(Parasolid text / binary). inputPath must be an absolute path to an " +
        "existing .sldprt. outputPath must be absolute, end in one of the " +
        "supported extensions, and differ from inputPath (refuses to " +
        "overwrite the .sldprt source).")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to export, e.g. C:/tmp/part.sldprt.")]
        string inputPath,
        [Description("Absolute output path; extension picks format. e.g. C:/tmp/part.step.")]
        string outputPath)
    {
        var spec = new ExportSpec
        {
            InputPath = inputPath,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(ExportSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return ExportInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"export_part failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "export_part requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult ExportInSw(ExportSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Open the existing part ───────────────────────────────────────
        int openErrors = 0;
        int openWarnings = 0;
        var model = swApp.OpenDoc6(
            FileName: spec.InputPath,
            Type: (int)swDocumentTypes_e.swDocPART,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{spec.InputPath}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}. " +
                "(See swFileLoadError_e in swconst.chm.)");
        }

        try
        {
            var ext = model.Extension;

            // ── 2. Export — SaveAs dispatches by file extension ─────────────
            //   ExportData = null is correct for STEP / STL / IGES / Parasolid;
            //   these exporters take their per-format options from SW System
            //   Options (UI) rather than per-call.
            //
            //   This is the same Extension.SaveAs we use in fillet/chamfer copy
            //   mode (M5 split), but here outputPath ≠ inputPath by spec
            //   validation, so the M5 SaveAs(samepath) trap is structurally
            //   impossible — no need for the Save3 branch.
            int saveErrors = 0;
            int saveWarnings = 0;
            var savedOk = ext.SaveAs(
                Name: spec.OutputPath,
                Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                ExportData: null,
                Errors: ref saveErrors,
                Warnings: ref saveWarnings);

            if (!savedOk || !File.Exists(spec.OutputPath))
            {
                throw new McpToolException(
                    $"SaveAs failed for '{spec.OutputPath}'. errors=0x{saveErrors:X} " +
                    $"warnings=0x{saveWarnings:X}. " +
                    "(See swFileSaveError_e in swconst.chm.)");
            }

            var formatLabel = ExportSpec.AllowedExtensions[Path.GetExtension(spec.OutputPath)];
            var byteCount = new FileInfo(spec.OutputPath).Length;
            return ToolResult.Ok(
                message: $"Exported to {formatLabel}; {byteCount:N0} bytes",
                path: spec.OutputPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }
#endif
}
