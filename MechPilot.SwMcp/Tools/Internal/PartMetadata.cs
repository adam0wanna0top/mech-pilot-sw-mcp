#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Shared part-metadata reader for the two inspect tools (M36 refactor,
/// rule-of-two): <see cref="InspectPartTool"/> (opens a .sldprt read-only)
/// and <see cref="InspectActiveTool"/> (reads the active doc in place). Both
/// produce the identical bbox / feature-list (each feature carries its editable
/// dimensions) / face+edge-count <see cref="ToolResult"/>; only the doc source
/// + lifecycle differ.
///
/// This helper neither opens nor closes documents — the caller owns the doc
/// lifecycle (open+close for inspect_part, leave-open for inspect_active).
/// </summary>
internal static class PartMetadata
{
    /// <summary>
    /// Builds the bbox / feature / face+edge <see cref="ToolResult"/> for a
    /// part document. Throws <see cref="McpToolException"/> if the doc is not
    /// a part (e.g. an assembly or drawing is active).
    /// </summary>
    public static ToolResult Build(IModelDoc2 model)
    {
        if (model is not IPartDoc part)
        {
            throw new McpToolException(
                "Inspection requires a part document; the active document is not a part " +
                "(it may be an assembly or drawing).");
        }

        var title = model.GetTitle();
        var boundingBox = ReadBoundingBoxMm(part);
        var (bodyCount, totalFaceCount, totalEdgeCount) = CountBodyEntities(part);
        var features = ReadTopLevelFeatures(model);
        var editableDimCount = features.Sum(
            f => ((List<Dictionary<string, object>>)f["dimensions"]).Count);

        var sizeXMm = boundingBox is null ? 0 : boundingBox["maxX"] - boundingBox["minX"];
        var sizeYMm = boundingBox is null ? 0 : boundingBox["maxY"] - boundingBox["minY"];
        var sizeZMm = boundingBox is null ? 0 : boundingBox["maxZ"] - boundingBox["minZ"];
        var sizeLabel = boundingBox is null
            ? "(no bounding box)"
            : $"{sizeXMm:G6} × {sizeYMm:G6} × {sizeZMm:G6} mm";
        var featureLabel = features.Count switch
        {
            0 => "no features",
            1 => "1 feature",
            _ => $"{features.Count} features",
        };

        var data = new Dictionary<string, object>
        {
            ["title"] = title,
            ["featureCount"] = features.Count,
            ["bodyCount"] = bodyCount,
            ["totalFaceCount"] = totalFaceCount,
            ["totalEdgeCount"] = totalEdgeCount,
            ["editableDimensionCount"] = editableDimCount,
            ["features"] = features,
            ["sizeMm"] = new Dictionary<string, double>
            {
                ["x"] = sizeXMm,
                ["y"] = sizeYMm,
                ["z"] = sizeZMm,
            },
        };
        if (boundingBox is not null)
        {
            data["boundingBoxMm"] = boundingBox;
        }

        return ToolResult.Ok(
            message:
                $"'{title}': {sizeLabel}; {bodyCount} body, {featureLabel}, " +
                $"{totalFaceCount} faces, {totalEdgeCount} edges, " +
                $"{editableDimCount} editable dims",
            data: data);
    }

    /// <summary>
    /// Reads <c>GetPartBox(NoConversion=true)</c> (raw SI meters) and returns a
    /// {minX,minY,minZ,maxX,maxY,maxZ} dictionary in **mm**. Returns null for
    /// empty parts (no solid bodies).
    /// </summary>
    private static Dictionary<string, double>? ReadBoundingBoxMm(IPartDoc part)
    {
        var bboxObj = part.GetPartBox(NoConversion: true);
        if (bboxObj is not double[] bbox || bbox.Length < 6)
        {
            return null;
        }
        // SW returns all-zero for parts without bodies; treat as null.
        if (bbox[0] == 0 && bbox[1] == 0 && bbox[2] == 0 &&
            bbox[3] == 0 && bbox[4] == 0 && bbox[5] == 0)
        {
            return null;
        }
        return new Dictionary<string, double>
        {
            ["minX"] = bbox[0] * 1000.0,
            ["minY"] = bbox[1] * 1000.0,
            ["minZ"] = bbox[2] * 1000.0,
            ["maxX"] = bbox[3] * 1000.0,
            ["maxY"] = bbox[4] * 1000.0,
            ["maxZ"] = bbox[5] * 1000.0,
        };
    }

    private static (int bodyCount, int totalFaceCount, int totalEdgeCount) CountBodyEntities(IPartDoc part)
    {
        var bodiesObj = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (bodiesObj is not object[] bodies || bodies.Length == 0)
        {
            return (0, 0, 0);
        }
        var totalFaces = 0;
        var totalEdges = 0;
        foreach (var bodyObj in bodies)
        {
            var body = (IBody2)bodyObj;
            totalFaces += body.GetFaceCount();
            totalEdges += body.GetEdgeCount();
        }
        return (bodies.Length, totalFaces, totalEdges);
    }

    /// <summary>
    /// Walks the top-level feature linked list and returns the user-meaningful
    /// features (boot nodes filtered via
    /// <see cref="PartGeometryHelpers.IsBootFeature"/>).
    /// </summary>
    private static List<Dictionary<string, object>> ReadTopLevelFeatures(IModelDoc2 model)
    {
        var features = new List<Dictionary<string, object>>();
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            var typeName = feature.GetTypeName2() ?? feature.GetTypeName() ?? "";
            if (!PartGeometryHelpers.IsBootFeature(typeName))
            {
                features.Add(new Dictionary<string, object>
                {
                    ["name"] = feature.Name ?? "",
                    ["typeName"] = typeName,
                    ["suppressed"] = feature.IsSuppressed(),
                    ["dimensions"] = ReadFeatureDimensions(feature),
                });
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return features;
    }

    /// <summary>
    /// Reads a feature's display dimensions as {name, value, unit} dicts so an
    /// LLM can see what is editable before calling modify_feature. <c>name</c>
    /// is the "D1@&lt;feature&gt;" handle modify_feature consumes; <c>value</c>
    /// is mm for length dimensions and degrees for angular ones; <c>unit</c> is
    /// "mm" / "deg". Undimensioned features (e.g. our generic sketches) yield an
    /// empty list. Pure read walk (GetFirstDisplayDimension → GetNextDisplayDimension):
    /// no GetDefinition/ModifyDefinition round-trip, so immune to the M38 NoPIA trap.
    /// </summary>
    private static List<Dictionary<string, object>> ReadFeatureDimensions(IFeature feature)
    {
        var dims = new List<Dictionary<string, object>>();
        var dispObj = feature.GetFirstDisplayDimension();
        while (dispObj is IDisplayDimension disp)
        {
            if (disp.GetDimension2(0) is IDimension dim)
            {
                var (value, unit) = DimensionFormat.ToDisplay(disp.Type2, dim.SystemValue);
                dims.Add(new Dictionary<string, object>
                {
                    ["name"] = $"{dim.Name}@{feature.Name}",
                    ["value"] = value,
                    ["unit"] = unit,
                });
            }
            dispObj = feature.GetNextDisplayDimension(dispObj);
        }
        return dims;
    }
}
#endif
