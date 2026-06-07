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
/// Imports a neutral CAD file (STEP / IGES / Parasolid) as a SolidWorks part
/// (.sldprt) — M43. The result is a DUMB body (no parametric feature tree; an
/// MBimport node), which inspect_assembly classifies as "imported": a fixed
/// anchor the resize orchestration must never edit. Lets an LLM bring an
/// external/vendor part into an assembly (then add_component it).
///
/// Import recipe confirmed by the M40 probe:
///   • <see cref="System.IO.Path.GetFullPath"/> first — LoadFile4 needs a
///     backslash-normalized path (golden rule #14; a forward-slash path returns
///     a generic error).
///   • <c>GetImportFileData(path)</c> returns the format's import-options object.
///     Under NoPIA a COM method returning <c>object</c> is typed <c>dynamic</c>,
///     so it is captured into an explicit <c>object</c> local before being passed
///     on — otherwise LoadFile4 dynamic-dispatches and throws TYPE_E_ELEMENTNOTFOUND.
///   • <c>LoadFile4(path, "r", importData, ref errors)</c> returns the imported
///     model. (OpenDoc6 with swDocPART does NOT import neutral formats — it
///     returns swFileRequiresRepairError.)
/// </summary>
[McpServerToolType]
public static class ImportStepTool
{
    [McpServerTool(Name = "import_step")]
    [Description(
        "Import a neutral CAD file (STEP / IGES / Parasolid) as a SolidWorks part " +
        "(.sldprt). The imported part is a DUMB body — no parametric feature tree — " +
        "so inspect_assembly classifies it as 'imported' (a fixed anchor that must " +
        "NOT be resized). inputPath: absolute path to a .step/.stp/.iges/.igs/.x_t/" +
        ".x_b. outputPath: absolute .sldprt. Use this to bring an external/vendor " +
        "part into an assembly (then add_component it) — e.g. a bought-in part the " +
        "resize orchestration must keep fixed. (STL meshes are not supported.)")]
    public static ToolResult Run(
        [Description("Absolute path to an existing neutral CAD file (.step/.stp/.iges/.igs/.x_t/.x_b).")]
        string inputPath,
        [Description("Absolute output path ending in .sldprt, e.g. C:/tmp/imported.sldprt.")]
        string outputPath)
    {
        return RunWithSpec(new ImportStepSpec { InputPath = inputPath, OutputPath = outputPath });
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(ImportStepSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return ImportInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"import_step failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException(
            "import_step requires SolidWorks Interop assemblies, which were not present " +
            "at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult ImportInSw(ImportStepSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // golden rule #14: LoadFile4 needs a backslash-normalized absolute path.
        var inputFull = Path.GetFullPath(spec.InputPath);

        // NoPIA: a COM method returning `object` is typed dynamic; collapse it to
        // an explicit object so the LoadFile4 call doesn't dynamic-dispatch.
        object importData = swApp.GetImportFileData(inputFull);

        int importErrors = 0;
        var model = swApp.LoadFile4(inputFull, "r", importData, ref importErrors) as IModelDoc2;
        if (model == null)
        {
            throw new McpToolException(
                $"LoadFile4 returned null importing '{inputFull}' (errors=0x{importErrors:X}). " +
                "The file may be malformed or an unsupported variant of the format.");
        }

        try
        {
            var outputFull = Path.GetFullPath(spec.OutputPath);
            int saveErrors = 0;
            int saveWarnings = 0;
            var savedOk = model.Extension.SaveAs(
                outputFull,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref saveErrors,
                ref saveWarnings);

            if (!savedOk || !File.Exists(outputFull))
            {
                throw new McpToolException(
                    $"SaveAs failed for '{outputFull}'. errors=0x{saveErrors:X} warnings=0x{saveWarnings:X}.");
            }

            return ToolResult.Ok(
                message: $"Imported '{Path.GetFileName(spec.InputPath)}' → '{outputFull}' (dumb body)",
                path: outputFull);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }
#endif
}
