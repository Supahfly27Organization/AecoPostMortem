namespace AecoPostMortem.Rules;

/// <summary>
/// FR-31's four layers, most confident first. <see cref="Unresolved"/> is its own member rather
/// than a null <see cref="ResolvedOperand.Layer"/>, so a caller can never mistake "matched nothing"
/// for "matching wasn't attempted".
/// </summary>
public enum OperandResolutionLayer
{
    ExactToolName,
    McpServerField,
    DerivedRole,
    Unresolved,
}

/// <summary>
/// One rule operand, resolved: the text it named, which layer resolved it, and the tools that text
/// resolved to. <see cref="Tools"/> is empty exactly when <see cref="Layer"/> is
/// <see cref="OperandResolutionLayer.Unresolved"/>, or when a two-operand resolution's subtraction
/// (<see cref="OperandResolver.ResolveTwoOperands"/>) left this operand nothing of its own.
/// </summary>
public sealed record ResolvedOperand
{
    public required string OperandText { get; init; }

    public required OperandResolutionLayer Layer { get; init; }

    public required IReadOnlySet<string> Tools { get; init; }
}

/// <summary>
/// FR-32: two operands from the same rule, resolved independently and then subtracted — a tool
/// both would otherwise claim belongs to <see cref="OperandA"/> only. <see cref="OperandA"/> is
/// exactly what <see cref="OperandResolver.Resolve"/> returned for it; <see cref="OperandB"/> keeps
/// its own resolved <see cref="ResolvedOperand.Layer"/> but has <see cref="OperandA"/>'s tools
/// removed from its <see cref="ResolvedOperand.Tools"/>.
/// </summary>
public sealed record TwoOperandResolution
{
    public required ResolvedOperand OperandA { get; init; }

    public required ResolvedOperand OperandB { get; init; }
}

/// <summary>
/// FR-31/FR-32 (issue #37, S-23): resolves a rule's operand text against whatever corpus is passed
/// in, trying the most confident layer first and never silently dropping a name that matched
/// nothing. Built on S-21's <see cref="ToolVocabulary"/> and <see cref="ToolRoleDeriver"/> rather
/// than reimplementing tool classification.
/// </summary>
public static class OperandResolver
{
    /// <summary>
    /// Resolves one operand's text against <paramref name="invocations"/>: an exact tool name is
    /// tried first, then a structural match against each call's own
    /// <see cref="ToolInvocationShape.McpServerName"/> (never a substring match on
    /// <see cref="ToolInvocationShape.ToolName"/> — that would wrongly pull in a different server's
    /// tool whose name happens to contain the same text), then the derived role the text names.
    /// Nothing matched is <see cref="OperandResolutionLayer.Unresolved"/>, not an empty result
    /// indistinguishable from "not tried".
    /// </summary>
    public static ResolvedOperand Resolve(string operandText, IEnumerable<ToolInvocationShape> invocations)
    {
        ArgumentNullException.ThrowIfNull(operandText);
        ArgumentNullException.ThrowIfNull(invocations);

        var calls = invocations as IReadOnlyCollection<ToolInvocationShape> ?? invocations.ToList();

        var vocabulary = ToolVocabulary.Build(calls);
        if (vocabulary.Contains(operandText))
        {
            return new ResolvedOperand
            {
                OperandText = operandText,
                Layer = OperandResolutionLayer.ExactToolName,
                Tools = new HashSet<string>(StringComparer.Ordinal) { operandText },
            };
        }

        var serverTools = calls
            .Where(call => string.Equals(call.McpServerName, operandText, StringComparison.Ordinal))
            .Select(call => call.ToolName)
            .ToHashSet(StringComparer.Ordinal);
        if (serverTools.Count > 0)
        {
            return new ResolvedOperand
            {
                OperandText = operandText,
                Layer = OperandResolutionLayer.McpServerField,
                Tools = serverTools,
            };
        }

        if (Enum.TryParse<ToolRole>(operandText, ignoreCase: false, out var role))
        {
            var roleTools = ToolRoleDeriver.Derive(calls).Roles[role].Tools
                .Select(tool => tool.ToolName)
                .ToHashSet(StringComparer.Ordinal);
            if (roleTools.Count > 0)
            {
                return new ResolvedOperand
                {
                    OperandText = operandText,
                    Layer = OperandResolutionLayer.DerivedRole,
                    Tools = roleTools,
                };
            }
        }

        return new ResolvedOperand
        {
            OperandText = operandText,
            Layer = OperandResolutionLayer.Unresolved,
            Tools = new HashSet<string>(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// FR-32: resolves both operands of a two-operand rule shape (e.g. <c>prefer-A-over-B</c>)
    /// against the same corpus, then subtracts <paramref name="operandAText"/>'s resolved tools
    /// from <paramref name="operandBText"/>'s — the known-unfixed defect from discovery finding 8,
    /// where the role layer pulled one tool into both operands at once. Operand A's own result is
    /// returned unchanged; it never loses a tool to this subtraction.
    /// </summary>
    public static TwoOperandResolution ResolveTwoOperands(
        string operandAText,
        string operandBText,
        IEnumerable<ToolInvocationShape> invocations)
    {
        ArgumentNullException.ThrowIfNull(operandAText);
        ArgumentNullException.ThrowIfNull(operandBText);
        ArgumentNullException.ThrowIfNull(invocations);

        var calls = invocations as IReadOnlyCollection<ToolInvocationShape> ?? invocations.ToList();

        var resolvedA = Resolve(operandAText, calls);
        var resolvedB = Resolve(operandBText, calls);

        var subtractedB = resolvedB.Tools
            .Where(tool => !resolvedA.Tools.Contains(tool))
            .ToHashSet(StringComparer.Ordinal);

        return new TwoOperandResolution
        {
            OperandA = resolvedA,
            OperandB = resolvedB with { Tools = subtractedB },
        };
    }
}
