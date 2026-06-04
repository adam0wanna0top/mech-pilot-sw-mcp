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
/// Shells an existing solid part — hollows it out leaving a uniform wall
/// thickness, opening the +Z end face so the result is a cup-like container.
/// M26 — first SW subtractive operation that produces LLM-irreplaceable
/// geometry (a shell can't be approximated by composing primitives, unlike
/// e.g. "two cylinders to make a tube" which is sort-of equivalent).
///
/// Unlocks: 电机壳 / 泵壳 / 减速箱外壳 / 杯具 / 罐体 / 接线盒 / IP6X 防护壳.
///
/// v1 history correction: v1's SW_API_REFERENCE §5 said "FeatureShell 全系列
/// 在 SW 2026 上完全不存在 — 只能走 swApp.RunCommand macro". M26 reflection
/// found <c>IModelDoc2.InsertFeatureShell(Thickness, Outward)</c> exists —
/// v1 looked on <c>IFeatureManager</c> (wrong type). This PR also fixes the
/// v1 knowledge-base entry (SW_API_REFERENCE §5 + 下一步候选 #11).
///
/// SW API: <c>IModelDoc2.InsertFeatureShell(Thickness, Outward)</c> returns
/// void — no success/failure signal. M22 收尾 established the "geometry
/// verification" pattern for tools with silent-fail risk: L2 calls
/// inspect-part to confirm featureCount increased and a Shell-type feature
/// exists.
///
/// Pipeline:
///   1. OpenDoc6 the input .sldprt (silent).
///   2. PartGeometryHelpers.FindPlanarEndFace → +Z planar end face (the one
///      that will be "removed" to open the shell).
///   3. IEntity.Select4 on that face (mark=0 default).
///   4. model.InsertFeatureShell(thicknessM, outward). NOTE: returns void —
///      no feature handle to null-check. Detect failure via the post-op
///      feature walk (step 5).
///   5. Verify a Shell-type feature was added by walking the feature list
///      (defense against InsertFeatureShell silently producing nothing).
///   6. Save: in-place → Save3; copy → Extension.SaveAs (M5 split).
///   7. CloseDoc (in finally).
/// </summary>
[McpServerToolType]
public static class AddShellTool
{
    [McpServerTool(Name = "add_shell")]
    [Description(
        "Shell an existing solid part — hollow it out leaving a uniform wall " +
        "of the given thickness in mm, with the +Z end face removed to form " +
        "an open cup. Works on cylinder / block / frustum (axis-Z extruded " +
        "parts); hemispheres (axis +Y) are not directly supported in this " +
        "MVP. outward=false (default) shells inward (outer geometry stays " +
        "the same, interior is hollowed). outward=true thickens outward " +
        "(less common). outputPath is optional: empty = overwrite the input " +
        "in place. Common LLM use: '把 D40 圆柱抽壳 2mm' → cup with 2 mm " +
        "walls. Unlocks 电机壳 / 减速箱外壳 / 杯具 / 罐体 / 接线盒. For " +
        "closed (no-opening) shell or shells opening on a different face, " +
        "a future PR will add a faceSelector field.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to shell, e.g. C:/tmp/cyl.sldprt.")]
        string inputPath,
        [Description("Wall thickness in mm, e.g. 2 for a 2 mm wall.")]
        double thickness,
        [Description("If true, thicken outward (less common). Default false = hollow inward.")]
        bool outward = false,
        [Description("Optional absolute .sldprt output path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new ShellSpec
        {
            InputPath = inputPath,
            ThicknessMm = thickness,
            Outward = outward,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(ShellSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return ShellInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_shell failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_shell requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult ShellInSw(ShellSpec spec)
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

            // ── 2. Find the +Z planar end face (the "removed face" that
            //   opens the shell). Reuses M14-extracted PartGeometryHelpers
            //   (also used by create_flange / add_axial_hole / add_threaded_hole
            //   / add_counterbore / add_countersink). ─────────────────────
            var openFace = Internal.PartGeometryHelpers.FindPlanarEndFace(model)
                ?? throw new McpToolException(
                    "Could not find a +Z planar end face to open the shell on. " +
                    "add_shell needs the part to have at least one planar face with " +
                    "normal along ±Z (cylinder / rectangular block / frustum / " +
                    "any axially-symmetric extruded part qualify). For hemispheres " +
                    "(axis +Y), this MVP does not auto-find the opening face — a " +
                    "future PR will add a faceSelector field.");

            // ── 3. Select the open face (mark=0 default) ─────────────────────
            model.ClearSelection2(true);
            if (!((IEntity)openFace).Select4(Append: false, Data: null))
            {
                throw new McpToolException(
                    "Failed to select the +Z planar end face as the shell's open face.");
            }

            // ── 4. InsertFeatureShell.
            //   IModelDoc2.InsertFeatureShell(Thickness, Outward) — returns void.
            //   v1 SW_API_REFERENCE said this didn't exist; reflection on SW 2026
            //   SP02.1 confirms it does (v1 looked on IFeatureManager — wrong type).
            //   Convert mm → m at the boundary.
            var thicknessM = spec.ThicknessMm / 1000.0;
            model.InsertFeatureShell(thicknessM, spec.Outward);

            // ── 5. Verify a Shell-type feature was added (silent-fail defense
            //   per M22 收尾 pattern — InsertFeatureShell returns void with no
            //   error signal). ────────────────────────────────────────────────
            if (!HasShellFeature(model))
            {
                throw new McpToolException(
                    $"InsertFeatureShell completed but no Shell feature appeared " +
                    $"in the feature tree (thickness={spec.ThicknessMm} mm, " +
                    $"outward={spec.Outward}). Common causes: the selected face " +
                    "is not a valid open face for shelling (e.g. it's tangent to " +
                    "another face), the thickness exceeds geometry minimums (try " +
                    "a thinner wall), or the part is not a single solid body. " +
                    "Check the FeatureManager log in SW UI.");
            }

            // ── 6. Save (in-place vs copy) — M5 split ──────────────────────
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

            var direction = spec.Outward ? "outward" : "inward";
            return ToolResult.Ok(
                message: $"Shelled part with {spec.ThicknessMm} mm wall ({direction}); " +
                         $"opened +Z end face; saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Walks the feature linked list looking for a Shell-type feature.
    /// SW assigns typeName "Shell" to features created by InsertFeatureShell.
    /// </summary>
    private static bool HasShellFeature(IModelDoc2 model)
    {
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            var typeName = feature.GetTypeName2() ?? feature.GetTypeName() ?? string.Empty;
            if (string.Equals(typeName, "Shell", StringComparison.Ordinal))
            {
                return true;
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return false;
    }
#endif
}
