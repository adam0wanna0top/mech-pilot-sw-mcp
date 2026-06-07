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
/// Reads metadata from an existing assembly: title, top-level component
/// list (instance name, source path, world position, suppression state).
/// Pure read-only — opens with ReadOnly flag and closes without saving.
///
/// Sibling of InspectPartTool — same Open(ReadOnly) → walk → Close pattern,
/// just walking <c>IAssemblyDoc.GetComponents</c> instead of the feature
/// tree.
///
/// LLM value: before add_mate the LLM doesn't know component instance names
/// ("asm_cyl_123-1" vs "asm_block_456-1") or where each component sits in
/// the world. This tool surfaces both so the LLM can wire up mates
/// confidently. v1 PR #19 in v1's history — that PR's lesson was that
/// inspect_assembly **before** add_mate gives the LLM eyes on the assembly,
/// otherwise add_mate's component-name argument is a wild guess.
/// </summary>
[McpServerToolType]
public static class InspectAssemblyTool
{
    [McpServerTool(Name = "inspect_assembly")]
    [Description(
        "Read metadata from an existing SolidWorks assembly (read-only). " +
        "Returns the assembly's title, top-level component count, and a list " +
        "of components with their instance name (e.g. 'asm_cyl_123-1'), " +
        "source file path + file name, world position in mm, suppression " +
        "state, and — for the resize/edit workflow — a 'kind' ('ourPart' = a " +
        "parametric part we built and can edit, 'imported' = a dumb STEP/" +
        "neutral body that must NOT be edited, 'subassembly', or 'unknown'), a " +
        "'standardCandidate' flag (file name looks like a standard fastener/" +
        "bearing), and 'editableDimensions' (modify_feature handles, for " +
        "ourPart components). Use this BEFORE add_mate to learn instance names, " +
        "and before any resize to see which components are editable vs fixed. " +
        "inputPath must be an absolute path to an existing .sldasm. " +
        "For parts (.sldprt) use inspect_part instead.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm to inspect.")]
        string inputPath)
    {
        var spec = new InspectAssemblySpec { InputPath = inputPath };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(InspectAssemblySpec spec)
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
                $"inspect_assembly failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "inspect_assembly requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult InspectInSw(InspectAssemblySpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Open read-only (same M5-safe pattern as inspect_part) ───────
        int openErrors = 0;
        int openWarnings = 0;
        const int openOptions =
            (int)swOpenDocOptions_e.swOpenDocOptions_Silent |
            (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
        var model = swApp.OpenDoc6(
            FileName: spec.InputPath,
            Type: (int)swDocumentTypes_e.swDocASSEMBLY,
            Options: openOptions,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{spec.InputPath}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            var title = model.GetTitle();
            var asmDoc = (IAssemblyDoc)model;

            // ── 2. Walk top-level components ────────────────────────────────
            var components = new List<Dictionary<string, object>>();
            var componentsObj = asmDoc.GetComponents(true);  // true = top-level only
            if (componentsObj is object[] comps)
            {
                foreach (var c in comps)
                {
                    if (c is not IComponent2 comp) continue;
                    components.Add(ReadComponent(comp));
                }
            }

            // ── 3. Build human summary + structured payload ────────────────
            var countLabel = components.Count switch
            {
                0 => "no components",
                1 => "1 component",
                _ => $"{components.Count} components",
            };
            var kindSummary = string.Join(", ",
                components.GroupBy(c => (string)c["kind"])
                          .OrderBy(g => g.Key)
                          .Select(g => $"{g.Count()} {g.Key}"));
            var summary = components.Count == 0
                ? $"'{title}': empty assembly"
                : $"'{title}': {countLabel} ({kindSummary}) — {string.Join(", ", components.ConvertAll(c => (string)c["name"]))}";

            var data = new Dictionary<string, object>
            {
                ["title"] = title,
                ["componentCount"] = components.Count,
                ["components"] = components,
            };

            return ToolResult.Ok(message: summary, data: data);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Reads one component's instance name, source path, world position
    /// (translation portion of GetXform, m → mm), and suppression state.
    /// </summary>
    /// <remarks>
    /// SW's transform matrix is column-major 4×4 (16 doubles): the first 9
    /// entries are the rotation block, entries [9..11] are the translation
    /// X / Y / Z in meters. v1 PR #19 lesson: <c>GetXform[9:12]</c> is the
    /// translation slot in row terms.
    ///
    /// **What positionMm means**: the component's **frame origin** in world
    /// coordinates, NOT the centroid. For a part extruded along +Z by length
    /// L, the frame origin sits at the Front-Plane (z=0 end) face; the
    /// centroid is L/2 above it. So `add_component(asm, cyl_L30, 0, 0, 0)`
    /// anchors the centroid at world (0,0,0) and the frame origin ends up
    /// at (0, 0, -15) — which is what GetXform reports. LLMs reading
    /// positionMm should treat X/Y as direct match to add_component's input
    /// and expect Z to differ by half the part's height for +Z-extruded
    /// parts. M17 L2 documents this with cyl L30 → z=-15, block H10 → z=-5.
    /// </remarks>
    private static Dictionary<string, object> ReadComponent(IComponent2 comp)
    {
        var sourcePath = comp.GetPathName() ?? string.Empty;
        var fileName = Path.GetFileName(sourcePath);
        var (kind, dimensions) = ClassifyComponent(comp);

        var info = new Dictionary<string, object>
        {
            ["name"] = comp.Name2 ?? string.Empty,
            ["sourcePath"] = sourcePath,
            ["fileName"] = fileName,
            ["suppressed"] = comp.IsSuppressed(),
            ["kind"] = kind,
            ["standardCandidate"] = Internal.StandardPartNames.IsStandardCandidate(fileName),
            ["editableDimensions"] = dimensions,
        };

        if (comp.GetXform() is double[] xform && xform.Length >= 12)
        {
            info["positionMm"] = new Dictionary<string, double>
            {
                ["x"] = xform[9] * 1000.0,
                ["y"] = xform[10] * 1000.0,
                ["z"] = xform[11] * 1000.0,
            };
        }
        return info;
    }

    /// <summary>
    /// Classifies a component as ourPart / imported / subassembly / unknown and,
    /// for our parametric parts, returns the editable dimensions (the
    /// modify_feature handles, reused from <see cref="Internal.PartMetadata"/>).
    /// "imported" = the part's feature tree carries an import node (e.g. MBimport
    /// from a STEP) — a fixed anchor the resize orchestrator must never edit;
    /// "ourPart" = it has parametric build features. Suppressed / unloaded
    /// components and non-part/non-assembly docs are "unknown".
    /// </summary>
    private static (string kind, List<Dictionary<string, object>> dimensions) ClassifyComponent(IComponent2 comp)
    {
        var empty = new List<Dictionary<string, object>>();
        if (comp.IsSuppressed())
        {
            return (Internal.PartKind.Unknown, empty);
        }
        if (comp.GetModelDoc2() is not IModelDoc2 model)
        {
            return (Internal.PartKind.Unknown, empty);
        }
        if (model is IAssemblyDoc)
        {
            return ("subassembly", empty);
        }
        if (model is not IPartDoc)
        {
            return (Internal.PartKind.Unknown, empty);
        }

        var features = Internal.PartMetadata.ReadTopLevelFeatures(model);
        var typeNames = features.ConvertAll(f => (string)f["typeName"]);
        var kind = Internal.PartKind.ClassifyPart(typeNames);
        if (kind != Internal.PartKind.OurPart)
        {
            return (kind, empty);
        }

        var dims = new List<Dictionary<string, object>>();
        foreach (var f in features)
        {
            dims.AddRange((List<Dictionary<string, object>>)f["dimensions"]);
        }
        return (kind, dims);
    }
#endif
}
