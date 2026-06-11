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
/// Creates a parametric rectangular block (cuboid) part: Front-Plane sketch
/// with a single centered rectangle, extruded blind along +Z to the requested
/// height, saved as .sldprt.
///
/// Sibling of <see cref="CreateCylinderTool"/> — same pipeline, only the
/// sketch primitive changes (CreateCenterRectangle instead of
/// CreateCircleByRadius). Adds a rectangular base shape that future
/// pattern_linear can use (cylinders/flanges have no straight edges).
/// </summary>
[McpServerToolType]
public static class CreateRectangularBlockTool
{
    [McpServerTool(Name = "create_rectangular_block")]
    [Description(
        "Create a parametric rectangular block (cuboid) part in SolidWorks and " +
        "save it to disk. Length / width / height are in millimeters, mapping " +
        "to the block's X / Y / Z extents respectively (Z = extrusion depth). " +
        "The block is centered at the origin on the Front Plane. savePath must " +
        "be an absolute path ending in .sldprt; the parent directory must " +
        "already exist.")]
    public static ToolResult Run(
        [Description("Block length (X extent) in mm, e.g. 100.")]
        double length,
        [Description("Block width (Y extent) in mm, e.g. 50.")]
        double width,
        [Description("Block height (Z extrusion depth) in mm, e.g. 20.")]
        double height,
        [Description("Absolute output path with .sldprt extension, e.g. C:/tmp/block.sldprt.")]
        string savePath)
    {
        var spec = new RectangularBlockSpec
        {
            LengthMm = length,
            WidthMm = width,
            HeightMm = height,
            SavePath = savePath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(RectangularBlockSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return CreateBlockInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"create_rectangular_block failed at SW Interop layer: " +
                $"{ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "create_rectangular_block requires SolidWorks Interop assemblies, " +
            "which were not present at build time. Build on a machine with " +
            "SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    // Same CN/EN dual-name dance as CreateCylinderTool.
    private static readonly string[] FrontPlaneAliases = { "前视基准面", "Front Plane" };
    private static readonly string[] Sketch1Aliases = { "草图1", "Sketch1" };

    private static ToolResult CreateBlockInSw(RectangularBlockSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Locate the default part template ─────────────────────────────
        var template = swApp.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
        {
            throw new McpToolException(
                $"Default part template not found (resolved to '{template}'). " +
                "Open SW once and set Tools → Options → Default Templates → Part.");
        }

        // ── 2. New part document ────────────────────────────────────────────
        var model = swApp.NewDocument(template, 0, 0.0, 0.0) as IModelDoc2
            ?? throw new McpToolException(
                $"swApp.NewDocument returned null for template '{template}'.");

        // ── 3. Select Front Plane ───────────────────────────────────────────
        var ext = model.Extension;
        if (!SelectFirstMatch(ext, FrontPlaneAliases, "PLANE", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select Front Plane. Tried: {string.Join(" / ", FrontPlaneAliases)}.");
        }

        // ── 4. Enter sketch mode ────────────────────────────────────────────
        var skMgr = model.SketchManager;
        skMgr.InsertSketch(true);

        // ── 5. Centered rectangle — CreateCenterRectangle takes (center, corner) ──
        //   Center at origin (0,0,0); corner at (L/2, W/2, 0). SW units = meters,
        //   so divide mm by 2000 to get half-extent in meters.
        var halfLengthM = spec.LengthMm / 2000.0;
        var halfWidthM = spec.WidthMm / 2000.0;
        var rect = skMgr.CreateCenterRectangle(
            X1: 0.0, Y1: 0.0, Z1: 0.0,
            X2: halfLengthM, Y2: halfWidthM, Z2: 0.0)
            ?? throw new McpToolException(
                $"CreateCenterRectangle returned null for L={spec.LengthMm} W={spec.WidthMm} mm.");

        // ── 5b. Driving L + W dimensions (M49) — so resize orchestration can
        //   change the footprint via modify_feature, not just the height. ─────
        Internal.SketchDimensioner.AddRectangle(
            model, rect, 0.0, 0.0, spec.LengthMm, spec.WidthMm);

        // ── 6. Exit sketch ──────────────────────────────────────────────────
        skMgr.InsertSketch(true);

        // ── 7. Re-select sketch by name for the extrude ─────────────────────
        model.ClearSelection2(true);
        if (!SelectFirstMatch(ext, Sketch1Aliases, "SKETCH", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select sketch after creation. Tried: {string.Join(" / ", Sketch1Aliases)}.");
        }

        // ── 8. Blind extrude to the requested height (mm → m) ───────────────
        var depthM = spec.HeightMm / 1000.0;
        var fm = model.FeatureManager;
        var feature = fm.FeatureExtrusion3(
            Sd: true,
            Flip: false,
            Dir: false,
            T1: (int)swEndConditions_e.swEndCondBlind,
            T2: (int)swEndConditions_e.swEndCondBlind,
            D1: depthM,
            D2: 0.0,
            Dchk1: false, Dchk2: false,
            Ddir1: false, Ddir2: false,
            Dang1: 0.0, Dang2: 0.0,
            OffsetReverse1: false, OffsetReverse2: false,
            TranslateSurface1: false, TranslateSurface2: false,
            Merge: true,
            UseFeatScope: true,
            UseAutoSelect: true,
            T0: (int)swStartConditions_e.swStartSketchPlane,
            StartOffset: 0.0,
            FlipStartOffset: false);

        if (feature == null)
        {
            throw new McpToolException(
                "FeatureExtrusion3 returned null. The sketch may be invalid " +
                "(self-intersecting / zero-area). Check the FeatureManager log in SW UI.");
        }

        // ── 9. Save as .sldprt ──────────────────────────────────────────────
        int saveErrors = 0;
        int saveWarnings = 0;
        var savedOk = ext.SaveAs(
            Name: spec.SavePath,
            Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
            Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            ExportData: null,
            Errors: ref saveErrors,
            Warnings: ref saveWarnings);

        if (!savedOk || !File.Exists(spec.SavePath))
        {
            throw new McpToolException(
                $"SaveAs failed for '{spec.SavePath}'. errors=0x{saveErrors:X} " +
                $"warnings=0x{saveWarnings:X}. (See swFileSaveError_e in swconst.chm.)");
        }

        // ── 10. Close to free resources ────────────────────────────────────
        swApp.CloseDoc(model.GetTitle());

        return ToolResult.Ok(
            message:
                $"Created rectangular block {spec.LengthMm}×{spec.WidthMm}×{spec.HeightMm} mm",
            path: spec.SavePath);
    }

    private static bool SelectFirstMatch(
        IModelDocExtension ext,
        IReadOnlyList<string> aliases,
        string swSelectionType,
        int mark)
    {
        foreach (var alias in aliases)
        {
            if (ext.SelectByID2(
                Name: alias,
                Type: swSelectionType,
                X: 0.0, Y: 0.0, Z: 0.0,
                Append: false,
                Mark: mark,
                Callout: null,
                SelectOption: 0))
            {
                return true;
            }
        }
        return false;
    }
#endif
}
