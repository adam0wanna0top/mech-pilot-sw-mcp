using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Spec for sketching a spline through points (M50). Points is a FLAT list
/// of sketch-plane coordinates in mm: [x1, y1, x2, y2, ...]. At least 3
/// points (6 numbers) — a 2-point spline is just a line; use sketch_line.
/// </summary>
public sealed record SketchSplineSpec
{
    /// <summary>Flat [x1, y1, x2, y2, ...] in mm. ≥ 3 points, even count.</summary>
    public required IReadOnlyList<double> Points { get; init; }

    private const double MaxAbsCoordMm = 10_000.0;

    public void Validate()
    {
        if (Points is null || Points.Count == 0)
        {
            throw new McpToolException(
                "points must not be empty. Pass a flat list [x1, y1, x2, y2, ...] in mm.");
        }
        if (Points.Count % 2 != 0)
        {
            throw new McpToolException(
                $"points has an odd number of values ({Points.Count}) — it must be " +
                "flat [x1, y1, x2, y2, ...] pairs.");
        }
        if (Points.Count < 6)
        {
            throw new McpToolException(
                $"points has only {Points.Count / 2} point(s) — a spline needs at " +
                "least 3 (for 2 points use sketch_line).");
        }
        for (int i = 0; i < Points.Count; i++)
        {
            var v = Points[i];
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                throw new McpToolException($"points[{i}] must be a finite number (got {v}).");
            }
            if (Math.Abs(v) > MaxAbsCoordMm)
            {
                throw new McpToolException(
                    $"points[{i}] = {v} mm exceeds ±{MaxAbsCoordMm} mm sanity bound.");
            }
        }
        for (int i = 2; i < Points.Count; i += 2)
        {
            if (Points[i] == Points[i - 2] && Points[i + 1] == Points[i - 1])
            {
                throw new McpToolException(
                    $"points {i / 2} and {i / 2 + 1} are identical " +
                    $"({Points[i]}, {Points[i + 1]}) — consecutive spline points must differ.");
            }
        }
    }
}

/// <summary>
/// Spec for inserting a helix curve from the ACTIVE sketch's single circle
/// (M50). The circle defines the helix diameter; the helix grows along the
/// sketch plane's normal. Defined by pitch + revolutions
/// (swHelixDefinedByPitchAndRevolution) — the natural spring parameters.
/// </summary>
public sealed record InsertHelixSpec
{
    /// <summary>Axial distance per revolution in mm. Must be &gt; 0.</summary>
    public required double PitchMm { get; init; }

    /// <summary>Number of revolutions. Must be &gt; 0 (fractions allowed).</summary>
    public required double Revolutions { get; init; }

    /// <summary>False (default) = grow along +normal; true = flip direction.</summary>
    public bool Reverse { get; init; }

    /// <summary>True (default) = clockwise winding.</summary>
    public bool Clockwise { get; init; } = true;

    /// <summary>Start angle on the base circle in degrees [0, 360). Default 0.</summary>
    public double StartAngleDeg { get; init; }

    private const double MaxPitchMm = 10_000.0;
    private const double MaxRevolutions = 1_000.0;

    public void Validate()
    {
        if (double.IsNaN(PitchMm) || double.IsInfinity(PitchMm) || PitchMm <= 0)
        {
            throw new McpToolException($"pitch must be a finite number > 0 mm (got {PitchMm}).");
        }
        if (PitchMm > MaxPitchMm)
        {
            throw new McpToolException($"pitch {PitchMm} mm exceeds {MaxPitchMm} mm sanity bound.");
        }
        if (double.IsNaN(Revolutions) || double.IsInfinity(Revolutions) || Revolutions <= 0)
        {
            throw new McpToolException($"revolutions must be a finite number > 0 (got {Revolutions}).");
        }
        if (Revolutions > MaxRevolutions)
        {
            throw new McpToolException($"revolutions {Revolutions} exceeds {MaxRevolutions} sanity bound.");
        }
        if (double.IsNaN(StartAngleDeg) || double.IsInfinity(StartAngleDeg) ||
            StartAngleDeg < 0 || StartAngleDeg >= 360)
        {
            throw new McpToolException(
                $"startAngle must be in [0, 360) degrees (got {StartAngleDeg}).");
        }
    }
}
