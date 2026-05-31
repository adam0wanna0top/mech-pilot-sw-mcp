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
/// Inserts one component (.sldprt or sub-.sldasm) into an existing assembly
/// at a given (x, y, z) world position in mm. Components are placed but
/// **not mated** — mating is a separate concern (future add_mate tool).
///
/// v1 PR #9 critical lesson: **`AddComponent5` does NOT auto-load the
/// component file** — calling it on an unloaded part silently returns
/// null. Workaround: <c>OpenDoc6</c> the component first to preload it
/// into SW memory, then call AddComponent5.
///
/// Pipeline:
///   1. OpenDoc6 the assembly (Silent, read-write).
///   2. OpenDoc6 the component (Silent) — preload into SW memory so
///      AddComponent5 can find it.
///   3. AddComponent5(componentPath, 0=default config, "", false, "", x, y, z)
///   4. Save3 the assembly (in-place; M5 lesson — don't SaveAs(samepath)).
///   5. CloseDoc both component and assembly (in finally).
/// </summary>
[McpServerToolType]
public static class AddComponentTool
{
    [McpServerTool(Name = "add_component")]
    [Description(
        "Insert one component (.sldprt or sub-.sldasm) into an existing " +
        "SolidWorks assembly at a given (positionX, positionY, positionZ) " +
        "world position in mm. The component is placed but not mated — for " +
        "mating use a future add_mate tool. assemblyPath must be an absolute " +
        "path to an existing .sldasm. componentPath must be an absolute path " +
        "to an existing .sldprt or .sldasm. Position defaults to (0, 0, 0).")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm to insert into.")]
        string assemblyPath,
        [Description("Absolute path to the .sldprt or .sldasm component to insert.")]
        string componentPath,
        [Description("Component origin X in the assembly in mm. Default 0.")]
        double positionX = 0,
        [Description("Component origin Y in the assembly in mm. Default 0.")]
        double positionY = 0,
        [Description("Component origin Z in the assembly in mm. Default 0.")]
        double positionZ = 0)
    {
        var spec = new AddComponentSpec
        {
            AssemblyPath = assemblyPath,
            ComponentPath = componentPath,
            PositionXMm = positionX,
            PositionYMm = positionY,
            PositionZMm = positionZ,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(AddComponentSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddComponentInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_component failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_component requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult AddComponentInSw(AddComponentSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Open the assembly ────────────────────────────────────────────
        int openErrors = 0;
        int openWarnings = 0;
        var asmModel = swApp.OpenDoc6(
            FileName: spec.AssemblyPath,
            Type: (int)swDocumentTypes_e.swDocASSEMBLY,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (asmModel == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for assembly '{spec.AssemblyPath}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        IModelDoc2? compModel = null;
        try
        {
            // ── 2. Preload the component (v1 PR #9 critical: AddComponent5
            //   doesn't auto-load files — silently returns null otherwise) ──
            var compTypeIsAsm = spec.ComponentPath.EndsWith(
                ".sldasm", StringComparison.OrdinalIgnoreCase);
            int compErrors = 0;
            int compWarnings = 0;
            compModel = swApp.OpenDoc6(
                FileName: spec.ComponentPath,
                Type: compTypeIsAsm
                    ? (int)swDocumentTypes_e.swDocASSEMBLY
                    : (int)swDocumentTypes_e.swDocPART,
                Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                Configuration: string.Empty,
                Errors: ref compErrors,
                Warnings: ref compWarnings) as IModelDoc2;

            if (compModel == null)
            {
                throw new McpToolException(
                    $"OpenDoc6 returned null for component '{spec.ComponentPath}'. " +
                    $"errors=0x{compErrors:X} warnings=0x{compWarnings:X}.");
            }

            // ── 3. Re-activate the assembly so AddComponent5 targets it ─────
            swApp.ActivateDoc3(
                Name: asmModel.GetTitle(),
                UseUserPreferences: false,
                Option: (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                Errors: ref openErrors);

            var asmDoc = (IAssemblyDoc)asmModel;
            var xM = spec.PositionXMm / 1000.0;
            var yM = spec.PositionYMm / 1000.0;
            var zM = spec.PositionZMm / 1000.0;

            // ── 4. AddComponent5 ────────────────────────────────────────────
            //   ConfigOption = 0: use default config; NewConfigName / Existing
            //   left empty. UseConfigForPartReferences = false (we're not
            //   referencing a specific config tree).
            var component = asmDoc.AddComponent5(
                CompName: spec.ComponentPath,
                ConfigOption: 0,
                NewConfigName: string.Empty,
                UseConfigForPartReferences: false,
                ExistingConfigName: string.Empty,
                X: xM, Y: yM, Z: zM) as IComponent2;

            if (component == null)
            {
                throw new McpToolException(
                    $"AddComponent5 returned null for '{spec.ComponentPath}'. " +
                    "Common causes: the component was not preloaded (we did " +
                    "preload it — check SW console for permission / version " +
                    "errors), or the assembly was not the active doc when called.");
            }

            // ── 5. Save assembly in-place (M5 lesson: Save3 not SaveAs) ─────
            int saveErrors = 0;
            int saveWarnings = 0;
            var savedOk = asmModel.Save3(
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                ref saveErrors,
                ref saveWarnings);

            if (!savedOk || !File.Exists(spec.AssemblyPath))
            {
                throw new McpToolException(
                    $"Save3 failed for assembly '{spec.AssemblyPath}'. " +
                    $"errors=0x{saveErrors:X} warnings=0x{saveWarnings:X}.");
            }

            var compName = Path.GetFileNameWithoutExtension(spec.ComponentPath);
            return ToolResult.Ok(
                message:
                    $"Inserted '{compName}' at ({spec.PositionXMm}, {spec.PositionYMm}, " +
                    $"{spec.PositionZMm}) mm; saved assembly in place",
                path: spec.AssemblyPath);
        }
        finally
        {
            if (compModel != null)
            {
                swApp.CloseDoc(compModel.GetTitle());
            }
            swApp.CloseDoc(asmModel.GetTitle());
        }
    }
#endif
}
