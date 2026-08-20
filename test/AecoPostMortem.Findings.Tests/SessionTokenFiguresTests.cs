using System.Reflection;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-24 (S-11, issue #20): the masthead's session-scoped token totals. Each test name maps to one
/// of the story's three Gherkin scenarios.
/// </summary>
public sealed class SessionTokenFiguresTests
{
    static Session SessionWith(long? inputTokens, long? outputTokens, long? cacheReadTokens = null,
        long? cacheWriteTokens = null, long? reasoningTokens = null, int? modelCount = null) => new()
    {
        SessionId = "session-1",
        StartedAt = "2026-08-09T20:14:36.758Z",
        CopilotVersion = "0.0.339",
        EventSchemaVersion = "1",
        SourceFile = @"~/.copilot/session-state/session-1/events.jsonl",
        Cwd = @"C:\repo",
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
        CacheReadTokens = cacheReadTokens,
        CacheWriteTokens = cacheWriteTokens,
        ReasoningTokens = reasoningTokens,
        ModelCount = modelCount,
    };

    /// <summary>Scenario 1: a session whose shutdown event carried per-model metrics reads its
    /// totals from that event, marked Observed.</summary>
    [Fact]
    public void Session_totals_come_from_the_shutdown_event()
    {
        var session = SessionWith(
            inputTokens: 12_345,
            outputTokens: 6_789,
            cacheReadTokens: 100,
            cacheWriteTokens: 50,
            reasoningTokens: 25,
            modelCount: 2);

        var figures = SessionTokenFigures.From(session);

        var observed = Assert.IsType<SessionTokenFigures.Observed>(figures);
        Assert.Equal(12_345, observed.InputTokens);
        Assert.Equal(6_789, observed.OutputTokens);
        Assert.Equal(100, observed.CacheReadTokens);
        Assert.Equal(50, observed.CacheWriteTokens);
        Assert.Equal(25, observed.ReasoningTokens);
        Assert.Equal(2, observed.ModelCount);
    }

    /// <summary>Scenario 2: a session with no shutdown metrics states that plainly rather than
    /// showing zero.</summary>
    [Fact]
    public void A_session_without_totals_says_so()
    {
        var session = SessionWith(inputTokens: null, outputTokens: null);

        var figures = SessionTokenFigures.From(session);

        Assert.Same(SessionTokenFigures.NotRecorded, figures);
        Assert.IsType<SessionTokenFigures.SessionTotalsNotRecorded>(figures);
    }

    /// <summary>The edge case named in the story: shutdown metrics missing on 4 of 35 measured
    /// sessions is common enough to be a designed state, not an afterthought — a session carrying
    /// only one of the pair is not a partial total, it is a missing one.</summary>
    [Fact]
    public void Half_a_pair_of_totals_is_treated_as_not_recorded()
    {
        var inputOnly = SessionWith(inputTokens: 500, outputTokens: null);
        var outputOnly = SessionWith(inputTokens: null, outputTokens: 500);

        Assert.Same(SessionTokenFigures.NotRecorded, SessionTokenFigures.From(inputOnly));
        Assert.Same(SessionTokenFigures.NotRecorded, SessionTokenFigures.From(outputOnly));
    }

    /// <summary>Scenario 3: no surface in the product renders a cost-like figure. Enforced
    /// structurally — no property on either shape may even exist to hold one, so there is nothing
    /// for a masthead to accidentally print.</summary>
    [Fact]
    public void No_shape_carries_a_cost_or_currency_field()
    {
        string[] forbiddenTerms = ["cost", "price", "currency", "dollar", "usd", "spend"];
        Type[] shapes =
        [
            typeof(SessionTokenFigures),
            typeof(SessionTokenFigures.Observed),
            typeof(SessionTokenFigures.SessionTotalsNotRecorded),
        ];

        foreach (var shape in shapes)
        {
            var offending = shape
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .Where(name => forbiddenTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Assert.True(
                offending.Length == 0,
                $"{shape.Name} carries a cost-like field the product must never show: "
                + string.Join(", ", offending));
        }
    }

    [Fact]
    public void From_rejects_a_null_session()
    {
        Assert.Throws<ArgumentNullException>(() => SessionTokenFigures.From(null!));
    }
}
