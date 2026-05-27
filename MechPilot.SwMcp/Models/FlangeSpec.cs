using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for a parametric flange / end-cap / bolt-circle plate.
/// All lengths in millimeters. Internal SW Interop converts to meters at the boundary.
///
/// Geometry: circular disk of <see cref="OuterDiameterMm"/> × <see cref="ThicknessMm"/>,
/// optionally with a concentric center hole of <see cref="CenterHoleDiameterMm"/>,
/// optionally with <see cref="BoltCount"/> bolt holes of <see cref="BoltDiameterMm"/>
/// evenly distributed around a pitch circle of <see cref="BoltCircleDiameterMm"/>.
///
/// The implementation deliberately picks **one sketch + one through-all cut**
/// (no FeatureCircularPattern), because mech-pilot v1 PR #35 proved
/// pattern_circular silently fails on multi-cut bodies in SW 2026
/// (see docs/v1-history.md §8.3).
/// </summary>
public sealed record FlangeSpec
{
    /// <summary>Outer disk diameter in mm. Must be &gt; 0.</summary>
    public required double OuterDiameterMm { get; init; }

    /// <summary>Disk thickness (extrusion depth) in mm. Must be &gt; 0.</summary>
    public required double ThicknessMm { get; init; }

    /// <summary>Concentric center hole diameter in mm. 0 = solid (no center hole).</summary>
    public double CenterHoleDiameterMm { get; init; }

    /// <summary>Number of bolt holes evenly distributed on the pitch circle. 0 = none.</summary>
    public int BoltCount { get; init; }

    /// <summary>Diameter of each bolt clearance hole in mm. Required when <see cref="BoltCount"/> &gt; 0.</summary>
    public double BoltDiameterMm { get; init; }

    /// <summary>Pitch circle diameter (PCD) for bolt holes in mm. Required when <see cref="BoltCount"/> &gt; 0.</summary>
    public double BoltCircleDiameterMm { get; init; }

    /// <summary>Absolute output path ending in .sldprt; parent directory must exist.</summary>
    public required string SavePath { get; init; }

    // Sanity bounds — same rationale as CylinderSpec: catch unit confusion.
    private const double MinDimMm = 0.1;
    private const double MaxDimMm = 10_000.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field or relation is invalid.</summary>
    public void Validate()
    {
        RequirePositiveFinite(OuterDiameterMm, "outerDiameter");
        RequirePositiveFinite(ThicknessMm, "thickness");
        RequireInRange(OuterDiameterMm, "outerDiameter");
        RequireInRange(ThicknessMm, "thickness");

        if (double.IsNaN(CenterHoleDiameterMm) || double.IsInfinity(CenterHoleDiameterMm) || CenterHoleDiameterMm < 0)
        {
            throw new McpToolException(
                $"centerHoleDiameter must be >= 0 mm (got {CenterHoleDiameterMm}). " +
                "Use 0 for a solid flange without a center hole.");
        }
        if (CenterHoleDiameterMm > 0)
        {
            RequireInRange(CenterHoleDiameterMm, "centerHoleDiameter");
            if (CenterHoleDiameterMm >= OuterDiameterMm)
            {
                throw new McpToolException(
                    $"centerHoleDiameter {CenterHoleDiameterMm} mm must be < outerDiameter " +
                    $"{OuterDiameterMm} mm (would consume the entire disk).");
            }
        }

        if (BoltCount < 0)
        {
            throw new McpToolException($"boltCount must be >= 0 (got {BoltCount}).");
        }
        if (BoltCount > 0)
        {
            RequirePositiveFinite(BoltDiameterMm, "boltDiameter");
            RequirePositiveFinite(BoltCircleDiameterMm, "boltCircleDiameter (PCD)");
            RequireInRange(BoltDiameterMm, "boltDiameter");
            RequireInRange(BoltCircleDiameterMm, "boltCircleDiameter");

            // Bolt holes must fit between center hole and outer edge.
            var maxPcd = OuterDiameterMm - BoltDiameterMm;
            if (BoltCircleDiameterMm > maxPcd)
            {
                throw new McpToolException(
                    $"PCD {BoltCircleDiameterMm} mm too large: bolt of {BoltDiameterMm} mm " +
                    $"would extend past outerDiameter {OuterDiameterMm} mm. " +
                    $"PCD must be <= {maxPcd} mm.");
            }
            var minPcd = CenterHoleDiameterMm + BoltDiameterMm;
            if (BoltCircleDiameterMm < minPcd)
            {
                throw new McpToolException(
                    $"PCD {BoltCircleDiameterMm} mm too small: bolt of {BoltDiameterMm} mm " +
                    $"would overlap centerHole {CenterHoleDiameterMm} mm. " +
                    $"PCD must be >= {minPcd} mm.");
            }

            // Adjacent bolt holes must not overlap each other.
            // Chord between adjacent bolts on PCD circle = 2 * (PCD/2) * sin(π/N).
            // For non-overlap: chord > BoltDiameter (use strict > to leave a hair of material).
            if (BoltCount >= 2)
            {
                var chord = BoltCircleDiameterMm * Math.Sin(Math.PI / BoltCount);
                if (chord <= BoltDiameterMm)
                {
                    throw new McpToolException(
                        $"{BoltCount} bolts of {BoltDiameterMm} mm on PCD {BoltCircleDiameterMm} mm " +
                        $"would overlap each other (chord {chord:F2} mm <= bolt {BoltDiameterMm} mm). " +
                        "Reduce boltCount, reduce boltDiameter, or increase PCD.");
                }
            }
        }
        else if (BoltDiameterMm > 0 || BoltCircleDiameterMm > 0)
        {
            // Caught the inverse: user set bolt geometry but boltCount=0. Don't silently ignore.
            throw new McpToolException(
                $"boltCount is 0 but boltDiameter={BoltDiameterMm} / PCD={BoltCircleDiameterMm} " +
                "were also given. Set boltCount > 0 or clear the bolt geometry.");
        }

        ValidatePath(SavePath);
    }

    private static void RequirePositiveFinite(double value, string fieldName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            throw new McpToolException(
                $"{fieldName} must be > 0 mm (got {value}). " +
                "Hint: pass millimeters, e.g. 80 for a D80 flange.");
        }
    }

    private static void RequireInRange(double value, string fieldName)
    {
        if (value < MinDimMm || value > MaxDimMm)
        {
            throw new McpToolException(
                $"{fieldName} {value} mm is outside the supported range " +
                $"[{MinDimMm}, {MaxDimMm}] mm.");
        }
    }

    private static void ValidatePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            throw new McpToolException("savePath must not be empty.");
        }
        if (!Path.IsPathRooted(savePath))
        {
            throw new McpToolException(
                $"savePath must be absolute (got '{savePath}'). " +
                "Hint: pass something like 'C:/tmp/flange.sldprt'.");
        }
        if (!savePath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"savePath must end in .sldprt (got '{savePath}').");
        }
        var dir = Path.GetDirectoryName(savePath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new McpToolException($"savePath has no parent directory: '{savePath}'.");
        }
        if (!Directory.Exists(dir))
        {
            throw new McpToolException(
                $"savePath parent directory does not exist: '{dir}'. " +
                "Create it first or pick an existing folder.");
        }
    }
}
