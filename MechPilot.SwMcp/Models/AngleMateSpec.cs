using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for adding one angle mate between two components' default
/// reference planes (Front / Top / Right) at a given degree angle. Fourth
/// member of the mate family alongside <see cref="CoincidentMateSpec"/> /
/// <see cref="DistanceMateSpec"/> / <see cref="ConcentricMateSpec"/>.
///
/// LLM use case (the long-awaited motion mate): "机械臂关节绕轴摆 30 度" /
/// "摇头风扇的电机壳偏转 45 度" / "L 型支架夹角 90 度". This is the mate
/// that finally lets LLM build articulated assemblies where joints rotate.
///
/// Same AddMate5 path + 4-magic-positions trick as M19 distance mate
/// (v1 PR #20), just with <c>swMateANGLE = 6</c> and the <c>Angle /
/// AngleAbsUpperLimit / AngleAbsLowerLimit</c> fields filled with the
/// requested rad (Distance fields stay 0).
/// </summary>
public sealed record AngleMateSpec
{
    /// <summary>Absolute path to an existing .sldasm to mate within. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>First component's instance name (e.g. "asm_link1_123-1"). Get from inspect_assembly.</summary>
    public required string Component1Name { get; init; }

    /// <summary>Reference plane of component 1: "front" / "top" / "right" (case-insensitive).</summary>
    public required string Plane1 { get; init; }

    /// <summary>Second component's instance name.</summary>
    public required string Component2Name { get; init; }

    /// <summary>Reference plane of component 2.</summary>
    public required string Plane2 { get; init; }

    /// <summary>
    /// Mate angle in degrees. Must be in (0, 180) exclusive — 0° means use
    /// add_mate_coincident instead, 180° is parallel-but-flipped which is
    /// not what users typically want from an angle mate.
    /// </summary>
    public required double AngleDeg { get; init; }

    /// <summary>
    /// Mate alignment: "aligned" (default) / "anti-aligned" / "closest"
    /// (case-insensitive). For angle mates, alignment picks which rotation
    /// sense — 'closest' is recommended when components are already
    /// positioned near the target angle.
    /// </summary>
    public string Alignment { get; init; } = "aligned";

    /// <summary>
    /// Optional absolute .sldasm output path. When null or empty the input
    /// assembly is overwritten in place (the common case).
    /// </summary>
    public string? OutputPath { get; init; }

    // Sanity bounds: 0.01° is below typical CAD precision; 179.99° is just
    // shy of degenerate parallel.
    private const double MinAngleDeg = 0.01;
    private const double MaxAngleDeg = 179.99;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateAssemblyPath(AssemblyPath);
        ValidateComponentName(Component1Name, "component1Name");
        ValidatePlane(Plane1, "plane1");
        ValidateComponentName(Component2Name, "component2Name");
        ValidatePlane(Plane2, "plane2");
        ValidateAngle(AngleDeg);
        ValidateAlignment(Alignment);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            ValidateOutputPath(OutputPath);
        }
        if (string.Equals(Component1Name, Component2Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"component1Name and component2Name must differ (both got '{Component1Name}').");
        }
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
                "Create the assembly first with new_assembly + add_component.");
        }
    }

    private static void ValidateComponentName(string name, string field)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new McpToolException(
                $"{field} must not be empty. Use inspect_assembly to learn component instance names.");
        }
    }

    private static void ValidatePlane(string plane, string field)
    {
        if (string.IsNullOrWhiteSpace(plane))
        {
            throw new McpToolException(
                $"{field} must not be empty. Use 'front', 'top', or 'right'.");
        }
        if (!CoincidentMateSpec.PlaneAliases.ContainsKey(plane))
        {
            throw new McpToolException(
                $"{field} '{plane}' is not recognized. Supported: " +
                $"{string.Join(", ", CoincidentMateSpec.PlaneAliases.Keys)}.");
        }
    }

    private static void ValidateAngle(double angleDeg)
    {
        if (double.IsNaN(angleDeg) || double.IsInfinity(angleDeg) || angleDeg <= 0)
        {
            throw new McpToolException(
                $"angle must be > 0° (got {angleDeg}). " +
                "Hint: pass degrees, e.g. 90 for a right angle. " +
                "For 0° / two planes coincident, use add_mate_coincident instead.");
        }
        if (angleDeg < MinAngleDeg || angleDeg > MaxAngleDeg)
        {
            throw new McpToolException(
                $"angle {angleDeg}° is outside the supported range " +
                $"[{MinAngleDeg}, {MaxAngleDeg}] degrees. " +
                "180° is degenerate (planes are parallel-flipped); use a value strictly less than 180.");
        }
    }

    private static void ValidateAlignment(string alignment)
    {
        if (string.IsNullOrWhiteSpace(alignment))
        {
            throw new McpToolException(
                "alignment must not be empty. Use 'aligned', 'anti-aligned', or 'closest'.");
        }
        if (!CoincidentMateSpec.AlignmentKeywords.Contains(alignment))
        {
            throw new McpToolException(
                $"alignment '{alignment}' is not recognized. Supported: " +
                $"{string.Join(", ", CoincidentMateSpec.AlignmentKeywords)}.");
        }
    }

    private static void ValidateOutputPath(string outputPath)
    {
        if (!Path.IsPathRooted(outputPath))
        {
            throw new McpToolException(
                $"outputPath must be absolute (got '{outputPath}').");
        }
        if (!outputPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"outputPath must end in .sldasm (got '{outputPath}').");
        }
        var dir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new McpToolException($"outputPath has no parent directory: '{outputPath}'.");
        }
        if (!Directory.Exists(dir))
        {
            throw new McpToolException(
                $"outputPath parent directory does not exist: '{dir}'.");
        }
    }
}
