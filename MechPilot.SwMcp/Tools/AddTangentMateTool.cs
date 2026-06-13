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
/// Adds a tangent mate between two components (M56) — a curved face (cylinder /
/// sphere / cone) touching a plane or another curved face. The fifth mate type,
/// expressing the junction the other four can't: a cylinder resting on a flat,
/// or two cylinders touching along a line. Born from the fan dogfooding — the
/// motor housing (a horizontal cylinder) sits on the pole top (a flat), a
/// perpendicular joint that coincident / concentric leave under-constrained.
///
/// Tangent is inherently about two SPECIFIC faces (no reference-plane shorthand,
/// no auto-pick), so both are addressed by inspect_topology face index via the
/// shared <see cref="Internal.ComponentFaceSelector"/>. At least one face must
/// be curved. Same AddMate5 path + v1 PR #20 magic positions as the other mates.
/// </summary>
[McpServerToolType]
public static class AddTangentMateTool
{
    [McpServerTool(Name = "add_mate_tangent")]
    [Description(
        "Add a tangent mate between two components — a curved face (cylinder / " +
        "sphere / cone) touching a plane or another curved face. This is the " +
        "mate for a cylinder resting on a flat (e.g. a motor housing on a pole " +
        "top) or two cylinders touching, which coincident / concentric can't " +
        "express. Both faces are addressed by their inspect_topology face index " +
        "(no reference-plane / auto-pick shorthand — tangent needs two specific " +
        "faces); at least one must be curved. Run inspect_assembly for the " +
        "component instance names and inspect_topology on each part for the face " +
        "indexes. alignment is 'closest' (default — let SW pick which side " +
        "touches), 'aligned', or 'anti-aligned'. assemblyPath must be an " +
        "absolute path to an existing .sldasm. outputPath optional: empty = " +
        "overwrite in place.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm.")]
        string assemblyPath,
        [Description("First component's instance name (from inspect_assembly).")]
        string component1Name,
        [Description("Second component's instance name.")]
        string component2Name,
        [Description("inspect_topology face index on component1's part (≥ 0).")]
        int face1Index,
        [Description("inspect_topology face index on component2's part (≥ 0).")]
        int face2Index,
        [Description("Alignment: 'closest' (default), 'aligned', or 'anti-aligned'.")]
        string alignment = "closest",
        [Description("Optional output .sldasm path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new TangentMateSpec
        {
            AssemblyPath = assemblyPath,
            Component1Name = component1Name,
            Component2Name = component2Name,
            Face1Index = face1Index,
            Face2Index = face2Index,
            Alignment = alignment,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(TangentMateSpec spec)
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
                $"add_mate_tangent failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_mate_tangent requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    // Same v1 PR #20 magic positions as the other mate tools.
    private const double MagicGearRatio = 0.001;
    private const double MagicAngleLimit = Math.PI / 6;

    private static ToolResult AddMateInSw(TangentMateSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();
        var asmPathNorm = Path.GetFullPath(spec.AssemblyPath);

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

            var comp1 = Internal.MateHelpers.FindComponentByName(asmDoc, spec.Component1Name)
                ?? throw new McpToolException(
                    $"Component '{spec.Component1Name}' not found in assembly. " +
                    "Verify the name with inspect_assembly first.");
            var comp2 = Internal.MateHelpers.FindComponentByName(asmDoc, spec.Component2Name)
                ?? throw new McpToolException(
                    $"Component '{spec.Component2Name}' not found in assembly.");

            var (face1, sig1, curved1) = Internal.ComponentFaceSelector.GetAnyFaceByIndex(
                comp1, spec.Face1Index, spec.Component1Name);
            var (face2, sig2, curved2) = Internal.ComponentFaceSelector.GetAnyFaceByIndex(
                comp2, spec.Face2Index, spec.Component2Name);

            if (!curved1 && !curved2)
            {
                throw new McpToolException(
                    $"A tangent mate needs at least one curved face, but both " +
                    $"'{sig1}' and '{sig2}' are planar. For two planes use " +
                    "add_mate_coincident; pick a cylinder / sphere / cone on at " +
                    "least one side.");
            }

            model.ClearSelection2(true);
            if (!((IEntity)face1).Select2(false, 0))
            {
                throw new McpToolException(
                    $"Failed to select face {sig1} on '{spec.Component1Name}'.");
            }
            if (!((IEntity)face2).Select2(true, 0))
            {
                throw new McpToolException(
                    $"Failed to select face {sig2} on '{spec.Component2Name}'.");
            }

            var alignment = Internal.MateHelpers.MapAlignment(spec.Alignment);
            var mate = asmDoc.AddMate5(
                MateTypeFromEnum: (int)swMateType_e.swMateTANGENT,
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
                    $"AddMate5 returned null for tangent '{sig1}'@'{spec.Component1Name}' ↔ " +
                    $"'{sig2}'@'{spec.Component2Name}' (ErrorStatus={errorStatus}, see " +
                    "swAddMateError_e). The faces may be unable to reach tangency at " +
                    "the components' current positions — try 'aligned' / 'anti-aligned', " +
                    "or move the component closer first.");
            }

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
                    $"Tangent mate '{sig1}'@'{spec.Component1Name}' ↔ " +
                    $"'{sig2}'@'{spec.Component2Name}' ({spec.Alignment}); " +
                    $"saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }
#endif
}
