using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for exporting an existing part to a neutral CAD format.
/// The output file's extension drives the format dispatch in
/// <c>IModelDocExtension.SaveAs</c>; this spec keeps the allowed set explicit
/// (so a typo like <c>.stop</c> fails at the validation layer with a friendly
/// hint instead of bubbling up an opaque SW error) and refuses to overwrite
/// the input .sldprt.
/// </summary>
public sealed record ExportSpec
{
    /// <summary>Absolute path to an existing .sldprt to export. Must exist.</summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Absolute output path; the extension picks the format
    /// (.step / .stp / .stl / .iges / .igs / .x_t / .x_b). Must differ from
    /// <see cref="InputPath"/>; the parent directory must already exist.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>Output extensions SW's Extension.SaveAs dispatches into neutral exporters.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".step"] = "STEP (ISO 10303-21, default AP214)",
            [".stp"] = "STEP (ISO 10303-21, default AP214)",
            [".stl"] = "STL mesh (for 3D printing / preview)",
            [".iges"] = "IGES (legacy NURBS interchange)",
            [".igs"] = "IGES (legacy NURBS interchange)",
            [".x_t"] = "Parasolid text",
            [".x_b"] = "Parasolid binary",
        };

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateInputPath(InputPath);
        ValidateOutputPath(OutputPath, InputPath);
    }

    private static void ValidateInputPath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new McpToolException("inputPath must not be empty.");
        }
        if (!Path.IsPathRooted(inputPath))
        {
            throw new McpToolException(
                $"inputPath must be absolute (got '{inputPath}').");
        }
        if (!inputPath.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                $"inputPath must end in .sldprt (got '{inputPath}').");
        }
        if (!File.Exists(inputPath))
        {
            throw new McpToolException(
                $"inputPath does not exist: '{inputPath}'. " +
                "Create the part first (e.g. with create_cylinder / create_flange).");
        }
    }

    private static void ValidateOutputPath(string outputPath, string inputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new McpToolException("outputPath must not be empty.");
        }
        if (!Path.IsPathRooted(outputPath))
        {
            throw new McpToolException(
                $"outputPath must be absolute (got '{outputPath}').");
        }

        var ext = Path.GetExtension(outputPath);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.ContainsKey(ext))
        {
            var supported = string.Join(", ", AllowedExtensions.Keys);
            throw new McpToolException(
                $"outputPath extension '{ext}' is not a supported neutral format. " +
                $"Supported: {supported}.");
        }

        if (string.Equals(outputPath, inputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException(
                "outputPath must differ from inputPath; export refuses to overwrite the .sldprt source.");
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
