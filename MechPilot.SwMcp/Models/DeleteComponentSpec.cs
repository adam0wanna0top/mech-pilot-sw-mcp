using MechPilot.SwMcp.Exceptions;

namespace MechPilot.SwMcp.Models;

/// <summary>
/// Specification for removing one component instance from an existing
/// assembly (M53-②). The assembly rollback primitive — the sibling of M48's
/// delete_feature for the assembly level. Born from the fan dogfooding pain:
/// a wrong component, or a "ghost" left behind by a half-failed add_component
/// (RPC_E_DISCONNECTED mid-insert), could not be removed → the only recourse
/// was rebuilding the whole assembly.
///
/// Identifies the instance by name (the <c>name</c> field inspect_assembly
/// reports, e.g. "bolt-2"). The component file on disk is NOT touched — only
/// the instance and its mates are removed from the assembly.
///
/// LLM use case: "把装配体里多出来的那颗 bolt-3 删掉" → inspect_assembly to
/// read instance names → delete_component(asm, "bolt-3").
/// </summary>
public sealed record DeleteComponentSpec
{
    /// <summary>Absolute path to an existing .sldasm to remove from. Must exist.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Instance name of the component to remove, exactly as inspect_assembly
    /// reports it in each component's <c>name</c> (e.g. "bolt-2"). Must not be
    /// empty.
    /// </summary>
    public required string ComponentName { get; init; }

    private const int MaxComponentNameLength = 512;

    /// <summary>Throws <see cref="McpToolException"/> if any field is invalid.</summary>
    public void Validate()
    {
        ValidateAssemblyPath(AssemblyPath);
        ValidateComponentName(ComponentName);
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

    private static void ValidateComponentName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new McpToolException(
                "componentName must not be empty. Use inspect_assembly to read " +
                "each component's instance name (e.g. 'bolt-2').");
        }
        if (name.Length > MaxComponentNameLength)
        {
            throw new McpToolException(
                $"componentName is suspiciously long ({name.Length} chars; " +
                $"max {MaxComponentNameLength}). Pass the exact instance name " +
                "from inspect_assembly, e.g. 'bolt-2'.");
        }
    }
}
