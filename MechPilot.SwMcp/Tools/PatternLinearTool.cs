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
/// Linear pattern (one- or two-direction) of a single seed feature.
///
/// Selection-mark layout (SW_API_REFERENCE §6):
///   • direction edge 1 → mark=1
///   • direction edge 2 → mark=2  (optional, only when CountDir2 > 1)
///   • seed feature      → mark=4
/// Then <c>FeatureLinearPattern2(num1, spacing1, num2, spacing2, flipDir1=false,
/// flipDir2=false, DName1="", DName2="", GeometryPattern=true)</c>.
///
/// Pipeline:
///   1. OpenDoc6 the input .sldprt (silent).
///   2. Find direction edge for axis 1 via body navigation (first straight
///      edge whose unit direction matches the requested ±axis). Select mark=1.
///   3. If axis 2 is set: same for direction edge 2, mark=2.
///   4. Select seed feature (by name or auto-pick last user feature), mark=4.
///   5. FeatureLinearPattern2.
///   6. Save: in-place → Save3; copy → Extension.SaveAs (M5 split).
///   7. CloseDoc (in finally).
/// </summary>
[McpServerToolType]
public static class PatternLinearTool
{
    [McpServerTool(Name = "pattern_linear")]
    [Description(
        "Linear pattern (one- or two-direction) of a single seed feature in " +
        "an existing SolidWorks part. Counts include the seed; spacings are " +
        "center-to-center in mm. direction1Axis / direction2Axis are 'x', 'y', " +
        "or 'z' (case-insensitive) — the tool finds the first straight edge on " +
        "the body matching that axis and uses it as the pattern direction. " +
        "featureName is optional: omit it to pattern the most recently added " +
        "user feature. outputPath is optional: empty = overwrite the input in place. " +
        "Common use: drill one Φ5 hole with add_axial_hole on a rectangular block, " +
        "then pattern_linear count1=3 spacing1=20 axis1=x to get a 3-hole row.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to edit, e.g. C:/tmp/block.sldprt.")]
        string inputPath,
        [Description("Direction-1 axis: 'x', 'y', or 'z' (case-insensitive).")]
        string direction1Axis,
        [Description("Total instances along direction 1, including seed. e.g. 3.")]
        int countDir1,
        [Description("Center-to-center spacing along direction 1 in mm, e.g. 20.")]
        double spacingDir1,
        [Description("Optional direction-2 axis (different from direction1Axis).")]
        string? direction2Axis = null,
        [Description("Total instances along direction 2 (with seed). Default 1.")]
        int countDir2 = 1,
        [Description("Spacing along direction 2 in mm; required when direction2Axis is set.")]
        double spacingDir2 = 0,
        [Description("Optional exact seed feature name; omit for last user feature.")]
        string? featureName = null,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new LinearPatternSpec
        {
            InputPath = inputPath,
            Direction1Axis = direction1Axis,
            CountDir1 = countDir1,
            SpacingDir1Mm = spacingDir1,
            Direction2Axis = direction2Axis,
            CountDir2 = countDir2,
            SpacingDir2Mm = spacingDir2,
            FeatureName = featureName,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(LinearPatternSpec spec)
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
                $"pattern_linear failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "pattern_linear requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult PatternInSw(LinearPatternSpec spec)
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
            var hasDir2 = !string.IsNullOrWhiteSpace(spec.Direction2Axis);

            // ── 2. Select direction edge 1 (mark=1) ─────────────────────────
            model.ClearSelection2(true);
            var dir1Edge = FindFirstStraightEdgeAlongAxis(model, spec.Direction1Axis)
                ?? throw new McpToolException(
                    $"Could not find a straight edge along the {spec.Direction1Axis} axis. " +
                    "pattern_linear needs at least one body edge aligned with each direction; " +
                    "use create_rectangular_block (which has straight edges in X/Y/Z) as the " +
                    "seed part, not create_cylinder / create_flange (round only).");
            if (!((IEntity)dir1Edge).Select4(Append: false, Data: null))
            {
                throw new McpToolException(
                    $"Failed to select direction edge for axis {spec.Direction1Axis}.");
            }
            // Re-mark to 1 (Select4 defaults mark to 0; SW needs explicit mark).
            ((IEntity)dir1Edge).Select2(Append: false, Mark: 1);

            // ── 3. Optional direction edge 2 (mark=2) ───────────────────────
            if (hasDir2)
            {
                var dir2Edge = FindFirstStraightEdgeAlongAxis(model, spec.Direction2Axis!)
                    ?? throw new McpToolException(
                        $"Could not find a straight edge along the {spec.Direction2Axis} axis.");
                if (!((IEntity)dir2Edge).Select2(Append: true, Mark: 2))
                {
                    throw new McpToolException(
                        $"Failed to select direction edge for axis {spec.Direction2Axis}.");
                }
            }

            // ── 4. Select seed feature (mark=4) ─────────────────────────────
            var seedName = SelectSeedFeature(model, ext, spec.FeatureName);

            // ── 5. FeatureLinearPattern2 (9 args; GeometryPattern=true is the
            //   robust default — pure geometry copy avoids parametric edge
            //   cases that bite multi-cut patterns) ───────────────────────────
            var spacing1M = spec.SpacingDir1Mm / 1000.0;
            var spacing2M = hasDir2 ? spec.SpacingDir2Mm / 1000.0 : 0.0;
            var patternFeature = fm.FeatureLinearPattern2(
                Num1: spec.CountDir1,
                Spacing1: spacing1M,
                Num2: hasDir2 ? spec.CountDir2 : 1,
                Spacing2: spacing2M,
                FlipDir1: false,
                FlipDir2: false,
                DName1: string.Empty,
                DName2: string.Empty,
                GeometryPattern: true);

            if (patternFeature == null)
            {
                var layout = hasDir2
                    ? $"{spec.CountDir1}×{spec.CountDir2} grid (spacing {spec.SpacingDir1Mm}×{spec.SpacingDir2Mm} mm)"
                    : $"{spec.CountDir1} instances (spacing {spec.SpacingDir1Mm} mm)";
                throw new McpToolException(
                    $"FeatureLinearPattern2 returned null for seed '{seedName}' / {layout}. " +
                    "Common causes: the pattern would extend outside the body, the chosen " +
                    "direction edge is in the wrong orientation, or the seed feature isn't " +
                    "pattern-able. Check the FeatureManager log in SW UI.");
            }

            // ── 6. Save (in-place vs copy) — same split as M5 ───────────────
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

            var summary = hasDir2
                ? $"Patterned '{seedName}' in a {spec.CountDir1}×{spec.CountDir2} grid " +
                  $"({spec.Direction1Axis}: {spec.SpacingDir1Mm} mm × {spec.Direction2Axis}: {spec.SpacingDir2Mm} mm)"
                : $"Patterned '{seedName}' {spec.CountDir1}× along {spec.Direction1Axis} " +
                  $"(spacing {spec.SpacingDir1Mm} mm)";
            return ToolResult.Ok(
                message: $"{summary}; saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Walks the first solid body's edges and returns the first straight
    /// edge whose unit direction is aligned with the requested axis
    /// (cos similarity > 0.99). Direction is computed from start/end vertex
    /// positions (more reliable than parsing get_LineParams' layout).
    /// </summary>
    private static IEdge? FindFirstStraightEdgeAlongAxis(IModelDoc2 model, string axis)
    {
        var part = (IPartDoc)model;
        var bodiesObj = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (bodiesObj is not object[] bodies || bodies.Length == 0)
        {
            return null;
        }

        var axisIndex = axis.ToLowerInvariant() switch
        {
            "x" => 0,
            "y" => 1,
            "z" => 2,
            _ => -1,
        };
        if (axisIndex < 0) return null;

        foreach (var bodyObj in bodies)
        {
            var body = (IBody2)bodyObj;
            if (body.GetEdges() is not object[] edges) continue;
            foreach (var edgeObj in edges)
            {
                var edge = (IEdge)edgeObj;
                var curve = edge.GetCurve() as ICurve;
                if (curve == null || !curve.IsLine()) continue;

                if (edge.GetStartVertex() is not IVertex startV ||
                    edge.GetEndVertex() is not IVertex endV)
                {
                    continue;   // unbounded edge (rare; skip safely)
                }
                if (startV.GetPoint() is not double[] startPt ||
                    endV.GetPoint() is not double[] endPt ||
                    startPt.Length < 3 || endPt.Length < 3)
                {
                    continue;
                }

                var dx = endPt[0] - startPt[0];
                var dy = endPt[1] - startPt[1];
                var dz = endPt[2] - startPt[2];
                var len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 1e-9) continue;

                var unit = new[] { dx / len, dy / len, dz / len };
                if (Math.Abs(unit[axisIndex]) > 0.99)
                {
                    return edge;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Selects the seed feature (mark=4, appended to direction edges).
    /// If <paramref name="featureName"/> is given, uses
    /// <c>SelectByID2("BODYFEATURE")</c>; otherwise walks the feature list
    /// and picks the last user-meaningful feature (same boot filter as
    /// inspect_part / mirror_feature). Returns the name actually selected.
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

        // Auto-pick via shared helper (Tools/Internal/PartGeometryHelpers).
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
