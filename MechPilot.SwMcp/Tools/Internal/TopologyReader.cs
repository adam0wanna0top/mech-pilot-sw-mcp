#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Reads the per-face / per-edge topology of a part (M51) — the geometric
/// "addresses" an LLM needs before precise solid operations can exist
/// ("round THIS edge", "cut into THAT face").
///
/// Per face: enumeration index, surface type (plane / cylinder / cone /
/// sphere / torus / other, via ISurface.Is* — no magic Identity constants),
/// area (mm²), bbox center (mm — an identification anchor, NOT the true
/// centroid), plus normal (planes) or axis+radius (cylinders).
/// Per edge: index, curve type (line / circle / other), length (mm), plus
/// endpoints (lines) or center+radius (circles).
///
/// NoPIA discipline throughout: every COM getter that returns object is
/// captured into an explicit object local before casting (M40/M43 lesson).
/// Enumeration order is SW's body face/edge order — stable for an unchanged
/// part, NOT stable across rebuilds; re-inspect after any edit.
/// </summary>
internal static class TopologyReader
{
    /// <summary>Cap on returned faces/edges — keeps pathological parts (large
    /// patterns) from flooding the LLM context. Counts are always exact.</summary>
    private const int MaxEntries = 200;

    public static ToolResult Build(IModelDoc2 model)
    {
        var title = model.GetTitle() ?? "(untitled)";
        object bodiesObj = ((IPartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false);

        var faces = new List<Dictionary<string, object>>();
        var edges = new List<Dictionary<string, object>>();
        var totalFaces = 0;
        var totalEdges = 0;
        var bodyCount = 0;

        if (bodiesObj is object[] bodies)
        {
            bodyCount = bodies.Length;
            foreach (var bodyObj in bodies)
            {
                var body = (IBody2)bodyObj;
                ReadFaces(body, faces, ref totalFaces);
                ReadEdges(body, edges, ref totalEdges);
            }
        }

        var data = new Dictionary<string, object>
        {
            ["title"] = title,
            ["bodyCount"] = bodyCount,
            ["faceCount"] = totalFaces,
            ["edgeCount"] = totalEdges,
            ["faces"] = faces,
            ["edges"] = edges,
        };
        if (totalFaces > MaxEntries || totalEdges > MaxEntries)
        {
            data["truncated"] = true;
        }

        var planeCount = faces.Count(f => (string)f["type"] == "plane");
        var cylCount = faces.Count(f => (string)f["type"] == "cylinder");
        return ToolResult.Ok(
            message: $"'{title}': {totalFaces} faces ({planeCount} plane / {cylCount} cylinder / " +
                     $"{totalFaces - planeCount - cylCount} other), {totalEdges} edges " +
                     $"across {bodyCount} body(ies)",
            data: data);
    }

    // ── faces ───────────────────────────────────────────────────────────────

    private static void ReadFaces(
        IBody2 body, List<Dictionary<string, object>> faces, ref int totalFaces)
    {
        object facesObj = body.GetFaces();
        if (facesObj is not object[] faceArr)
        {
            return;
        }
        foreach (var faceObj in faceArr)
        {
            var index = totalFaces++;
            if (faces.Count >= MaxEntries)
            {
                continue;
            }
            var face = (IFace2)faceObj;
            var entry = new Dictionary<string, object>
            {
                ["index"] = index,
                ["areaMm2"] = Round2(face.GetArea() * 1_000_000.0),
            };

            object boxObj = face.GetBox();
            if (boxObj is double[] box && box.Length >= 6)
            {
                entry["centerMm"] = PointMm(
                    (box[0] + box[3]) / 2.0, (box[1] + box[4]) / 2.0, (box[2] + box[5]) / 2.0);
            }

            object surfObj = face.GetSurface();
            if (surfObj is ISurface surface)
            {
                if (surface.IsPlane())
                {
                    entry["type"] = "plane";
                    object normalObj = face.Normal;
                    if (normalObj is double[] n && n.Length >= 3)
                    {
                        entry["normal"] = PointMm(n[0], n[1], n[2], scale: 1.0);
                    }
                }
                else if (surface.IsCylinder())
                {
                    entry["type"] = "cylinder";
                    object cylObj = surface.CylinderParams;
                    if (cylObj is double[] cp && cp.Length >= 7)
                    {
                        entry["axisOriginMm"] = PointMm(cp[0], cp[1], cp[2]);
                        entry["axisDir"] = PointMm(cp[3], cp[4], cp[5], scale: 1.0);
                        entry["radiusMm"] = Round2(cp[6] * 1000.0);
                    }
                }
                else if (surface.IsCone())
                {
                    entry["type"] = "cone";
                }
                else if (surface.IsSphere())
                {
                    entry["type"] = "sphere";
                }
                else if (surface.IsTorus())
                {
                    entry["type"] = "torus";
                }
                else
                {
                    entry["type"] = "other";
                }
            }
            else
            {
                entry["type"] = "other";
            }

            faces.Add(entry);
        }
    }

    // ── edges ───────────────────────────────────────────────────────────────

    private static void ReadEdges(
        IBody2 body, List<Dictionary<string, object>> edges, ref int totalEdges)
    {
        object edgesObj = body.GetEdges();
        if (edgesObj is not object[] edgeArr)
        {
            return;
        }
        foreach (var edgeObj in edgeArr)
        {
            var index = totalEdges++;
            if (edges.Count >= MaxEntries)
            {
                continue;
            }
            var edge = (IEdge)edgeObj;
            var entry = new Dictionary<string, object> { ["index"] = index };

            object curveObj = edge.GetCurve();
            if (curveObj is ICurve curve)
            {
                if (curve.GetEndParams(
                        out double startParam, out double endParam,
                        out bool isClosed, out bool isPeriodic))
                {
                    entry["lengthMm"] = Round2(curve.GetLength3(startParam, endParam) * 1000.0);
                }

                if (curve.IsLine())
                {
                    entry["type"] = "line";
                    AddLineEndpoints(edge, entry);
                }
                else if (curve.IsCircle())
                {
                    entry["type"] = "circle";
                    object circObj = curve.CircleParams;
                    if (circObj is double[] cp && cp.Length >= 7)
                    {
                        entry["centerMm"] = PointMm(cp[0], cp[1], cp[2]);
                        entry["radiusMm"] = Round2(cp[6] * 1000.0);
                    }
                }
                else
                {
                    entry["type"] = "other";
                }
            }
            else
            {
                entry["type"] = "other";
            }

            edges.Add(entry);
        }
    }

    private static void AddLineEndpoints(IEdge edge, Dictionary<string, object> entry)
    {
        object startObj = edge.GetStartVertex();
        object endObj = edge.GetEndVertex();
        if (startObj is IVertex sv && endObj is IVertex ev)
        {
            object spObj = sv.GetPoint();
            object epObj = ev.GetPoint();
            if (spObj is double[] sp && epObj is double[] ep &&
                sp.Length >= 3 && ep.Length >= 3)
            {
                entry["startMm"] = PointMm(sp[0], sp[1], sp[2]);
                entry["endMm"] = PointMm(ep[0], ep[1], ep[2]);
            }
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static Dictionary<string, object> PointMm(
        double x, double y, double z, double scale = 1000.0) => new()
        {
            ["x"] = Round2(x * scale),
            ["y"] = Round2(y * scale),
            ["z"] = Round2(z * scale),
        };

    private static double Round2(double v) => Math.Round(v, 2);
}
#endif
