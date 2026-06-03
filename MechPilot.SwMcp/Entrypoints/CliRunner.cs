using System.CommandLine;
using System.Text.Json;
using MechPilot.SwMcp.Exceptions;
using MechPilot.SwMcp.Models;
using MechPilot.SwMcp.Tools;

namespace MechPilot.SwMcp.Entrypoints;

public static class CliRunner
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var root = new RootCommand("mech-pilot-sw: SolidWorks MCP server + CLI");

        root.Subcommands.Add(BuildPingCommand());
        root.Subcommands.Add(BuildCreateCylinderCommand());
        root.Subcommands.Add(BuildCreateFlangeCommand());
        root.Subcommands.Add(BuildCreateRectangularBlockCommand());
        root.Subcommands.Add(BuildAddFilletCommand());
        root.Subcommands.Add(BuildAddChamferCommand());
        root.Subcommands.Add(BuildExportPartCommand());
        root.Subcommands.Add(BuildAddAxialHoleCommand());
        root.Subcommands.Add(BuildInspectPartCommand());
        root.Subcommands.Add(BuildMirrorFeatureCommand());
        root.Subcommands.Add(BuildPatternLinearCommand());
        root.Subcommands.Add(BuildAddThreadedHoleCommand());
        root.Subcommands.Add(BuildAddCounterboreCommand());
        root.Subcommands.Add(BuildAddCountersinkCommand());
        root.Subcommands.Add(BuildNewAssemblyCommand());
        root.Subcommands.Add(BuildAddComponentCommand());
        root.Subcommands.Add(BuildInspectAssemblyCommand());
        root.Subcommands.Add(BuildAddCoincidentMateCommand());
        root.Subcommands.Add(BuildAddDistanceMateCommand());
        root.Subcommands.Add(BuildAddConcentricMateCommand());

        var parseResult = root.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static Command BuildPingCommand()
    {
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("ping", "Sanity check; returns 'pong'.")
        {
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var result = PingTool.Run();
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildCreateCylinderCommand()
    {
        var diameterOpt = new Option<double>("--diameter")
        {
            Description = "Outer diameter in mm (e.g. 30).",
            Required = true,
        };
        var lengthOpt = new Option<double>("--length")
        {
            Description = "Extrusion length in mm (e.g. 50).",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Absolute output path ending in .sldprt (e.g. C:/tmp/cyl.sldprt).",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("create-cylinder", "Create a parametric cylinder part.")
        {
            diameterOpt,
            lengthOpt,
            outOpt,
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new CylinderSpec
                {
                    DiameterMm = parseResult.GetValue(diameterOpt),
                    LengthMm = parseResult.GetValue(lengthOpt),
                    SavePath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = CreateCylinderTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildCreateFlangeCommand()
    {
        var outerOpt = new Option<double>("--outer")
        {
            Description = "Outer disk diameter in mm, e.g. 80.",
            Required = true,
        };
        var thicknessOpt = new Option<double>("--thickness")
        {
            Description = "Disk thickness in mm, e.g. 10.",
            Required = true,
        };
        var outPathOpt = new Option<string>("--out")
        {
            Description = "Absolute output .sldprt path, e.g. C:/tmp/flange.sldprt.",
            Required = true,
        };
        var centerHoleOpt = new Option<double>("--center-hole")
        {
            Description = "Concentric center hole diameter in mm; 0 for none.",
            DefaultValueFactory = _ => 0.0,
        };
        var boltCountOpt = new Option<int>("--bolt-count")
        {
            Description = "Number of bolt holes evenly distributed on PCD; 0 for none.",
            DefaultValueFactory = _ => 0,
        };
        var boltDOpt = new Option<double>("--bolt-d")
        {
            Description = "Diameter of each bolt clearance hole in mm.",
            DefaultValueFactory = _ => 0.0,
        };
        var pcdOpt = new Option<double>("--pcd")
        {
            Description = "Pitch circle diameter (PCD) for bolt holes in mm.",
            DefaultValueFactory = _ => 0.0,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("create-flange",
            "Create a parametric flange / end-cap / bolt-circle plate.")
        {
            outerOpt, thicknessOpt, outPathOpt,
            centerHoleOpt, boltCountOpt, boltDOpt, pcdOpt,
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new FlangeSpec
                {
                    OuterDiameterMm = parseResult.GetValue(outerOpt),
                    ThicknessMm = parseResult.GetValue(thicknessOpt),
                    SavePath = parseResult.GetValue(outPathOpt) ?? string.Empty,
                    CenterHoleDiameterMm = parseResult.GetValue(centerHoleOpt),
                    BoltCount = parseResult.GetValue(boltCountOpt),
                    BoltDiameterMm = parseResult.GetValue(boltDOpt),
                    BoltCircleDiameterMm = parseResult.GetValue(pcdOpt),
                };
                var result = CreateFlangeTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddFilletCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to fillet.",
            Required = true,
        };
        var radiusOpt = new Option<double>("--radius")
        {
            Description = "Constant fillet radius in mm applied to every edge, e.g. 2.",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldprt path. Omit to overwrite the input in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-fillet",
            "Add a constant-radius fillet to every edge of an existing part.")
        {
            inputOpt, radiusOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new FilletSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    RadiusMm = parseResult.GetValue(radiusOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddFilletTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddChamferCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to chamfer.",
            Required = true,
        };
        var distanceOpt = new Option<double>("--distance")
        {
            Description = "Equal-distance chamfer width in mm applied to every edge, e.g. 2.",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldprt path. Omit to overwrite the input in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-chamfer",
            "Add an equal-distance chamfer to every edge of an existing part.")
        {
            inputOpt, distanceOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ChamferSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    DistanceMm = parseResult.GetValue(distanceOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = ChamferTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildExportPartCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to export.",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description =
                "Absolute output path; extension picks format " +
                "(.step / .stp / .stl / .iges / .igs / .x_t / .x_b).",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("export-part",
            "Export an existing part to a neutral CAD format (STEP / STL / IGES / Parasolid).")
        {
            inputOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ExportSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    OutputPath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = ExportPartTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddAxialHoleCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to drill.",
            Required = true,
        };
        var diameterOpt = new Option<double>("--diameter")
        {
            Description = "Hole diameter in mm, e.g. 6.6 for an M6 clearance hole.",
            Required = true,
        };
        var depthOpt = new Option<double?>("--depth")
        {
            Description = "Blind depth in mm; omit for through-all.",
            DefaultValueFactory = _ => null,
        };
        var posXOpt = new Option<double>("--position-x")
        {
            Description = "Hole-center X on the end face in mm. Default 0 (centroid).",
            DefaultValueFactory = _ => 0.0,
        };
        var posYOpt = new Option<double>("--position-y")
        {
            Description = "Hole-center Y on the end face in mm. Default 0 (centroid).",
            DefaultValueFactory = _ => 0.0,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldprt path. Omit to overwrite the input in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-axial-hole",
            "Drill a single axial (±Z) cylindrical hole (through-all or blind) into an existing part.")
        {
            inputOpt, diameterOpt, depthOpt, posXOpt, posYOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new AxialHoleSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    DiameterMm = parseResult.GetValue(diameterOpt),
                    DepthMm = parseResult.GetValue(depthOpt),
                    PositionXMm = parseResult.GetValue(posXOpt),
                    PositionYMm = parseResult.GetValue(posYOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddAxialHoleTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildInspectPartCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to inspect.",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("inspect-part",
            "Read metadata (bbox / features / face+edge counts) from an existing part. Read-only.")
        {
            inputOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new InspectSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                };
                var result = InspectPartTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildMirrorFeatureCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to edit.",
            Required = true,
        };
        var planeOpt = new Option<string>("--plane")
        {
            Description = "Mirror plane: 'front' / 'top' / 'right' (case-insensitive).",
            Required = true,
        };
        var featureOpt = new Option<string>("--feature")
        {
            Description = "Optional exact feature name to mirror; omit for last user feature.",
            DefaultValueFactory = _ => string.Empty,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldprt path. Omit to overwrite the input in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("mirror-feature",
            "Mirror a feature of an existing part across Front / Top / Right reference plane.")
        {
            inputOpt, planeOpt, featureOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new MirrorSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    MirrorPlane = parseResult.GetValue(planeOpt) ?? string.Empty,
                    FeatureName = parseResult.GetValue(featureOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = MirrorFeatureTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildCreateRectangularBlockCommand()
    {
        var lengthOpt = new Option<double>("--length")
        {
            Description = "Block length (X extent) in mm, e.g. 100.",
            Required = true,
        };
        var widthOpt = new Option<double>("--width")
        {
            Description = "Block width (Y extent) in mm, e.g. 50.",
            Required = true,
        };
        var heightOpt = new Option<double>("--height")
        {
            Description = "Block height (Z extrusion depth) in mm, e.g. 20.",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Absolute output path ending in .sldprt.",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("create-rectangular-block",
            "Create a parametric rectangular block (cuboid) part.")
        {
            lengthOpt, widthOpt, heightOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new RectangularBlockSpec
                {
                    LengthMm = parseResult.GetValue(lengthOpt),
                    WidthMm = parseResult.GetValue(widthOpt),
                    HeightMm = parseResult.GetValue(heightOpt),
                    SavePath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = CreateRectangularBlockTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildPatternLinearCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to edit.",
            Required = true,
        };
        var axis1Opt = new Option<string>("--axis1")
        {
            Description = "Direction-1 axis: 'x', 'y', or 'z' (case-insensitive).",
            Required = true,
        };
        var count1Opt = new Option<int>("--count1")
        {
            Description = "Total instances along direction 1 (including seed), e.g. 3.",
            Required = true,
        };
        var spacing1Opt = new Option<double>("--spacing1")
        {
            Description = "Center-to-center spacing along direction 1 in mm, e.g. 20.",
            Required = true,
        };
        var axis2Opt = new Option<string>("--axis2")
        {
            Description = "Optional direction-2 axis (different from --axis1).",
            DefaultValueFactory = _ => string.Empty,
        };
        var count2Opt = new Option<int>("--count2")
        {
            Description = "Total instances along direction 2 (with seed). Default 1.",
            DefaultValueFactory = _ => 1,
        };
        var spacing2Opt = new Option<double>("--spacing2")
        {
            Description = "Spacing along direction 2 in mm; required when --axis2 is set.",
            DefaultValueFactory = _ => 0.0,
        };
        var featureOpt = new Option<string>("--feature")
        {
            Description = "Optional exact seed feature name; omit for last user feature.",
            DefaultValueFactory = _ => string.Empty,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldprt path. Omit to overwrite the input in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("pattern-linear",
            "Linear pattern (1D or 2D) of a single seed feature in an existing part.")
        {
            inputOpt, axis1Opt, count1Opt, spacing1Opt,
            axis2Opt, count2Opt, spacing2Opt,
            featureOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new LinearPatternSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    Direction1Axis = parseResult.GetValue(axis1Opt) ?? string.Empty,
                    CountDir1 = parseResult.GetValue(count1Opt),
                    SpacingDir1Mm = parseResult.GetValue(spacing1Opt),
                    Direction2Axis = parseResult.GetValue(axis2Opt),
                    CountDir2 = parseResult.GetValue(count2Opt),
                    SpacingDir2Mm = parseResult.GetValue(spacing2Opt),
                    FeatureName = parseResult.GetValue(featureOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = PatternLinearTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddThreadedHoleCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to drill.",
            Required = true,
        };
        var threadOpt = new Option<string>("--thread")
        {
            Description = "GB metric-coarse thread size: M3/M4/M5/M6/M8/M10/M12.",
            Required = true,
        };
        var depthOpt = new Option<double?>("--depth")
        {
            Description = "Blind tap depth in mm; omit for through-all.",
            DefaultValueFactory = _ => null,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldprt path. Omit to overwrite the input in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-threaded-hole",
            "Drill one GB metric-coarse tap (M3..M12) at the end-face centroid.")
        {
            inputOpt, threadOpt, depthOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ThreadedHoleSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    ThreadSize = parseResult.GetValue(threadOpt) ?? string.Empty,
                    DepthMm = parseResult.GetValue(depthOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddThreadedHoleTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddCounterboreCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to drill.",
            Required = true,
        };
        var threadOpt = new Option<string>("--thread")
        {
            Description = "GB thread size: M3/M4/M5/M6/M8/M10/M12.",
            Required = true,
        };
        var depthOpt = new Option<double?>("--depth")
        {
            Description = "Blind clearance depth in mm; omit for through-all.",
            DefaultValueFactory = _ => null,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldprt path. Omit to overwrite in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-counterbore",
            "Drill one GB/T 152.3 counterbore (M3-M12) at the end-face centroid.")
        {
            inputOpt, threadOpt, depthOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new CounterboreSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    ThreadSize = parseResult.GetValue(threadOpt) ?? string.Empty,
                    DepthMm = parseResult.GetValue(depthOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddCounterboreTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddCountersinkCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to drill.",
            Required = true,
        };
        var threadOpt = new Option<string>("--thread")
        {
            Description = "GB thread size: M6/M8/M10/M12 (M3-M5 not supported by SW GB DB).",
            Required = true,
        };
        var depthOpt = new Option<double?>("--depth")
        {
            Description = "Blind clearance depth in mm; omit for through-all.",
            DefaultValueFactory = _ => null,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldprt path. Omit to overwrite in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-countersink",
            "Drill one GB/T 152.2 countersink (M6-M12, 90°) at the end-face centroid.")
        {
            inputOpt, threadOpt, depthOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new CountersinkSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    ThreadSize = parseResult.GetValue(threadOpt) ?? string.Empty,
                    DepthMm = parseResult.GetValue(depthOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddCountersinkTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildNewAssemblyCommand()
    {
        var outOpt = new Option<string>("--out")
        {
            Description = "Absolute output path with .sldasm extension.",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("new-assembly",
            "Create an empty SolidWorks assembly (.sldasm).")
        {
            outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new NewAssemblySpec
                {
                    SavePath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = NewAssemblyTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddComponentCommand()
    {
        var asmOpt = new Option<string>("--assembly")
        {
            Description = "Absolute path to an existing .sldasm to insert into.",
            Required = true,
        };
        var compOpt = new Option<string>("--component")
        {
            Description = "Absolute path to the .sldprt or .sldasm component to insert.",
            Required = true,
        };
        var posXOpt = new Option<double>("--position-x")
        {
            Description = "Component origin X in the assembly in mm. Default 0.",
            DefaultValueFactory = _ => 0.0,
        };
        var posYOpt = new Option<double>("--position-y")
        {
            Description = "Component origin Y in the assembly in mm. Default 0.",
            DefaultValueFactory = _ => 0.0,
        };
        var posZOpt = new Option<double>("--position-z")
        {
            Description = "Component origin Z in the assembly in mm. Default 0.",
            DefaultValueFactory = _ => 0.0,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-component",
            "Insert one component (.sldprt or sub-.sldasm) into an existing assembly.")
        {
            asmOpt, compOpt, posXOpt, posYOpt, posZOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new AddComponentSpec
                {
                    AssemblyPath = parseResult.GetValue(asmOpt) ?? string.Empty,
                    ComponentPath = parseResult.GetValue(compOpt) ?? string.Empty,
                    PositionXMm = parseResult.GetValue(posXOpt),
                    PositionYMm = parseResult.GetValue(posYOpt),
                    PositionZMm = parseResult.GetValue(posZOpt),
                };
                var result = AddComponentTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildInspectAssemblyCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldasm to inspect.",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("inspect-assembly",
            "Read metadata (component list / positions) from an existing assembly. Read-only.")
        {
            inputOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new InspectAssemblySpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                };
                var result = InspectAssemblyTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddCoincidentMateCommand()
    {
        var asmOpt = new Option<string>("--assembly")
        {
            Description = "Absolute path to an existing .sldasm.",
            Required = true,
        };
        var comp1Opt = new Option<string>("--component1")
        {
            Description = "First component's instance name (from inspect_assembly).",
            Required = true,
        };
        var plane1Opt = new Option<string>("--plane1")
        {
            Description = "Reference plane of component 1: 'front' / 'top' / 'right'.",
            Required = true,
        };
        var comp2Opt = new Option<string>("--component2")
        {
            Description = "Second component's instance name.",
            Required = true,
        };
        var plane2Opt = new Option<string>("--plane2")
        {
            Description = "Reference plane of component 2: 'front' / 'top' / 'right'.",
            Required = true,
        };
        var alignOpt = new Option<string>("--alignment")
        {
            Description = "Alignment: 'aligned' (default), 'anti-aligned', or 'closest'.",
            DefaultValueFactory = _ => "aligned",
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldasm path. Omit to overwrite in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-mate-coincident",
            "Add a coincident mate between two components' reference planes in an assembly.")
        {
            asmOpt, comp1Opt, plane1Opt, comp2Opt, plane2Opt, alignOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new CoincidentMateSpec
                {
                    AssemblyPath = parseResult.GetValue(asmOpt) ?? string.Empty,
                    Component1Name = parseResult.GetValue(comp1Opt) ?? string.Empty,
                    Plane1 = parseResult.GetValue(plane1Opt) ?? string.Empty,
                    Component2Name = parseResult.GetValue(comp2Opt) ?? string.Empty,
                    Plane2 = parseResult.GetValue(plane2Opt) ?? string.Empty,
                    Alignment = parseResult.GetValue(alignOpt) ?? "aligned",
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddCoincidentMateTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddDistanceMateCommand()
    {
        var asmOpt = new Option<string>("--assembly")
        {
            Description = "Absolute path to an existing .sldasm.",
            Required = true,
        };
        var comp1Opt = new Option<string>("--component1")
        {
            Description = "First component's instance name (from inspect_assembly).",
            Required = true,
        };
        var plane1Opt = new Option<string>("--plane1")
        {
            Description = "Reference plane of component 1: 'front' / 'top' / 'right'.",
            Required = true,
        };
        var comp2Opt = new Option<string>("--component2")
        {
            Description = "Second component's instance name.",
            Required = true,
        };
        var plane2Opt = new Option<string>("--plane2")
        {
            Description = "Reference plane of component 2: 'front' / 'top' / 'right'.",
            Required = true,
        };
        var distOpt = new Option<double>("--distance")
        {
            Description = "Mate distance in mm. Must be > 0.",
            Required = true,
        };
        var alignOpt = new Option<string>("--alignment")
        {
            Description = "Alignment: 'aligned' (default), 'anti-aligned', or 'closest'.",
            DefaultValueFactory = _ => "aligned",
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldasm path. Omit to overwrite in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-mate-distance",
            "Add a distance mate between two components' reference planes in an assembly.")
        {
            asmOpt, comp1Opt, plane1Opt, comp2Opt, plane2Opt, distOpt, alignOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new DistanceMateSpec
                {
                    AssemblyPath = parseResult.GetValue(asmOpt) ?? string.Empty,
                    Component1Name = parseResult.GetValue(comp1Opt) ?? string.Empty,
                    Plane1 = parseResult.GetValue(plane1Opt) ?? string.Empty,
                    Component2Name = parseResult.GetValue(comp2Opt) ?? string.Empty,
                    Plane2 = parseResult.GetValue(plane2Opt) ?? string.Empty,
                    DistanceMm = parseResult.GetValue(distOpt),
                    Alignment = parseResult.GetValue(alignOpt) ?? "aligned",
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddDistanceMateTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static Command BuildAddConcentricMateCommand()
    {
        var asmOpt = new Option<string>("--assembly")
        {
            Description = "Absolute path to an existing .sldasm.",
            Required = true,
        };
        var comp1Opt = new Option<string>("--component1")
        {
            Description = "First component's instance name (from inspect_assembly).",
            Required = true,
        };
        var comp2Opt = new Option<string>("--component2")
        {
            Description = "Second component's instance name.",
            Required = true,
        };
        var alignOpt = new Option<string>("--alignment")
        {
            Description = "Alignment: 'aligned' (default), 'anti-aligned', or 'closest'.",
            DefaultValueFactory = _ => "aligned",
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Optional output .sldasm path. Omit to overwrite in place.",
            DefaultValueFactory = _ => string.Empty,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("add-mate-concentric",
            "Add a concentric mate between two components' first axial-Z cylindrical faces.")
        {
            asmOpt, comp1Opt, comp2Opt, alignOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ConcentricMateSpec
                {
                    AssemblyPath = parseResult.GetValue(asmOpt) ?? string.Empty,
                    Component1Name = parseResult.GetValue(comp1Opt) ?? string.Empty,
                    Component2Name = parseResult.GetValue(comp2Opt) ?? string.Empty,
                    Alignment = parseResult.GetValue(alignOpt) ?? "aligned",
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddConcentricMateTool.RunWithSpec(spec);
                WriteResult(result, parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 1;
            }
        });

        return cmd;
    }

    private static void WriteResult(ToolResult result, string format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
        }
        else
        {
            var msg = result.Message ?? result.Status;
            if (!string.IsNullOrEmpty(result.Path))
            {
                msg += $" → {result.Path}";
            }
            Console.WriteLine(msg);
        }
    }
}
