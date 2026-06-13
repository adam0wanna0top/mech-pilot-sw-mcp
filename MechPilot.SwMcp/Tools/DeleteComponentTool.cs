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
/// Removes one component instance from an assembly by name (M53-②) — the
/// assembly-level rollback primitive, sibling of M48's delete_feature. Born
/// from the fan dogfooding pain: a wrong component, or a "ghost" left behind
/// by a half-failed add_component (RPC_E_DISCONNECTED mid-insert), could not
/// be removed without rebuilding the whole assembly.
///
/// Mechanics (reflection-verified): open the assembly →
/// <c>IAssemblyDoc.GetComponentByName</c> (fall back to a Name2 scan) →
/// <c>IComponent2.Select2</c> → <c>IModelDocExtension.DeleteSelection2</c>
/// with <c>swDelete_Children</c> so the mates that reference the component go
/// with it (no dangling mates) → rebuild → Save3 in place → close.
///
/// File-mode only (assemblies are always edited as files here, like
/// add_component / inspect_assembly). The component file on disk is never
/// touched — only the instance + its mates leave the assembly. A
/// top-level-count guard turns a silent no-op into a loud failure.
/// </summary>
[McpServerToolType]
public static class DeleteComponentTool
{
    /// <summary>Max instance names listed in the "not found" error.</summary>
    private const int MaxNamesInError = 20;

    [McpServerTool(Name = "delete_component")]
    [Description(
        "Remove one component instance from an assembly by its instance name " +
        "(the 'name' inspect_assembly reports, e.g. 'bolt-2') — the assembly " +
        "rollback primitive: inserted the wrong part, or a half-failed " +
        "add_component left a ghost instance? Delete it instead of rebuilding " +
        "the whole assembly. The mates that reference the component are removed " +
        "with it. The component's .sldprt/.sldasm file on disk is NOT deleted — " +
        "only the instance leaves this assembly. If the name doesn't match, the " +
        "error lists the available instance names. assemblyPath must be an " +
        "absolute path to an existing .sldasm. Run inspect_assembly first to " +
        "read instance names (and to count instances before retrying a " +
        "half-failed insert).")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm to remove from.")]
        string assemblyPath,
        [Description("Instance name to remove, exactly as inspect_assembly reports it (e.g. 'bolt-2').")]
        string componentName)
    {
        var spec = new DeleteComponentSpec
        {
            AssemblyPath = assemblyPath,
            ComponentName = componentName,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(DeleteComponentSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return DeleteComponentInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"delete_component failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "delete_component requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult DeleteComponentInSw(DeleteComponentSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // M20 lesson: normalize the path to OS-canonical form before OpenDoc6.
        var asmPathNorm = Path.GetFullPath(spec.AssemblyPath);

        int openErrors = 0;
        int openWarnings = 0;
        var asmModel = swApp.OpenDoc6(
            FileName: asmPathNorm,
            Type: (int)swDocumentTypes_e.swDocASSEMBLY,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (asmModel == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for assembly '{asmPathNorm}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            var asmDoc = (IAssemblyDoc)asmModel;
            var countBefore = asmDoc.GetComponentCount(ToplevelOnly: true);

            var component = ResolveComponent(asmDoc, spec.ComponentName);

            asmModel.ClearSelection2(true);
            if (!component.Select2(false, 0))
            {
                throw new McpToolException(
                    $"Could not select component '{spec.ComponentName}' for deletion " +
                    "(Select2 failed). It may be suppressed or in an edit state — " +
                    "inspect_assembly and retry.");
            }

            // swDelete_Children cascades into the mates that reference the
            // component so no dangling mate is left behind.
            var options = (int)swDeleteSelectionOptions_e.swDelete_Children;
            if (!asmModel.Extension.DeleteSelection2(options))
            {
                throw new McpToolException(
                    $"DeleteSelection2 failed for component '{spec.ComponentName}'.");
            }

            asmModel.ClearSelection2(true);
            asmModel.EditRebuild3();

            // Guard: top-level count must drop — a returned-true-but-no-op would
            // otherwise pass silently (chamfer-delta lesson, M52).
            var countAfter = asmDoc.GetComponentCount(ToplevelOnly: true);
            if (countAfter >= countBefore)
            {
                throw new McpToolException(
                    $"Component count did not drop after deleting '{spec.ComponentName}' " +
                    $"({countBefore} → {countAfter}). The delete silently no-op'd — " +
                    "the assembly was not modified.");
            }

            int saveErrors = 0;
            int saveWarnings = 0;
            var savedOk = asmModel.Save3(
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                ref saveErrors,
                ref saveWarnings);

            if (!savedOk || !File.Exists(asmPathNorm))
            {
                throw new McpToolException(
                    $"Save3 failed for assembly '{asmPathNorm}'. " +
                    $"errors=0x{saveErrors:X} warnings=0x{saveWarnings:X}.");
            }

            return ToolResult.Ok(
                message:
                    $"Deleted component '{spec.ComponentName}' (and its mates) from the " +
                    $"assembly; {countBefore} → {countAfter} top-level components; " +
                    "saved assembly in place",
                path: spec.AssemblyPath);
        }
        finally
        {
            swApp.CloseDoc(asmModel.GetTitle());
        }
    }

    /// <summary>
    /// Resolves the instance by name: GetComponentByName (exact), else a scan
    /// of the top-level components by Name2 (exact, then case-insensitive).
    /// On a miss, throws listing the available instance names (parse-friendly,
    /// quoted) so the LLM can pick a real one.
    /// </summary>
    private static IComponent2 ResolveComponent(IAssemblyDoc asmDoc, string name)
    {
        var trimmed = name.Trim();

        if (asmDoc.GetComponentByName(trimmed) is IComponent2 direct)
        {
            return direct;
        }

        var components = EnumerateTopLevel(asmDoc);

        var exact = components.FirstOrDefault(
            c => string.Equals(c.Name2, trimmed, StringComparison.Ordinal));
        if (exact != null)
        {
            return exact;
        }

        var caseInsensitive = components.FirstOrDefault(
            c => string.Equals(c.Name2, trimmed, StringComparison.OrdinalIgnoreCase));
        if (caseInsensitive != null)
        {
            return caseInsensitive;
        }

        var names = components
            .Select(c => c.Name2)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToArray();
        var shown = names.Take(MaxNamesInError).Select(n => $"'{n}'");
        var more = names.Length > MaxNamesInError
            ? $" … and {names.Length - MaxNamesInError} more"
            : string.Empty;
        var available = names.Length == 0
            ? "the assembly has no top-level components"
            : $"available instance names ({Math.Min(MaxNamesInError, names.Length)} of " +
              $"{names.Length}): {string.Join(", ", shown)}{more}";
        throw new McpToolException(
            $"Component '{trimmed}' not found in the assembly — {available}. " +
            "Pass an exact instance name from inspect_assembly.");
    }

    /// <summary>Top-level components as a typed list (NoPIA: GetComponents
    /// returns object → explicit object local, never var/dynamic).</summary>
    private static List<IComponent2> EnumerateTopLevel(IAssemblyDoc asmDoc)
    {
        var result = new List<IComponent2>();
        object compsObj = asmDoc.GetComponents(ToplevelOnly: true);
        if (compsObj is object[] comps)
        {
            foreach (var c in comps)
            {
                if (c is IComponent2 comp)
                {
                    result.Add(comp);
                }
            }
        }
        return result;
    }
#endif
}
