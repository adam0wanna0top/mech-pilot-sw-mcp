#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Cross-tool body / feature navigation helpers. Extracted at the
/// rule-of-three threshold:
///   • FindPlanarEndFace — was duplicated in CreateFlangeTool, AddAxialHoleTool,
///     AddThreadedHoleTool (3 copies). Used wherever a tool needs "the part's
///     ±Z end face" for sketch / hole placement.
///   • FindLastUserFeature — was duplicated in MirrorFeatureTool,
///     PatternLinearTool (and the same boot filter lived once more in
///     InspectPartTool's feature-list walker — kept inline there because it
///     returns the full list, not just the last). Used to auto-pick a seed
///     when an LLM doesn't name a feature explicitly.
///
/// Both helpers use body navigation rather than coordinate-based SelectByID2
/// (golden rule #6 in CLAUDE.md — coord ray-cast is unreliable in API mode
/// without an active view).
/// </summary>
internal static class PartGeometryHelpers
{
    /// <summary>
    /// SW-internal "boot" feature type names that show up in every part but
    /// aren't user-meaningful (reference planes / origin / various container
    /// folders). Plus any TypeName ending in "Folder" via the EndsWith check
    /// below — that catches the SW 2026 *Folder containers (CommentsFolder,
    /// SelectionSetFolder, InkMarkupFolder, EnvFolder, ConfigTableFolder, ...)
    /// L2 probed in M9.
    /// </summary>
    private static readonly HashSet<string> BootFeatureTypeNames = new(StringComparer.Ordinal)
    {
        "RefPlane",                  // Front / Top / Right
        "OriginProfileFeature",      // origin point
        "CoordSys",                  // default coordinate system
        "Lights, Cameras and Scene",
        "MateReferences",
        "DimXpertManager",
        "DesignBinder",
        "DetailCabinet",
    };

    /// <summary>
    /// Returns true if a feature is one of the SW-internal "boot" features
    /// that show up in every part (reference planes, *Folder containers, etc.)
    /// — i.e. not a user-meaningful modeling feature.
    /// </summary>
    public static bool IsBootFeature(string typeName) =>
        BootFeatureTypeNames.Contains(typeName) ||
        typeName.EndsWith("Folder", StringComparison.Ordinal);

    /// <summary>
    /// Returns the first planar face on the part's first solid body whose
    /// normal is aligned with the Z axis (cos similarity > 0.99). For a part
    /// extruded from the Front Plane this picks one of the two end faces;
    /// both are equivalent for axially-symmetric operations
    /// (through-all cuts / centered drills).
    /// </summary>
    public static IFace2? FindPlanarEndFace(IModelDoc2 model)
    {
        var part = (IPartDoc)model;
        var bodiesObj = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (bodiesObj is not object[] bodies || bodies.Length == 0)
        {
            return null;
        }
        var body = (IBody2)bodies[0];

        if (body.GetFaces() is not object[] faces)
        {
            return null;
        }

        foreach (var faceObj in faces)
        {
            var face = (IFace2)faceObj;
            var surface = (ISurface)face.GetSurface();
            if (!surface.IsPlane())
            {
                continue;
            }
            if (face.Normal is not double[] normal || normal.Length < 3)
            {
                continue;
            }
            if (Math.Abs(normal[2]) > 0.99)
            {
                return face;
            }
        }
        return null;
    }

    /// <summary>
    /// Walks the feature linked list (FirstFeature → GetNextFeature) and
    /// returns the most recently added user-meaningful feature, skipping SW
    /// boot features via <see cref="IsBootFeature"/>. Returns null if the
    /// part has no user features yet (just reference planes).
    /// </summary>
    public static IFeature? FindLastUserFeature(IModelDoc2 model)
    {
        IFeature? lastUserFeature = null;
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            var typeName = feature.GetTypeName2() ?? feature.GetTypeName() ?? string.Empty;
            if (!IsBootFeature(typeName))
            {
                lastUserFeature = feature;
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return lastUserFeature;
    }
}
#endif
