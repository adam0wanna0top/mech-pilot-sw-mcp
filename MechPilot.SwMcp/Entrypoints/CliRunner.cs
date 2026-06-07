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
        root.Subcommands.Add(BuildNewPartCommand());
        root.Subcommands.Add(BuildSavePartCommand());
        root.Subcommands.Add(BuildStartSketchCommand());
        root.Subcommands.Add(BuildEndSketchCommand());
        root.Subcommands.Add(BuildSketchLineCommand());
        root.Subcommands.Add(BuildSketchArc3PointCommand());
        root.Subcommands.Add(BuildSketchArcCenterCommand());
        root.Subcommands.Add(BuildSketchCircleCommand());
        root.Subcommands.Add(BuildSketchCenterLineCommand());
        root.Subcommands.Add(BuildSketchRectangleCenterCommand());
        root.Subcommands.Add(BuildExtrudeCommand());
        root.Subcommands.Add(BuildRevolveCommand());
        root.Subcommands.Add(BuildAddRefPlaneCommand());
        root.Subcommands.Add(BuildLoftCommand());
        root.Subcommands.Add(BuildSweepCommand());
        root.Subcommands.Add(BuildExtrudeCutCommand());
        root.Subcommands.Add(BuildRevolveCutCommand());
        root.Subcommands.Add(BuildRibCommand());
        root.Subcommands.Add(BuildModifyFeatureCommand());
        root.Subcommands.Add(BuildModifyMateCommand());
        root.Subcommands.Add(BuildCreateCylinderCommand());
        root.Subcommands.Add(BuildCreateHemisphereCommand());
        root.Subcommands.Add(BuildCreateSphereCommand());
        root.Subcommands.Add(BuildCreateFrustumCommand());
        root.Subcommands.Add(BuildCreateLoftedRoundToSquareCommand());
        root.Subcommands.Add(BuildCreateFlangeCommand());
        root.Subcommands.Add(BuildCreateRectangularBlockCommand());
        root.Subcommands.Add(BuildAddFilletCommand());
        root.Subcommands.Add(BuildAddChamferCommand());
        root.Subcommands.Add(BuildExportPartCommand());
        root.Subcommands.Add(BuildImportStepCommand());
        root.Subcommands.Add(BuildAddAxialHoleCommand());
        root.Subcommands.Add(BuildInspectPartCommand());
        root.Subcommands.Add(BuildInspectActiveCommand());
        root.Subcommands.Add(BuildMirrorFeatureCommand());
        root.Subcommands.Add(BuildPatternLinearCommand());
        root.Subcommands.Add(BuildPatternCircularCommand());
        root.Subcommands.Add(BuildAddThreadedHoleCommand());
        root.Subcommands.Add(BuildAddCounterboreCommand());
        root.Subcommands.Add(BuildAddCountersinkCommand());
        root.Subcommands.Add(BuildNewAssemblyCommand());
        root.Subcommands.Add(BuildAddComponentCommand());
        root.Subcommands.Add(BuildInspectAssemblyCommand());
        root.Subcommands.Add(BuildAddCoincidentMateCommand());
        root.Subcommands.Add(BuildAddDistanceMateCommand());
        root.Subcommands.Add(BuildAddConcentricMateCommand());
        root.Subcommands.Add(BuildAddAngleMateCommand());
        root.Subcommands.Add(BuildAddShellCommand());

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

    private static Command BuildNewPartCommand()
    {
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("new-part",
            "Open a new blank SolidWorks part document (generic primitives layer entry).")
        {
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var result = NewPartTool.RunWithSpec(new NewPartSpec());
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

    private static Command BuildSavePartCommand()
    {
        var outOpt = new Option<string>("--out")
        {
            Description = "Absolute output path ending in .sldprt (e.g. C:/tmp/part.sldprt).",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("save-part",
            "Save the active SolidWorks part to disk and close it (generic primitives layer exit).")
        {
            outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SavePartSpec
                {
                    SavePath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = SavePartTool.RunWithSpec(spec);
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

    private static Command BuildStartSketchCommand()
    {
        var planeOpt = new Option<string>("--plane")
        {
            Description = "Plane name: 'front' / 'top' / 'right' (case-insensitive), or a literal plane name like 'Plane1'.",
            Required = true,
        };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("start-sketch",
            "Enter sketch mode on a named plane of the active part.")
        { planeOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new StartSketchSpec { Plane = parseResult.GetValue(planeOpt) ?? string.Empty };
                WriteResult(StartSketchTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildEndSketchCommand()
    {
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("end-sketch",
            "Exit sketch mode on the active part; returns the sketch's auto-assigned SW name.")
        { formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                WriteResult(EndSketchTool.RunWithSpec(new EndSketchSpec()), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildSketchLineCommand()
    {
        var x1 = new Option<double>("--x1") { Description = "Start X (mm).", Required = true };
        var y1 = new Option<double>("--y1") { Description = "Start Y (mm).", Required = true };
        var x2 = new Option<double>("--x2") { Description = "End X (mm).", Required = true };
        var y2 = new Option<double>("--y2") { Description = "End Y (mm).", Required = true };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("sketch-line", "Add a line segment to the active sketch.") { x1, y1, x2, y2, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SketchLineSpec
                {
                    X1 = parseResult.GetValue(x1),
                    Y1 = parseResult.GetValue(y1),
                    X2 = parseResult.GetValue(x2),
                    Y2 = parseResult.GetValue(y2),
                };
                WriteResult(SketchLineTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildSketchArc3PointCommand()
    {
        var x1 = new Option<double>("--x1") { Description = "Start X (mm).", Required = true };
        var y1 = new Option<double>("--y1") { Description = "Start Y (mm).", Required = true };
        var x2 = new Option<double>("--x2") { Description = "End X (mm).", Required = true };
        var y2 = new Option<double>("--y2") { Description = "End Y (mm).", Required = true };
        var x3 = new Option<double>("--x3") { Description = "Middle X (mm).", Required = true };
        var y3 = new Option<double>("--y3") { Description = "Middle Y (mm).", Required = true };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("sketch-arc-3point", "Add a 3-point arc to the active sketch.")
        { x1, y1, x2, y2, x3, y3, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SketchArc3PointSpec
                {
                    X1 = parseResult.GetValue(x1),
                    Y1 = parseResult.GetValue(y1),
                    X2 = parseResult.GetValue(x2),
                    Y2 = parseResult.GetValue(y2),
                    X3 = parseResult.GetValue(x3),
                    Y3 = parseResult.GetValue(y3),
                };
                WriteResult(SketchArc3PointTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildSketchArcCenterCommand()
    {
        var cx = new Option<double>("--cx") { Description = "Center X (mm).", Required = true };
        var cy = new Option<double>("--cy") { Description = "Center Y (mm).", Required = true };
        var x1 = new Option<double>("--x1") { Description = "Start X (mm).", Required = true };
        var y1 = new Option<double>("--y1") { Description = "Start Y (mm).", Required = true };
        var x2 = new Option<double>("--x2") { Description = "End X (mm).", Required = true };
        var y2 = new Option<double>("--y2") { Description = "End Y (mm).", Required = true };
        var dir = new Option<int>("--direction") { Description = "1=CCW, -1=CW. Default 1.", DefaultValueFactory = _ => 1 };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("sketch-arc-center", "Add a center+endpoints arc to the active sketch.")
        { cx, cy, x1, y1, x2, y2, dir, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SketchArcCenterSpec
                {
                    Cx = parseResult.GetValue(cx),
                    Cy = parseResult.GetValue(cy),
                    X1 = parseResult.GetValue(x1),
                    Y1 = parseResult.GetValue(y1),
                    X2 = parseResult.GetValue(x2),
                    Y2 = parseResult.GetValue(y2),
                    Direction = parseResult.GetValue(dir),
                };
                WriteResult(SketchArcCenterTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildSketchCircleCommand()
    {
        var cx = new Option<double>("--cx") { Description = "Center X (mm).", Required = true };
        var cy = new Option<double>("--cy") { Description = "Center Y (mm).", Required = true };
        var r = new Option<double>("--radius") { Description = "Radius (mm, > 0).", Required = true };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("sketch-circle", "Add a circle to the active sketch.") { cx, cy, r, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SketchCircleSpec
                {
                    Cx = parseResult.GetValue(cx),
                    Cy = parseResult.GetValue(cy),
                    RadiusMm = parseResult.GetValue(r),
                };
                WriteResult(SketchCircleTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildSketchCenterLineCommand()
    {
        var x1 = new Option<double>("--x1") { Description = "Start X (mm).", Required = true };
        var y1 = new Option<double>("--y1") { Description = "Start Y (mm).", Required = true };
        var x2 = new Option<double>("--x2") { Description = "End X (mm).", Required = true };
        var y2 = new Option<double>("--y2") { Description = "End Y (mm).", Required = true };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("sketch-centerline", "Add a centerline (construction line) to the active sketch.")
        { x1, y1, x2, y2, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SketchCenterLineSpec
                {
                    X1 = parseResult.GetValue(x1),
                    Y1 = parseResult.GetValue(y1),
                    X2 = parseResult.GetValue(x2),
                    Y2 = parseResult.GetValue(y2),
                };
                WriteResult(SketchCenterLineTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildSketchRectangleCenterCommand()
    {
        var cx = new Option<double>("--cx") { Description = "Center X (mm).", Required = true };
        var cy = new Option<double>("--cy") { Description = "Center Y (mm).", Required = true };
        var rx = new Option<double>("--corner-x") { Description = "Corner X (mm).", Required = true };
        var ry = new Option<double>("--corner-y") { Description = "Corner Y (mm).", Required = true };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("sketch-rectangle-center", "Add a centered rectangle to the active sketch.")
        { cx, cy, rx, ry, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SketchRectangleCenterSpec
                {
                    Cx = parseResult.GetValue(cx),
                    Cy = parseResult.GetValue(cy),
                    CornerX = parseResult.GetValue(rx),
                    CornerY = parseResult.GetValue(ry),
                };
                WriteResult(SketchRectangleCenterTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildExtrudeCommand()
    {
        var sketchOpt = new Option<string>("--sketch") { Description = "Sketch name (from end_sketch).", Required = true };
        var depthOpt = new Option<double>("--depth") { Description = "Extrusion depth (mm, > 0).", Required = true };
        var reverseOpt = new Option<bool>("--reverse") { Description = "Flip extrude direction.", DefaultValueFactory = _ => false };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("extrude", "Extrude a named sketch into a solid body on the active part.")
        { sketchOpt, depthOpt, reverseOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ExtrudeSpec
                {
                    SketchName = parseResult.GetValue(sketchOpt) ?? string.Empty,
                    DepthMm = parseResult.GetValue(depthOpt),
                    Reverse = parseResult.GetValue(reverseOpt),
                };
                WriteResult(ExtrudeTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildRevolveCommand()
    {
        var sketchOpt = new Option<string>("--sketch") { Description = "Sketch name (from end_sketch, must contain centerline).", Required = true };
        var angleOpt = new Option<double>("--angle") { Description = "Revolve angle (degrees, default 360).", DefaultValueFactory = _ => 360.0 };
        var reverseOpt = new Option<bool>("--reverse") { Description = "Flip revolve direction.", DefaultValueFactory = _ => false };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("revolve", "Revolve a named sketch around its embedded centerline.")
        { sketchOpt, angleOpt, reverseOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new RevolveSpec
                {
                    SketchName = parseResult.GetValue(sketchOpt) ?? string.Empty,
                    AngleDeg = parseResult.GetValue(angleOpt),
                    Reverse = parseResult.GetValue(reverseOpt),
                };
                WriteResult(RevolveTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildAddRefPlaneCommand()
    {
        var sourceOpt = new Option<string>("--source") { Description = "Source plane: 'front'/'top'/'right' or literal name.", Required = true };
        var distOpt = new Option<double>("--distance") { Description = "Offset distance (mm, signed).", Required = true };
        var revOpt = new Option<bool>("--reverse") { Description = "Flip offset direction.", DefaultValueFactory = _ => false };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("add-ref-plane", "Create an offset reference plane from a source plane.")
        { sourceOpt, distOpt, revOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new AddRefPlaneSpec
                {
                    SourcePlane = parseResult.GetValue(sourceOpt) ?? string.Empty,
                    DistanceMm = parseResult.GetValue(distOpt),
                    Reverse = parseResult.GetValue(revOpt),
                };
                WriteResult(AddRefPlaneTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildLoftCommand()
    {
        var sketchOpt = new Option<string[]>("--sketches") { Description = "Comma-separated sketch names.", Required = true };
        var closedOpt = new Option<bool>("--closed") { Description = "Treat as closed loop.", DefaultValueFactory = _ => false };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("loft", "Loft (blend) over 2+ named sketches.")
        { sketchOpt, closedOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var sketches = parseResult.GetValue(sketchOpt) ?? Array.Empty<string>();
                // Allow either repeated --sketches X --sketches Y, or single --sketches "X,Y" form.
                if (sketches.Length == 1 && sketches[0].Contains(','))
                {
                    sketches = sketches[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
                var spec = new LoftSpec
                {
                    SketchNames = sketches,
                    Closed = parseResult.GetValue(closedOpt),
                };
                WriteResult(LoftTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildSweepCommand()
    {
        var profileOpt = new Option<string>("--profile") { Description = "Profile sketch name (closed area).", Required = true };
        var pathOpt = new Option<string>("--path") { Description = "Path sketch name (open curve).", Required = true };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("sweep", "Sweep a profile sketch along a path sketch.")
        { profileOpt, pathOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SweepSpec
                {
                    ProfileSketchName = parseResult.GetValue(profileOpt) ?? string.Empty,
                    PathSketchName = parseResult.GetValue(pathOpt) ?? string.Empty,
                };
                WriteResult(SweepTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildExtrudeCutCommand()
    {
        var sketchOpt = new Option<string>("--sketch") { Description = "Sketch name (from end_sketch).", Required = true };
        var depthOpt = new Option<double>("--depth") { Description = "Cut depth (mm, > 0).", Required = true };
        var reverseOpt = new Option<bool>("--reverse") { Description = "Flip cut direction.", DefaultValueFactory = _ => false };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("extrude-cut", "Cut a sketch into the active part's body (subtractive).")
        { sketchOpt, depthOpt, reverseOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ExtrudeSpec
                {
                    SketchName = parseResult.GetValue(sketchOpt) ?? string.Empty,
                    DepthMm = parseResult.GetValue(depthOpt),
                    Reverse = parseResult.GetValue(reverseOpt),
                };
                WriteResult(ExtrudeCutTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildRevolveCutCommand()
    {
        var sketchOpt = new Option<string>("--sketch") { Description = "Sketch name (from end_sketch, must contain centerline).", Required = true };
        var angleOpt = new Option<double>("--angle") { Description = "Revolve angle (degrees, default 360).", DefaultValueFactory = _ => 360.0 };
        var reverseOpt = new Option<bool>("--reverse") { Description = "Flip revolve direction.", DefaultValueFactory = _ => false };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("revolve-cut", "Revolve-cut: subtract a swept profile around its centerline.")
        { sketchOpt, angleOpt, reverseOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new RevolveSpec
                {
                    SketchName = parseResult.GetValue(sketchOpt) ?? string.Empty,
                    AngleDeg = parseResult.GetValue(angleOpt),
                    Reverse = parseResult.GetValue(reverseOpt),
                };
                WriteResult(RevolveCutTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
        });
        return cmd;
    }

    private static Command BuildRibCommand()
    {
        var sketchOpt = new Option<string>("--sketch") { Description = "Open-contour rib sketch name (from end_sketch).", Required = true };
        var thicknessOpt = new Option<double>("--thickness") { Description = "Rib thickness (mm, > 0).", Required = true };
        var reverseOpt = new Option<bool>("--reverse") { Description = "Flip rib fill direction.", DefaultValueFactory = _ => false };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("rib", "Add a structural rib (stiffener / gusset) by thickening an open sketch.")
        { sketchOpt, thicknessOpt, reverseOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new RibSpec
                {
                    SketchName = parseResult.GetValue(sketchOpt) ?? string.Empty,
                    ThicknessMm = parseResult.GetValue(thicknessOpt),
                    Reverse = parseResult.GetValue(reverseOpt),
                };
                WriteResult(RibTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
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

    private static Command BuildCreateHemisphereCommand()
    {
        var diameterOpt = new Option<double>("--diameter")
        {
            Description = "Hemisphere diameter in mm (full sphere diameter), e.g. 60.",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Absolute output path ending in .sldprt (e.g. C:/tmp/hemi.sldprt).",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("create-hemisphere",
            "Create a parametric solid hemisphere part (axis +Y, base on Y=0 plane).")
        {
            diameterOpt,
            outOpt,
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new HemisphereSpec
                {
                    DiameterMm = parseResult.GetValue(diameterOpt),
                    SavePath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = CreateHemisphereTool.RunWithSpec(spec);
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

    private static Command BuildCreateSphereCommand()
    {
        var diameterOpt = new Option<double>("--diameter")
        {
            Description = "Sphere diameter in mm, e.g. 40 for a D40 sphere.",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Absolute output path ending in .sldprt (e.g. C:/tmp/sphere.sldprt).",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("create-sphere",
            "Create a parametric solid sphere part (centered at origin).")
        {
            diameterOpt,
            outOpt,
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new SphereSpec
                {
                    DiameterMm = parseResult.GetValue(diameterOpt),
                    SavePath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = CreateSphereTool.RunWithSpec(spec);
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

    private static Command BuildCreateFrustumCommand()
    {
        var baseOpt = new Option<double>("--base-diameter")
        {
            Description = "Base (Y=0) circle diameter in mm, e.g. 60.",
            Required = true,
        };
        var topOpt = new Option<double>("--top-diameter")
        {
            Description = "Top (Y=height) circle diameter in mm. Must be > 0 and strictly < --base-diameter.",
            Required = true,
        };
        var heightOpt = new Option<double>("--height")
        {
            Description = "Frustum height along +Y in mm, e.g. 40.",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Absolute output path ending in .sldprt (e.g. C:/tmp/frustum.sldprt).",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("create-frustum",
            "Create a parametric solid frustum (truncated cone, axis +Y).")
        {
            baseOpt,
            topOpt,
            heightOpt,
            outOpt,
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new FrustumSpec
                {
                    BaseDiameterMm = parseResult.GetValue(baseOpt),
                    TopDiameterMm = parseResult.GetValue(topOpt),
                    HeightMm = parseResult.GetValue(heightOpt),
                    SavePath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = CreateFrustumTool.RunWithSpec(spec);
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

    private static Command BuildInspectActiveCommand()
    {
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("inspect-active",
            "Read metadata (bbox / features / face+edge counts) from the active part WITHOUT saving/closing it.")
        {
            formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var result = InspectActiveTool.RunWithSpec(new InspectActiveSpec());
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

    private static Command BuildImportStepCommand()
    {
        var inputOpt = new Option<string>("--input") { Description = "Absolute path to a neutral CAD file (.step/.stp/.iges/.igs/.x_t/.x_b).", Required = true };
        var outOpt = new Option<string>("--out") { Description = "Absolute output .sldprt path.", Required = true };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("import-step", "Import a neutral CAD file (STEP / IGES / Parasolid) as a .sldprt (dumb body).")
        { inputOpt, outOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ImportStepSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    OutputPath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = ImportStepTool.RunWithSpec(spec);
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

    private static Command BuildModifyMateCommand()
    {
        var asmOpt = new Option<string>("--assembly") { Description = "Absolute path to an existing .sldasm.", Required = true };
        var mateOpt = new Option<string>("--mate") { Description = "Exact mate name from inspect-assembly's mates list (e.g. '距离1').", Required = true };
        var valueOpt = new Option<double>("--value") { Description = "New value: distance (mm) or angle (deg) by mate type. > 0.", Required = true };
        var outOpt = new Option<string>("--out") { Description = "Optional output .sldasm path. Omit to overwrite in place." };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("modify-mate", "Edit an existing mate's value (distance mm / angle deg) in an assembly and rebuild.")
        { asmOpt, mateOpt, valueOpt, outOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ModifyMateSpec
                {
                    AssemblyPath = parseResult.GetValue(asmOpt) ?? string.Empty,
                    MateName = parseResult.GetValue(mateOpt) ?? string.Empty,
                    Value = parseResult.GetValue(valueOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = ModifyMateTool.RunWithSpec(spec);
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

    private static Command BuildModifyFeatureCommand()
    {
        var featureOpt = new Option<string>("--feature") { Description = "Exact feature name from inspect-active / inspect-part.", Required = true };
        var valueOpt = new Option<double>("--value") { Description = "New primary dimension: depth (mm) / angle (deg) / radius (mm) by feature type. > 0.", Required = true };
        var partOpt = new Option<string>("--part") { Description = "Optional absolute .sldprt to edit a saved part file instead of the active part." };
        var outOpt = new Option<string>("--out") { Description = "Optional output .sldprt (with --part). Omit to overwrite in place." };
        var formatOpt = new Option<string>("--output") { Description = "text | json", DefaultValueFactory = _ => "text" };
        var cmd = new Command("modify-feature", "Edit a feature's primary dimension (active part, or a saved part via --part) and regenerate.")
        { featureOpt, valueOpt, partOpt, outOpt, formatOpt };
        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ModifyFeatureSpec
                {
                    FeatureName = parseResult.GetValue(featureOpt) ?? string.Empty,
                    Value = parseResult.GetValue(valueOpt),
                    PartPath = parseResult.GetValue(partOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                WriteResult(ModifyFeatureTool.RunWithSpec(spec), parseResult.GetValue(formatOpt) ?? "text");
                return 0;
            }
            catch (McpToolException ex) { Console.Error.WriteLine($"[error] {ex.Message}"); return 1; }
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

    private static Command BuildPatternCircularCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to edit.",
            Required = true,
        };
        var countOpt = new Option<int>("--count")
        {
            Description = "Total instances around the axis (including seed), e.g. 6.",
            Required = true,
        };
        var angleOpt = new Option<double>("--angle")
        {
            Description = "Total sweep angle in degrees. Default 360 (full circle).",
            DefaultValueFactory = _ => 360.0,
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

        var cmd = new Command("pattern-circular",
            "Circular (rotational) pattern of a single seed feature around the part's ±Z axis.")
        {
            inputOpt, countOpt, angleOpt, featureOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new CircularPatternSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    Count = parseResult.GetValue(countOpt),
                    TotalAngleDeg = parseResult.GetValue(angleOpt),
                    FeatureName = parseResult.GetValue(featureOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = PatternCircularTool.RunWithSpec(spec);
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

    private static Command BuildAddAngleMateCommand()
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
        var angleOpt = new Option<double>("--angle")
        {
            Description = "Mate angle in degrees. Must be > 0 and < 180.",
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

        var cmd = new Command("add-mate-angle",
            "Add an angle mate between two components' reference planes in an assembly.")
        {
            asmOpt, comp1Opt, plane1Opt, comp2Opt, plane2Opt, angleOpt, alignOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new AngleMateSpec
                {
                    AssemblyPath = parseResult.GetValue(asmOpt) ?? string.Empty,
                    Component1Name = parseResult.GetValue(comp1Opt) ?? string.Empty,
                    Plane1 = parseResult.GetValue(plane1Opt) ?? string.Empty,
                    Component2Name = parseResult.GetValue(comp2Opt) ?? string.Empty,
                    Plane2 = parseResult.GetValue(plane2Opt) ?? string.Empty,
                    AngleDeg = parseResult.GetValue(angleOpt),
                    Alignment = parseResult.GetValue(alignOpt) ?? "aligned",
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddAngleMateTool.RunWithSpec(spec);
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

    private static Command BuildAddShellCommand()
    {
        var inputOpt = new Option<string>("--input")
        {
            Description = "Absolute path to an existing .sldprt to shell.",
            Required = true,
        };
        var thickOpt = new Option<double>("--thickness")
        {
            Description = "Wall thickness in mm, e.g. 2 for a 2 mm wall.",
            Required = true,
        };
        var outwardOpt = new Option<bool>("--outward")
        {
            Description = "If set, thicken outward (less common). Default false = hollow inward.",
            DefaultValueFactory = _ => false,
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

        var cmd = new Command("add-shell",
            "Shell an existing solid part — hollow it out with a uniform wall thickness, opening the +Z end face.")
        {
            inputOpt, thickOpt, outwardOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new ShellSpec
                {
                    InputPath = parseResult.GetValue(inputOpt) ?? string.Empty,
                    ThicknessMm = parseResult.GetValue(thickOpt),
                    Outward = parseResult.GetValue(outwardOpt),
                    OutputPath = parseResult.GetValue(outOpt),
                };
                var result = AddShellTool.RunWithSpec(spec);
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

    private static Command BuildCreateLoftedRoundToSquareCommand()
    {
        var bottomDOpt = new Option<double>("--bottom-diameter")
        {
            Description = "Bottom-face circle diameter in mm, e.g. 60.",
            Required = true,
        };
        var topLOpt = new Option<double>("--top-length")
        {
            Description = "Top-face rectangle length (X extent) in mm, e.g. 40.",
            Required = true,
        };
        var topWOpt = new Option<double>("--top-width")
        {
            Description = "Top-face rectangle width (Y extent) in mm, e.g. 40.",
            Required = true,
        };
        var heightOpt = new Option<double>("--height")
        {
            Description = "Loft height (Z direction) in mm, e.g. 30.",
            Required = true,
        };
        var outOpt = new Option<string>("--out")
        {
            Description = "Absolute output path ending in .sldprt (e.g. C:/tmp/transition.sldprt).",
            Required = true,
        };
        var formatOpt = new Option<string>("--output")
        {
            Description = "Output format: text | json",
            DefaultValueFactory = _ => "text",
        };

        var cmd = new Command("create-lofted-round-to-square",
            "Create a parametric solid lofted transition (round bottom → square top).")
        {
            bottomDOpt, topLOpt, topWOpt, heightOpt, outOpt, formatOpt,
        };

        cmd.SetAction(parseResult =>
        {
            try
            {
                var spec = new LoftedRoundToSquareSpec
                {
                    BottomDiameterMm = parseResult.GetValue(bottomDOpt),
                    TopLengthMm = parseResult.GetValue(topLOpt),
                    TopWidthMm = parseResult.GetValue(topWOpt),
                    HeightMm = parseResult.GetValue(heightOpt),
                    SavePath = parseResult.GetValue(outOpt) ?? string.Empty,
                };
                var result = CreateLoftedRoundToSquareTool.RunWithSpec(spec);
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
