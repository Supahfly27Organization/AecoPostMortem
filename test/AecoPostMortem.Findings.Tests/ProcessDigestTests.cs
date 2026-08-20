namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-41's corpus digest (issue #44, S-36): findings ranked by sessions affected, and a masthead
/// that states its own scope honestly — including the two designed "nothing to show yet" states a
/// bare zero could otherwise hide.
/// </summary>
public sealed class ProcessDigestTests
{
    static MastheadCounters Counters(int sessionCount = 35, bool ingestInProgress = false) => new()
    {
        SessionCount = sessionCount,
        SpanStart = sessionCount == 0 ? null : new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        SpanEnd = sessionCount == 0 ? null : new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
        RepositoryCount = sessionCount == 0 ? 0 : 3,
        EventCount = sessionCount == 0 ? 0 : 56_138,
        ToolCallCount = sessionCount == 0 ? 0 : 12_345,
        IngestInProgress = ingestInProgress,
    };

    static RepositoryScope SingleRepoScope() => new()
    {
        SelectedRepository = "aeco/AecoPostMortem",
        AvailableRepositories = ["aeco/AecoPostMortem"],
        SessionIds = ["session-1", "session-2"],
    };

    static CheckRegistry EmptyRegistry() => new() { Entries = [] };

    static CheckRegistry RanCleanRegistry() => new()
    {
        Entries =
        [
            new CheckRegistryEntry
            {
                CheckId = "repeated-file-read",
                Status = CheckRunStatus.Ran,
                Population = 35,
                FindingCount = 0,
            },
        ],
    };

    static Finding WasteFinding(string path, params string[] sessionIds) => new()
    {
        Class = FindingClass.Waste,
        Provenance = Provenance.Derived,
        Evidence = [new EvidenceItem { Field = "data.path", Value = path }],
        Recurrence = new Recurrence
        {
            Key = path,
            Occurrences = [.. sessionIds.Select(id => new RecurrenceOccurrence { SessionId = id })],
        },
    };

    static Finding InferredFinding(string toolName, params string[] sessionIds) => new()
    {
        Class = FindingClass.MissingCapability,
        Provenance = Provenance.Inferred,
        Evidence = [new EvidenceItem { Field = "data.toolName", Value = toolName }],
        Recurrence = new Recurrence
        {
            Key = toolName,
            Occurrences = [.. sessionIds.Select(id => new RecurrenceOccurrence { SessionId = id })],
        },
    };

    [Fact]
    public void Inferred_findings_are_excluded_from_the_ranked_list_and_appear_in_their_own_section()
    {
        var observedOrDerived = WasteFinding("src/hot.cs", [.. Enumerable.Range(1, 30).Select(i => $"session-{i}")]);
        var inferred = InferredFinding("web_fetch", "session-1");

        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [observedOrDerived, inferred], SingleRepoScope());

        Assert.DoesNotContain(digest.RankedFindings, f => f.Provenance == Provenance.Inferred);
        Assert.Single(digest.RankedFindings);
        Assert.Equal("src/hot.cs", digest.RankedFindings[0].Recurrence.Key);

        Assert.Single(digest.InferredFindings);
        Assert.Equal("web_fetch", digest.InferredFindings[0].Recurrence.Key);
    }

    [Fact]
    public void A_corpus_with_only_inferred_findings_has_an_empty_ranked_list()
    {
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [InferredFinding("web_fetch", "session-1")], SingleRepoScope());

        Assert.Empty(digest.RankedFindings);
        Assert.Single(digest.InferredFindings);
    }

    /// <summary>Combines multiple ranked findings (including a tie) with Inferred findings in one
    /// input, rather than exercising ranking and exclusion in isolation: an Inferred finding present
    /// anywhere in the input must not shift the rank or the tie-break order of the Observed/Derived
    /// findings around it.</summary>
    [Fact]
    public void Ranking_of_observed_and_derived_findings_is_unaffected_by_inferred_findings_present_in_the_same_input()
    {
        var touchedThirty = WasteFinding("src/hot.cs", [.. Enumerable.Range(1, 30).Select(i => $"session-{i}")]);
        var tiedFirst = WasteFinding("src/first.cs", "session-1", "session-2");
        var tiedSecond = WasteFinding("src/second.cs", "session-3", "session-4");
        var inferredA = InferredFinding("web_fetch", "session-1");
        var inferredB = InferredFinding("search_code", "session-2", "session-3");

        var digest = ProcessDigest.Build(
            Counters(),
            RanCleanRegistry(),
            [inferredA, tiedFirst, touchedThirty, inferredB, tiedSecond],
            SingleRepoScope());

        Assert.Equal(
            ["src/hot.cs", "src/first.cs", "src/second.cs"],
            digest.RankedFindings.Select(f => f.Recurrence.Key));
        Assert.DoesNotContain(digest.RankedFindings, f => f.Provenance == Provenance.Inferred);

        Assert.Equal(
            ["web_fetch", "search_code"],
            digest.InferredFindings.Select(f => f.Recurrence.Key));
    }

    [Fact]
    public void Findings_are_ranked_by_distinct_sessions_affected_descending()
    {
        var touchedThirty = WasteFinding("src/hot.cs", [.. Enumerable.Range(1, 30).Select(i => $"session-{i}")]);
        var touchedOne = WasteFinding("src/rare.cs", "session-1");
        var touchedFive = WasteFinding("src/warm.cs", [.. Enumerable.Range(1, 5).Select(i => $"session-{i}")]);

        var digest = ProcessDigest.Build(
            Counters(),
            RanCleanRegistry(),
            [touchedOne, touchedThirty, touchedFive],
            SingleRepoScope());

        Assert.Equal(
            ["src/hot.cs", "src/warm.cs", "src/rare.cs"],
            digest.RankedFindings.Select(f => f.Recurrence.Key));
    }

    [Fact]
    public void A_tie_in_sessions_affected_preserves_input_order_rather_than_reordering_arbitrarily()
    {
        // OrderByDescending is a stable sort: two findings tied on the ranking key keep the order
        // they arrived in, deterministically — nothing about a tie is a licence to reorder.
        var first = WasteFinding("src/first.cs", "session-1", "session-2");
        var second = WasteFinding("src/second.cs", "session-3", "session-4");

        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [first, second], SingleRepoScope());

        Assert.Equal(
            ["src/first.cs", "src/second.cs"],
            digest.RankedFindings.Select(f => f.Recurrence.Key));
    }

    [Fact]
    public void Ranking_ignores_input_order_which_would_otherwise_read_as_recency()
    {
        var touchedThirty = WasteFinding("src/hot.cs", [.. Enumerable.Range(1, 30).Select(i => $"session-{i}")]);
        var touchedOne = WasteFinding("src/rare.cs", "session-1");

        // The "recent" finding (touchedOne) arrives first in the input; the ranking must not treat
        // input order as a proxy for recency or severity — only the session count decides.
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [touchedOne, touchedThirty], SingleRepoScope());

        Assert.Equal("src/hot.cs", digest.RankedFindings[0].Recurrence.Key);
    }

    [Fact]
    public void The_masthead_states_sessions_span_repositories_events_tool_calls_and_rule_coverage()
    {
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [], SingleRepoScope());

        Assert.Equal(35, digest.Masthead.Counters.SessionCount);
        Assert.NotNull(digest.Masthead.Counters.SpanStart);
        Assert.NotNull(digest.Masthead.Counters.SpanEnd);
        Assert.Equal(3, digest.Masthead.Counters.RepositoryCount);
        Assert.Equal(56_138, digest.Masthead.Counters.EventCount);
        Assert.Equal(12_345, digest.Masthead.Counters.ToolCallCount);
        Assert.Equal(RuleCoverageStatus.NotYetAnalyzed, digest.Masthead.RuleCoverage);
    }

    [Fact]
    public void An_empty_store_reads_as_not_yet_analyzed_not_as_finding_nothing()
    {
        // No check has ever run — CheckRegistry has no Ran entry.
        var digest = ProcessDigest.Build(Counters(sessionCount: 0), EmptyRegistry(), [], SingleRepoScope());

        Assert.Equal(DigestState.NotYetAnalyzed, digest.State);
        Assert.Empty(digest.RankedFindings);
    }

    [Fact]
    public void A_corpus_where_every_check_ran_and_found_nothing_reads_as_analyzed_not_not_yet_analyzed()
    {
        // Distinct from the empty-store scenario above: the checks ran, they just found nothing.
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [], SingleRepoScope());

        Assert.Equal(DigestState.Analyzed, digest.State);
        Assert.Empty(digest.RankedFindings);
    }

    [Fact]
    public void A_session_mid_ingest_reads_as_incomplete_rather_than_a_final_count()
    {
        var digest = ProcessDigest.Build(Counters(ingestInProgress: true), RanCleanRegistry(), [], SingleRepoScope());

        Assert.Equal(DigestState.Incomplete, digest.State);
    }

    [Fact]
    public void Incomplete_ingest_takes_precedence_over_whether_a_check_has_run()
    {
        // Even with no check registered yet, a mid-ingest corpus reads "incomplete", not
        // "not yet analysed" — the two designed states answer different questions and must not
        // collapse into one when both conditions happen to hold at once.
        var digest = ProcessDigest.Build(Counters(ingestInProgress: true), EmptyRegistry(), [], SingleRepoScope());

        Assert.Equal(DigestState.Incomplete, digest.State);
    }

    [Fact]
    public void Rule_coverage_reads_not_yet_analyzed_never_a_zero_violation_count()
    {
        // Release 1 ships exactly one RuleCoverageStatus value — there is no case here that could
        // be mistaken for "zero violations found" (FR-26/FR-40 are Release 2).
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [], SingleRepoScope());

        Assert.Equal(RuleCoverageStatus.NotYetAnalyzed, digest.Masthead.RuleCoverage);
    }

    [Fact]
    public void Sessions_affected_counts_distinct_sessions_not_raw_occurrences()
    {
        var finding = WasteFinding("src/dup.cs", "session-1", "session-1", "session-2");

        Assert.Equal(2, ProcessDigest.SessionsAffected(finding));
    }

    [Fact]
    public void The_masthead_states_which_repository_is_selected_and_that_it_is_the_only_one_available()
    {
        // PRD Part 8 Q5: default to one repository. A single-repository corpus has nothing else to
        // select, so the available list is exactly the selected one.
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [], SingleRepoScope());

        Assert.Equal("aeco/AecoPostMortem", digest.Masthead.RepositoryScope.SelectedRepository);
        Assert.Equal(["aeco/AecoPostMortem"], digest.Masthead.RepositoryScope.AvailableRepositories);
    }

    [Fact]
    public void A_multi_repository_store_still_names_exactly_one_selected_repository_with_the_rest_offered_not_shown()
    {
        // The measured corpus holds 3 repositories with one dominant (PRD Part 8 Q5's own figure).
        // The digest defaults to the dominant one; the other two are offered by the selector's seam,
        // not ranked or rendered as findings.
        var scope = new RepositoryScope
        {
            SelectedRepository = "aeco/AecoPostMortem",
            AvailableRepositories = ["aeco/AecoLedger", "aeco/AecoPostMortem", "aeco/Upfront"],
            SessionIds = ["session-1", "session-2"],
        };

        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [], scope);

        Assert.Equal("aeco/AecoPostMortem", digest.Masthead.RepositoryScope.SelectedRepository);
        Assert.Equal(3, digest.Masthead.RepositoryScope.AvailableRepositories.Count);
        Assert.Contains("aeco/AecoPostMortem", digest.Masthead.RepositoryScope.AvailableRepositories);
    }

    // The strip a per-finding session component would render needs the scope's session ids, in
    // order — RepositoryScope is an already-resolved plain input (this project's own established
    // pattern for MastheadCounters/RepositoryScope), so ProcessDigest.Build passes it through
    // unchanged rather than re-deriving or re-sorting it.
    [Fact]
    public void The_masthead_carries_the_scopes_session_ids_verbatim_and_in_the_order_the_caller_gave_them()
    {
        var scope = new RepositoryScope
        {
            SelectedRepository = "aeco/AecoPostMortem",
            AvailableRepositories = ["aeco/AecoPostMortem"],
            SessionIds = ["session-3", "session-1", "session-2"],
        };

        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [], scope);

        Assert.Equal(
            ["session-3", "session-1", "session-2"],
            digest.Masthead.RepositoryScope.SessionIds);
    }
}
