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
/// Mirrors one feature across a default reference plane (Front / Top / Right).
///
/// Selection-mark layout (SW_API_REFERENCE §6, opposite of LinearPattern):
///   • mirror plane → mark=2
///   • seed feature → mark=1   ← append after plane
/// Then <c>InsertMirrorFeature2(bMirrorBody=false, bGeometryPattern=true,
/// bMerge=true, bKnit=false, ScopeOptions=0)</c>.
///
/// Pipeline:
///   1. OpenDoc6 the input .sldprt (silent).
///   2. Select mirror plane by SW's named reference plane (CN / EN both tried).
///   3. Select seed feature — by name if <see cref="MirrorSpec.FeatureName"/>
///      is given, otherwise auto-pick the last user-meaningful feature
///      (same boot filter as inspect_part).
///   4. InsertMirrorFeature2.
///   5. Save: in-place → Save3; copy → Extension.SaveAs (M5 split).
///   6. CloseDoc (in finally).
/// </summary>
[McpServerToolType]
public static class MirrorFeatureTool
{
    [McpServerTool(Name = "mirror_feature")]
    [Description(
        "Mirror a feature of an existing SolidWorks part across one of the " +
        "three default reference planes (Front / Top / Right), then save the " +
        "result. Useful for symmetric geometry — e.g. drill one hole with " +
        "add_axial_hole, then mirror it across the Front plane to get the " +
        "matching hole on the other side. mirrorPlane must be 'front', 'top', " +
        "or 'right' (case-insensitive). featureName is optional: omit it to " +
        "mirror the most recently added user feature (LLM-common pattern), " +
        "or pass an exact feature name like 'Cut-Extrude1'. outputPath is " +
        "optional: empty = overwrite the input in place.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to edit, e.g. C:/tmp/part.sldprt.")]
        string inputPath,
        [Description("Mirror plane keyword: 'front', 'top', or 'right' (case-insensitive).")]
        string mirrorPlane,
        [Description("Optional exact feature name to mirror, e.g. 'Cut-Extrude1'. Omit for last user feature.")]
        string? featureName = null,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new MirrorSpec
        {
            InputPath = inputPath,
            MirrorPlane = mirrorPlane,
            FeatureName = featureName,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(MirrorSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return MirrorInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"mirror_feature failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "mirror_feature requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult MirrorInSw(MirrorSpec spec)
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
            var fm = model.FeatureManager;

            // ── 2. Select mirror plane (mark=2) ─────────────────────────────
            model.ClearSelection2(true);
            var planeAliases = MirrorSpec.PlaneAliases[spec.MirrorPlane];
            var planeSelected = false;
            foreach (var alias in planeAliases)
            {
                if (ext.SelectByID2(
                    Name: alias,
                    Type: "PLANE",
                    X: 0.0, Y: 0.0, Z: 0.0,
                    Append: false,
                    Mark: 2,
                    Callout: null,
                    SelectOption: 0))
                {
                    planeSelected = true;
                    break;
                }
            }
            if (!planeSelected)
            {
                throw new McpToolException(
                    $"Could not select mirror plane '{spec.MirrorPlane}'. " +
                    $"Tried: {string.Join(" / ", planeAliases)}. The part may have " +
                    "no default reference planes (very unusual).");
            }

            // ── 3. Select seed feature (mark=1, appended to plane) ──────────
            var seedName = SelectSeedFeature(model, ext, spec.FeatureName);

            // ── 4. Mirror — SW 2026 InsertMirrorFeature2 has 5 args (4 + ScopeOptions) ──
            //   bMirrorBody=false: we're mirroring a feature, not the whole body.
            //   bGeometryPattern=true: pure geometry copy (faster, more robust
            //     than parametric mirror for cut features).
            //   bMerge=true: merge the mirrored geometry into the same body.
            //   bKnit=false: surface-mirror only; not our path.
            //   ScopeOptions=0: default (mirror to all bodies in scope).
            var mirrorFeature = fm.InsertMirrorFeature2(
                BMirrorBody: false,
                BGeometryPattern: true,
                BMerge: true,
                BKnit: false,
                ScopeOptions: 0);

            if (mirrorFeature == null)
            {
                throw new McpToolException(
                    $"InsertMirrorFeature2 returned null. Seed feature '{seedName}' may " +
                    "not be mirror-able across this plane (e.g. already symmetric, or " +
                    "geometry would self-intersect). Check the FeatureManager log in SW UI.");
            }

            // ── 5. Save (in-place vs copy) — same split as M5 ───────────────
            var targetPath = string.IsNullOrWhiteSpace(spec.OutputPath)
                ? spec.InputPath
                : spec.OutputPath!;
            var isInPlace = string.Equals(targetPath, spec.InputPath, StringComparison.OrdinalIgnoreCase);

            int saveErrors = 0;
            int saveWarnings = 0;
            bool savedOk;
            if (isInPlace)
            {
                savedOk = model.Save3(
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref saveErrors,
                    ref saveWarnings);
            }
            else
            {
                savedOk = ext.SaveAs(
                    Name: targetPath,
                    Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ExportData: null,
                    Errors: ref saveErrors,
                    Warnings: ref saveWarnings);
            }

            if (!savedOk || !File.Exists(targetPath))
            {
                var api = isInPlace ? "Save3" : "SaveAs";
                throw new McpToolException(
                    $"{api} failed for '{targetPath}'. errors=0x{saveErrors:X} " +
                    $"warnings=0x{saveWarnings:X}.");
            }

            return ToolResult.Ok(
                message: $"Mirrored '{seedName}' across {spec.MirrorPlane} plane; saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Selects the seed feature (mark=1, appended to the plane already at
    /// mark=2). If <paramref name="featureName"/> is given, uses
    /// <c>SelectByID2("BODYFEATURE")</c>; otherwise walks the feature list
    /// and picks the last user-meaningful feature (same boot filter as
    /// inspect_part). Returns the name actually selected.
    /// </summary>
    private static string SelectSeedFeature(IModelDoc2 model, IModelDocExtension ext, string? featureName)
    {
        if (!string.IsNullOrWhiteSpace(featureName))
        {
            if (!ext.SelectByID2(
                Name: featureName,
                Type: "BODYFEATURE",
                X: 0.0, Y: 0.0, Z: 0.0,
                Append: true,
                Mark: 1,
                Callout: null,
                SelectOption: 0))
            {
                throw new McpToolException(
                    $"Could not select feature '{featureName}' for mirroring. " +
                    "Verify the name with inspect_part first.");
            }
            return featureName;
        }

        // Auto-pick via shared helper (Tools/Internal/PartGeometryHelpers).
        var lastUserFeature = Internal.PartGeometryHelpers.FindLastUserFeature(model);
        if (lastUserFeature == null)
        {
            throw new McpToolException(
                "Cannot auto-pick a seed feature: the part has no user-meaningful features " +
                "(only reference planes / folders). Add a feature first " +
                "(e.g. with add_axial_hole) or pass an explicit featureName.");
        }
        var seedName = lastUserFeature.Name ?? "(unnamed)";
        if (!((IEntity)lastUserFeature).Select2(Append: true, Mark: 1))
        {
            throw new McpToolException($"IEntity.Select2 failed on seed feature '{seedName}'.");
        }
        return seedName;
    }
#endif
}
