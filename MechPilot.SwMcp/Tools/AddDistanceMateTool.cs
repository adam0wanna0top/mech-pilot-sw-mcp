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
        "Add a distance mate between two components' default reference " +
        "planes (Front / Top / Right) in an existing SolidWorks assembly, " +
        "at a given mm distance. LLM use: 'the cylinder sits 25 mm above " +
        "the base block' → distance=25 plane1=top plane2=top alignment=aligned. " +
        "Use inspect_assembly first to learn the component instance names. " +
        "plane1 / plane2 are 'front' / 'top' / 'right' (case-insensitive). " +
        "alignment is 'aligned' (default), 'anti-aligned', or 'closest' — " +
        "picks which side of plane1 plane2 sits on. assemblyPath must be an " +
        "absolute path to an existing .sldasm. outputPath optional: " +
        "empty = overwrite the input in place.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm.")]
        string assemblyPath,
        [Description("First component's instance name (from inspect_assembly).")]
        string component1Name,
        [Description("Reference plane of component 1: 'front' / 'top' / 'right'.")]
        string plane1,
        [Description("Second component's instance name.")]
        string component2Name,
        [Description("Reference plane of component 2: 'front' / 'top' / 'right'.")]
        string plane2,
        [Description("Mate distance in mm. Must be > 0.")]
        double distance,
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
            var asmTitle = StripSldasmExt(model.GetTitle());

            // ── 2. Select plane1 (mark=0, append=false) then plane2 (append=true) ──
            var plane1Aliases = CoincidentMateSpec.PlaneAliases[spec.Plane1];
            var plane2Aliases = CoincidentMateSpec.PlaneAliases[spec.Plane2];

            model.ClearSelection2(true);
            var selected1Name = SelectFirstPlane(ext, plane1Aliases,
                spec.Component1Name, asmTitle, append: false);
            if (selected1Name == null)
            {
                throw new McpToolException(
                    $"Could not select '{spec.Plane1}' plane on component " +
                    $"'{spec.Component1Name}'. Tried " +
                    $"{FormatAttempts(plane1Aliases, spec.Component1Name, asmTitle)}. " +
                    "Verify the component name with inspect_assembly first.");
            }

            var selected2Name = SelectFirstPlane(ext, plane2Aliases,
                spec.Component2Name, asmTitle, append: true);
            if (selected2Name == null)
            {
                throw new McpToolException(
                    $"Could not select '{spec.Plane2}' plane on component " +
                    $"'{spec.Component2Name}'. Tried " +
                    $"{FormatAttempts(plane2Aliases, spec.Component2Name, asmTitle)}.");
            }

            // ── 3. AddMate5 with type=DISTANCE and the 4 magic positions ───
            //   For distance mates, v1 PR #20 sets Distance + DistanceAbs
            //   Upper/Lower = the actual mm (locked single value, no range).
            //   The angle / gear-ratio fields stay non-zero magic defaults.
            var alignment = MapAlignment(spec.Alignment);
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
                    $"Distance mate {spec.DistanceMm} mm: '{spec.Plane1}@{spec.Component1Name}' " +
                    $"↔ '{spec.Plane2}@{spec.Component2Name}' ({spec.Alignment}); " +
                    $"saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    // ── private helpers (same shape as AddCoincidentMateTool) ──────────────

    private static string? SelectFirstPlane(
        IModelDocExtension ext,
        IReadOnlyList<string> aliases,
        string componentName,
        string asmTitle,
        bool append)
    {
        foreach (var alias in aliases)
        {
            var fullName = $"{alias}@{componentName}@{asmTitle}";
            if (ext.SelectByID2(
                Name: fullName,
                Type: "PLANE",
                X: 0.0, Y: 0.0, Z: 0.0,
                Append: append,
                Mark: 0,
                Callout: null,
                SelectOption: 0))
            {
                return fullName;
            }
        }
        return null;
    }

    private static string FormatAttempts(
        IReadOnlyList<string> aliases, string componentName, string asmTitle) =>
        string.Join(" / ",
            aliases.Select(a => $"'{a}@{componentName}@{asmTitle}'"));

    private static int MapAlignment(string keyword) => keyword.ToLowerInvariant() switch
    {
        "aligned" => (int)swMateAlign_e.swMateAlignALIGNED,
        "anti-aligned" => (int)swMateAlign_e.swMateAlignANTI_ALIGNED,
        "closest" => (int)swMateAlign_e.swMateAlignCLOSEST,
        _ => throw new McpToolException($"unmapped alignment '{keyword}'"),
    };

    private static string StripSldasmExt(string title)
    {
        const string ext = ".SLDASM";
        return title.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
            ? title.Substring(0, title.Length - ext.Length)
            : title;
    }
#endif
}
