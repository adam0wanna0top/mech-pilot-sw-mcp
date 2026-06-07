#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Reads the mates from an assembly model — who connects to whom, the mate type,
/// and (for distance / angle mates) the value — so the resize orchestrator can
/// see how components are constrained (and later adjust the values). M41.
///
/// Traversal is the canonical one: walk the top-level features, descend into each
/// "MateGroup" folder via GetFirstSubFeature / GetNextSubFeature, and cast each
/// sub-feature's <c>GetSpecificFeature2()</c> to <see cref="IMate2"/>. Per the
/// M40 NoPIA lesson, the <c>object</c>-returning COM calls (GetFirstSubFeature /
/// GetNextSubFeature / GetSpecificFeature2) are collapsed into explicit
/// <c>object</c> locals before use so they don't dynamic-dispatch.
/// </summary>
internal static class MateReader
{
    public static List<Dictionary<string, object>> ReadMates(IModelDoc2 model)
    {
        var mates = new List<Dictionary<string, object>>();
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            if ((feature.GetTypeName2() ?? string.Empty) == "MateGroup")
            {
                object subObj = feature.GetFirstSubFeature();
                while (subObj is IFeature sub)
                {
                    object specific = sub.GetSpecificFeature2();
                    if (specific is IMate2 mate)
                    {
                        mates.Add(ReadOneMate(sub, mate));
                    }
                    subObj = sub.GetNextSubFeature();
                }
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return mates;
    }

    private static Dictionary<string, object> ReadOneMate(IFeature mateFeat, IMate2 mate)
    {
        var components = new List<string>();
        var count = mate.GetMateEntityCount();
        for (var i = 0; i < count; i++)
        {
            if (mate.MateEntity(i) is IMateEntity2 ent &&
                ent.ReferenceComponent is IComponent2 comp)
            {
                components.Add(comp.Name2 ?? string.Empty);
            }
        }

        var info = new Dictionary<string, object>
        {
            ["name"] = mateFeat.Name ?? string.Empty,
            ["type"] = MateType.Name(mate.Type),
            ["components"] = components,
        };

        // Distance / angle mates expose a single editable value via their display
        // dimension (SI → mm / deg via the shared DimensionFormat helper, M39).
        if (MateType.HasValue(mate.Type) &&
            mate.DisplayDimension is IDisplayDimension disp &&
            disp.GetDimension2(0) is IDimension dim)
        {
            var (value, unit) = DimensionFormat.ToDisplay(disp.Type2, dim.SystemValue);
            info["value"] = value;
            info["unit"] = unit;
        }
        return info;
    }
}
#endif
