using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Turn the ACTIVE sketch's single circle into a helix curve (M50) — the
/// missing path primitive for springs, swept threads, and spiral features.
///
/// SW recipe (reflection-verified): <c>IModelDoc2.InsertHelix(Reversed,
/// Clockwised, Tapered, Outward, Helixdef, Height, Pitch, Revolution,
/// TaperAngle, Startangle)</c> — defined by pitch + revolutions
/// (swHelixDefinedByPitchAndRevolution = 0), untapered. The call returns
/// VOID, so success is detected by diffing the feature tree before/after
/// (the M35 rib pattern); the new feature's name is returned for use as a
/// sweep PATH (sweep selects it via the REFERENCECURVES fallback).
///
/// The helix consumes the active sketch (its circle = helix diameter) and
/// grows along the sketch plane's normal from the sketch plane.
/// </summary>
[McpServerToolType]
public static class InsertHelixTool
{
    [McpServerTool(Name = "insert_helix")]
    [Description(
        "Turn the ACTIVE sketch's single circle into a helix curve — the path " +
        "primitive for springs, swept threads, and spirals. Workflow: " +
        "start_sketch → sketch_circle (the circle Ø = helix Ø) → insert_helix " +
        "(do NOT end_sketch first — the helix consumes the active sketch). " +
        "pitch is the axial distance per revolution (mm); revolutions may be " +
        "fractional; the helix grows along the sketch plane's normal " +
        "(reverse=true flips it; height = pitch × revolutions). Returns the " +
        "helix feature's name — pass it to sweep as pathSketchName (sweep " +
        "accepts curve features as paths). E.g. spring: front-plane circle " +
        "Ø30 → insert_helix pitch 8 rev 5 → top-plane wire-profile circle at " +
        "(15, 0) → sweep.")]
    public static ToolResult Run(
        [Description("Axial distance per revolution in mm. Must be > 0.")]
        double pitch,
        [Description("Number of revolutions (> 0, fractions allowed), e.g. 5.")]
        double revolutions,
        [Description("If true, grow against the sketch plane's normal. Default false.")]
        bool reverse = false,
        [Description("True (default) = clockwise winding; false = counter-clockwise.")]
        bool clockwise = true,
        [Description("Start angle on the base circle in degrees [0, 360). Default 0.")]
        double startAngle = 0)
    {
        return RunWithSpec(new InsertHelixSpec
        {
            PitchMm = pitch,
            Revolutions = revolutions,
            Reverse = reverse,
            Clockwise = clockwise,
            StartAngleDeg = startAngle,
        });
    }

    public static ToolResult RunWithSpec(InsertHelixSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"insert_helix failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("insert_helix requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult RunSw(InsertHelixSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        _ = Internal.SketchSession.RequireActiveSketch();

        // InsertHelix returns void — diff the feature tree to detect success
        // (M35 rib pattern: SW's void-returning inserts fail silently).
        var before = CollectFeatureNames(model);

        model.InsertHelix(
            Reversed: spec.Reverse,
            Clockwised: spec.Clockwise,
            Tapered: false,
            Outward: false,
            Helixdef: 0,                                    // swHelixDefinedByPitchAndRevolution
            Height: 0.0,
            Pitch: spec.PitchMm / 1000.0,
            Revolution: spec.Revolutions,
            TaperAngle: 0.0,
            Startangle: spec.StartAngleDeg * Math.PI / 180.0);

        var newHelix = FindNewHelixFeature(model, before);
        if (newHelix == null)
        {
            throw new McpToolException(
                $"InsertHelix did not create a helix feature (pitch {spec.PitchMm} mm × " +
                $"{spec.Revolutions} rev). The ACTIVE sketch must contain exactly ONE " +
                "circle (the helix diameter) and nothing else — start_sketch → " +
                "sketch_circle → insert_helix, without end_sketch in between.");
        }

        var heightMm = spec.PitchMm * spec.Revolutions;
        return ToolResult.Ok(
            message: $"Inserted helix '{newHelix}' (pitch {spec.PitchMm} mm × {spec.Revolutions} rev " +
                     $"= height {heightMm} mm) — use as a sweep path",
            path: null);
    }

    private static HashSet<string> CollectFeatureNames(IModelDoc2 model)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            names.Add(feature.Name);
            feature = feature.GetNextFeature() as IFeature;
        }
        return names;
    }

    /// <summary>
    /// Finds a feature that was not in <paramref name="before"/> and whose
    /// type name marks it as a helix curve ("Helix" / "HelixSpiral" — SW
    /// names drift, so a contains-match is used). Null when nothing new.
    /// </summary>
    private static string? FindNewHelixFeature(IModelDoc2 model, HashSet<string> before)
    {
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            if (!before.Contains(feature.Name))
            {
                var typeName = feature.GetTypeName2() ?? feature.GetTypeName() ?? string.Empty;
                if (typeName.Contains("Helix", StringComparison.OrdinalIgnoreCase))
                {
                    return feature.Name;
                }
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return null;
    }
#endif
}
