namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Pure (SolidWorks-free) classification of a part's "kind" from its
/// user-meaningful (non-boot) feature type names — so an LLM can tell OUR
/// parametric parts (which it may resize) from imported dumb bodies (fixed
/// anchors it must never edit). Kept SW-free so it is L1-testable.
///
/// Signal (probed on SW 2026, M40): a STEP/neutral import produces a feature
/// whose typeName is <c>MBimport</c> and NO parametric build features; a part we
/// built has build features (ProfileFeature / Extrusion / Revolution / ...) and
/// no import node. The caller passes the list AFTER boot-feature filtering
/// (see <see cref="PartGeometryHelpers.IsBootFeature"/>), so any remaining
/// feature is user-meaningful.
/// </summary>
internal static class PartKind
{
    public const string OurPart = "ourPart";
    public const string Imported = "imported";
    public const string Unknown = "unknown";

    // Feature type names that mark imported / dumb geometry. "MBimport" is the
    // confirmed STEP/IGES/Parasolid neutral-import node on SW 2026 (M40 probe);
    // the others are defensive aliases seen across SW versions/import paths.
    private static readonly HashSet<string> ImportFeatureTypes = new(StringComparer.Ordinal)
    {
        "MBimport",
        "ImportedFeature",
        "Imported",
    };

    public static bool IsImportFeatureType(string typeName) =>
        ImportFeatureTypes.Contains(typeName);

    /// <summary>
    /// Classifies a part from its non-boot feature type names: an import node
    /// wins (<see cref="Imported"/>); otherwise any build feature means it is
    /// <see cref="OurPart"/>; an empty list (no user features) is
    /// <see cref="Unknown"/>.
    /// </summary>
    public static string ClassifyPart(IReadOnlyCollection<string> nonBootTypeNames)
    {
        if (nonBootTypeNames.Any(IsImportFeatureType))
        {
            return Imported;
        }
        return nonBootTypeNames.Count > 0 ? OurPart : Unknown;
    }
}
