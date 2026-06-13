using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Interop;
using MechPilot.SwMcp.Tools.Internal;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Inserts a SolidWorks Toolbox standard part (fastener / bearing / washer /
/// pin / ...) into an existing assembly at a chosen SIZE — the size is a
/// configuration of the Toolbox library .sldprt (M47, fan-dogfooding gap:
/// plain add_component can only insert the default config = default size).
///
/// Recipe (reflection-verified, M47):
///   - Sizes are configurations inside the Toolbox part. Enumerate via
///     <c>IModelDoc2.GetConfigurationNames()</c> (returns object — NoPIA:
///     capture into an explicit <c>object</c> local, never var/dynamic).
///   - <c>swAddComponentConfigOptions_e</c> has NO "existing config" member —
///     selecting an existing config is ConfigOption=0 (CurrentSelectedConfig)
///     + <c>ExistingConfigName</c> = the size config.
///   - Belt-and-braces: after insert, if the component's
///     <c>ReferencedConfiguration</c> differs from the requested config, set
///     it directly + rebuild (covers AddComponent5 ignoring
///     ExistingConfigName in some SW states).
///   - <c>IAssemblyDoc.UpdateToolboxComponent</c> exists but is NOT needed
///     here — the config IS the size; that API is for refreshing Toolbox
///     components after library changes.
///
/// Pipeline (mirrors AddComponentTool — M20 path normalize, v1 PR #9
/// preload, M5 Save3, finally CloseDoc):
///   1. Path.GetFullPath both paths.
///   2. OpenDoc6 the Toolbox part (preload; also gives us the config list).
///   3. Resolve configName against the part's configurations (exact, then
///      case-insensitive); unknown → throw listing available names.
///   4. OpenDoc6 the assembly (becomes active doc).
///   5. AddComponent5(..., ExistingConfigName: resolved, x, y, z).
///   6. Verify / force ReferencedConfiguration; EditRebuild3 if forced.
///   7. Save3 the assembly in place; CloseDoc both in finally.
/// </summary>
[McpServerToolType]
public static class InsertToolboxFastenerTool
{
    /// <summary>Max config names listed in the "not found" error.</summary>
    private const int MaxConfigsInError = 15;

    [McpServerTool(Name = "insert_toolbox_fastener")]
    [Description(
        "Insert a SolidWorks Toolbox STANDARD PART (bolt / screw / nut / " +
        "washer / bearing / pin...) into an existing assembly at a chosen " +
        "size. partPath is the Toolbox library .sldprt under the Toolbox " +
        "data folder's browser/<standard>/<category>/<type>/ tree (e.g. " +
        "...browser/GB/bolts and studs/hexagon head bolts/hexagon head " +
        "bolts gb.sldprt). configName picks the size — each size lives as a " +
        "configuration of that part (NOTE: Toolbox generates a size's config " +
        "the first time that size is used in SW's Toolbox UI; a fresh master " +
        "may only have 'Default'). Omit configName to insert the default " +
        "size. If configName doesn't match, the error lists the available " +
        "configuration names to choose from. The component is " +
        "placed at (positionX/Y/Z) mm but not mated — use add_mate_* after. " +
        "A Toolbox fastener's shank runs along the part's own local axis, so " +
        "it lands lying flat by default; use (rotationX/Y/Z) degrees to stand " +
        "it up along the desired assembly axis (e.g. rotate 90° so the shank " +
        "points along assembly Z). Rotation is applied about the world origin " +
        "before positioning. assemblyPath must be an existing .sldasm " +
        "(new_assembly creates one).")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm to insert into.")]
        string assemblyPath,
        [Description("Absolute path to the Toolbox library .sldprt (under the Toolbox data folder).")]
        string partPath,
        [Description("Size configuration name, e.g. 'M6X30'. Omit/empty = default size.")]
        string? configName = null,
        [Description("Component origin X in the assembly in mm. Default 0.")]
        double positionX = 0,
        [Description("Component origin Y in the assembly in mm. Default 0.")]
        double positionY = 0,
        [Description("Component origin Z in the assembly in mm. Default 0.")]
        double positionZ = 0,
        [Description("Rotation about the world X axis in degrees. Default 0.")]
        double rotationX = 0,
        [Description("Rotation about the world Y axis in degrees. Default 0.")]
        double rotationY = 0,
        [Description("Rotation about the world Z axis in degrees. Default 0.")]
        double rotationZ = 0)
    {
        var spec = new ToolboxFastenerSpec
        {
            AssemblyPath = assemblyPath,
            PartPath = partPath,
            ConfigName = configName,
            PositionXMm = positionX,
            PositionYMm = positionY,
            PositionZMm = positionZ,
            RotationXDeg = rotationX,
            RotationYDeg = rotationY,
            RotationZDeg = rotationZ,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(ToolboxFastenerSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return InsertInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"insert_toolbox_fastener failed at SW Interop layer: " +
                $"{ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "insert_toolbox_fastener requires SolidWorks Interop assemblies, " +
            "which were not present at build time. Build on a machine with " +
            "SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult InsertInSw(ToolboxFastenerSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // M20 lesson: AddComponent5 matches the path string against SW's
        // internal doc-table key (OS-canonical, backslash) — normalize.
        var asmPathNorm = Path.GetFullPath(spec.AssemblyPath);
        var partPathNorm = Path.GetFullPath(spec.PartPath);

        // ── 1. Open the Toolbox part FIRST (v1 PR #9: AddComponent5 doesn't
        //   auto-load; null otherwise). Also the source of the config list. ──
        int partErrors = 0;
        int partWarnings = 0;
        var partModel = swApp.OpenDoc6(
            FileName: partPathNorm,
            Type: (int)swDocumentTypes_e.swDocPART,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref partErrors,
            Warnings: ref partWarnings) as IModelDoc2;

        if (partModel == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for Toolbox part '{partPathNorm}'. " +
                $"errors=0x{partErrors:X} warnings=0x{partWarnings:X}.");
        }

        IModelDoc2? asmModel = null;
        try
        {
            // ── 2. Resolve the size configuration ───────────────────────────
            //   NoPIA: GetConfigurationNames returns object → capture into an
            //   explicit object local (var would become dynamic → dispatch
            //   blow-ups downstream; M40/M43 lesson).
            object rawNames = partModel.GetConfigurationNames();
            var configNames =
                rawNames as string[]
                ?? (rawNames as object[])?.OfType<string>().ToArray()
                ?? Array.Empty<string>();

            var resolvedConfig = ResolveConfig(spec.ConfigName, configNames, partPathNorm);

            // ── 3. Open the assembly SECOND so it becomes the active doc ────
            int openErrors = 0;
            int openWarnings = 0;
            asmModel = swApp.OpenDoc6(
                FileName: asmPathNorm,
                Type: (int)swDocumentTypes_e.swDocASSEMBLY,
                Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                Configuration: string.Empty,
                Errors: ref openErrors,
                Warnings: ref openWarnings) as IModelDoc2;

            if (asmModel == null)
            {
                throw new McpToolException(
                    $"OpenDoc6 returned null for assembly '{asmPathNorm}'. " +
                    $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}.");
            }

            var asmDoc = (IAssemblyDoc)asmModel;
            var xM = spec.PositionXMm / 1000.0;
            var yM = spec.PositionYMm / 1000.0;
            var zM = spec.PositionZMm / 1000.0;

            // ── 4. AddComponent5 with the size config ───────────────────────
            //   swAddComponentConfigOptions_e has no "existing config" member;
            //   the documented way to pick an existing config is ConfigOption=0
            //   (CurrentSelectedConfig) + ExistingConfigName=<config>.
            var component = asmDoc.AddComponent5(
                CompName: partPathNorm,
                ConfigOption: (int)swAddComponentConfigOptions_e
                    .swAddComponentConfigOptions_CurrentSelectedConfig,
                NewConfigName: string.Empty,
                UseConfigForPartReferences: false,
                ExistingConfigName: resolvedConfig,
                X: xM, Y: yM, Z: zM) as IComponent2;

            if (component == null)
            {
                throw new McpToolException(
                    $"AddComponent5 returned null for Toolbox part '{partPathNorm}' " +
                    $"(config '{resolvedConfig}'). The part was preloaded — check " +
                    "for SW version/permission errors, or whether the Toolbox " +
                    "add-in blocked the insertion.");
            }

            // ── 5. Verify the size config took; force it if SW ignored
            //   ExistingConfigName (belt-and-braces — ReferencedConfiguration
            //   is settable, and this also covers Toolbox add-in interference).
            var referenced = component.ReferencedConfiguration ?? string.Empty;
            var forced = false;
            if (resolvedConfig.Length > 0
                && !string.Equals(referenced, resolvedConfig, StringComparison.Ordinal))
            {
                component.ReferencedConfiguration = resolvedConfig;
                asmModel.EditRebuild3();
                referenced = component.ReferencedConfiguration ?? string.Empty;
                forced = true;

                if (!string.Equals(referenced, resolvedConfig, StringComparison.Ordinal))
                {
                    throw new McpToolException(
                        $"Could not set configuration '{resolvedConfig}' on the " +
                        $"inserted component (it stayed on '{referenced}'). The " +
                        "config exists in the part — this points at a Toolbox " +
                        "add-in override; try inserting without configName and " +
                        "report what the default config is.");
                }
            }

            // ── 5b. Orient the fastener if a rotation was requested (M53-①).
            //   AddComponent5 drops the part at its own orientation — a bolt's
            //   shank along its local axis lands lying flat. Spin it in place
            //   about its frame origin (position preserved). Done after the
            //   config force so its EditRebuild3 can't reset the transform.
            //   All-zero rotation skips this → pre-M53 behaviour preserved.
            if (ComponentTransform.HasRotation(
                spec.RotationXDeg, spec.RotationYDeg, spec.RotationZDeg))
            {
                ComponentTransform.Apply(
                    swApp, asmModel, component,
                    spec.RotationXDeg, spec.RotationYDeg, spec.RotationZDeg);
            }

            // ── 6. Save assembly in-place (M5 lesson: Save3, not SaveAs) ────
            int saveErrors = 0;
            int saveWarnings = 0;
            var savedOk = asmModel.Save3(
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                ref saveErrors,
                ref saveWarnings);

            if (!savedOk || !File.Exists(asmPathNorm))
            {
                throw new McpToolException(
                    $"Save3 failed for assembly '{asmPathNorm}'. " +
                    $"errors=0x{saveErrors:X} warnings=0x{saveWarnings:X}.");
            }

            var partName = Path.GetFileNameWithoutExtension(partPathNorm);
            var configNote = referenced.Length > 0
                ? $" config='{referenced}'{(forced ? " (set post-insert)" : string.Empty)}"
                : string.Empty;
            var rotNote = ComponentTransform.HasRotation(
                spec.RotationXDeg, spec.RotationYDeg, spec.RotationZDeg)
                ? $" rotated ({spec.RotationXDeg}, {spec.RotationYDeg}, " +
                  $"{spec.RotationZDeg})°"
                : string.Empty;
            return ToolResult.Ok(
                message:
                    $"Inserted Toolbox part '{partName}'{configNote} at " +
                    $"({spec.PositionXMm}, {spec.PositionYMm}, {spec.PositionZMm}) mm" +
                    $"{rotNote}; saved assembly in place",
                path: spec.AssemblyPath);
        }
        finally
        {
            // Close in reverse open order (assembly opened last → close first).
            if (asmModel != null)
            {
                swApp.CloseDoc(asmModel.GetTitle());
            }
            swApp.CloseDoc(partModel.GetTitle());
        }
    }

    /// <summary>
    /// Resolve the requested size config against the part's configuration
    /// list: empty request → "" (default config); exact match → as-is;
    /// case-insensitive match → corrected name; otherwise throw with the
    /// available names (first <see cref="MaxConfigsInError"/>, quoted —
    /// parse-friendly so callers/tests can pick a real one).
    /// </summary>
    private static string ResolveConfig(
        string? requested, string[] configNames, string partPath)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return string.Empty;
        }

        var trimmed = requested.Trim();
        var exact = configNames.FirstOrDefault(
            n => string.Equals(n, trimmed, StringComparison.Ordinal));
        if (exact != null)
        {
            return exact;
        }

        var caseInsensitive = configNames.FirstOrDefault(
            n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
        if (caseInsensitive != null)
        {
            return caseInsensitive;
        }

        var partName = Path.GetFileName(partPath);
        var shown = configNames.Take(MaxConfigsInError)
            .Select(n => $"'{n}'");
        var more = configNames.Length > MaxConfigsInError
            ? $" … and {configNames.Length - MaxConfigsInError} more"
            : string.Empty;
        throw new McpToolException(
            $"Configuration '{trimmed}' not found in '{partName}'. " +
            $"Available configurations ({Math.Min(MaxConfigsInError, configNames.Length)} " +
            $"of {configNames.Length}): {string.Join(", ", shown)}{more}. " +
            "Pass one of these exact names as configName.");
    }
#endif
}
