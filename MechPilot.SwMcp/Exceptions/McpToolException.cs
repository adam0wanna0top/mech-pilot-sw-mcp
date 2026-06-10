using ModelContextProtocol;

namespace MechPilot.SwMcp.Exceptions;

/// <summary>
/// Business-rule failure thrown by tool cores. Both entrypoints surface the
/// MESSAGE to the caller — it is written for the LLM (friendly guidance:
/// available config lists, geometry hints, "create the assembly first", ...):
///   - CLI: caught in each verb → "[error] {message}" + non-zero exit.
///   - MCP: derives from the SDK's <see cref="McpException"/> so the message
///     passes through to the client. A plain Exception is swallowed by the
///     SDK into a bare "An error occurred invoking 'tool'." (L3 finding,
///     2026-06-10) — the SDK only forwards messages of McpException-typed
///     exceptions into its "An error occurred invoking '{tool}': {message}"
///     template.
/// </summary>
public sealed class McpToolException : McpException
{
    public McpToolException(string message) : base(message) { }

    public McpToolException(string message, Exception inner) : base(message, inner) { }
}
