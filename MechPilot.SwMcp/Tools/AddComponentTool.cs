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
/// Inserts one component (.sldprt or sub-.sldasm) into an existing assembly
/// at a given (x, y, z) world position in mm. Components are placed but
/// **not mated** — mating is a separate concern (future add_mate tool).
///
/// v1 PR #9 critical lesson: **`AddComponent5` does NOT auto-load the
/// component file** — calling it on an unloaded part silently returns
/// null. Workaround: <c>OpenDoc6</c> the component first to preload it
/// into SW memory, then call AddComponent5.
///
/// Pipeline:
///   1. OpenDoc6 the assembly (Silent, read-write).
///   2. OpenDoc6 the component (Silent) — preload into SW memory so
///      AddComponent5 can find it.
///   3. AddComponent5(componentPath, 0=default config, "", false, "", x, y, z)
///   4. Save3 the assembly (in-place; M5 lesson — don't SaveAs(samepath)).
///   5. CloseDoc both component and assembly (in finally).
/// </summary>
[McpServerToolType]
public static class AddComponentTool
{
    [McpServerTool(Name = "add_component")]
    [Description(
        "Insert one component (.sldprt or sub-.sldasm) into an existing " +
        "SolidWorks assembly at a given (positionX, positionY, positionZ) " +
        "world position in mm, optionally rotated by (rotationX/Y/Z) degrees. " +
        "The component is placed but not mated — for mating use add_mate_*. " +
        "assemblyPath must be an absolute path to an existing .sldasm. " +
        "componentPath must be an absolute path to an existing .sldprt or " +
        ".sldasm. Position defaults to (0, 0, 0); rotation to (0, 0, 0) = the " +
        "part's own orientation. Use rotation to orient a part whose useful " +
        "axis isn't aligned with the assembly (e.g. a part modelled along its " +
        "local axis that should stand up along assembly Z — rotate 90° about " +
        "the appropriate axis). Rotation is applied about the world origin " +
        "before the part is moved to position. Set skipIfPresent=true to make " +
        "the insert idempotent: if an instance of the same component file is " +
        "already in the assembly it is left unchanged (no duplicate) — use this " +
        "when retrying after a possible half-failed insert (a dropped " +
        "connection can leave a ghost instance). Leave it false (default) for " +
        "legitimate repeated instances of the same part (e.g. four identical " +
        "bolts).")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldasm to insert into.")]
        string assemblyPath,
        [Description("Absolute path to the .sldprt or .sldasm component to insert.")]
        string componentPath,
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
        double rotationZ = 0,
        [Description("Skip the insert if the same component file is already present (idempotent retry). Default false.")]
        bool skipIfPresent = false)
    {
        var spec = new AddComponentSpec
        {
            AssemblyPath = assemblyPath,
            ComponentPath = componentPath,
            PositionXMm = positionX,
            PositionYMm = positionY,
            PositionZMm = positionZ,
            RotationXDeg = rotationX,
            RotationYDeg = rotationY,
            RotationZDeg = rotationZ,
            SkipIfPresent = skipIfPresent,
        };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(AddComponentSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return AddComponentInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"add_component failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "add_component requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult AddComponentInSw(AddComponentSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 0. Normalize paths to OS-canonical form (Windows backslashes) ───
        //   **M20 critical lesson**: `AddComponent5` compares the component
        //   path string against SW's internal doc-table key, which is stored
        //   in OS-canonical form (Windows = backslash). If we pass a
        //   forward-slash path (which OpenDoc6 happily accepts), the doc gets
        //   loaded but AddComponent5 can't find it → silently returns null.
        //   L2 M16-assembly never hit this because PowerShell's Join-Path
        //   produces backslash paths; LLMs and bash typically pass slash
        //   paths and trigger the bug.
        //   Path.GetFullPath canonicalizes (mixed slashes → all backslash on
        //   Windows) without touching the filesystem.
        var asmPathNorm = Path.GetFullPath(spec.AssemblyPath);
        var compPathNorm = Path.GetFullPath(spec.ComponentPath);

        // ── 1. Open the component FIRST (v1 PR #9: AddComponent5 doesn't
        //   auto-load files — silently returns null otherwise. OpenDoc6
        //   preloads into SW memory). Opening component first then assembly
        //   makes the assembly naturally the active doc (SW activates the
        //   most-recently-opened doc), so no ActivateDoc3 dance needed. ──
        var compTypeIsAsm = compPathNorm.EndsWith(
            ".sldasm", StringComparison.OrdinalIgnoreCase);
        int compErrors = 0;
        int compWarnings = 0;
        var compModel = swApp.OpenDoc6(
            FileName: compPathNorm,
            Type: compTypeIsAsm
                ? (int)swDocumentTypes_e.swDocASSEMBLY
                : (int)swDocumentTypes_e.swDocPART,
            Options: (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            Configuration: string.Empty,
            Errors: ref compErrors,
            Warnings: ref compWarnings) as IModelDoc2;

        if (compModel == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for component '{compPathNorm}'. " +
                $"errors=0x{compErrors:X} warnings=0x{compWarnings:X}.");
        }

        IModelDoc2? asmModel = null;
        try
        {
            // ── 2. Open the assembly SECOND so it becomes the active doc ───
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

            // ── 3. Idempotency guard (M53-④): when skipIfPresent, don't
            //   re-insert a component already in the assembly (dedup by source
            //   file path). Makes retrying a half-failed insert safe — a
            //   dropped connection mid-insert can leave a ghost instance, and a
            //   naive retry would then produce a duplicate. Default false keeps
            //   legitimate multi-instance inserts (e.g. four bolts) working. ──
            if (spec.SkipIfPresent)
            {
                var existing = CountInstancesByPath(asmDoc, compPathNorm);
                if (existing > 0)
                {
                    var presentName = Path.GetFileNameWithoutExtension(compPathNorm);
                    return ToolResult.Ok(
                        message:
                            $"Component '{presentName}' already present " +
                            $"({existing} instance(s)) — skipped (skipIfPresent); " +
                            "assembly unchanged.",
                        path: spec.AssemblyPath);
                }
            }

            var xM = spec.PositionXMm / 1000.0;
            var yM = spec.PositionYMm / 1000.0;
            var zM = spec.PositionZMm / 1000.0;

            // ── 4. AddComponent5 — use normalized path so SW's internal
            //   doc-table key match succeeds (M20 lesson) ─────────────────────
            //   ConfigOption = 0: use default config; NewConfigName / Existing
            //   left empty. UseConfigForPartReferences = false.
            var component = asmDoc.AddComponent5(
                CompName: compPathNorm,
                ConfigOption: 0,
                NewConfigName: string.Empty,
                UseConfigForPartReferences: false,
                ExistingConfigName: string.Empty,
                X: xM, Y: yM, Z: zM) as IComponent2;

            if (component == null)
            {
                throw new McpToolException(
                    $"AddComponent5 returned null for '{compPathNorm}'. " +
                    "Common causes: the component was not preloaded (we did " +
                    "preload it — check SW console for permission / version " +
                    "errors), or the assembly was not the active doc when called.");
            }

            // ── 4b. Orient the component if a rotation was requested (M53-①).
            //   AddComponent5 has no orientation argument — it always drops the
            //   part at its own orientation. When rotation is asked for, spin it
            //   in place about its frame origin (position preserved). All-zero
            //   rotation skips this entirely → the default path stays
            //   byte-identical to pre-M53 behaviour.
            if (ComponentTransform.HasRotation(
                spec.RotationXDeg, spec.RotationYDeg, spec.RotationZDeg))
            {
                ComponentTransform.Apply(
                    swApp, asmModel, component,
                    spec.RotationXDeg, spec.RotationYDeg, spec.RotationZDeg);
            }

            // ── 5. Save assembly in-place (M5 lesson: Save3 not SaveAs) ─────
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

            var compName = Path.GetFileNameWithoutExtension(compPathNorm);
            var rotNote = ComponentTransform.HasRotation(
                spec.RotationXDeg, spec.RotationYDeg, spec.RotationZDeg)
                ? $" rotated ({spec.RotationXDeg}, {spec.RotationYDeg}, " +
                  $"{spec.RotationZDeg})°"
                : string.Empty;
            return ToolResult.Ok(
                message:
                    $"Inserted '{compName}' at ({spec.PositionXMm}, {spec.PositionYMm}, " +
                    $"{spec.PositionZMm}) mm{rotNote}; saved assembly in place",
                path: spec.AssemblyPath);
        }
        finally
        {
            // Close in reverse open order (asm opened last → close first).
            if (asmModel != null)
            {
                swApp.CloseDoc(asmModel.GetTitle());
            }
            swApp.CloseDoc(compModel.GetTitle());
        }
    }

    /// <summary>
    /// Counts top-level component instances whose source file is
    /// <paramref name="normPath"/> (the M20-normalized component path).
    /// GetPathName is normalized + compared case-insensitively so a
    /// forward-slash input still matches SW's backslash-canonical store.
    /// Suppressed / ghost instances still carry a path, so they are counted —
    /// which is exactly what the idempotency guard needs to detect.
    /// </summary>
    private static int CountInstancesByPath(IAssemblyDoc asmDoc, string normPath)
    {
        var count = 0;
        object compsObj = asmDoc.GetComponents(true);
        if (compsObj is not object[] comps)
        {
            return count;
        }
        foreach (var c in comps)
        {
            if (c is not IComponent2 comp)
            {
                continue;
            }
            var p = comp.GetPathName();
            if (string.IsNullOrEmpty(p))
            {
                continue;
            }
            string pNorm;
            try { pNorm = Path.GetFullPath(p); }
            catch { pNorm = p; }
            if (string.Equals(pNorm, normPath, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return count;
    }
#endif
}
