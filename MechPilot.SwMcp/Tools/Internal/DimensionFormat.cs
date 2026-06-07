namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Pure (SolidWorks-free) helpers for turning a dimension's raw SI value into
/// the display units the inspect tools report and modify_feature consumes.
/// Kept free of SW Interop types so it is L1-testable without a SolidWorks
/// install (the surrounding <see cref="PartMetadata"/> dimension walk needs
/// live COM and only runs under #if HAS_SOLIDWORKS).
///
/// SW <c>IDimension.SystemValue</c> is SI: metres for length dimensions,
/// radians for angular ones. We surface mm / degrees so the values line up with
/// modify_feature's contract (extrude/cut depth in mm, revolve angle in degrees).
/// </summary>
internal static class DimensionFormat
{
    // swDimensionType_e values that are angular (reported in degrees):
    //   3  = swAngularDimension
    //   16 = swAngularOrdinateDimension
    // Every other type (linear / radial / diametric / arc-length / chamfer / ...)
    // is a length and is reported in mm. (Reflected from swconst on SW 2026.)
    public static bool IsAngular(int swDimensionType) =>
        swDimensionType is 3 or 16;

    /// <summary>
    /// Converts a dimension's SI <paramref name="systemValue"/> to a
    /// (display value, unit) pair — "deg" for angular dimensions, "mm" for
    /// lengths — rounded to 6 decimals to shed double-precision noise
    /// (e.g. 0.03 m → 30 mm exactly).
    /// </summary>
    public static (double Value, string Unit) ToDisplay(int swDimensionType, double systemValue)
    {
        return IsAngular(swDimensionType)
            ? (Math.Round(systemValue * 180.0 / Math.PI, 6), "deg")
            : (Math.Round(systemValue * 1000.0, 6), "mm");
    }
}
