namespace MechPilot.SwMcp.Models;

public sealed record ToolResult
{
    public required string Status { get; init; }

    public string? Path { get; init; }

    public string? Message { get; init; }

    /// <summary>
    /// Optional structured payload — used by read-only tools (inspect_part)
    /// to surface bounding box / feature list / face+edge counts in a form
    /// the CLI's `--output json` mode serializes directly and an LLM can
    /// consume programmatically. Other tools leave this null.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Data { get; init; }

    public static ToolResult Ok(
        string? message = null,
        string? path = null,
        IReadOnlyDictionary<string, object>? data = null) =>
        new() { Status = "ok", Message = message, Path = path, Data = data };
}
