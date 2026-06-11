#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Exceptions;
using SolidWorks.Interop.sldworks;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Shared feature lookup for the M48 feature-management pair
/// (DeleteFeatureTool / SuppressFeatureTool): exact-name walk of the feature
/// tree + a guard that refuses reference/boot geometry (default planes,
/// origin, folders — and any RefPlane, since deleting one cascades into the
/// sketches built on it).
/// </summary>
internal static class FeatureLookup
{
    /// <summary>
    /// Finds the feature with the exact given name (suppressed features are
    /// still in the tree, so an unsuppress can find its target). Throws a
    /// friendly <see cref="McpToolException"/> when absent.
    /// </summary>
    public static IFeature RequireFeatureByName(IModelDoc2 model, string featureName)
    {
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            if (string.Equals(feature.Name, featureName, StringComparison.Ordinal))
            {
                return feature;
            }
            feature = feature.GetNextFeature() as IFeature;
        }

        throw new McpToolException(
            $"No feature named '{featureName}' on the part. Call inspect_active / " +
            "inspect_part and use an exact name from the features list.");
    }

    /// <summary>
    /// Refuses reference/boot geometry for destructive ops. Boot types come
    /// from <see cref="PartGeometryHelpers.IsBootFeature"/> (RefPlane / origin /
    /// CoordSys / *Folder ...) — that covers the default planes AND ref planes
    /// from add_ref_plane (deleting those would cascade into their sketches).
    /// </summary>
    public static void RejectBootFeature(IFeature feature, string verb)
    {
        var typeName = feature.GetTypeName2() ?? feature.GetTypeName() ?? string.Empty;
        if (PartGeometryHelpers.IsBootFeature(typeName))
        {
            throw new McpToolException(
                $"Refusing to {verb} '{feature.Name}' (type {typeName}) — reference/" +
                "boot geometry (planes, origin, folders) is protected. Only model " +
                "features (extrudes, cuts, fillets, sketches, ...) can be " + verb + "d.");
        }
    }
}
#endif
