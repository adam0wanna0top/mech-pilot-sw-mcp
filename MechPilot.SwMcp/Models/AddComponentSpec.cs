using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for inserting one component (.sldprt or .sldasm) into an
/// existing assembly at a given (x, y, z) world position in mm. The
/// component is placed at the position but **not mated** — mating is a
/// separate concern (future add_mate tool).
///
/// LLM use case: "把 cyl.sldprt 放在装配体里 (0, 0, 0); 再把 block.sldprt
/// 放在 (50, 0, 0)" → two tool calls.
/// </summary>
public sealed record AddComponentSpec
{
    /// <summary>Absolute path to an existing .sldasm to insert into. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Absolute path to the component file to insert. Must exist and end in
    /// .sldprt or .sldasm (sub-assembly is allowed).
    /// </summary>
    public required string ComponentPath { get; init; }

    /// <summary>Component-origin X position in the assembly in mm. Default 0.</summary>
    public double PositionXMm { get; init; }

    /// <summary>Component-origin Y position in the assembly in mm. Default 0.</summary>
    public double PositionYMm { get; init; }

    /// <summary>Component-origin Z position in the assembly in mm. Default 0.</summary>
    public double PositionZMm { get; init; }

    /// <summary>Rotation about the world X axis in degrees, applied before
    /// positioning. Default 0.</summary>
    public double RotationXDeg { get; init; }

    /// <summary>Rotation about the world Y axis in degrees, applied before
    /// positioning. Default 0.</summary>
    public double RotationYDeg { get; init; }

    /// <summary>Rotation about the world Z axis in degrees, applied before
    /// positioning. Default 0.</summary>
    public double RotationZDeg { get; init; }

    /// <summary>
    /// Idempotency guard (M53-④): when true, skip the insert if an instance of
    /// the same component file is already in the assembly (dedup by source
    /// path) — makes retrying a half-failed insert safe instead of producing a
    /// duplicate/ghost. Default false so legitimate multi-instance inserts
    /// (e.g. four bolts of the same part) still work.
    /// </summary>
    public bool SkipIfPresent { get; init; }

    private const double MaxAbsPositionMm = 100_000.0;
    private const double MaxAbsRotationDeg = 3_600.0;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateAssemblyPath(AssemblyPath);
        ValidateComponentPath(ComponentPath);
        ValidatePosition(PositionXMm, nameof(PositionXMm));
        ValidatePosition(PositionYMm, nameof(PositionYMm));
        ValidatePosition(PositionZMm, nameof(PositionZMm));
        ValidateRotation(RotationXDeg, nameof(RotationXDeg));
        ValidateRotation(RotationYDeg, nameof(RotationYDeg));
        ValidateRotation(RotationZDeg, nameof(RotationZDeg));
    }

    private static void ValidateAssemblyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpToolException("assemblyPath must not be empty.");
        }
        if (!Path.IsPathRooted(path))
        {
            throw new McpToolException($"assemblyPath must be absolute (got '{path}').");
        }
        if (!path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"assemblyPath must end in .sldasm (got '{path}').");
        }
        if (!File.Exists(path))
        {
            throw new McpToolException(
                $"assemblyPath does not exist: '{path}'. " +
                "Create the assembly first with new_assembly.");
        }
    }

    private static void ValidateComponentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpToolException("componentPath must not be empty.");
        }
        if (!Path.IsPathRooted(path))
        {
            throw new McpToolException($"componentPath must be absolute (got '{path}').");
        }
        var isPart = path.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase);
        var isAsm = path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
        if (!isPart && !isAsm)
        {
            throw new McpToolException(
                $"componentPath must end in .sldprt or .sldasm (got '{path}').");
        }
        if (!File.Exists(path))
        {
            throw new McpToolException(
                $"componentPath does not exist: '{path}'. " +
                "Create the part first (e.g. with create_cylinder / create_rectangular_block).");
        }
    }

    private static void ValidatePosition(double v, string name)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            throw new McpToolException($"{name} must be a finite number (got {v}).");
        }
        if (Math.Abs(v) > MaxAbsPositionMm)
        {
            throw new McpToolException(
                $"{name} {v} mm exceeds ±{MaxAbsPositionMm} mm sanity bound.");
        }
    }

    private static void ValidateRotation(double v, string name)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            throw new McpToolException($"{name} must be a finite number (got {v}).");
        }
        if (Math.Abs(v) > MaxAbsRotationDeg)
        {
            throw new McpToolException(
                $"{name} {v} exceeds ±{MaxAbsRotationDeg}° sanity bound — angles " +
                "are DEGREES, not radians.");
        }
    }
}
