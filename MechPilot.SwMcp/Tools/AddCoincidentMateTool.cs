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
/// Adds one coincident mate between two components' default reference planes.
///
/// v1 PR #20 lesson: distance / angle mates need <c>AddMate5</c> (CreateMate
/// returns null), and the 4 "magic" positions (gear ratio + angle limits)
/// must be non-zero (0.001 / π/6) otherwise AddMate5 also fails. We use the
/// same recipe for coincident — passing distance=0 and angle=0, but keeping
/// the magic limits non-zero so the API doesn't reject the call.
///
/// Pipeline:
///   1. OpenDoc6 the assembly (Silent, R/W).
///   2. ClearSelection2.
///   3. SelectByID2 plane1 of component1 (CN/EN aliases tried in order),
///      mark=0 (same as v1's distance-mate path).
///   4. SelectByID2 plane2 of component2 (append=true, mark=0).
///   5. AddMate5 with type=COINCIDENT, alignment from spec, magic positions
///      non-zero (0.001 gear ratio + π/6 angle limits).
///   6. Save3 the assembly (in-place; M5 lesson: Save3 not SaveAs(samepath)).
///   7. CloseDoc in finally.
///
/// The plane name format SelectByID2 expects is
/// <c>"&lt;PlaneAlias&gt;@&lt;ComponentInstanceName&gt;@&lt;AssemblyTitle&gt;"</c> — e.g.
/// "Front Plane@cyl-1@asm_42". AssemblyTitle is taken from
/// <c>model.GetTitle()</c> stripped of any .SLDASM extension.
/// </summary>
[McpServerToolType]
public static class AddCoincidentMateTool
{
    [McpServerTool(Name = "add_mate_coincident")]
    [Description(
        "Add a coincident mate between two components' default reference " +
        "planes (Front / Top / Right) in an existing SolidWorks assembly. " +
        "This constrains the two planes to lie in the same world plane — " +
        "the most common LLM mate type (~80% of 'put face A on face B' " +
        "requests). Use inspect_assembly first to learn the component " +
        "instance names (e.g. 'asm_cyl_123-1'). plane1 / plane2 are 'front', " +
        "'top', or 'right' (case-insensitive). alignment is 'aligned' " +
        "(default), 'anti-aligned', or 'closest'. assemblyPath must be an " +
        "absolute path to an existing .sldasm. outputPath optional: empty = " +
        "overwrite the input in place. For concentric (cylindrical-face) or " +
        "distance mates, see future add_mate tools.")]
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
        [Description("Alignment: 'aligned' (default), 'anti-aligned', or 'closest'.")]
        string alignment = "aligned",
        [Description("Optional output .sldasm path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        var spec = new CoincidentMateSpec
        {
            AssemblyPath = assemblyPath,
            Component1Name = component1Name,
            Plane1 = plane1,
            Component2Name = component2Name,
            Plane2 = plane2,
            Alignment = alignment,
            OutputPath = outputPath,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(CoincidentMateSpec spec)
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
                $"add_mate_coincident failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_mate_coincident requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    // v1 PR #20 "magic positions" — AddMate5 with these fields at 0 returns
    // null silently. Recorded SW macro showed SW itself passes these defaults.
    private const double MagicGearRatio = 0.001;
    private const double MagicAngleLimit = Math.PI / 6;  // 30°

    private static ToolResult AddMateInSw(CoincidentMateSpec spec)
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
            // SW selection names use the assembly title sans extension.
            var asmTitle = StripSldasmExt(model.GetTitle());

            // ── 2. Build the qualified plane-selection names ────────────────
            var plane1Aliases = CoincidentMateSpec.PlaneAliases[spec.Plane1];
            var plane2Aliases = CoincidentMateSpec.PlaneAliases[spec.Plane2];

            // ── 3. Select plane 1, mark=0 (same as v1 distance-mate path) ──
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

            // ── 4. Select plane 2, mark=0, append=true ──────────────────────
            var selected2Name = SelectFirstPlane(ext, plane2Aliases,
                spec.Component2Name, asmTitle, append: true);
            if (selected2Name == null)
            {
                throw new McpToolException(
                    $"Could not select '{spec.Plane2}' plane on component " +
                    $"'{spec.Component2Name}'. Tried " +
                    $"{FormatAttempts(plane2Aliases, spec.Component2Name, asmTitle)}.");
            }

            // ── 5. AddMate5 — coincident mate via v1 PR #20 recipe ──────────
            var alignment = MapAlignment(spec.Alignment);
            var mate = asmDoc.AddMate5(
                MateTypeFromEnum: (int)swMateType_e.swMateCOINCIDENT,
                AlignFromEnum: alignment,
                Flip: false,
                Distance: 0.0,
                DistanceAbsUpperLimit: 0.0,
                DistanceAbsLowerLimit: 0.0,
                GearRatioNumerator: MagicGearRatio,     // ★ non-zero
                GearRatioDenominator: MagicGearRatio,   // ★ non-zero
                Angle: 0.0,
                AngleAbsUpperLimit: MagicAngleLimit,    // ★ non-zero
                AngleAbsLowerLimit: MagicAngleLimit,    // ★ non-zero
                ForPositioningOnly: false,
                LockRotation: false,
                WidthMateOption: 0,
                ErrorStatus: out int errorStatus);

            if (mate == null)
            {
                throw new McpToolException(
                    $"AddMate5 returned null for coincident '{selected1Name}' " +
                    $"to '{selected2Name}' (ErrorStatus={errorStatus}, see " +
                    "swAddMateError_e). Common causes: the planes' normals " +
                    "are already mutually constrained, or the alignment " +
                    "creates a geometric conflict.");
            }

            // ── 6. Save (in-place via Save3 — M5 lesson) ────────────────────
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
                    $"Coincident mate '{spec.Plane1}@{spec.Component1Name}' ↔ " +
                    $"'{spec.Plane2}@{spec.Component2Name}' ({spec.Alignment}); " +
                    $"saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Tries each plane alias in order, building the qualified selection
    /// name <c>"&lt;alias&gt;@&lt;component&gt;@&lt;assemblyTitle&gt;"</c>
    /// and calling SelectByID2 (type="PLANE", mark=0). Returns the alias
    /// that succeeded, or null if all failed.
    /// </summary>
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
                Mark: 0,                // v1 PR #20: distance / AddMate5 path uses mark=0
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

    /// <summary>
    /// SW's <c>GetTitle()</c> sometimes returns "asm.SLDASM" and sometimes
    /// "asm" depending on whether the doc was opened from disk vs new. SW's
    /// selection names use the title **without** the extension, so strip it.
    /// </summary>
    private static string StripSldasmExt(string title)
    {
        const string ext = ".SLDASM";
        return title.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
            ? title.Substring(0, title.Length - ext.Length)
            : title;
    }
#endif
}
