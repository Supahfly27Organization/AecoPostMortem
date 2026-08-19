using System.Globalization;
using System.Text.RegularExpressions;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-56's rendering mechanism. <c>static</c> so it can hold no instance field — there is nowhere
/// for a model client or a clock to be injected — and every parameter below is already-resolved,
/// pure data: a template, the finding's evidence, and its resolution. Nothing reachable from
/// <see cref="Render"/> can make a model call or read a clock (issue #48, Scenario "No model is
/// called"), which <c>SuggestionRendererStructureTests</c> proves by reflecting over this type
/// rather than merely asserting it.
/// </summary>
public static partial class SuggestionRenderer
{
    /// <summary>The two placeholder names <see cref="Resolution"/> answers directly, rather than
    /// through an evidence field — FR-33's "layer used per operand" and "resulting call count".</summary>
    const string OperandLayerPlaceholder = "OperandLayer";
    const string CallCountPlaceholder = "CallCount";

    /// <summary>
    /// Renders <paramref name="template"/> against <paramref name="evidence"/> and
    /// <paramref name="resolution"/>, or returns <c>null</c> when there is no template to render
    /// (FR-56: "a finding class with no template ships with its evidence and no suggestion, never a
    /// generic one") or when a placeholder cannot be bound to a concrete operand (FR-56's edge case:
    /// "a template that cannot name a concrete operand should produce no suggestion rather than a
    /// vague one").
    /// </summary>
    public static Suggestion? Render(
        SuggestionTemplate? template,
        IReadOnlyList<EvidenceItem> evidence,
        Resolution? resolution)
    {
        if (template is null)
        {
            return null;
        }

        var text = template.Format;

        foreach (Match match in Placeholder().Matches(template.Format))
        {
            var name = match.Groups[1].Value;

            if (!TryResolveOperand(name, evidence, resolution, out var value))
            {
                return null;
            }

            text = text.Replace(match.Value, value, StringComparison.Ordinal);
        }

        return new Suggestion { Text = text };
    }

    static bool TryResolveOperand(
        string placeholderName,
        IReadOnlyList<EvidenceItem> evidence,
        Resolution? resolution,
        out string value)
    {
        if (placeholderName == OperandLayerPlaceholder)
        {
            value = resolution?.OperandLayer!;
            return resolution is not null;
        }

        if (placeholderName == CallCountPlaceholder)
        {
            value = resolution?.CallCount.ToString(CultureInfo.InvariantCulture)!;
            return resolution is not null;
        }

        var matches = evidence
            .Where(item => item.Field == placeholderName)
            .Select(item => item.Value)
            .ToArray();

        if (matches.Length == 0)
        {
            value = "";
            return false;
        }

        value = FormatOperandList(matches);
        return true;
    }

    /// <summary>Generalises FR-35's worked example — "name `rg`, `glob` and `view`" — to any number
    /// of values sharing one evidence field: one value is quoted alone, two are joined with "and",
    /// three or more use an Oxford comma before the final "and".</summary>
    static string FormatOperandList(IReadOnlyList<string> values)
    {
        var quoted = values.Select(value => $"`{value}`").ToArray();

        return quoted.Length == 1
            ? quoted[0]
            : string.Join(", ", quoted[..^1]) + " and " + quoted[^1];
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Placeholder();
}
