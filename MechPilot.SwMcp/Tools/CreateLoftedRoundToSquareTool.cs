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
/// Creates a parametric round-to-square lofted transition part. M28 —
/// **first multi-plane sketch tool** in the project. All prior modeling
/// tools (cylinder / flange / block / hemisphere / sphere / frustum) used
/// at most one sketch on the Front Plane; this one needs two sketches on
/// two different planes (Front Plane + a Z-offset reference plane) plus
/// <c>InsertProtrusionBlend</c> to blend them. v1 PR #27 sweep+loft
/// experience: SW's loft API is <c>InsertProtrusionBlend</c> (17 args),
/// all profiles selected with mark=1 in order.
///
/// Sketch / plane layout:
///   Sketch 1 (Front Plane, world XY at Z=0):
///     CreateCircleByRadius(0, 0, 0, D_bottom/2)        ← bottom circle
///   Reference Plane 1: InsertRefPlane(Distance=8, H_m, ...)
///                                                       ← Z-offset plane at Z=H
///   Sketch 2 (RefPlane1, world XY at Z=H):
///     CreateCenterRectangle(-L/2, -W/2, 0, L/2, W/2, 0) ← top rectangle
///   Then:
///     ClearSelection2
///     SelectByID2(Sketch1, "SKETCH", mark=1, append=false)
///     SelectByID2(Sketch2, "SKETCH", mark=1, append=true)
///     InsertProtrusionBlend(17 args, educated defaults)
///
/// Selection-mark layout (v1 PR #27 sweep+loft):
///   • Profile sketches all → mark=1 (order matters: bottom first then top)
///
/// Pipeline:
///   1. Resolve default part template + NewDocument.
///   2. Select Front Plane (CN/EN aliases — same as cylinder).
///   3. InsertSketch → CreateCircleByRadius → InsertSketch off  (Sketch1)
///   4. Select Front Plane → InsertRefPlane(Distance, height_m, …)  (Plane1)
///   5. ClearSelection2 → Select RefPlane1 by name
///   6. InsertSketch → CreateCenterRectangle → InsertSketch off  (Sketch2)
///   7. ClearSelection2 → Select Sketch1 (mark=1, append=false)
///                     → Select Sketch2 (mark=1, append=true)
///   8. InsertProtrusionBlend(17 args, all educated defaults — Closed=false,
///      KeepTangency=false, Merge=true, UseFeatScope=true, UseAutoSelect=true,
///      all other position fields zero/false).
///   9. SaveAs, CloseDoc.
///
/// InsertProtrusionBlend educated defaults (v1 PR #27 verified):
///   • Closed=false              — open loft (not a closed loop)
///   • KeepTangency=false        — only 2 profiles, no tangent constraints
///   • ForceNonRational=false    — standard
///   • TessToleranceFactor=0     — default
///   • Start/EndMatchingType=0   — no profile-end matching
///   • Start/EndTangentLength=1  — default tangent magnitude
///   • Start/EndTangentDir=false — default direction
///   • IsThinBody=false          — solid (not shell)
///   • Thickness1/2=0, ThinType=0— irrelevant for solid
///   • Merge=true, UseFeatScope=true, UseAutoSelect=true — standard solid
/// </summary>
[McpServerToolType]
public static class CreateLoftedRoundToSquareTool
{
    [McpServerTool(Name = "create_lofted_round_to_square")]
    [Description(
        "Create a parametric SOLID lofted transition with a round bottom " +
        "(circle, diameter bottomDiameter) and a rectangular top " +
        "(topLength × topWidth), connected by an InsertProtrusionBlend " +
        "loft body of height H. Bottom face sits at Z=0 in the Front Plane; " +
        "top face sits at Z=height on an auto-created offset reference plane. " +
        "savePath must be an absolute path ending in .sldprt; the parent " +
        "directory must already exist. Common use cases: HVAC 风管转接 / " +
        "空调出风口 / 漏斗式集料口 / 喇叭口转方形出料 / 圆烟囱接方形排烟道 / " +
        "圆形进风口转矩形机箱. For round-to-round transitions, use " +
        "create_frustum (single revolve, no loft needed).")]
    public static ToolResult Run(
        [Description("Bottom-face circle diameter in millimeters, e.g. 60 for D60 bottom.")]
        double bottomDiameter,
        [Description("Top-face rectangle length (X extent) in millimeters, e.g. 40.")]
        double topLength,
        [Description("Top-face rectangle width (Y extent) in millimeters, e.g. 40.")]
        double topWidth,
        [Description("Loft height (Z direction) in millimeters, e.g. 30.")]
        double height,
        [Description("Absolute output path with .sldprt extension, e.g. C:/tmp/transition.sldprt.")]
        string savePath)
    {
        var spec = new LoftedRoundToSquareSpec
        {
            BottomDiameterMm = bottomDiameter,
            TopLengthMm = topLength,
            TopWidthMm = topWidth,
            HeightMm = height,
            SavePath = savePath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(LoftedRoundToSquareSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return CreateLoftInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"create_lofted_round_to_square failed at SW Interop layer: " +
                $"{ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "create_lofted_round_to_square requires SolidWorks Interop assemblies, " +
            "which were not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static readonly string[] FrontPlaneAliases = { "前视基准面", "Front Plane" };
    private static readonly string[] Sketch1Aliases = { "草图1", "Sketch1" };
    private static readonly string[] Sketch2Aliases = { "草图2", "Sketch2" };
    private static readonly string[] RefPlane1Aliases = { "基准面1", "Plane1" };

    // swRefPlaneReferenceConstraints_e (reflected — bitflag enum):
    //   Parallel=1, Perpendicular=2, Coincident=4, Distance=8, Angle=16, ...
    private const int RefPlaneDistanceConstraint = 8;

    private static ToolResult CreateLoftInSw(LoftedRoundToSquareSpec spec)
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

        var ext = model.Extension;
        var skMgr = model.SketchManager;
        var fm = model.FeatureManager;

        // SI units conversion mm → m at the boundary.
        var bottomRadiusM = spec.BottomDiameterMm / 2000.0;
        var halfLengthM = spec.TopLengthMm / 2000.0;
        var halfWidthM = spec.TopWidthMm / 2000.0;
        var heightM = spec.HeightMm / 1000.0;

        // ── 3. Sketch 1: bottom circle on the Front Plane ───────────────────
        if (!SelectFirstMatch(ext, FrontPlaneAliases, "PLANE", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select Front Plane. Tried: {string.Join(" / ", FrontPlaneAliases)}.");
        }
        skMgr.InsertSketch(true);
        var bottomCircle = skMgr.CreateCircleByRadius(0.0, 0.0, 0.0, bottomRadiusM)
            ?? throw new McpToolException(
                $"CreateCircleByRadius returned null for bottom radius {bottomRadiusM} m.");
        _ = bottomCircle;
        skMgr.InsertSketch(true);   // toggle off — Sketch1 saved

        // ── 4. Offset reference plane at Z = +height ────────────────────────
        //   InsertRefPlane needs an active selection as the source plane.
        //   Select Front Plane again; SW creates "基准面1" / "Plane1" at the
        //   requested Distance constraint offset.
        model.ClearSelection2(true);
        if (!SelectFirstMatch(ext, FrontPlaneAliases, "PLANE", mark: 0))
        {
            throw new McpToolException(
                "Cannot re-select Front Plane for offset-plane reference.");
        }
        var refPlane = fm.InsertRefPlane(
            FirstConstraint: RefPlaneDistanceConstraint,
            FirstConstraintAngleOrDistance: heightM,
            SecondConstraint: 0,
            SecondConstraintAngleOrDistance: 0.0,
            ThirdConstraint: 0,
            ThirdConstraintAngleOrDistance: 0.0);
        if (refPlane == null)
        {
            throw new McpToolException(
                $"InsertRefPlane returned null for distance {spec.HeightMm} mm. " +
                "SW may have rejected the Distance constraint — ensure the Front " +
                "Plane was selected immediately before this call.");
        }

        // ── 5. Sketch 2: top rectangle on RefPlane1 ─────────────────────────
        model.ClearSelection2(true);
        if (!SelectFirstMatch(ext, RefPlane1Aliases, "PLANE", mark: 0))
        {
            throw new McpToolException(
                $"Cannot select the newly-created offset plane. Tried: " +
                $"{string.Join(" / ", RefPlane1Aliases)}.");
        }
        skMgr.InsertSketch(true);
        // CreateCenterRectangle(x1, y1, z1, x2, y2, z2) — center + one corner.
        // Center at origin in sketch coords (which is on RefPlane1, so world
        // (0, 0, H)); corner at (+L/2, +W/2) → centered L × W rectangle.
        var topRect = skMgr.CreateCenterRectangle(
            0.0, 0.0, 0.0,
            halfLengthM, halfWidthM, 0.0)
            ?? throw new McpToolException(
                "CreateCenterRectangle returned null for top rectangle " +
                $"{spec.TopLengthMm} × {spec.TopWidthMm} mm.");
        _ = topRect;
        skMgr.InsertSketch(true);   // toggle off — Sketch2 saved

        // ── 6. Select both sketches as loft profiles (mark=1, in order) ─────
        //   v1 PR #27 sweep+loft empirically: all profiles selected with
        //   mark=1; the selection order is the loft "stack" order.
        model.ClearSelection2(true);
        if (!SelectFirstMatch(ext, Sketch1Aliases, "SKETCH", mark: 1))
        {
            throw new McpToolException(
                $"Cannot select Sketch1 as loft profile 1. Tried: " +
                $"{string.Join(" / ", Sketch1Aliases)}.");
        }
        if (!SelectAppend(ext, Sketch2Aliases, "SKETCH", mark: 1))
        {
            throw new McpToolException(
                $"Cannot select Sketch2 as loft profile 2. Tried: " +
                $"{string.Join(" / ", Sketch2Aliases)}.");
        }

        // ── 7. InsertProtrusionBlend — 17 args, all educated defaults ───────
        var feature = fm.InsertProtrusionBlend(
            Closed: false,                  // open loft, not a closed loop
            KeepTangency: false,            // no tangent constraints (only 2 profiles)
            ForceNonRational: false,
            TessToleranceFactor: 0.0,
            StartMatchingType: 0,           // no profile-end matching
            EndMatchingType: 0,
            StartTangentLength: 1.0,        // default magnitude
            EndTangentLength: 1.0,
            StartTangentDir: false,
            EndTangentDir: false,
            IsThinBody: false,              // solid, not shell
            Thickness1: 0.0,
            Thickness2: 0.0,
            ThinType: 0,
            Merge: true,                    // standard
            UseFeatScope: true,
            UseAutoSelect: true);

        if (feature == null)
        {
            throw new McpToolException(
                "InsertProtrusionBlend returned null. Common causes: the two " +
                "sketches were selected in wrong order, one of the sketches is " +
                "open / self-intersecting / zero-area, or the offset reference " +
                "plane was not created correctly. Check the FeatureManager log " +
                "in SW UI.");
        }

        // ── 8. Save as .sldprt ──────────────────────────────────────────────
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

        // ── 9. Close to free resources ──────────────────────────────────────
        swApp.CloseDoc(model.GetTitle());

        return ToolResult.Ok(
            message:
                $"Created lofted round-to-square transition: " +
                $"bottom D{spec.BottomDiameterMm} mm → " +
                $"top {spec.TopLengthMm} × {spec.TopWidthMm} mm, " +
                $"height {spec.HeightMm} mm",
            path: spec.SavePath);
    }

    private static bool SelectFirstMatch(
        IModelDocExtension ext,
        IReadOnlyList<string> aliases,
        string swSelectionType,
        int mark) =>
        aliases.Any(a => ext.SelectByID2(
            Name: a, Type: swSelectionType,
            X: 0.0, Y: 0.0, Z: 0.0,
            Append: false, Mark: mark,
            Callout: null, SelectOption: 0));

    private static bool SelectAppend(
        IModelDocExtension ext,
        IReadOnlyList<string> aliases,
        string swSelectionType,
        int mark) =>
        aliases.Any(a => ext.SelectByID2(
            Name: a, Type: swSelectionType,
            X: 0.0, Y: 0.0, Z: 0.0,
            Append: true, Mark: mark,
            Callout: null, SelectOption: 0));
#endif
}
