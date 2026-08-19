namespace AecoPostMortem.Rules;

/// <summary>One tool's standing within a <see cref="ToolRole"/>: its name and how many calls of it
/// were observed.</summary>
public sealed record ToolRoleCount
{
    public required string ToolName { get; init; }

    public required int CallCount { get; init; }
}

/// <summary>One derived role and the tools classified into it. <see cref="DominantTool"/> is the
/// tool carrying the most calls — S-26 depends on knowing which tool actually does the job, which
/// matters more than how many tools share the role.</summary>
public sealed record ToolRoleSummary
{
    public required ToolRole Role { get; init; }

    public required IReadOnlyList<ToolRoleCount> Tools { get; init; }

    public ToolRoleCount? DominantTool =>
        Tools.Count == 0
            ? null
            : Tools
                .OrderByDescending(tool => tool.CallCount)
                .ThenBy(tool => tool.ToolName, StringComparer.Ordinal)
                .First();
}

/// <summary>The result of one derivation pass: all five roles, always present, plus whatever tools
/// matched no known shape.</summary>
public sealed record ToolRoleDerivation
{
    public required IReadOnlyDictionary<ToolRole, ToolRoleSummary> Roles { get; init; }

    /// <summary>Tools whose arguments matched no known shape — recorded, not guessed into a role
    /// (Scenario 5).</summary>
    public required IReadOnlyList<string> Unclassified { get; init; }
}

/// <summary>
/// Scenarios 2 through 5 of issue #34: derives each observed tool's role from its calls' argument
/// shapes alone — never from the tool's name — and re-runs from scratch on whatever corpus is
/// passed in, since the next machine has different tools.
/// </summary>
public static class ToolRoleDeriver
{
    public static ToolRoleDerivation Derive(IEnumerable<ToolInvocationShape> invocations)
    {
        ArgumentNullException.ThrowIfNull(invocations);

        var roleTools = Enum.GetValues<ToolRole>()
            .ToDictionary(role => role, _ => new List<ToolRoleCount>());
        var unclassified = new List<string>();

        foreach (var group in invocations.GroupBy(invocation => invocation.ToolName, StringComparer.Ordinal))
        {
            var calls = group.ToArray();
            var role = Classify(calls);

            if (role is { } classified)
            {
                roleTools[classified].Add(new ToolRoleCount
                {
                    ToolName = group.Key,
                    CallCount = calls.Length,
                });
            }
            else
            {
                unclassified.Add(group.Key);
            }
        }

        var roles = roleTools.ToDictionary(
            entry => entry.Key,
            entry => new ToolRoleSummary { Role = entry.Key, Tools = entry.Value });

        return new ToolRoleDerivation
        {
            Roles = roles,
            Unclassified = unclassified.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>
    /// Precedence matters when a tool's calls carry more than one signal: a call with both a path
    /// and replacement text is a write, not a read, so writing and searching are checked before
    /// reading. Spawn is checked first because it is the only structural (non-argument) signal.
    /// </summary>
    static ToolRole? Classify(IReadOnlyList<ToolInvocationShape> calls)
    {
        if (calls.Any(call => call.SpawnsAgent))
        {
            return ToolRole.Spawn;
        }

        if (calls.Any(call => call.HasReplacement || call.HasFileText))
        {
            return ToolRole.FileWrite;
        }

        if (calls.Any(call => call.HasPattern))
        {
            return ToolRole.Search;
        }

        if (calls.Any(call => call.HasPath))
        {
            return ToolRole.FileRead;
        }

        if (calls.Any(call => call.HasCommand))
        {
            return ToolRole.Shell;
        }

        return null;
    }
}
