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
/// Adds one distance mate between two components' default reference planes
/// (Front / Top / Right) at a given mm distance.
///
/// Sibling of <see cref="AddCoincidentMateTool"/> — same AddMate5 path,
/// same selection plumbing (plane name `{Alias}@{Component}@{AsmTitle}`,
/// mark=0), only the mate type and distance argument change. v1 PR #20 is
/// the historical reason distance mate works at all: <c>CreateMate</c>
/// returns null on distance, only AddMate5 + the 4 magic positions
/// (gear ratio 0.001, angle limits π/6) non-zero will produce a mate.
/// </summary>
[McpServerToolType]
public static class AddDistanceMateTool
{
    [McpServerTool(Name = "add_mate_distance")]
    [Description(
        "Add a distance mate between two components in an existing SolidWorks " +
        "assembly, at a given mm distance — by default between their reference " +
        "planes (Front / Top / Right). LLM use: 'the cylinder sits 25 mm above " +
        "the base block' → distance=25 plane1=top plane2=top alignment=aligned. " +
        "To offset from a SPECIFIC planar model face instead of a reference " +
        "plane, pass face1Index / face2Index: the planar face index from " +
        "running inspect_topology on that component's .sldprt. A face index " +
        "overrides the plane keyword for that side; sides are independent. " +
        "Use inspect_assembly first to learn the component instance names. " +
        "plane1 / plane2 are 'front' / 'top' / 'right' (case-insensitive); omit " +
        "a plane when its face index is given. alignment is 'aligned' " +
        "(default), 'anti-aligned', or 'closest' — picks which side of " +
        "reference 1 reference 2 sits on. assemblyPath must be an absolute path " +
        "to an existing .sldasm. outputPath optional: empty = overwrite in place.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm.")]
        string assemblyPath,
        [Description("First component's instance name (from inspect_assembly).")]
        string component1Name,
        [Description("Second component's instance name.")]
        string component2Name,
        [Description("Mate distance in mm. Must be > 0.")]
        double distance,
        [Description("Reference plane of component 1: 'front' / 'top' / 'right'. Omit if face1Index is given.")]
        string? plane1 = null,
        [Description("Reference plane of component 2: 'front' / 'top' / 'right'. Omit if face2Index is given.")]
        string? plane2 = null,
        [Description("Optional inspect_topology planar-face index on component1's part (overrides plane1).")]
        int? face1Index = null,
        [Description("Optional inspect_topology planar-face index on component2's part (overrides plane2).")]
        int? face2Index = null,
        [Description("Alignment: 'aligned' (default), 'anti-aligned', or 'closest'.")]
        string alignment = "aligned",
        [Description("Optional output .sldasm path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new DistanceMateSpec
        {
            AssemblyPath = assemblyPath,
            Component1Name = component1Name,
            Plane1 = plane1,
            Component2Name = component2Name,
            Plane2 = plane2,
            Face1Index = face1Index,
            Face2Index = face2Index,
            DistanceMm = distance,
            Alignment = alignment,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(DistanceMateSpec spec)
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
                $"add_mate_distance failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_mate_distance requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    // Same v1 PR #20 magic positions as AddCoincidentMateTool.
    private const double MagicGearRatio = 0.001;
    private const double MagicAngleLimit = Math.PI / 6;

    private static ToolResult AddMateInSw(DistanceMateSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Open the assembly ────────────────────────────────────────────
        int openErrors = 0;
        int openWarnings = 0;
        var model = swApp.OpenDoc6(
            FileName: spec.AssemblyPath,
            Type: (int)swDocumentTypes_e.swDocASSEMBLY,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{spec.AssemblyPath}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
        }

        try
        {
            var ext = model.Extension;
            var asmDoc = (IAssemblyDoc)model;
            var asmTitle = Internal.MateHelpers.StripSldasmExt(model.GetTitle());

            // ── 2. Select each side: a specific planar face by topology index
            //   (M54) or the named reference plane. ───────────────────────────
            model.ClearSelection2(true);
            var aliases1 = spec.Face1Index.HasValue
                ? null : CoincidentMateSpec.PlaneAliases[spec.Plane1!];
            var aliases2 = spec.Face2Index.HasValue
                ? null : CoincidentMateSpec.PlaneAliases[spec.Plane2!];
            var selected1Name = Internal.MateHelpers.SelectMateReference(
                asmDoc, ext, spec.Component1Name, spec.Face1Index,
                aliases1, spec.Plane1, asmTitle, append: false);
            var selected2Name = Internal.MateHelpers.SelectMateReference(
                asmDoc, ext, spec.Component2Name, spec.Face2Index,
                aliases2, spec.Plane2, asmTitle, append: true);

            // ── 3. AddMate5 with type=DISTANCE and the 4 magic positions ───
            //   For distance mates, v1 PR #20 sets Distance + DistanceAbs
            //   Upper/Lower = the actual mm (locked single value, no range).
            //   The angle / gear-ratio fields stay non-zero magic defaults.
            var alignment = Internal.MateHelpers.MapAlignment(spec.Alignment);
            var distanceM = spec.DistanceMm / 1000.0;
            var mate = asmDoc.AddMate5(
                MateTypeFromEnum: (int)swMateType_e.swMateDISTANCE,
                AlignFromEnum: alignment,
                Flip: false,
                Distance: distanceM,
                DistanceAbsUpperLimit: distanceM,
                DistanceAbsLowerLimit: distanceM,
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
                    $"AddMate5 returned null for distance {spec.DistanceMm} mm " +
                    $"between '{selected1Name}' and '{selected2Name}' " +
                    $"(ErrorStatus={errorStatus}, see swAddMateError_e). The " +
                    "two planes may already be over-constrained or the chosen " +
                    "alignment may conflict with the components' current " +
                    "positions — try 'closest' alignment to let SW pick.");
            }

            // ── 4. Save (in-place via Save3 — M5 lesson) ────────────────────
            var targetPath = string.IsNullOrWhiteSpace(spec.OutputPath)
                ? spec.AssemblyPath
                : spec.OutputPath!;
            var isInPlace = string.Equals(
                targetPath, spec.AssemblyPath, StringComparison.OrdinalIgnoreCase);

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

            return ToolResult.Ok(
                message:
                    $"Distance mate {spec.DistanceMm} mm: '{selected1Name}' ↔ " +
                    $"'{selected2Name}' ({spec.Alignment}); " +
                    $"saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    // Mate-family helpers extracted to Tools/Internal/MateHelpers.cs (PR #30).
#endif
}
