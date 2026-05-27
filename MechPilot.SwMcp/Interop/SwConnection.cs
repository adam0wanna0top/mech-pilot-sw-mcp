#if HAS_SOLIDWORKS
using System.Runtime.InteropServices;
using MechPilot.SwMcp.Exceptions;
using SolidWorks.Interop.sldworks;

namespace MechPilot.SwMcp.Interop;

/// <summary>
/// Singleton wrapper around <see cref="ISldWorks"/>. Lazy-connects on first
/// <see cref="GetApp"/>. Tools never construct this directly — use
/// <see cref="Instance"/>. CLI and MCP entrypoints share the same instance.
/// </summary>
public sealed class SwConnection
{
    private const string SwProgId = "SldWorks.Application";

    private static readonly Lazy<SwConnection> _instance =
        new(() => new SwConnection(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static SwConnection Instance => _instance.Value;

    private readonly object _lock = new();
    private ISldWorks? _app;

    private SwConnection() { }

    /// <summary>
    /// Returns a live <see cref="ISldWorks"/>. Attaches to a running SolidWorks
    /// instance if present, otherwise starts a new one. Idempotent.
    /// </summary>
    public ISldWorks GetApp(bool makeVisible = true)
    {
        lock (_lock)
        {
            if (_app != null)
            {
                return _app;
            }

            var swType = Type.GetTypeFromProgID(SwProgId)
                ?? throw new McpToolException(
                    $"SolidWorks COM ProgID '{SwProgId}' is not registered. " +
                    "Is SolidWorks installed on this machine?");

            ISldWorks? app;
            try
            {
                // SolidWorks is a single-use COM server: if an instance is already running,
                // CreateInstance attaches to it instead of starting a new one.
                app = Activator.CreateInstance(swType) as ISldWorks;
            }
            catch (COMException ex)
            {
                throw new McpToolException(
                    $"Failed to launch / attach to SolidWorks (HRESULT 0x{ex.HResult:X8}): {ex.Message}",
                    ex);
            }

            if (app == null)
            {
                throw new McpToolException("Activator.CreateInstance returned null for SldWorks.Application");
            }

            if (makeVisible)
            {
                app.Visible = true;
            }

            _app = app;
            return _app;
        }
    }
}
#endif
