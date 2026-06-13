#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Exceptions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Resolves a component's face by its <see cref="TopologyReader"/> index for
/// the M53-③ topology-addressed concentric mate. CRITICAL CONTRACT: the
/// enumeration here must match TopologyReader's face order exactly (bodies via
/// GetBodies2(swSolidBody) then each body's GetFaces, flat) — that order IS
/// the address space inspect_topology hands the LLM. The only difference from
/// TopologyReader is the source: we walk the in-assembly COMPONENT
/// (<c>IComponent2.GetBodies2</c>, assembly-context faces that select for a
/// mate) instead of the standalone part (<c>IPartDoc.GetBodies2</c>). For
/// single-body parts the two orders are identical, so an index read from
/// inspect_topology on the .sldprt addresses the same face on the instance.
/// </summary>
internal static class ComponentFaceSelector
{
    /// <summary>
    /// Returns the cylindrical face at the given topology index on the
    /// component (for concentric mates), with a short signature
    /// ("#3 cylinder r3"). Throws a friendly error on an out-of-range index or
    /// when the face is not cylindrical — both steer the LLM to inspect_topology.
    /// </summary>
    public static (IFace2 Face, string Signature) GetCylindricalFaceByIndex(
        IComponent2 comp, int index, string componentName)
    {
        return GetFaceByIndex(
            comp, index, componentName,
            wanted: s => s.IsCylinder(),
            wantedLabel: "cylinder",
            wantedHint: "a concentric mate needs a cylindrical face (a hole wall or a shaft)",
            signature: (i, s) =>
            {
                var radius = s.CylinderParams is double[] cp && cp.Length >= 7
                    ? $" r{Math.Round(cp[6] * 1000.0, 2)}" : string.Empty;
                return $"#{i} cylinder{radius}";
            });
    }

    /// <summary>
    /// Returns the planar face at the given topology index on the component
    /// (for coincident / distance mates, M54), with a short signature
    /// ("#3 plane"). Throws a friendly error on an out-of-range index or when
    /// the face is not planar — both steer the LLM to inspect_topology.
    /// </summary>
    public static (IFace2 Face, string Signature) GetPlanarFaceByIndex(
        IComponent2 comp, int index, string componentName)
    {
        return GetFaceByIndex(
            comp, index, componentName,
            wanted: s => s.IsPlane(),
            wantedLabel: "plane",
            wantedHint: "a coincident / distance mate needs a planar face",
            signature: (i, _) => $"#{i} plane");
    }

    /// <summary>
    /// Shared core: bounds-checks the topology index, fetches the face, and
    /// verifies its surface matches <paramref name="wanted"/> — friendly errors
    /// (valid range, or "is a &lt;type&gt;, not a &lt;wantedLabel&gt;") point back at
    /// inspect_topology. Returns the face + a caller-supplied signature.
    /// </summary>
    private static (IFace2 Face, string Signature) GetFaceByIndex(
        IComponent2 comp, int index, string componentName,
        Func<ISurface, bool> wanted, string wantedLabel, string wantedHint,
        Func<int, ISurface, string> signature)
    {
        var faces = EnumerateFaces(comp);
        var maxIndex = faces.Count - 1;
        if (faces.Count == 0)
        {
            throw new McpToolException(
                $"Component '{componentName}' has no solid faces to address.");
        }
        if (index > maxIndex)
        {
            throw new McpToolException(
                $"face index {index} is out of range for '{componentName}' — it has " +
                $"{faces.Count} face(s) (valid: 0..{maxIndex}). Run inspect_topology on " +
                "the component's part to get current face indexes.");
        }

        var face = faces[index];
        object surfObj = face.GetSurface();
        if (surfObj is not ISurface surface || !wanted(surface))
        {
            var type = DescribeSurface(surfObj);
            throw new McpToolException(
                $"face #{index} on '{componentName}' is a {type}, not a {wantedLabel} — " +
                $"{wantedHint}. Run inspect_topology on the part and pick a face whose " +
                $"type is '{wantedLabel}'.");
        }

        return (face, signature(index, surface));
    }

    /// <summary>Flat face list in TopologyReader's exact enumeration order, but
    /// over the in-assembly component body (faces that select for a mate).</summary>
    private static List<IFace2> EnumerateFaces(IComponent2 comp)
    {
        var result = new List<IFace2>();
        object bodiesObj = comp.GetBodies2((int)swBodyType_e.swSolidBody);
        if (bodiesObj is not object[] bodies)
        {
            return result;
        }
        foreach (var bodyObj in bodies)
        {
            if (bodyObj is not IBody2 body)
            {
                continue;
            }
            object facesObj = body.GetFaces();
            if (facesObj is not object[] faceArr)
            {
                continue;
            }
            foreach (var faceObj in faceArr)
            {
                result.Add((IFace2)faceObj);
            }
        }
        return result;
    }

    private static string DescribeSurface(object surfObj)
    {
        if (surfObj is not ISurface s)
        {
            return "non-surface";
        }
        if (s.IsPlane()) return "plane";
        if (s.IsCone()) return "cone";
        if (s.IsSphere()) return "sphere";
        if (s.IsTorus()) return "torus";
        return "other surface";
    }
}
#endif
