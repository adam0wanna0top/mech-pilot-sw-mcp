#if HAS_SOLIDWORKS
using MechPilot.SwMcp.Interop;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Shared driving-dimension recipes (M46, extracted in M49 for the catalog
/// helpers). Call while the target sketch is ACTIVE (before exiting it).
///
/// The M46 hard-won rules baked in here:
///   - <c>swInputDimValOnCreate</c> MUST be toggled off before AddDimension2,
///     or SW pops a modal "Modify" value dialog that deadlocks the API call.
///   - A circle's dimension is made Ø (not radius) via
///     <c>IDisplayDimension.Diametric</c>.
///   - Dimensions are read back per OWNING feature (M46 owner-filter), so a
///     sketch Ø consumed by an extrude still lists once, under the sketch.
/// </summary>
internal static class SketchDimensioner
{
    /// <summary>
    /// Turns off the modal "Modify" value box AddDimension2 would otherwise
    /// pop (M46: it deadlocks API callers). Idempotent and cheap — called
    /// before every dimension add.
    /// </summary>
    public static void DisableModifyDialog()
    {
        SwConnection.Instance.GetApp().SetUserPreferenceToggle(
            (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
    }

    /// <summary>
    /// Adds a driving Ø dimension to a circle in the active sketch. The
    /// annotation is placed just right of the circle (cx + r + 10 mm).
    /// </summary>
    public static void AddDiameter(
        IModelDoc2 model, ISketchSegment circle,
        double cxMm, double cyMm, double radiusMm)
    {
        DisableModifyDialog();
        model.ClearSelection2(true);
        circle.Select2(false, 0);
        var placeX = (cxMm + radiusMm) / 1000.0 + 0.010;
        object dispObj = model.AddDimension2(placeX, cyMm / 1000.0, 0.0);
        if (dispObj is IDisplayDimension disp)
        {
            disp.Diametric = true;
        }
        model.ClearSelection2(true);
    }

    /// <summary>
    /// Adds a driving length dimension to one sketch segment (annotation at
    /// the given mm point). No-ops on a non-segment (defensive for the
    /// object[] CreateCenterRectangle returns).
    /// </summary>
    public static void AddLength(
        IModelDoc2 model, object? segObj, double placeXMm, double placeYMm)
    {
        if (segObj is not ISketchSegment seg)
        {
            return;
        }
        DisableModifyDialog();
        model.ClearSelection2(true);
        seg.Select2(false, 0);
        _ = model.AddDimension2(placeXMm / 1000.0, placeYMm / 1000.0, 0.0);
        model.ClearSelection2(true);
    }

    /// <summary>
    /// Adds driving width + height dimensions to a centered rectangle's two
    /// adjacent sides (CreateCenterRectangle's segs[0] = X-direction side,
    /// segs[1] = Y-direction side; M46 layout: annotations above and right).
    /// </summary>
    public static void AddRectangle(
        IModelDoc2 model, object? segsObj,
        double cxMm, double cyMm, double widthMm, double heightMm)
    {
        if (segsObj is not object[] segs || segs.Length < 2)
        {
            return;
        }
        AddLength(model, segs[0], cxMm, cyMm + heightMm / 2.0 + 15.0);
        AddLength(model, segs[1], cxMm + widthMm / 2.0 + 15.0, cyMm);
    }
}
#endif
