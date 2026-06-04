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
/// Adds one angle mate between two components' default reference planes
/// (Front / Top / Right) at a given degree angle. Fourth member of the mate
/// family — coincident / distance / concentric / **angle** — and the one
/// that unlocks articulated assemblies (机械臂关节摆角 / 摇头风扇摆头 /
/// L 型支架夹角).
///
/// Same AddMate5 path + 4-magic-positions trick as M19 distance mate
/// (v1 PR #20), just with <c>swMateANGLE = 6</c> and the <c>Angle /
/// AngleAbsUpperLimit / AngleAbsLowerLimit</c> fields filled with the
/// requested rad (Distance fields stay 0 — angle mate has no distance
/// semantic).
///
/// Pipeline:
///   1. OpenDoc6 the assembly (Silent, R/W; path normalized — M20 lesson).
///   2. Select plane1@component1@asm (mark=0, append=false).
///   3. Select plane2@component2@asm (mark=0, append=true).
///   4. AddMate5(swMateANGLE, alignment, ..., Angle = angle_rad,
///      AngleAbsUpper/Lower = angle_rad, 4 magic positions non-zero,
///      ErrorStatus out).
///   5. Save3 the assembly (in-place) or SaveAs (copy). CloseDoc in finally.
///
/// **Inline helpers note**: rule of three has been exceeded for the mate
/// family helpers (SelectFirstPlane / MapAlignment / StripSldasmExt /
/// FormatAttempts — copy 4 in M18/M19/M21+M25). Refactor to a shared
/// MateHelpers internal class is queued as a separate PR to keep this PR
/// pure-additive (zero-trial-and-error streak protection).
/// </summary>
[McpServerToolType]
public static class AddAngleMateTool
{
    [McpServerTool(Name = "add_mate_angle")]
    [Description(
        "Add an angle mate between two components' default reference planes " +
        "(Front / Top / Right) in an existing SolidWorks assembly, at a given " +
        "degree angle. LLM use: '机械臂关节 link2 相对 link1 摆 30°' → " +
        "angle=30 plane1=front plane2=front alignment=closest. Use " +
        "inspect_assembly first to learn the component instance names. " +
        "plane1 / plane2 are 'front' / 'top' / 'right' (case-insensitive). " +
        "angle must be in (0, 180) degrees exclusive — for 0° use " +
        "add_mate_coincident instead, 180° is degenerate. alignment is " +
        "'aligned' (default), 'anti-aligned', or 'closest' (recommended " +
        "when components are already near the target angle). assemblyPath " +
        "must be an absolute path to an existing .sldasm. outputPath " +
        "optional: empty = overwrite the input in place.")]
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
        [Description("Mate angle in degrees. Must be > 0 and < 180.")]
        double angle,
        [Description("Alignment: 'aligned' (default), 'anti-aligned', or 'closest'.")]
        string alignment = "aligned",
        [Description("Optional output .sldasm path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new AngleMateSpec
        {
            AssemblyPath = assemblyPath,
            Component1Name = component1Name,
            Plane1 = plane1,
            Component2Name = component2Name,
            Plane2 = plane2,
            AngleDeg = angle,
            Alignment = alignment,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(AngleMateSpec spec)
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
                $"add_mate_angle failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_mate_angle requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    // v1 PR #20 magic positions, same as M18/M19/M21 mate tools.
    private const double MagicGearRatio = 0.001;
    private const double MagicAngleLimit = Math.PI / 6;

    private static ToolResult AddMateInSw(AngleMateSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // Path normalize — M20 lesson (黄金法则 #14).
        var asmPath = Path.GetFullPath(spec.AssemblyPath);

        // ── 1. Open the assembly ────────────────────────────────────────────
        int openErrors = 0;
        int openWarnings = 0;
        var model = swApp.OpenDoc6(
            FileName: asmPath,
            Type: (int)swDocumentTypes_e.swDocASSEMBLY,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{asmPath}'. " +
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

            // ── 3. AddMate5 with type=ANGLE.
            //   For angle mates, fill Angle + AngleAbsUpper/Lower with the
            //   actual rad (locked single value, no range). Distance fields
            //   stay 0 (angle mate has no distance semantic). Gear ratio
            //   magic positions stay non-zero per v1 PR #20.
            var alignment = MapAlignment(spec.Alignment);
            var angleRad = spec.AngleDeg * Math.PI / 180.0;
            var mate = asmDoc.AddMate5(
                MateTypeFromEnum: (int)swMateType_e.swMateANGLE,
                AlignFromEnum: alignment,
                Flip: false,
                Distance: 0.0,
                DistanceAbsUpperLimit: 0.0,
                DistanceAbsLowerLimit: 0.0,
                GearRatioNumerator: MagicGearRatio,
                GearRatioDenominator: MagicGearRatio,
                Angle: angleRad,
                AngleAbsUpperLimit: angleRad,
                AngleAbsLowerLimit: angleRad,
                ForPositioningOnly: false,
                LockRotation: false,
                WidthMateOption: 0,
                ErrorStatus: out int errorStatus);

            if (mate == null)
            {
                throw new McpToolException(
                    $"AddMate5 returned null for angle {spec.AngleDeg}° " +
                    $"between '{selected1Name}' and '{selected2Name}' " +
                    $"(ErrorStatus={errorStatus}, see swAddMateError_e). The " +
                    "two planes may already be over-constrained, the chosen " +
                    "alignment may conflict with the components' current " +
                    "positions, or the angle may be physically unreachable " +
                    "given other existing mates — try 'closest' alignment " +
                    "to let SW pick the rotation sense.");
            }

            // ── 4. Save (in-place via Save3 — M5 lesson) ────────────────────
            var targetPath = string.IsNullOrWhiteSpace(spec.OutputPath)
                ? asmPath
                : Path.GetFullPath(spec.OutputPath!);
            var isInPlace = string.Equals(
                targetPath, asmPath, StringComparison.OrdinalIgnoreCase);

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
                    $"Angle mate {spec.AngleDeg}°: '{spec.Plane1}@{spec.Component1Name}' " +
                    $"↔ '{spec.Plane2}@{spec.Component2Name}' ({spec.Alignment}); " +
                    $"saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    // ── inline mate helpers (rule-of-three exceeded — refactor queued in
    //   a follow-up PR; see class docstring) ─────────────────────────────────

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
