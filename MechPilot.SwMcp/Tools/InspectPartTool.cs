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
/// Reads metadata from an existing part: bounding box, feature list,
/// face / edge counts. Pure read-only — opens with the ReadOnly flag and
/// closes without saving.
///
/// LLM value: lets an LLM "see" a .sldprt it didn't create. Before this
/// tool the LLM could only guess at a part's size / feature set from the
/// file name or earlier conversation; now it can ask the part directly
/// (e.g. before drilling a Φ30 hole into a D20 cylinder, inspect first).
///
/// Pipeline:
///   1. OpenDoc6 with Silent | ReadOnly.
///   2. Read GetTitle, GetFeatureCount, GetPartBox (raw SI meters).
///   3. For each solid body: GetFaceCount + GetEdgeCount, accumulate.
///   4. Walk the top-level feature list via IGetFeatures(true), collect
///      { name, typeName, suppressed } for each.
///   5. CloseDoc — no save (ReadOnly mode means no dirty state).
///   6. Return ToolResult with both a human-readable Message and a
///      structured Data dictionary (CLI --output json surfaces both).
/// </summary>
[McpServerToolType]
public static class InspectPartTool
{
    [McpServerTool(Name = "inspect_part")]
    [Description(
        "Read metadata from an existing SolidWorks part (read-only). Returns " +
        "the part's title, top-level feature count and list, total face / " +
        "edge count across solid bodies, and a bounding box in millimeters. " +
        "Use this to 'see' a part before editing it — e.g. check the diameter " +
        "before drilling a hole that's too large for the part. inputPath must " +
        "be an absolute path to an existing .sldprt.")]
    public static ToolResult Run(
        [Description("Absolute path to an existing .sldprt to inspect, e.g. C:/tmp/part.sldprt.")]
        string inputPath)
    {
        var spec = new InspectSpec { InputPath = inputPath };
        return RunWithSpec(spec);
    }

    /// <summary>Tool-internal entry. CLI and L1 tests call this directly.</summary>
    public static ToolResult RunWithSpec(InspectSpec spec)
    {
        spec.Validate();

#if HAS_SOLIDWORKS
        try
        {
            return InspectInSw(spec);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException(
                $"inspect_part failed at SW Interop layer: {ex.GetType().Name}: {ex.Message}",
                ex);
        }
#else
        throw new McpToolException(
            "inspect_part requires SolidWorks Interop assemblies, which were " +
            "not present at build time. Build on a machine with SolidWorks installed.");
#endif
    }

#if HAS_SOLIDWORKS
    private static ToolResult InspectInSw(InspectSpec spec)
    {
        var swApp = SwConnection.Instance.GetApp();

        // ── 1. Open the existing part read-only ─────────────────────────────
        //   Silent | ReadOnly: no UI prompts, no dirty state. CloseDoc later
        //   is a clean drop, no Save / Save3 needed (M5 trap structurally
        //   impossible on read-only docs).
        int openErrors = 0;
        int openWarnings = 0;
        const int openOptions =
            (int)swOpenDocOptions_e.swOpenDocOptions_Silent |
            (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;
        var model = swApp.OpenDoc6(
            FileName: spec.InputPath,
            Type: (int)swDocumentTypes_e.swDocPART,
            Options: openOptions,
            Configuration: string.Empty,
            Errors: ref openErrors,
            Warnings: ref openWarnings) as IModelDoc2;

        if (model == null)
        {
            throw new McpToolException(
                $"OpenDoc6 returned null for '{spec.InputPath}'. " +
                $"errors=0x{openErrors:X} warnings=0x{openWarnings:X}. " +
                "(See swFileLoadError_e in swconst.chm.)");
        }

        try
        {
            // ── 2. Document-level metadata ──────────────────────────────────
            var title = model.GetTitle();
            var part = (IPartDoc)model;

            // GetPartBox(NoConversion=true) returns raw SI meters (no unit
            // scaling), as a Variant SAFEARRAY of 6 doubles:
            //   [minX, minY, minZ, maxX, maxY, maxZ].
            var boundingBox = ReadBoundingBoxMm(part);

            // ── 3. Body iteration: face + edge counts ───────────────────────
            var (bodyCount, totalFaceCount, totalEdgeCount) = CountBodyEntities(part);

            // ── 4. Top-level feature walk ───────────────────────────────────
            var features = ReadTopLevelFeatures(model);

            // ── 5. Build human summary + structured payload ────────────────
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
                    $"{totalFaceCount} faces, {totalEdgeCount} edges",
                data: data);
        }
        finally
        {
            // Read-only doc: clean drop, no Save needed.
            swApp.CloseDoc(model.GetTitle());
        }
    }

    /// <summary>
    /// Reads <c>GetPartBox(NoConversion=true)</c> (raw SI meters) and returns
    /// a {minX,minY,minZ,maxX,maxY,maxZ} dictionary in **mm**. Returns null
    /// for empty parts (no solid bodies).
    /// </summary>
    private static Dictionary<string, double>? ReadBoundingBoxMm(IPartDoc part)
    {
        var bboxObj = part.GetPartBox(NoConversion: true);
        if (bboxObj is not double[] bbox || bbox.Length < 6)
        {
            return null;
        }
        // SW returns 0..0..0..0..0..0 for parts without bodies; treat as null.
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
    /// Walks the top-level feature linked list via IFeature.GetNextFeature
    /// and returns only the user-meaningful features (no SW-internal boot
    /// nodes). Boot filter lives in
    /// <see cref="Internal.PartGeometryHelpers.IsBootFeature"/> — single
    /// source of truth shared with mirror_feature / pattern_linear's
    /// auto-pick of the seed feature.
    /// </summary>
    private static List<Dictionary<string, object>> ReadTopLevelFeatures(IModelDoc2 model)
    {
        var features = new List<Dictionary<string, object>>();
        var feature = model.FirstFeature() as IFeature;
        while (feature != null)
        {
            var typeName = feature.GetTypeName2() ?? feature.GetTypeName() ?? "";
            if (!Internal.PartGeometryHelpers.IsBootFeature(typeName))
            {
                features.Add(new Dictionary<string, object>
                {
                    ["name"] = feature.Name ?? "",
                    ["typeName"] = typeName,
                    ["suppressed"] = feature.IsSuppressed(),
                });
            }
            feature = feature.GetNextFeature() as IFeature;
        }
        return features;
    }
#endif
}
