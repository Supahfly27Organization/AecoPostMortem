namespace AecoPostMortem.Rules;

/// <summary>
/// One observed tool call, reduced to its argument shape: which generic kinds of argument it
/// carried, never the tool's own name interpreted as meaning. <see cref="ToolName"/> is an opaque
/// label the corpus supplies — nothing here treats it as one of a known set (FR-34, Repo Rule 6).
/// </summary>
public sealed record ToolInvocationShape
{
    public required string ToolName { get; init; }

    /// <summary>The call named a file-system path.</summary>
    public bool HasPath { get; init; }

    /// <summary>The call named a search pattern (e.g. a glob or regular expression).</summary>
    public bool HasPattern { get; init; }

    /// <summary>The call named replacement text for an existing file.</summary>
    public bool HasReplacement { get; init; }

    /// <summary>The call named the full text of a file to write.</summary>
    public bool HasFileText { get; init; }

    /// <summary>The call named a command line to run.</summary>
    public bool HasCommand { get; init; }

    /// <summary>The call produced a subagent — a structural fact (it matches a spawned agent's
    /// record), not a name read from the call's own arguments.</summary>
    public bool SpawnsAgent { get; init; }

    /// <summary>The MCP server the call was logged against, e.g. the provider's own
    /// <c>mcpServerName</c> field — an opaque label like <see cref="ToolName"/>, carried so
    /// <see cref="OperandResolver"/>'s second layer can match it structurally. <c>null</c> for a
    /// tool the provider did not log against any server.</summary>
    public string? McpServerName { get; init; }
}
