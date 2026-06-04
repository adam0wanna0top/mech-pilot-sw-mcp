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
/// Creates a parametric solid sphere — the M23 hemisphere sibling but
/// revolved over a half-disc instead of a quarter-disc. Same sketch+revolve
/// framework, same FeatureRevolve2 20-arg call; only the sketch profile
/// shape changes (one diameter line + one half-circle 3-point arc replaces
/// the M23 1 line + 1 arc + 1 line trio).
///
/// Unlocks: 球阀芯 / 滚珠 / 球形支撑 / 装饰球 / 玻璃珠 / 圆球把手 /
/// 球关节 (paired with a future cylindrical socket).
///
/// Sketch layout (Front Plane = XY, half-disc profile + Y-axis centerline):
///   Line:                (0, -R, 0) → (0, R, 0)         diameter line (sketch Y)
///   Create3PointArc:     start (0, R, 0), end (0, -R, 0), middle (R, 0, 0)
///                                                          half-circle, +X side
///   CenterLine:          (0, -2R, 0) → (0, 2R, 0)         revolve axis (sketch Y)
///
/// We use <c>Create3PointArc</c> (start + end + middle) instead of
/// <c>CreateArc</c> (center + start + end + direction) — both endpoints
/// sit on the Y axis at (0, ±R), so the standard direction-1-CCW arc has
/// 180° ambiguity. Specifying a third point on the curve resolves it
/// unambiguously.
///
/// Pipeline mirrors CreateHemisphereTool exactly, only steps 5a / 5b change.
///
/// Bounding box: D × D × D (X / Y / Z all ∈ [−R, R]). Note Y extent = D
/// vs. hemisphere's Y = D/2 — this is the inspect-level cue distinguishing
/// "sphere" from "hemisphere".
/// </summary>
[McpServerToolType]
public static class CreateSphereTool
{
    [McpServerTool(Name = "create_sphere")]
    [Description(
        "Create a parametric SOLID sphere and save it to disk. diameter is " +
        "in mm — the sphere is centered at the origin with X / Y / Z all in " +
        "[−D/2, D/2]. savePath must be an absolute path ending in .sldprt; " +
        "the parent directory must already exist. Common use cases: 球阀芯 / " +
        "滚珠 / 球形支撑 / 装饰球 / 玻璃珠 / 圆球把手. For a hemisphere " +
        "(half a sphere), use create_hemisphere instead — its bounding box " +
        "Y extent is D/2 vs. sphere's Y = D.")]
    public static ToolResult Run(
        [Description("Sphere diameter in millimeters, e.g. 40 for a D40 sphere.")]
        double diameter,
        [Description("Absolute output path with .sldprt extension, e.g. C:/tmp/sphere.sldprt.")]
        string savePath)
    {
        var spec = new SphereSpec
        {
            DiameterMm = diameter,
            SavePath = savePath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(SphereSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return CreateSphereInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"create_sphere failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "create_sphere requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static readonly string[] FrontPlaneAliases = { "前视基准面", "Front Plane" };
    private static readonly string[] Sketch1Aliases = { "草图1", "Sketch1" };

    private static ToolResult CreateSphereInSw(SphereSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Default part template ────────────────────────────────────────
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

        // ── 5. Draw the profile (half-disc in XY) + centerline.
        //   SW units are meters — convert mm → m at the boundary.
        var radiusM = spec.DiameterMm / 2000.0;

        // 5a. Diameter line: (0, -R) → (0, R) along sketch Y. This IS the
        //   side of the profile that touches the revolve axis (zero radius
        //   at every point, degenerate edge at revolve).
        var diameterLine = skMgr.CreateLine(0.0, -radiusM, 0.0, 0.0, radiusM, 0.0)
            ?? throw new McpToolException("CreateLine (diameter) returned null.");
        _ = diameterLine;

        // 5b. Half-circle arc via Create3PointArc — start + end + middle.
        //   start (0, +R), end (0, -R), middle (+R, 0): a semicircle on the
        //   +X side. We pick Create3PointArc instead of CreateArc because
        //   both endpoints sit on the Y axis, making the standard
        //   direction-CCW arc 180°-ambiguous; a third point on the curve
        //   resolves it unambiguously.
        var arc = skMgr.Create3PointArc(
            X1: 0.0, Y1: radiusM, Z1: 0.0,           // start
            X2: 0.0, Y2: -radiusM, Z2: 0.0,          // end
            X3: radiusM, Y3: 0.0, Z3: 0.0)           // middle (+X side)
            ?? throw new McpToolException(
                "Create3PointArc returned null. The three points must define a " +
                "valid (non-degenerate, non-collinear) arc.");
        _ = arc;

        // 5c. Centerline along Y. SW only uses its direction, not endpoints;
        //   made longer than the profile for safety. SW auto-binds the
        //   embedded centerline as the revolve axis when the sketch is
        //   selected with mark=0 (SW_API_REFERENCE §6).
        var twoR = 2.0 * radiusM;
        var centerline = skMgr.CreateCenterLine(0.0, -twoR, 0.0, 0.0, twoR, 0.0)
            ?? throw new McpToolException("CreateCenterLine returned null.");
        _ = centerline;

        // ── 6. Exit sketch (InsertSketch is a toggle) ───────────────────────
        skMgr.InsertSketch(true);

        // ── 7. Select Sketch1 by name for the revolve. mark=0 per §6. ───────
        model.ClearSelection2(true);
        if (!SelectFirstMatch(ext, Sketch1Aliases, "SKETCH", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select sketch after creation. Tried: {string.Join(" / ", Sketch1Aliases)}.");
        }

        // ── 8. FeatureRevolve2 — 20 args, identical to create_hemisphere.
        //   Same call, different profile shape produces a full sphere instead
        //   of a hemisphere.
        var fm = model.FeatureManager;
        var feature = fm.FeatureRevolve2(
            SingleDir: true,
            IsSolid: true,
            IsThin: false,
            IsCut: false,
            ReverseDir: false,
            BothDirectionUpToSameEntity: false,
            Dir1Type: (int)swEndConditions_e.swEndCondBlind,
            Dir2Type: (int)swEndConditions_e.swEndCondBlind,
            Dir1Angle: 2.0 * Math.PI,
            Dir2Angle: 0.0,
            OffsetReverse1: false,
            OffsetReverse2: false,
            OffsetDistance1: 0.0,
            OffsetDistance2: 0.0,
            ThinType: 0,
            ThinThickness1: 0.0,
            ThinThickness2: 0.0,
            Merge: true,
            UseFeatScope: true,
            UseAutoSelect: true);

        if (feature == null)
        {
            throw new McpToolException(
                "FeatureRevolve2 returned null. Common causes: the sketch profile " +
                "is open / self-intersecting / does not touch the centerline, or " +
                "the centerline was not embedded in the same sketch. (SW typically " +
                "reports the underlying reason in the FeatureManager log.)");
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
                $"warnings=0x{saveWarnings:X}.");
        }

        // ── 10. Close to free resources ─────────────────────────────────────
        swApp.CloseDoc(model.GetTitle());

        return ToolResult.Ok(
            message: $"Created solid sphere D{spec.DiameterMm} mm (centered at origin)",
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
