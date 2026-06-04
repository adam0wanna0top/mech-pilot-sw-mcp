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
/// Creates a parametric solid hemisphere part. M23 — first revolved-geometry
/// tool, alongside the 4 prismatic create_* tools (cylinder / flange / block /
/// assembly). Unlocks "non-prismatic" parts (球壳/球阀/球关节/电风扇底圆顶/
/// 球冠罩) without forcing the LLM to author a sketch profile + centerline.
///
/// Sketch layout (Front Plane = XY):
///   Line:        (0, 0, 0) → (R, 0, 0)         base radius line (sketch X)
///   Arc:         center (0,0,0), start (R,0,0), end (0,R,0), dir=1 (CCW)
///   Line:        (0, R, 0) → (0, 0, 0)         axis-side closure
///   CenterLine:  (0, -2R, 0) → (0, 2R, 0)      axis of revolution (sketch Y, =world Y)
///                ^ longer than profile for safety — SW only cares about direction.
///
/// Pipeline:
///   1. Resolve default part template + NewDocument.
///   2. Select Front Plane (CN/EN aliases — same as CreateCylinderTool).
///   3. InsertSketch (toggle on).
///   4. Draw line + arc + line + centerline (the 1/4 circle quadrant + axis).
///   5. InsertSketch (toggle off).
///   6. Select Sketch1 by name, FeatureRevolve2(360°, solid, boss).
///   7. SaveAs, CloseDoc.
///
/// Why Front Plane and not Right/Top:
///   Right/Top Plane sketch-coordinate ↔ world-axis mapping has SW-internal
///   handedness that reflection doesn't expose, so a "wrong direction" would
///   silently produce a mirrored hemisphere. Front Plane is sketch-X=world-X,
///   sketch-Y=world-Y unambiguously. Cost: hemisphere axis is +Y rather than
///   +Z (the cylinder convention). Documented in HemisphereSpec / tool desc.
///
/// FeatureRevolve2 (20 args reflected from SW 2026 — v1 PR #5 教训: 文档说 15,
/// 实际 20, 多 5 个 Variant 尾部参数; mark=0 sketch + embedded centerline
/// auto-binds as axis per SW_API_REFERENCE §6).
/// </summary>
[McpServerToolType]
public static class CreateHemisphereTool
{
    [McpServerTool(Name = "create_hemisphere")]
    [Description(
        "Create a parametric SOLID hemisphere (upper half of a sphere) and save " +
        "it to disk. diameter is the full sphere diameter in mm — the hemisphere " +
        "has X∈[−D/2, D/2], Y∈[0, D/2], Z∈[−D/2, D/2] (axis along +Y, base on " +
        "Y=0 plane). savePath must be an absolute path ending in .sldprt; the " +
        "parent directory must already exist. " +
        "Common use cases: 电风扇底圆顶 / 球关节 / 球阀外壳 / 球冠罩 / 电池 " +
        "正负极半球端面. For a full sphere, mate two hemispheres back-to-back " +
        "with add_mate_coincident (future PR may add create_sphere directly).")]
    public static ToolResult Run(
        [Description("Hemisphere diameter in millimeters (full sphere diameter), e.g. 60 for D60.")]
        double diameter,
        [Description("Absolute output path with .sldprt extension, e.g. C:/tmp/hemi.sldprt.")]
        string savePath)
    {
        var spec = new HemisphereSpec
        {
            DiameterMm = diameter,
            SavePath = savePath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(HemisphereSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return CreateHemisphereInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"create_hemisphere failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "create_hemisphere requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static readonly string[] FrontPlaneAliases = { "前视基准面", "Front Plane" };
    private static readonly string[] Sketch1Aliases = { "草图1", "Sketch1" };

    private static ToolResult CreateHemisphereInSw(HemisphereSpec spec)
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

        // ── 5. Draw the profile (1/4 circle in XY, closed back to origin) + centerline.
        //   SW units are meters — convert mm → m at the boundary.
        var radiusM = spec.DiameterMm / 2000.0;

        // 5a. Base radius line: origin → (R, 0).
        var baseLine = skMgr.CreateLine(0.0, 0.0, 0.0, radiusM, 0.0, 0.0)
            ?? throw new McpToolException("CreateLine (base) returned null.");
        _ = baseLine;

        // 5b. Quarter arc: center origin, start (R, 0), end (0, R), direction
        //   = 1 (CCW). SW arc direction sign convention: +1 = counter-clockwise
        //   viewed from sketch normal (Front Plane normal = +Z toward viewer).
        var arc = skMgr.CreateArc(
            XC: 0.0, YC: 0.0, Zc: 0.0,
            X1: radiusM, Y1: 0.0, Z1: 0.0,
            X2: 0.0, Y2: radiusM, Z2: 0.0,
            Direction: 1)
            ?? throw new McpToolException(
                "CreateArc returned null. Check the start/end points are equidistant from the center.");
        _ = arc;

        // 5c. Axis-side closure: (0, R) → origin. This MUST be along the axis
        //   of revolution (the Y axis here) so the profile is closed and the
        //   axis-side edge has zero radius (degenerate point at revolve).
        var axisSide = skMgr.CreateLine(0.0, radiusM, 0.0, 0.0, 0.0, 0.0)
            ?? throw new McpToolException("CreateLine (axis-side closure) returned null.");
        _ = axisSide;

        // 5d. Centerline along Y. Made longer than the profile for safety;
        //   FeatureRevolve2 only uses its direction, not its endpoints.
        //   SW auto-binds the embedded centerline as the revolve axis when the
        //   sketch is selected with mark=0 (SW_API_REFERENCE §6).
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

        // ── 8. FeatureRevolve2 — 20 args, full 360° solid boss revolve.
        //   SingleDir=true: revolve in one direction only (Dir1Angle = 2π).
        //   IsSolid=true,  IsThin=false:    create a solid (not thin-shell) feature.
        //   IsCut=false:                    boss (not cut).
        //   ReverseDir=false:               default sense (Y+ direction).
        //   Dir1Type=0 (Blind):             use Dir1Angle, not "up to ref".
        //   Dir1Angle=2π:                   full 360° → complete hemisphere body.
        //   Merge=true, UseFeatScope=true, UseAutoSelect=true: standard solid defaults.
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
            message: $"Created solid hemisphere D{spec.DiameterMm} mm (axis +Y, base on Y=0)",
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
