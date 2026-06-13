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
/// Runs SolidWorks interference (clash) detection on an assembly (M55) — the
/// tool-ified pairwise envelope audit. Confirming "nothing collides" used to
/// mean reading every component's world box (now in inspect_assembly's
/// worldBoundingBoxMm) and checking each pair by hand; this calls SW's real
/// solid-intersection check and returns the clashing component pairs + overlap
/// volume.
///
/// Mechanics (reflection-verified): IAssemblyDoc.InterferenceDetectionManager →
/// set options (TreatCoincidenceAsInterference, sub-assemblies-as-components,
/// multibody) → GetInterferenceCount() runs the calc → GetInterferences()
/// returns IInterference[] (each: Volume m³ + the interfering Component2 pair)
/// → Done(). Coincident (touching) faces are NOT flagged by default, so a part
/// resting on another or a shaft seated in a bore reads clean — only real solid
/// overlap counts.
///
/// Read-only: opens with the ReadOnly flag and closes without saving.
/// </summary>
[McpServerToolType]
public static class CheckInterferenceTool
{
    /// <summary>Cap on interference pairs listed (counts stay exact).</summary>
    private const int MaxListed = 100;

    [McpServerTool(Name = "check_interference")]
    [Description(
        "Run SolidWorks interference (clash) detection on an assembly and " +
        "report which components physically overlap — the real solid check " +
        "behind a 'does anything collide?' question, replacing a hand-computed " +
        "envelope audit. Returns the interference count and, for each clash, " +
        "the two component instance names and the overlap volume in mm³. By " +
        "default two faces merely touching (a part resting on another, a shaft " +
        "seated in a bore, intentional contacts) are NOT flagged — only real " +
        "solid overlap; set treatCoincidentAsInterference=true to also flag " +
        "zero-volume contacts. Read-only (the assembly is not modified). " +
        "Pair this with inspect_assembly's worldBoundingBoxMm to both see where " +
        "parts are and confirm none clash. assemblyPath must be an absolute " +
        "path to an existing .sldasm.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm to check.")]
        string assemblyPath,
        [Description("Also flag faces that merely touch (zero-volume contact). Default false.")]
        bool treatCoincidentAsInterference = false)
    {
        var spec = new CheckInterferenceSpec
        {
            AssemblyPath = assemblyPath,
            TreatCoincidentAsInterference = treatCoincidentAsInterference,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(CheckInterferenceSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return CheckInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"check_interference failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "check_interference requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult CheckInSw(CheckInterferenceSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();
        var asmPathNorm = Path.GetFullPath(spec.AssemblyPath);

        int openErrors = 0;
        int openWarnings = 0;
        const int openOptions =
            (int)swOpenDocOptions_e.swOpenDocOptions_Silent |
            (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
        var model = swApp.OpenDoc6(
            FileName: asmPathNorm,
            Type: (int)swDocumentTypes_e.swDocASSEMBLY,
            Options: openOptions,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for assembly '{asmPathNorm}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            var asmDoc = (IAssemblyDoc)model;

            // NoPIA: the manager getter returns object → explicit local before cast.
            object idmObj = asmDoc.InterferenceDetectionManager;
            if (idmObj is not IInterferenceDetectionMgr idm)
            {
                throw new McpToolException(
                    "InterferenceDetectionManager was unavailable for this assembly.");
            }

            // Options: only real solid overlap by default; treat sub-assemblies
            // as components and include multibody-part self-interferences so the
            // check is whole-assembly. Don't touch display (no transparency).
            idm.TreatCoincidenceAsInterference = spec.TreatCoincidentAsInterference;
            idm.TreatSubAssembliesAsComponents = true;
            idm.IncludeMultibodyPartInterferences = true;
            idm.MakeInterferingPartsTransparent = false;
            idm.IgnoreHiddenBodies = false;

            // GetInterferenceCount() triggers the calculation.
            var count = idm.GetInterferenceCount();
            var interferences = new List<Dictionary<string, object>>();

            if (count > 0)
            {
                object listObj = idm.GetInterferences();
                if (listObj is object[] arr)
                {
                    foreach (var itemObj in arr)
                    {
                        if (itemObj is not IInterference interference)
                        {
                            continue;
                        }
                        if (interferences.Count >= MaxListed)
                        {
                            break;
                        }
                        interferences.Add(DescribeInterference(interference));
                    }
                }
            }

            idm.Done();

            var data = new Dictionary<string, object>
            {
                ["title"] = model.GetTitle(),
                ["interferenceCount"] = count,
                ["interferences"] = interferences,
            };
            if (count > MaxListed)
            {
                data["truncated"] = true;
            }

            var message = count == 0
                ? $"'{model.GetTitle()}': no interference — all components clear."
                : $"'{model.GetTitle()}': {count} interference(s) — " +
                  string.Join("; ", interferences.Take(5).Select(SummarizeOne)) +
                  (count > 5 ? $"; … (+{count - 5} more)" : string.Empty);

            return ToolResult.Ok(message: message, data: data);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// One interference → { components: [nameA, nameB...], volumeMm3 }.
    /// NoPIA: Components returns object → object[] of Component2.
    /// </summary>
    private static Dictionary<string, object> DescribeInterference(IInterference interference)
    {
        var names = new List<string>();
        object compsObj = interference.Components;
        if (compsObj is object[] comps)
        {
            foreach (var c in comps)
            {
                if (c is IComponent2 comp)
                {
                    names.Add(comp.Name2 ?? string.Empty);
                }
            }
        }

        // Volume is m³ → mm³ (×1e9).
        var volumeMm3 = Math.Round(interference.Volume * 1_000_000_000.0, 2);
        return new Dictionary<string, object>
        {
            ["components"] = names,
            ["volumeMm3"] = volumeMm3,
        };
    }

    private static string SummarizeOne(Dictionary<string, object> i)
    {
        var names = i["components"] is List<string> n ? string.Join(" ↔ ", n) : "?";
        var vol = i["volumeMm3"];
        return $"{names} ({vol} mm³)";
    }
#endif
}
