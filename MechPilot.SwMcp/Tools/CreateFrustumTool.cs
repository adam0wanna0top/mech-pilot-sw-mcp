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
/// Creates a parametric solid frustum (truncated cone). M24 — second
/// revolved-geometry tool, paired with M23 create_hemisphere. Reuses the
/// same sketch+revolve framework (Front Plane sketch + Y-axis centerline +
/// FeatureRevolve2 360°) but the sketch profile is a trapezoid instead of
/// a quarter-circle quadrant.
///
/// Unlocks tapered parts: 漏斗 / 喇叭口 / 机械臂关节 taper / 喷嘴 / 沙漏分段 /
/// 散热翅片底座 — anything that would otherwise need an extruded body plus a
/// chamfer (which would only approximate the taper, not produce a true cone
/// surface).
///
/// Sketch layout (Front Plane = XY, trapezoid profile + Y-axis centerline):
///   Line:        (0, 0, 0)            → (baseR, 0, 0)             base radius
///   Line:        (baseR, 0, 0)        → (topR, heightM, 0)        slant edge
///   Line:        (topR, heightM, 0)   → (0, heightM, 0)           top radius
///   Line:        (0, heightM, 0)      → (0, 0, 0)                 axis closure
///   CenterLine:  (0, -2*S, 0)         → (0, 2*S, 0)               revolve axis
///                where S = max(baseR, heightM) for safe length.
///
/// Pipeline:
///   1. Resolve default part template + NewDocument.
///   2. Select Front Plane (CN/EN aliases — same as CreateHemisphereTool).
///   3. InsertSketch (toggle on).
///   4. Draw 4 lines (trapezoid) + 1 centerline.
///   5. InsertSketch (toggle off).
///   6. Select Sketch1 by name, FeatureRevolve2(360°, solid, boss).
///   7. SaveAs, CloseDoc.
///
/// Why the same FeatureRevolve2 20-arg parameter set as hemisphere:
///   The trapezoid profile + Y-axis centerline produces the same kind of
///   revolved boss as the quarter-circle case — only the sketch shape
///   differs. SingleDir=true, IsSolid=true, IsCut=false, Dir1Angle=2π,
///   Merge=true, the rest 0/false (v1 PR #5 + M23 educated defaults).
/// </summary>
[McpServerToolType]
public static class CreateFrustumTool
{
    [McpServerTool(Name = "create_frustum")]
    [Description(
        "Create a parametric SOLID frustum (truncated cone) and save it to " +
        "disk. The frustum has base diameter at Y=0 and top diameter at " +
        "Y=height, revolved around the Y axis (axis +Y, same convention as " +
        "create_hemisphere). topDiameter must be strictly less than " +
        "baseDiameter — for equal diameters use create_cylinder instead. " +
        "Bounding box: baseDiameter × height × baseDiameter (X × Y × Z). " +
        "savePath must be an absolute path ending in .sldprt; parent must " +
        "exist. Common use cases: 漏斗 / 喇叭口 / 机械臂关节 taper / 喷嘴 / " +
        "散热翅片底座 / 沙漏分段.")]
    public static ToolResult Run(
        [Description("Base (Y=0) circle diameter in mm, e.g. 60 for a 60 mm bottom.")]
        double baseDiameter,
        [Description("Top (Y=height) circle diameter in mm. Must be > 0 and strictly < baseDiameter.")]
        double topDiameter,
        [Description("Frustum height along +Y in mm, e.g. 40.")]
        double height,
        [Description("Absolute output path with .sldprt extension, e.g. C:/tmp/frustum.sldprt.")]
        string savePath)
    {
        var spec = new FrustumSpec
        {
            BaseDiameterMm = baseDiameter,
            TopDiameterMm = topDiameter,
            HeightMm = height,
            SavePath = savePath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(FrustumSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return CreateFrustumInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"create_frustum failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "create_frustum requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static readonly string[] FrontPlaneAliases = { "前视基准面", "Front Plane" };
    private static readonly string[] Sketch1Aliases = { "草图1", "Sketch1" };

    private static ToolResult CreateFrustumInSw(FrustumSpec spec)
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

        // ── 5. Draw the trapezoid profile + centerline.
        //   SW units are meters — convert mm → m at the boundary.
        var baseR = spec.BaseDiameterMm / 2000.0;
        var topR = spec.TopDiameterMm / 2000.0;
        var heightM = spec.HeightMm / 1000.0;

        // 5a. Base radius line: origin → (baseR, 0).
        var baseLine = skMgr.CreateLine(0.0, 0.0, 0.0, baseR, 0.0, 0.0)
            ?? throw new McpToolException("CreateLine (base radius) returned null.");
        _ = baseLine;

        // 5b. Slant edge: (baseR, 0) → (topR, height). This is the cone surface
        //   after revolve. Length sqrt((baseR-topR)² + height²) in meters.
        var slant = skMgr.CreateLine(baseR, 0.0, 0.0, topR, heightM, 0.0)
            ?? throw new McpToolException("CreateLine (slant) returned null.");
        _ = slant;

        // 5c. Top radius line: (topR, height) → (0, height).
        var topLine = skMgr.CreateLine(topR, heightM, 0.0, 0.0, heightM, 0.0)
            ?? throw new McpToolException("CreateLine (top radius) returned null.");
        _ = topLine;

        // 5d. Axis-side closure: (0, height) → (0, 0). MUST coincide with the
        //   axis of revolution (Y axis) so the profile has zero radius there
        //   (degenerate point) — this is what makes the revolve a solid frustum
        //   rather than an annulus.
        var axisSide = skMgr.CreateLine(0.0, heightM, 0.0, 0.0, 0.0, 0.0)
            ?? throw new McpToolException("CreateLine (axis-side closure) returned null.");
        _ = axisSide;

        // 5e. Centerline along Y. Length = 2 × max(baseR, height) for safety;
        //   FeatureRevolve2 only cares about direction, not endpoints.
        //   SW auto-binds the embedded centerline as the revolve axis when
        //   the sketch is selected with mark=0 (SW_API_REFERENCE §6).
        var safeLen = 2.0 * Math.Max(baseR, heightM);
        var centerline = skMgr.CreateCenterLine(0.0, -safeLen, 0.0, 0.0, safeLen, 0.0)
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

        // ── 8. FeatureRevolve2 — 20 args (M23 educated defaults).
        //   Same parameter set as create_hemisphere: SingleDir=true + IsSolid
        //   + Dir1Angle=2π give a full 360° solid boss. The profile shape
        //   (trapezoid here, quarter-circle in hemisphere) is what makes the
        //   geometry differ — the revolve call itself is identical.
        var fm = model.FeatureManager;
        var feature = fm.FeatureRevolve2(
            SingleDir: true,
            IsSolid: true,
            IsThin: false,
            IsCut: false,
            ReverseDir: false,
            BothDirectionUpToSameEntity: false,
            Dir1Type: (int)swEndConditions_e.swEndCondBlind,   // = 0
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
                "FeatureRevolve2 returned null. Common causes: the trapezoid " +
                "profile is open / self-intersecting / does not touch the " +
                "centerline, or the centerline was not embedded in the same " +
                "sketch. (SW typically reports the underlying reason in the " +
                "FeatureManager log.)");
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
            message: $"Created solid frustum baseD{spec.BaseDiameterMm} × topD{spec.TopDiameterMm} × H{spec.HeightMm} mm (axis +Y)",
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
