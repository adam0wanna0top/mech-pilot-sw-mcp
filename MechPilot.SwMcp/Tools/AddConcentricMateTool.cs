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
/// Adds one concentric mate between two components' cylindrical faces — by
/// default the first axial-Z cylinder on each, or (M53-③) a SPECIFIC face
/// addressed by its inspect_topology index (face1Index / face2Index) so the
/// LLM can target e.g. the flange's 3rd bolt hole, not just whichever cylinder
/// is found first. Third member of the mate family alongside
/// <see cref="AddCoincidentMateTool"/> and <see cref="AddDistanceMateTool"/>;
/// like them it uses the AddMate5 path with the v1 PR #20 magic
/// positions (gear ratio 0.001, angle limits π/6), but the **selection
/// strategy is different**: instead of qualified plane names, the tool
/// walks each component's body, finds a face whose surface
/// <c>IsCylinder()</c> with axis ≈ ±Z, and selects it via
/// <c>IEntity.Select4(append, mark=0)</c>.
///
/// Pipeline:
///   1. OpenDoc6 the assembly (Silent, R/W; path normalized — M20 lesson).
///   2. Walk asmDoc.GetComponents(true) to find IComponent2 by Name2 for
///      each spec component.
///   3. For each component: GetBody → GetFaces → first one whose
///      surface IsCylinder and CylinderParams[3..5] (axis) has |Z| > 0.99.
///   4. IEntity.Select4(append=false, mark=0) for face 1;
///      IEntity.Select4(append=true,  mark=0) for face 2.
///   5. AddMate5(swMateCONCENTRIC, alignment, ..., 4 magic positions
///      non-zero, ErrorStatus out).
///   6. Save3 the assembly (in-place) or SaveAs (copy). CloseDoc in finally.
///
/// Why mark=0 (same as distance/coincident AddMate5 path): SW_API_REFERENCE
/// §6 says CreateMate uses mark=1 for ref1/ref2 and AddMate5 uses mark=0.
/// We've validated mark=0 works for COINCIDENT (M18) and DISTANCE (M19), so
/// we stay on the same path for CONCENTRIC.
/// </summary>
[McpServerToolType]
public static class AddConcentricMateTool
{
    [McpServerTool(Name = "add_mate_concentric")]
    [Description(
        "Add a concentric mate between two components' cylindrical faces in an " +
        "existing SolidWorks assembly. By default the tool auto-finds the first " +
        "cylindrical face on each component (axis along ±Z, matching the " +
        "extrusion direction of create_cylinder / create_flange and the inner " +
        "faces of add_axial_hole / add_threaded_hole holes), so the LLM only " +
        "supplies the two component instance names. To target a SPECIFIC face — " +
        "e.g. 'the flange's 3rd bolt hole' rather than whichever cylinder is " +
        "found first — pass face1Index / face2Index: the face index from " +
        "running inspect_topology on that component's underlying .sldprt (the " +
        "part's face order equals the component's in-assembly order for " +
        "single-body parts, so the index bridges directly). Omit an index to " +
        "auto-pick that side; the two sides are independent. " +
        "Use inspect_assembly first to learn instance names (e.g. 'cyl-1'). " +
        "alignment is 'aligned' (default), 'anti-aligned', or 'closest'. " +
        "assemblyPath must be an absolute path to an existing .sldasm. " +
        "outputPath optional: empty = overwrite in place.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm.")]
        string assemblyPath,
        [Description("First component's instance name (from inspect_assembly).")]
        string component1Name,
        [Description("Second component's instance name.")]
        string component2Name,
        [Description("Alignment: 'aligned' (default), 'anti-aligned', or 'closest'.")]
        string alignment = "aligned",
        [Description("Optional inspect_topology face index on component1's part to mate that exact cylinder. Omit = auto-pick first axial-Z cylinder.")]
        int? face1Index = null,
        [Description("Optional inspect_topology face index on component2's part. Omit = auto-pick.")]
        int? face2Index = null,
        [Description("Optional output .sldasm path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new ConcentricMateSpec
        {
            AssemblyPath = assemblyPath,
            Component1Name = component1Name,
            Component2Name = component2Name,
            Alignment = alignment,
            Face1Index = face1Index,
            Face2Index = face2Index,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(ConcentricMateSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddMateInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_mate_concentric failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_mate_concentric requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    // Same magic positions as M18/M19 (v1 PR #20 finding: must be non-zero).
    private const double MagicGearRatio = 0.001;
    private const double MagicAngleLimit = Math.PI / 6;

    // Z-axis cos-similarity threshold for "axis is along ±Z".
    private const double ZAxisThreshold = 0.99;

    private static ToolResult AddMateInSw(ConcentricMateSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 0. Normalize asm path (M20 lesson: SW's internal doc-table key
        //   uses OS-canonical form). ──────────────────────────────────────────
        var asmPathNorm = Path.GetFullPath(spec.AssemblyPath);

        // ── 1. Open the assembly ────────────────────────────────────────────
        int openErrors = 0;
        int openWarnings = 0;
        var model = swApp.OpenDoc6(
            FileName: asmPathNorm,
            Type: (int)swDocumentTypes_e.swDocASSEMBLY,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{asmPathNorm}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            var asmDoc = (IAssemblyDoc)model;

            // ── 2. Find both components by instance name ───────────────────
            var comp1 = FindComponentByName(asmDoc, spec.Component1Name)
                ?? throw new McpToolException(
                    $"Component '{spec.Component1Name}' not found in assembly. " +
                    "Verify the name with inspect_assembly first.");
            var comp2 = FindComponentByName(asmDoc, spec.Component2Name)
                ?? throw new McpToolException(
                    $"Component '{spec.Component2Name}' not found in assembly.");

            // ── 3. Resolve a cylindrical face on each component: by topology
            //   index when given (M53-③ precise addressing), else auto-find the
            //   first axial-Z cylinder (back-compat). ────────────────────────
            var (face1, face1Sig) = ResolveFace(
                comp1, spec.Face1Index, spec.Component1Name);
            var (face2, face2Sig) = ResolveFace(
                comp2, spec.Face2Index, spec.Component2Name);

            // ── 4. Select both faces, mark=0 (AddMate5 path, M18/M19 same) ─
            model.ClearSelection2(true);
            if (!((IEntity)face1).Select4(Append: false, Data: null))
            {
                throw new McpToolException(
                    $"Failed to select cylindrical face on '{spec.Component1Name}'.");
            }
            // Re-mark to 0 (Select4 leaves default 0 but be explicit).
            ((IEntity)face1).Select2(Append: false, Mark: 0);

            if (!((IEntity)face2).Select2(Append: true, Mark: 0))
            {
                throw new McpToolException(
                    $"Failed to select cylindrical face on '{spec.Component2Name}'.");
            }

            // ── 5. AddMate5 with type=CONCENTRIC + magic positions ─────────
            var alignment = Internal.MateHelpers.MapAlignment(spec.Alignment);
            var mate = asmDoc.AddMate5(
                MateTypeFromEnum: (int)swMateType_e.swMateCONCENTRIC,
                AlignFromEnum: alignment,
                Flip: false,
                Distance: 0.0,
                DistanceAbsUpperLimit: 0.0,
                DistanceAbsLowerLimit: 0.0,
                GearRatioNumerator: MagicGearRatio,
                GearRatioDenominator: MagicGearRatio,
                Angle: 0.0,
                AngleAbsUpperLimit: MagicAngleLimit,
                AngleAbsLowerLimit: MagicAngleLimit,
                ForPositioningOnly: false,
                LockRotation: false,
                WidthMateOption: 0,
                ErrorStatus: out int errorStatus);

            if (mate == null)
            {
                throw new McpToolException(
                    $"AddMate5 returned null for concentric '{spec.Component1Name}' ↔ " +
                    $"'{spec.Component2Name}' (ErrorStatus={errorStatus}, see " +
                    "swAddMateError_e). The cylindrical faces may already be " +
                    "constrained, or the alignment may conflict with the current " +
                    "component positions — try 'closest' alignment.");
            }

            // ── 6. Save (in-place via Save3 — M5 lesson) ────────────────────
            var targetPath = string.IsNullOrWhiteSpace(spec.OutputPath)
                ? asmPathNorm
                : Path.GetFullPath(spec.OutputPath!);
            var isInPlace = string.Equals(
                targetPath, asmPathNorm, StringComparison.OrdinalIgnoreCase);

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
                savedOk = model.Extension.SaveAs(
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
                message:
                    $"Concentric mate: '{spec.Component1Name}' [{face1Sig}] ↔ " +
                    $"'{spec.Component2Name}' [{face2Sig}] ({spec.Alignment}); " +
                    $"saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Walks <c>asmDoc.GetComponents(true)</c> (top-level) and returns the
    /// first <see cref="IComponent2"/> whose <c>Name2</c> matches
    /// case-insensitively. Returns null if none found.
    /// </summary>
    private static IComponent2? FindComponentByName(IAssemblyDoc asmDoc, string name)
    {
        var componentsObj = asmDoc.GetComponents(true);
        if (componentsObj is not object[] comps) return null;

        foreach (var c in comps)
        {
            if (c is not IComponent2 comp) continue;
            if (string.Equals(comp.Name2, name, StringComparison.OrdinalIgnoreCase))
            {
                return comp;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the cylindrical face to mate on a component: by inspect_topology
    /// index when <paramref name="faceIndex"/> is given (M53-③ precise
    /// addressing, shared <see cref="Internal.ComponentFaceSelector"/>), else
    /// the first axial-Z cylinder (back-compat auto-pick). Returns the face plus
    /// a short signature for the success message.
    /// </summary>
    private static (IFace2 Face, string Signature) ResolveFace(
        IComponent2 comp, int? faceIndex, string componentName)
    {
        if (faceIndex is int idx)
        {
            return Internal.ComponentFaceSelector.GetCylindricalFaceByIndex(
                comp, idx, componentName);
        }

        var auto = FindFirstAxialCylinderFace(comp)
            ?? throw new McpToolException(
                $"Could not find any cylindrical face on '{componentName}' to mate " +
                "concentric. The component has no cylinder (a hole wall, a shaft, a " +
                "boss). To target a specific face, run inspect_topology on the part " +
                "and pass its face index.");
        return (auto, "auto cylinder");
    }

    /// <summary>
    /// Walks the component's body's faces and returns a cylindrical face to
    /// mate, preferring one whose axis is along ±Z (|axis.Z| > 0.99 — matches
    /// every create_* tool's extrusion direction), and **falling back to the
    /// first cylinder of any axis** (M56) so a rotated part — whose cylinders
    /// now run along X or Y — still auto-picks instead of forcing a faceIndex.
    /// Returns null only if the component has no cylindrical face at all.
    /// </summary>
    /// <remarks>
    /// <c>ISurface.get_CylinderParams</c> returns a 7-double array:
    /// [0..2] = a root point on the axis (meters),
    /// [3..5] = axis direction unit vector,
    /// [6]    = radius (meters).
    /// </remarks>
    private static IFace2? FindFirstAxialCylinderFace(IComponent2 comp)
    {
        if (comp.GetBody() is not IBody2 body) return null;
        if (body.GetFaces() is not object[] faces) return null;

        IFace2? firstCylinder = null;
        foreach (var faceObj in faces)
        {
            var face = (IFace2)faceObj;
            var surface = (ISurface)face.GetSurface();
            if (!surface.IsCylinder()) continue;
            if (surface.CylinderParams is not double[] cp || cp.Length < 6) continue;
            firstCylinder ??= face;
            // Prefer an axis along ±Z (axis direction at indices 3..5).
            if (Math.Abs(cp[5]) > ZAxisThreshold)
            {
                return face;
            }
        }
        return firstCylinder;
    }

    // MapAlignment extracted to Tools/Internal/MateHelpers.cs (PR #30).
#endif
}
