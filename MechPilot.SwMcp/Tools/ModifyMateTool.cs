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
/// Edits an existing mate's value (distance in mm, angle in degrees) in an
/// assembly and rebuilds — the mate counterpart of <see cref="ModifyFeatureTool"/>
/// (M42). Pairs with inspect_assembly's mates list (read mate names + values) to
/// close the see → edit loop on mates, the write primitive an assembly resize
/// needs (scale a distance mate as the parts grow).
///
/// Mirrors modify_feature: locate the mate, set its display dimension's
/// <see cref="IDimension.SystemValue"/> (SI), then EditRebuild3 — the NoPIA-safe
/// "change this number" path (no GetDefinition/ModifyDefinition). Opens the
/// assembly by path like the add_mate_* tools and saves in place (Save3) or to a
/// copy (SaveAs), per the M5 in-place-save lesson.
/// </summary>
[McpServerToolType]
public static class ModifyMateTool
{
    [McpServerTool(Name = "modify_mate")]
    [Description(
        "Edit an existing mate's value in a SolidWorks assembly and rebuild. " +
        "mateName is the EXACT name from inspect_assembly's mates list (e.g. " +
        "'距离1' / 'Distance1'). value is the new distance in mm (distance mate) " +
        "or angle in degrees (angle mate) — only distance / angle mates have an " +
        "editable value. Use inspect_assembly first to get mate names and types. " +
        "assemblyPath must be an absolute path to an existing .sldasm. outputPath " +
        "optional: empty = overwrite the input in place. This is the mate " +
        "counterpart of modify_feature, used when resizing an assembly.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm.")]
        string assemblyPath,
        [Description("Exact mate name from inspect_assembly's mates list (e.g. '距离1').")]
        string mateName,
        [Description("New value: distance in mm or angle in degrees (by mate type). > 0.")]
        double value,
        [Description("Optional output .sldasm path. Empty = overwrite input in place.")]
        string? outputPath = null)
    {
        return RunWithSpec(new ModifyMateSpec
        {
            AssemblyPath = assemblyPath,
            MateName = mateName,
            Value = value,
            OutputPath = outputPath,
        });
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(ModifyMateSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return ModifyInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"modify_mate failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException(
            "modify_mate requires SolidWorks Interop assemblies, which were not present " +
            "at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult ModifyInSw(ModifyMateSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

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
            var mate = Internal.MateReader.FindMate(model, spec.MateName)
                ?? throw new McpToolException(
                    $"Cannot find a mate named '{spec.MateName}' in the assembly. " +
                    "Call inspect_assembly to list the mate names.");

            var swType = mate.Type;
            if (!Internal.MateType.HasValue(swType))
            {
                throw new McpToolException(
                    $"modify_mate only edits distance / angle mates; '{spec.MateName}' is a " +
                    $"'{Internal.MateType.Name(swType)}' mate with no editable value.");
            }

            if (mate.DisplayDimension is not IDisplayDimension disp ||
                disp.GetDimension2(0) is not IDimension dim)
            {
                throw new McpToolException(
                    $"Mate '{spec.MateName}' has no accessible display dimension to edit.");
            }

            // SystemValue is SI (metres / radians); convert from mm / degrees.
            var isAngle = Internal.MateType.IsAngle(swType);
            dim.SystemValue = isAngle ? spec.Value * Math.PI / 180.0 : spec.Value / 1000.0;

            if (!model.EditRebuild3())
            {
                throw new McpToolException(
                    $"Rebuild failed after setting mate '{spec.MateName}' to {spec.Value}. " +
                    "The value may over-constrain the assembly or conflict with other mates.");
            }

            // Save in place via Save3 (M5 lesson) or to a copy via SaveAs.
            var targetPath = string.IsNullOrWhiteSpace(spec.OutputPath)
                ? spec.AssemblyPath
                : spec.OutputPath!;
            var isInPlace = string.Equals(
                targetPath, spec.AssemblyPath, StringComparison.OrdinalIgnoreCase);

            int saveErrors = 0;
            int saveWarnings = 0;
            bool savedOk = isInPlace
                ? model.Save3(
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings)
                : model.Extension.SaveAs(
                    targetPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref saveErrors, ref saveWarnings);

            if (!savedOk || !File.Exists(targetPath))
            {
                var api = isInPlace ? "Save3" : "SaveAs";
                throw new McpToolException(
                    $"{api} failed for '{targetPath}'. errors=0x{saveErrors:X} warnings=0x{saveWarnings:X}.");
            }

            var what = isAngle ? $"angle → {spec.Value}°" : $"distance → {spec.Value} mm";
            return ToolResult.Ok(
                message: $"Modified mate '{spec.MateName}': {what}; saved {(isInPlace ? "in place" : "as a copy")}",
                path: targetPath);
        }
        finally
        {
            swApp.CloseDoc(model.GetTitle());
        }
    }
#endif
}
