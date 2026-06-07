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
/// Reads metadata from an existing part file: bounding box, feature list,
/// face / edge counts. Pure read-only — opens with the ReadOnly flag and
/// closes without saving. The metadata extraction itself lives in
/// <see cref="Internal.PartMetadata"/>, shared with <see cref="InspectActiveTool"/>.
///
/// LLM value: lets an LLM "see" a .sldprt it didn't create. Before this tool
/// the LLM could only guess at a part's size / feature set from the file name
/// or earlier conversation; now it can ask the part directly (e.g. before
/// drilling a Φ30 hole into a D20 cylinder, inspect first).
///
/// Pipeline:
///   1. OpenDoc6 with Silent | ReadOnly.
///   2. Internal.PartMetadata.Build(model) — bbox + body face/edge counts +
///      top-level feature list → ToolResult.
///   3. CloseDoc — no save (ReadOnly mode means no dirty state).
/// </summary>
[McpServerToolType]
public static class InspectPartTool
{
    [McpServerTool(Name = "inspect_part")]
    [Description(
        "Read metadata from an existing SolidWorks part (read-only). Returns " +
        "the part's title, top-level feature count and list — each feature with " +
        "its editable dimensions (name like 'D1@凸台-拉伸1', value, and unit " +
        "'mm'/'deg') that modify_feature can change — total face / edge count " +
        "across solid bodies, and a bounding box in millimeters. " +
        "Use this to 'see' a part before editing it — e.g. check the diameter " +
        "before drilling a hole that's too large for the part. inputPath must " +
        "be an absolute path to an existing .sldprt. (To inspect the part you " +
        "are currently building in the generic layer without saving, use " +
        "inspect_active instead.)")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to inspect, e.g. C:/tmp/part.sldprt.")]
        string inputPath)
    {
        var spec = new InspectSpec { InputPath = inputPath };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(InspectSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return InspectInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"inspect_part failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "inspect_part requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult InspectInSw(InspectSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Open the existing part read-only ─────────────────────────────
        //   Silent | ReadOnly: no UI prompts, no dirty state. CloseDoc later
        //   is a clean drop, no Save / Save3 needed (M5 trap structurally
        //   impossible on read-only docs).
        int openErrors = 0;
        int openWarnings = 0;
        const int openOptions =
            (int)swOpenDocOptions_e.swOpenDocOptions_Silent |
            (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
        var model = swApp.OpenDoc6(
            FileName: spec.InputPath,
            Type: (int)swDocumentTypes_e.swDocPART,
            Options: openOptions,
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
            return Internal.PartMetadata.Build(model);
        }
        finally
        {
            // Read-only doc: clean drop, no Save needed.
            swApp.CloseDoc(model.GetTitle());
        }
    }
#endif
}
