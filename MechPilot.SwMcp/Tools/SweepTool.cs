using System.ComponentModel;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using ModelContextProtocol.Server;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace MechPilot.SwMcp.Tools;

/// <summary>
/// Sweep a profile sketch along a path sketch into a solid body. M33 —
/// switched from M32's <c>InsertProtrusionSwept</c> (14 args, silent-fails
/// on orientation edge cases) to v1 PR #27's verified path:
/// <c>CreateDefinition(swFmSweep=17) + setattr + CreateFeature</c>.
///
/// swFmSweep = 17 (reflected from swFeatureNameID_e; CHM does not expose
/// the integer value). The returned <c>ISweepFeatureData</c> has properties
/// for Profile / Path / Merge / TangentPropagation / etc — set them, then
/// call CreateFeature(def) to materialize the feature.
///
/// Pipeline:
///   1. Find Profile + Path sketch features by name (walk FM via
///      FindFeatureByName, similar to FindLastUserFeature).
///   2. Pull ISketch instances out of the features via GetSpecificFeature2.
///   3. CreateDefinition(17) → cast to ISweepFeatureData.
///   4. def.Profile = profileSketch; def.Path = pathSketch; def.Merge = true.
///   5. CreateFeature(def).
///
/// This path is robust to the profile/path orientation issues that broke
/// InsertProtrusionSwept's 14-arg version (e.g. Front Plane circle +
/// Top Plane line silent-failing in M32).
/// </summary>
[McpServerToolType]
public static class SweepTool
{
    [McpServerTool(Name = "sweep")]
    [Description(
        "Sweep a profile sketch along a path sketch into a solid body. " +
        "profileSketchName must be a closed-area sketch (the cross-section); " +
        "pathSketchName must be an open-curve sketch (the trajectory). Both " +
        "names from end_sketch. Common uses: pipes / cables / fan blades / " +
        "cams / extrusions along curved paths. Internally uses the v1-verified " +
        "CreateDefinition(swFmSweep=17) + setattr + CreateFeature path " +
        "(reflected ISweepFeatureData properties), which is robust to the " +
        "profile/path orientation issues that affect the 14-arg InsertProtrusionSwept API.")]
    public static ToolResult Run(
        [Description("Name of the cross-section profile sketch (closed area).")]
        string profileSketchName,
        [Description("Name of the path sketch (open curve).")]
        string pathSketchName)
    {
        return RunWithSpec(new SweepSpec
        {
            ProfileSketchName = profileSketchName,
            PathSketchName = pathSketchName,
        });
    }

    public static ToolResult RunWithSpec(SweepSpec spec)
    {
        spec.Validate();
#if HAS_SOLIDWORKS
        try { return RunSw(spec); }
        catch (McpToolException) { throw; }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"sweep failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}", ex);
        }
#else
        throw new McpToolException("sweep requires SolidWorks Interop assemblies.");
#endif
    }

#if HAS_SOLIDWORKS
    // Reflected from swFeatureNameID_e (CHM doesn't expose the integer values).
    private const int SwFmSweep = 17;

    private static ToolResult RunSw(SweepSpec spec)
    {
        var model = Internal.SketchSession.RequireActiveDoc();
        var fm = model.FeatureManager;

        // ── 1. Find both sketch features by name ────────────────────────────
        var profileFeature = FindSketchFeatureByName(model, spec.ProfileSketchName)
            ?? throw new McpToolException(
                $"Cannot find sketch '{spec.ProfileSketchName}' on the active part.");
        var pathFeature = FindSketchFeatureByName(model, spec.PathSketchName)
            ?? throw new McpToolException(
                $"Cannot find sketch '{spec.PathSketchName}' on the active part.");

        // ── 2. Sanity-verify the features are sketches via GetSpecificFeature2.
        //   The Profile / Path properties on ISweepFeatureData take the
        //   sketch's IFeature (not the ISketch interface) — SW marshals the
        //   IDispatch via the underlying feature wrapper.
        if (profileFeature.GetSpecificFeature2() is not ISketch)
        {
            throw new McpToolException(
                $"'{spec.ProfileSketchName}' is not a sketch feature.");
        }
        if (pathFeature.GetSpecificFeature2() is not ISketch)
        {
            throw new McpToolException(
                $"'{spec.PathSketchName}' is not a sketch feature.");
        }

        // ── 3. CreateDefinition(17) — returns ISweepFeatureData ────────────
        if (fm.CreateDefinition(SwFmSweep) is not ISweepFeatureData def)
        {
            throw new McpToolException(
                $"FeatureManager.CreateDefinition(swFmSweep={SwFmSweep}) returned null " +
                "or non-sweep type. SW Interop may have changed signature.");
        }

        // ── 4. AccessSelections + set Profile/Path + ReleaseSelectionAccess ──
        //   v1 PR #21/#27 pattern: CreateDefinition path needs AccessSelections
        //   wrapping the setattr calls or attributes silently don't bind.
        if (!def.AccessSelections(model, null))
        {
            throw new McpToolException(
                "ISweepFeatureData.AccessSelections returned false — cannot bind " +
                "Profile/Path attributes.");
        }

        def.Profile = profileFeature;
        def.Path = pathFeature;
        def.Merge = true;
        def.TangentPropagation = false;
        def.ThinFeature = false;
        def.AdvancedSmoothing = false;
        def.MaintainTangency = false;
        def.FeatureScope = true;
        def.AutoSelect = true;

        // ── 5. CreateFeature(def) ───────────────────────────────────────────
        var feature = fm.CreateFeature(def);
        def.ReleaseSelectionAccess();
        if (feature == null)
        {
            throw new McpToolException(
                $"CreateFeature(SweepFeatureData) returned null for profile " +
                $"'{spec.ProfileSketchName}' path '{spec.PathSketchName}'. " +
                "Common causes: profile is not closed, path is not a single " +
                "open curve, or profile/path planes are incompatible.");
        }

        var featureName = feature.Name ?? "(unnamed)";
        return ToolResult.Ok(
            message: $"Swept profile '{spec.ProfileSketchName}' along path " +
                     $"'{spec.PathSketchName}' → feature '{featureName}' (via CreateDefinition path)",
            path: null);
    }

    /// <summary>
    /// Walks the feature manager design tree and returns the first feature
    /// whose Name equals <paramref name="name"/>. Used to resolve sketch
    /// names ("草图1" etc.) to <see cref="IFeature"/> instances for the
    /// CreateDefinition sweep path.
    /// </summary>
    private static IFeature? FindSketchFeatureByName(IModelDoc2 model, string name)
    {
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            if (string.Equals(feature.Name, name, StringComparison.Ordinal))
            {
                return feature;
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return null;
    }
#endif
}
