#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Exceptions;
using SolidWorks.Interop.sldworks;
#endif

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Builds and applies a rotation + translation transform to a freshly
/// inserted assembly component (M53-①). <c>AddComponent5</c> only positions a
/// component — it has no orientation argument, so any part whose useful axis
/// is not already aligned with the assembly's axes (the classic case: a
/// Toolbox bolt whose shank runs along its own local axis) lands lying flat.
/// Setting the component's <c>Transform2</c> after insertion orients it.
///
/// The rotation is an XYZ Euler rotation in DEGREES about the assembly's world
/// axes, applied X then Y then Z (R = Rz·Ry·Rx), about the world origin,
/// followed by translation to the requested (x, y, z) position. We compute the
/// full 3×3 matrix here and hand SolidWorks the 16-element ArrayData transform
/// directly — no chained Multiply calls to get the order wrong.
///
/// The pure matrix math (<see cref="BuildTransformArray"/>) is kept free of SW
/// Interop types so it is L1-testable without a SolidWorks install (the
/// <see cref="Apply"/> wrapper that talks to COM is the only #if-guarded part).
/// </summary>
internal static class ComponentTransform
{
    /// <summary>
    /// Builds the 16-element SolidWorks <c>IMathTransform</c> ArrayData for an
    /// XYZ-Euler rotation (degrees) plus translation (mm → metres). Layout
    /// (documented + reflection-confirmed on SW 2026):
    ///   [0..8]  = 3×3 rotation, COLUMN-major (each column is the image of a
    ///             basis vector = the component's local X / Y / Z axis as it
    ///             points in assembly space — matches GetData's XAxis/YAxis/
    ///             ZAxis vectors).
    ///   [9..11] = translation (metres).
    ///   [12]    = scale (1).
    ///   [13..15]= 0 (reserved).
    /// Rotation order R = Rz·Ry·Rx (apply X, then Y, then Z to a vector).
    /// </summary>
    public static double[] BuildTransformArray(
        double rotXDeg, double rotYDeg, double rotZDeg,
        double txMm, double tyMm, double tzMm)
    {
        const double deg2Rad = Math.PI / 180.0;
        var cx = Math.Cos(rotXDeg * deg2Rad);
        var sx = Math.Sin(rotXDeg * deg2Rad);
        var cy = Math.Cos(rotYDeg * deg2Rad);
        var sy = Math.Sin(rotYDeg * deg2Rad);
        var cz = Math.Cos(rotZDeg * deg2Rad);
        var sz = Math.Sin(rotZDeg * deg2Rad);

        // R = Rz·Ry·Rx, written out (rows = R00..R22):
        //   [ cz·cy            cz·sy·sx − sz·cx     cz·sy·cx + sz·sx ]
        //   [ sz·cy            sz·sy·sx + cz·cx     sz·sy·cx − cz·sx ]
        //   [ −sy              cy·sx                cy·cx            ]
        // ArrayData stores it column-major (column j = image of basis vector j).
        var col0X = cz * cy;
        var col0Y = sz * cy;
        var col0Z = -sy;

        var col1X = (cz * sy * sx) - (sz * cx);
        var col1Y = (sz * sy * sx) + (cz * cx);
        var col1Z = cy * sx;

        var col2X = (cz * sy * cx) + (sz * sx);
        var col2Y = (sz * sy * cx) - (cz * sx);
        var col2Z = cy * cx;

        return new[]
        {
            col0X, col0Y, col0Z,
            col1X, col1Y, col1Z,
            col2X, col2Y, col2Z,
            txMm / 1000.0, tyMm / 1000.0, tzMm / 1000.0,
            1.0,
            0.0, 0.0, 0.0,
        };
    }

    /// <summary>True if any of the three Euler angles is non-zero — i.e. the
    /// component actually needs reorienting (the all-zero case is left to
    /// AddComponent5's own placement so the default path is byte-identical to
    /// pre-M53 behaviour).</summary>
    public static bool HasRotation(double rotXDeg, double rotYDeg, double rotZDeg) =>
        rotXDeg != 0.0 || rotYDeg != 0.0 || rotZDeg != 0.0;

#if HAS_SOLIDWORKS
    /// <summary>
    /// Reorients an already-placed component about its own frame origin,
    /// leaving its position untouched. We read the translation AddComponent5
    /// already set (which puts the part's bbox centre at the requested
    /// position — its frame origin lands offset by the part's geometry) and
    /// write back a transform with the SAME translation but our rotation
    /// block. The part therefore spins in place rather than jumping, so a
    /// rotated insert reports the exact same positionMm as an un-rotated one.
    ///
    /// Assumes the freshly-inserted component carries an identity rotation
    /// (true for AddComponent5 without a mate) so swapping the rotation block
    /// is a clean compose; if SW ever hands back a pre-rotated transform we
    /// fall back to plain rotation about that translation, which is still a
    /// sane orientation. Rebuilds so the move takes geometric effect.
    /// </summary>
    public static void Apply(
        ISldWorks swApp, IModelDoc2 asmModel, IComponent2 component,
        double rotXDeg, double rotYDeg, double rotZDeg)
    {
        object mathUtilObj = swApp.GetMathUtility();
        if (mathUtilObj is not IMathUtility mathUtil)
        {
            throw new McpToolException(
                "GetMathUtility returned null — cannot build the rotation " +
                "transform. Retry, or insert without rotation and orient by mate.");
        }

        // Read the placement AddComponent5 set so rotation keeps the same
        // frame-origin position (no jump). Translation slots [9..11] are metres.
        var existingTxM = 0.0;
        var existingTyM = 0.0;
        var existingTzM = 0.0;
        object existingObj = component.Transform2;
        if (existingObj is IMathTransform existing
            && existing.ArrayData is double[] ad && ad.Length >= 12)
        {
            existingTxM = ad[9];
            existingTyM = ad[10];
            existingTzM = ad[11];
        }

        var data = BuildTransformArray(
            rotXDeg, rotYDeg, rotZDeg,
            existingTxM * 1000.0, existingTyM * 1000.0, existingTzM * 1000.0);
        object xformObj = mathUtil.CreateTransform(data);
        if (xformObj is not IMathTransform xform)
        {
            throw new McpToolException(
                "CreateTransform returned null for the rotation transform " +
                $"(rx={rotXDeg}, ry={rotYDeg}, rz={rotZDeg} deg).");
        }

        component.Transform2 = (MathTransform)xform;
        asmModel.EditRebuild3();
    }
#endif
}
