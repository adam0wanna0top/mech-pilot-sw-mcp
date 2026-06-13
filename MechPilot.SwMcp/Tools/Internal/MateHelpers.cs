#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Exceptions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Cross-tool mate-family helpers extracted at the rule-of-four threshold:
///   • <see cref="SelectFirstPlane"/> + <see cref="FormatAttempts"/> — copy
///     in 3 tools (AddCoincidentMateTool / AddDistanceMateTool /
///     AddAngleMateTool; AddConcentricMateTool does not use these because
///     concentric mates a cylindrical face, not a reference plane).
///   • <see cref="MapAlignment"/> — copy in **all 4** mate tools.
///   • <see cref="StripSldasmExt"/> — copy in 3 tools (same 3 as
///     SelectFirstPlane). AddConcentricMateTool does not strip the title
///     since it doesn't build qualified plane names.
///
/// All four are pure functions (no state, no side effects, no SW Interop
/// nondeterminism). Extraction is mechanical — verified byte-equivalent
/// across all 4 source tools before this PR.
/// </summary>
internal static class MateHelpers
{
    /// <summary>
    /// Try to select a default reference plane (Front / Top / Right) on a
    /// component by its qualified name <c>"{alias}@{componentName}@{asmTitle}"</c>.
    /// Falls through the alias list (CN "前视基准面" first since the SW UI is
    /// configured in Chinese, EN "Front Plane" as fallback). Mark=0 per
    /// SW_API_REFERENCE §6 (AddMate5 path uses mark=0; CreateMate uses mark=1).
    /// Returns the actually-selected qualified name on success, null on failure.
    /// </summary>
    public static string? SelectFirstPlane(
        IModelDocExtension ext,
        IReadOnlyList<string> aliases,
        string componentName,
        string asmTitle,
        bool append)
    {
        foreach (var alias in aliases)
        {
            var fullName = $"{alias}@{componentName}@{asmTitle}";
            if (ext.SelectByID2(
                Name: fullName,
                Type: "PLANE",
                X: 0.0, Y: 0.0, Z: 0.0,
                Append: append,
                Mark: 0,
                Callout: null,
                SelectOption: 0))
            {
                return fullName;
            }
        }
        return null;
    }

    /// <summary>
    /// Format the qualified-name attempt list for a "could not select X plane"
    /// error message. Renders as <c>"'前视基准面@cyl-1@asm' / 'Front Plane@cyl-1@asm'"</c>.
    /// </summary>
    public static string FormatAttempts(
        IReadOnlyList<string> aliases, string componentName, string asmTitle) =>
        string.Join(" / ",
            aliases.Select(a => $"'{a}@{componentName}@{asmTitle}'"));

    /// <summary>
    /// Selects the mate reference on one side of a coincident / distance mate
    /// (M54): a SPECIFIC planar model face by inspect_topology index when
    /// <paramref name="faceIndex"/> is given, otherwise the named reference
    /// plane (Front / Top / Right) — the same call site handles both. Returns a
    /// human signature for the success message ("front@cyl-1" or
    /// "#3 plane@block-1"); throws <see cref="McpToolException"/> on failure.
    /// </summary>
    public static string SelectMateReference(
        IAssemblyDoc asmDoc, IModelDocExtension ext,
        string componentName, int? faceIndex,
        IReadOnlyList<string>? planeAliases, string? planeLabel,
        string asmTitle, bool append)
    {
        if (faceIndex is int idx)
        {
            var comp = FindComponentByName(asmDoc, componentName)
                ?? throw new McpToolException(
                    $"Component '{componentName}' not found in the assembly. " +
                    "Verify the instance name with inspect_assembly first.");
            var (face, sig) = ComponentFaceSelector.GetPlanarFaceByIndex(
                comp, idx, componentName);
            if (!((IEntity)face).Select2(append, 0))
            {
                throw new McpToolException(
                    $"Failed to select face {sig} on '{componentName}'.");
            }
            return $"{sig}@{componentName}";
        }

        var selected = SelectFirstPlane(ext, planeAliases!, componentName, asmTitle, append);
        if (selected == null)
        {
            throw new McpToolException(
                $"Could not select '{planeLabel}' plane on component " +
                $"'{componentName}'. Tried " +
                $"{FormatAttempts(planeAliases!, componentName, asmTitle)}. " +
                "Verify the component name with inspect_assembly first.");
        }
        return $"{planeLabel}@{componentName}";
    }

    /// <summary>
    /// Finds a top-level component by its <c>Name2</c> (case-insensitive).
    /// Returns null if none match. (NoPIA: GetComponents returns object → an
    /// explicit object local before the array cast.)
    /// </summary>
    public static IComponent2? FindComponentByName(IAssemblyDoc asmDoc, string name)
    {
        object compsObj = asmDoc.GetComponents(true);
        if (compsObj is not object[] comps)
        {
            return null;
        }
        foreach (var c in comps)
        {
            if (c is IComponent2 comp
                && string.Equals(comp.Name2, name, StringComparison.OrdinalIgnoreCase))
            {
                return comp;
            }
        }
        return null;
    }

    /// <summary>
    /// Map an LLM-facing alignment keyword to the <see cref="swMateAlign_e"/>
    /// enum int passed to AddMate5. Throws <see cref="McpToolException"/> for
    /// unrecognized keywords (spec-layer validation should catch these
    /// upstream — this is defense in depth).
    /// </summary>
    public static int MapAlignment(string keyword) => keyword.ToLowerInvariant() switch
    {
        "aligned" => (int)swMateAlign_e.swMateAlignALIGNED,
        "anti-aligned" => (int)swMateAlign_e.swMateAlignANTI_ALIGNED,
        "closest" => (int)swMateAlign_e.swMateAlignCLOSEST,
        _ => throw new McpToolException($"unmapped alignment '{keyword}'"),
    };

    /// <summary>
    /// Strip the <c>.SLDASM</c> extension (case-insensitive) from a doc title.
    /// The assembly title returned by <see cref="IModelDoc2.GetTitle"/> includes
    /// the extension, but the qualified plane name <c>"plane@component@asmTitle"</c>
    /// expects it without — empirically verified across M18/M19/M25 L2 tests.
    /// </summary>
    public static string StripSldasmExt(string title)
    {
        const string ext = ".SLDASM";
        return title.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
            ? title.Substring(0, title.Length - ext.Length)
            : title;
    }
}
#endif
