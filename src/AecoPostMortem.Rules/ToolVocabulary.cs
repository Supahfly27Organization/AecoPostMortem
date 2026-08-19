namespace AecoPostMortem.Rules;

/// <summary>
/// Scenario 1 of issue #34: the tool vocabulary is whatever the corpus contains, never a list named
/// in source code.
/// </summary>
public static class ToolVocabulary
{
    public static IReadOnlySet<string> Build(IEnumerable<ToolInvocationShape> invocations)
    {
        ArgumentNullException.ThrowIfNull(invocations);

        return invocations
            .Select(invocation => invocation.ToolName)
            .ToHashSet(StringComparer.Ordinal);
    }
}
