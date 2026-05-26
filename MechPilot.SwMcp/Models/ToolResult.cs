namespace MechPilot.SwMcp.Models;

public sealed record ToolResult
{
    public required string Status { get; init; }

    public string? Path { get; init; }

    public string? Message { get; init; }

    public static ToolResult Ok(string? message = null, string? path = null) =>
        new() { Status = "ok", Message = message, Path = path };
}
