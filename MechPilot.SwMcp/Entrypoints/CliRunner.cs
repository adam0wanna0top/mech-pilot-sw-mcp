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
        root.Subcommands.Add(BuildAddFilletCommand());
        root.Subcommands.Add(BuildAddChamferCommand());

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
