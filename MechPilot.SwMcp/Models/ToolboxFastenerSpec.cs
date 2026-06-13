using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for inserting a SolidWorks Toolbox standard part (fastener /
/// bearing / washer / pin / ...) into an existing assembly at a chosen
/// size — the size is a CONFIGURATION of the Toolbox library part.
///
/// Toolbox parts live under the Toolbox data folder (SW setting "Toolbox Data
/// Location", e.g. <c>G:\solidwork\SOLIDWORKS Data2026</c>) in a
/// <c>browser/&lt;standard&gt;/&lt;category&gt;/&lt;type&gt;/*.sldprt</c> tree, and every
/// size of a fastener (M6×30, M8×40, ...) is a configuration inside the one
/// .sldprt. Plain add_component can only insert the default/active config;
/// this spec carries the config name so the M47 tool can pick the size.
///
/// LLM use case: "在装配体里放一颗 GB 六角头螺栓 M6×30" →
/// partPath = ...browser/GB/bolts and studs/hexagon head bolts/hexagon head
/// bolts gb.sldprt + configName = the matching size configuration.
/// </summary>
public sealed record ToolboxFastenerSpec
{
    /// <summary>Absolute path to an existing .sldasm to insert into. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Absolute path to the Toolbox library part (.sldprt only — Toolbox
    /// standard parts are parts, never assemblies). Must exist.
    /// </summary>
    public required string PartPath { get; init; }

    /// <summary>
    /// Size configuration name inside the Toolbox part (e.g. "M6X30").
    /// Null / empty = insert the part's default (active) configuration.
    /// </summary>
    public string? ConfigName { get; init; }

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

    private const double MaxAbsPositionMm = 100_000.0;
    private const double MaxAbsRotationDeg = 3_600.0;
    private const int MaxConfigNameLength = 256;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateAssemblyPath(AssemblyPath);
        ValidatePartPath(PartPath);
        ValidateConfigName(ConfigName);
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

    private static void ValidatePartPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpToolException("partPath must not be empty.");
        }
        if (!Path.IsPathRooted(path))
        {
            throw new McpToolException($"partPath must be absolute (got '{path}').");
        }
        if (!path.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"partPath must end in .sldprt — Toolbox standard parts are " +
                $"part files (got '{path}').");
        }
        if (!File.Exists(path))
        {
            throw new McpToolException(
                $"partPath does not exist: '{path}'. Point it at a Toolbox " +
                "library part under the Toolbox data folder, e.g. " +
                "<ToolboxData>/browser/GB/bolts and studs/hexagon head bolts/" +
                "hexagon head bolts gb.sldprt.");
        }
    }

    private static void ValidateConfigName(string? config)
    {
        if (config != null && config.Length > MaxConfigNameLength)
        {
            throw new McpToolException(
                $"configName is suspiciously long ({config.Length} chars; " +
                $"max {MaxConfigNameLength}). Pass the exact configuration " +
                "name, e.g. 'M6X30'.");
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
