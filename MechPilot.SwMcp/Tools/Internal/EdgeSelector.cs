#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Exceptions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Selects edges by their <see cref="TopologyReader"/> index for the M52
/// precise edge operations. CRITICAL CONTRACT: enumeration here must match
/// TopologyReader's order exactly (bodies via GetBodies2(swSolidBody, false),
/// then each body's GetEdges, flat) — that order IS the address space
/// inspect_topology hands the LLM.
/// </summary>
internal static class EdgeSelector
{
    /// <summary>
    /// Selects the edges at the given topology indexes with the given
    /// selection mark (FeatureFillet3 documents mark=1; InsertFeatureChamfer
    /// accepts 0 and 1 — M52 probe matrix; the parameter stays for future
    /// mark-sensitive consumers). Returns a short signature per selected
    /// edge ("#3 line 30 mm") so the caller's success message lets the LLM
    /// confirm it hit the intended edges. Throws a friendly error on an
    /// out-of-range index or a failed selection.
    /// </summary>
    public static List<string> SelectEdgesByIndex(
        IModelDoc2 model, IReadOnlyList<int> indexes, int mark)
    {
        var edges = EnumerateEdges(model);
        var maxIndex = edges.Count - 1;
        foreach (var i in indexes)
        {
            if (i > maxIndex)
            {
                throw new McpToolException(
                    $"Edge index {i} is out of range — the part has {edges.Count} " +
                    $"edge(s) (valid: 0..{maxIndex}). Call inspect_topology to get " +
                    "current indexes (they refresh after every edit).");
            }
        }

        model.ClearSelection2(true);
        var signatures = new List<string>();
        foreach (var i in indexes)
        {
            var edge = edges[i];
            if (!((IEntity)edge).Select2(true, mark))
            {
                throw new McpToolException(
                    $"Select2 failed for edge index {i}. The topology may have " +
                    "changed since inspect_topology — re-inspect and retry.");
            }
            signatures.Add(Describe(i, edge));
        }
        return signatures;
    }

    /// <summary>Flat edge list in TopologyReader's exact enumeration order.</summary>
    private static List<IEdge> EnumerateEdges(IModelDoc2 model)
    {
        var result = new List<IEdge>();
        object bodiesObj = ((IPartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (bodiesObj is not object[] bodies)
        {
            return result;
        }
        foreach (var bodyObj in bodies)
        {
            object edgesObj = ((IBody2)bodyObj).GetEdges();
            if (edgesObj is not object[] edgeArr)
            {
                continue;
            }
            foreach (var edgeObj in edgeArr)
            {
                result.Add((IEdge)edgeObj);
            }
        }
        return result;
    }

    /// <summary>"#3 line 30 mm" / "#0 circle r20" — enough for the LLM to
    /// cross-check against its inspect_topology output.</summary>
    private static string Describe(int index, IEdge edge)
    {
        object curveObj = edge.GetCurve();
        if (curveObj is not ICurve curve)
        {
            return $"#{index}";
        }
        if (curve.IsCircle())
        {
            object circObj = curve.CircleParams;
            var r = circObj is double[] cp && cp.Length >= 7
                ? $" r{Math.Round(cp[6] * 1000.0, 2)}" : string.Empty;
            return $"#{index} circle{r}";
        }
        var type = curve.IsLine() ? "line" : "curve";
        if (curve.GetEndParams(out double s, out double e, out _, out _))
        {
            return $"#{index} {type} {Math.Round(curve.GetLength3(s, e) * 1000.0, 2)} mm";
        }
        return $"#{index} {type}";
    }
}
#endif
