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
/// Deep per-face / per-edge inspection (M51) — the read-side prerequisite of
/// precise solid operations. inspect_part/inspect_active stay lean (counts
/// only); this tool returns the full topology map on demand.
///
/// Two modes mirroring the modify_feature shape: ACTIVE doc (default, no
/// save/close) or partPath FILE mode (open read-only, close in finally —
/// nothing is written).
/// </summary>
[McpServerToolType]
public static class InspectTopologyTool
{
    [McpServerTool(Name = "inspect_topology")]
    [Description(
        "Deep-inspect a part's faces and edges — the geometric addresses for " +
        "precise operations. Per FACE: index, type (plane/cylinder/cone/" +
        "sphere/torus), area mm², bbox-center mm, plus normal (planes) or " +
        "axis+radius (cylinders). Per EDGE: index, type (line/circle), length " +
        "mm, plus endpoints (lines) or center+radius (circles). E.g. find " +
        "'the top face' = the plane with normal +Z and the highest center z; " +
        "'the hole wall' = the cylinder face with the matching radius. By " +
        "default inspects the ACTIVE part; pass partPath (absolute .sldprt) " +
        "to read a saved file instead. Indexes follow SW's enumeration order " +
        "— stable while the part is unchanged, refreshed after any edit (re-" +
        "inspect after modifying). Arrays cap at 200 entries (counts stay exact).")]
    public static ToolResult Run(
        [Description("Optional absolute .sldprt to inspect a SAVED part file instead of the active part.")]
        string? partPath = null)
    {
        return RunWithSpec(new InspectTopologySpec { PartPath = partPath });
    }

    public static ToolResult RunWithSpec(InspectTopologySpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try
        {
            return string.IsNullOrWhiteSpace(spec.PartPath) ? RunActive() : RunFile(spec.PartPath!);
        }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"inspect_topology failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("inspect_topology requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunActive()
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        // No save, no close — the caller keeps building on this doc.
        return Internal.TopologyReader.Build(model);
    }

    private static ToolResult RunFile(string partPath)
    {
        var swApp = SwConnection.Instance.GetApp();
        int openErrors = 0;
        int openWarnings = 0;
        var model = swApp.OpenDoc6(
            FileName: partPath,
            Type: (int)swDocumentTypes_e.swDocPART,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{partPath}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            return Internal.TopologyReader.Build(model);
        }
        finally
        {
            // Read-only: close without saving.
            swApp.CloseDoc(model.GetTitle());
        }
    }
#endif
}
