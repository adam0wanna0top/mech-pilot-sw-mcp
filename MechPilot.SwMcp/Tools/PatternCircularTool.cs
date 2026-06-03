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
/// Circular (rotational) pattern of a single seed feature around the part's
/// first axial-Z cylindrical face. v1 PR #32 真根因 already encoded:
/// <c>FeatureCircularPattern3.Spacing</c> with <c>EqualSpacing=false</c>
/// is the per-instance angle (not total), so we pass
/// <c>spacing = totalAngleRad / count</c>. <c>EqualSpacing=true</c> made the
/// returned feat null in v1, so we stick with <c>false</c>.
///
/// Selection-mark layout (SW_API_REFERENCE §6):
///   • axis face (cylindrical, axis ≈ ±Z) → mark=1
///   • seed feature                       → mark=4
/// Then <c>FeatureCircularPattern3(count, spacingRad, flipDirection=false,
/// DName="", GeometryPattern=true, EqualSpacing=false)</c>.
///
/// Pipeline:
///   1. OpenDoc6 the input .sldprt (silent).
///   2. Find first axial-Z cylindrical face on body. Select mark=1.
///      (Inline copy of M21 AddConcentricMateTool.FindFirstAxialCylinderFace —
///      this is the 2nd use; promote to PartGeometryHelpers on the 3rd.)
///   3. Select seed feature (by name or auto-pick last user feature), mark=4.
///   4. FeatureCircularPattern3.
///   5. Save: in-place → Save3; copy → Extension.SaveAs (M5 split).
///   6. CloseDoc (in finally).
///
/// SW limitation (v1 PR #35): on parts with multiple existing cut features
/// stacked on one body, FeatureCircularPattern3 silent-fails along every path.
/// LLM-facing description tells the model to use create_flange for PCD bolt
/// circles on flange-class parts instead.
/// </summary>
[McpServerToolType]
public static class PatternCircularTool
{
    [McpServerTool(Name = "pattern_circular")]
    [Description(
        "Circular (rotational) pattern of a single seed feature around the " +
        "part's central ±Z axis (auto-detected from the first cylindrical face " +
        "whose axis is aligned with Z — matches create_cylinder / create_flange / " +
        "any add_axial_hole'd part). count includes the seed; totalAngleDeg " +
        "defaults to 360 (full equal-pitch circle) — pass less for a partial arc. " +
        "featureName is optional: omit to pattern the most recently added user " +
        "feature. outputPath is optional: empty = overwrite the input in place. " +
        "Common use: 'create_cylinder D40 L20 + add_axial_hole at (10, 0) + " +
        "pattern_circular count=6' produces a 6-hole PCD20 ring. " +
        "LIMITATION (SW): cannot pattern a seed on a part that already has " +
        "multiple cut features stacked (SW silent-fails 12 known paths). " +
        "For flange-class PCD bolt patterns, use create_flange instead " +
        "(it packs all holes into one sketch + one cut, no pattern API needed).")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to edit, e.g. C:/tmp/cyl.sldprt.")]
        string inputPath,
        [Description("Total instances around the axis, including the seed. e.g. 6.")]
        int count,
        [Description("Total sweep angle in degrees. Default 360 (full circle). Range (0, 360].")]
        double totalAngleDeg = 360.0,
        [Description("Optional exact seed feature name; omit for last user feature.")]
        string? featureName = null,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new CircularPatternSpec
        {
            InputPath = inputPath,
            Count = count,
            TotalAngleDeg = totalAngleDeg,
            FeatureName = featureName,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(CircularPatternSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return PatternInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"pattern_circular failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "pattern_circular requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private const double ZAxisThreshold = 0.99;

    private static ToolResult PatternInSw(CircularPatternSpec spec)
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
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            var ext = model.Extension;
            var fm = model.FeatureManager;

            // ── 2. Select axial-Z cylindrical face as pattern axis (mark=1) ──
            model.ClearSelection2(true);
            var axisFace = FindFirstAxialCylinderFace(model)
                ?? throw new McpToolException(
                    "Could not find a cylindrical face with axis along ±Z. " +
                    "pattern_circular needs the part to have at least one axial-Z " +
                    "cylindrical surface (create_cylinder / create_flange / any part " +
                    "drilled with add_axial_hole all qualify). For a rectangular block " +
                    "without a center axis, this tool does not apply.");
            if (!((IEntity)axisFace).Select4(Append: false, Data: null))
            {
                throw new McpToolException(
                    "Failed to select the axial-Z cylindrical face as pattern axis.");
            }
            // Re-mark to 1 (SW_API_REFERENCE §6: circular pattern axis → mark=1).
            ((IEntity)axisFace).Select2(Append: false, Mark: 1);

            // ── 3. Select seed feature (mark=4) ─────────────────────────────
            var seedName = SelectSeedFeature(model, ext, spec.FeatureName);

            // ── 4. FeatureCircularPattern3.
            //   v1 PR #32 真根因: Spacing with EqualSpacing=false is the
            //   per-instance angle (in radians). EqualSpacing=true caused the
            //   returned feat to be null in v1, so we stick with false.
            //   GeometryPattern=true: pure-geometry copy, the robust default
            //   (matches pattern_linear). FlipDirection=false: default sense.
            var spacingRad = (spec.TotalAngleDeg * Math.PI / 180.0) / spec.Count;
            var patternFeature = fm.FeatureCircularPattern3(
                Number: spec.Count,
                Spacing: spacingRad,
                FlipDirection: false,
                DName: string.Empty,
                GeometryPattern: true,
                EqualSpacing: false);

            if (patternFeature == null)
            {
                throw new McpToolException(
                    $"FeatureCircularPattern3 returned null for seed '{seedName}' / " +
                    $"count={spec.Count} totalAngle={spec.TotalAngleDeg}°. " +
                    "Common cause (v1 PR #35): SW silent-fails circular pattern when " +
                    "the part already has multiple cut features on the same body. " +
                    "If you're trying to make a PCD bolt circle on a flange, use " +
                    "create_flange instead (it packs all holes into one sketch + " +
                    "one cut, sidestepping the pattern API). " +
                    "Other possible cause: the seed feature isn't pattern-able.");
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

            var arcLabel = Math.Abs(spec.TotalAngleDeg - 360.0) < 1e-6
                ? $"full circle ({spec.Count}×)"
                : $"{spec.TotalAngleDeg}° arc ({spec.Count}×, {spacingRad * 180.0 / Math.PI:F2}°/instance)";
            return ToolResult.Ok(
                message: $"Patterned '{seedName}' circularly around ±Z axis — {arcLabel}; " +
                         $"saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Walks the first solid body's faces and returns the first one whose
    /// surface is cylindrical with its axis aligned with ±Z
    /// (|axis.Z| > 0.99 cos similarity).
    /// </summary>
    /// <remarks>
    /// <c>ISurface.get_CylinderParams</c> returns a 7-double array:
    /// [0..2] = a root point on the axis (meters),
    /// [3..5] = axis direction unit vector,
    /// [6]    = radius (meters).
    /// Inline copy of the same logic in AddConcentricMateTool (which operates
    /// on IComponent2 instead of IModelDoc2). Rule of two — extract to
    /// PartGeometryHelpers when a 3rd caller appears.
    /// </remarks>
    private static IFace2? FindFirstAxialCylinderFace(IModelDoc2 model)
    {
        var part = (IPartDoc)model;
        var bodiesObj = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (bodiesObj is not object[] bodies || bodies.Length == 0)
        {
            return null;
        }
        var body = (IBody2)bodies[0];
        if (body.GetFaces() is not object[] faces) return null;

        foreach (var faceObj in faces)
        {
            var face = (IFace2)faceObj;
            var surface = (ISurface)face.GetSurface();
            if (!surface.IsCylinder()) continue;
            if (surface.CylinderParams is not double[] cp || cp.Length < 6) continue;
            // axis direction at indices 3..5
            if (Math.Abs(cp[5]) > ZAxisThreshold)
            {
                return face;
            }
        }
        return null;
    }

    /// <summary>
    /// Selects the seed feature (mark=4, appended to the axis face).
    /// If <paramref name="featureName"/> is given, uses
    /// <c>SelectByID2("BODYFEATURE")</c>; otherwise walks the feature list
    /// and picks the last user-meaningful feature (same boot filter as
    /// inspect_part / mirror_feature / pattern_linear). Returns the name
    /// actually selected.
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
                Mark: 4,
                Callout: null,
                SelectOption: 0))
            {
                throw new McpToolException(
                    $"Could not select feature '{featureName}' as pattern seed. " +
                    "Verify the name with inspect_part first.");
            }
            return featureName;
        }

        var lastUserFeature = Internal.PartGeometryHelpers.FindLastUserFeature(model);
        if (lastUserFeature == null)
        {
            throw new McpToolException(
                "Cannot auto-pick a seed feature: the part has no user-meaningful features. " +
                "Add a feature first (e.g. with add_axial_hole) or pass an explicit featureName.");
        }
        var seedName = lastUserFeature.Name ?? "(unnamed)";
        if (!((IEntity)lastUserFeature).Select2(Append: true, Mark: 4))
        {
            throw new McpToolException($"IEntity.Select2 failed on seed feature '{seedName}'.");
        }
        return seedName;
    }
#endif
}
