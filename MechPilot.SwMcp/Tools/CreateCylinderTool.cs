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
/// Creates a parametric cylinder part: Front-Plane sketch with a single circle,
/// extruded blind to the requested length, saved as .sldprt.
///
/// Single sketch + single extrude — the simplest possible SW Interop happy path,
/// verifies the early-binding wiring works before we move on to multi-feature
/// parts (M3 flange) and the SW limitations documented in docs/v1-history.md.
/// </summary>
[McpServerToolType]
public static class CreateCylinderTool
{
    [McpServerTool(Name = "create_cylinder")]
    [Description(
        "Create a parametric cylinder part in SolidWorks and save it to disk. " +
        "Diameter and length are in millimeters. savePath must be an absolute " +
        "path ending in .sldprt; the parent directory must already exist.")]
    public static ToolResult Run(
        [Description("Outer diameter in millimeters (e.g. 30 for a 30 mm cylinder).")]
        double diameter,
        [Description("Extrusion length in millimeters (e.g. 50 for a 50 mm long cylinder).")]
        double length,
        [Description("Absolute output path with .sldprt extension, e.g. C:/tmp/cyl.sldprt.")]
        string savePath)
    {
        var spec = new CylinderSpec
        {
            DiameterMm = diameter,
            LengthMm = length,
            SavePath = savePath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(CylinderSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return CreateCylinderInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"create_cylinder failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "create_cylinder requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    // Front-plane name in Chinese SW UI vs. English. SelectByID2 is literal string
    // match — must try both. See docs/SW_API_REFERENCE.md §7.
    private static readonly string[] FrontPlaneAliases = { "前视基准面", "Front Plane" };
    private static readonly string[] Sketch1Aliases = { "草图1", "Sketch1" };

    private static ToolResult CreateCylinderInSw(CylinderSpec spec)
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

        // ── 3. Select Front Plane (try CN first since SW UI is set to 中文) ──
        var ext = model.Extension;
        if (!SelectFirstMatch(ext, FrontPlaneAliases, "PLANE", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select Front Plane. Tried: {string.Join(" / ", FrontPlaneAliases)}.");
        }

        // ── 4. Enter sketch mode ────────────────────────────────────────────
        var skMgr = model.SketchManager;
        skMgr.InsertSketch(true);

        // ── 5. Draw the circle. SW units are meters — convert mm → m. ───────
        var radiusM = spec.DiameterMm / 2000.0;
        var circle = skMgr.CreateCircleByRadius(0.0, 0.0, 0.0, radiusM) as ISketchSegment
            ?? throw new McpToolException(
                $"CreateCircleByRadius returned null for radius {radiusM} m.");

        // ── 5b. Driving Ø dimension (M49) — so resize orchestration can change
        //   the DIAMETER via modify_feature, not just the extrude length. ─────
        Internal.SketchDimensioner.AddDiameter(model, circle, 0.0, 0.0, spec.DiameterMm / 2.0);

        // ── 6. Exit sketch (InsertSketch is a toggle) ───────────────────────
        skMgr.InsertSketch(true);

        // ── 7. Select the just-created sketch by name for the extrude ───────
        model.ClearSelection2(true);
        if (!SelectFirstMatch(ext, Sketch1Aliases, "SKETCH", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select sketch after creation. Tried: {string.Join(" / ", Sketch1Aliases)}.");
        }

        // ── 8. Blind extrude to the requested depth (mm → m) ────────────────
        var depthM = spec.LengthMm / 1000.0;
        var fm = model.FeatureManager;
        var feature = fm.FeatureExtrusion3(
            Sd: true,                                                   // single-direction
            Flip: false,
            Dir: false,
            T1: (int)swEndConditions_e.swEndCondBlind,                  // = 0
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
            T0: (int)swStartConditions_e.swStartSketchPlane,            // = 0
            StartOffset: 0.0,
            FlipStartOffset: false);

        if (feature == null)
        {
            throw new McpToolException(
                "FeatureExtrusion3 returned null. The sketch may be open, self-intersecting, " +
                "or zero-area. (SW typically reports the underlying reason in the FeatureManager log.)");
        }

        // ── 9. Save as .sldprt ──────────────────────────────────────────────
        int saveErrors = 0;
        int saveWarnings = 0;
        var savedOk = ext.SaveAs(
            Name: spec.SavePath,
            Version: (int)swSaveAsVersion_e.swSaveAsCurrentVersion,     // = 0
            Options: (int)swSaveAsOptions_e.swSaveAsOptions_Silent,     // suppress dialogs
            ExportData: null,
            Errors: ref saveErrors,
            Warnings: ref saveWarnings);

        if (!savedOk || !File.Exists(spec.SavePath))
        {
            throw new McpToolException(
                $"SaveAs failed for '{spec.SavePath}'. errors=0x{saveErrors:X} " +
                $"warnings=0x{saveWarnings:X}. (See swFileSaveError_e in swconst.chm.)");
        }

        // ── 10. Close to free resources (file is on disk; SW process stays alive) ──
        swApp.CloseDoc(model.GetTitle());

        return ToolResult.Ok(
            message: $"Created cylinder D{spec.DiameterMm} mm × L{spec.LengthMm} mm",
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
