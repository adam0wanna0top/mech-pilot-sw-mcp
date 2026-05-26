namespace MechPilot.SwMcp.Exceptions;

public sealed class McpToolException : Exception
{
    public McpToolException(string message) : base(message) { }

    public McpToolException(string message, Exception inner) : base(message, inner) { }
}
