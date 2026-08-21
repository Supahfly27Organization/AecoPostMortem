using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-41's real orchestration (S-36, issue #44): <see cref="ApiHost.GetDigest"/> assembles a live
/// <see cref="ProcessDigest"/> from six of the seven waste/missing-capability check orchestrators,
/// read straight through <c>Data.Execution</c> the same way <see cref="SessionRouteTests"/> already
/// exercises <c>ApiHost.GetSession</c>.
/// </summary>
public sealed class DigestRouteTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static readonly JsonSerializerOptions ClientOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    static Session ASession(string sessionId, string? repository = null) => new()
    {
        SessionId = sessionId,
        StartedAt = "2026-08-16T10:00:00Z",
        EndedAt = "2026-08-16T10:10:00Z",
        CopilotVersion = "0.0.339",
        EventSchemaVersion = "1",
        SourceFile = $@"~/.copilot/session-state/{sessionId}/events.jsonl",
        Cwd = @"C:\repo",
        Repository = repository,
    };

    [Fact]
    public async Task An_empty_store_serves_an_analyzed_digest_with_no_findings()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(DigestState.Analyzed, envelope!.State);
            Assert.Empty(envelope.RankedFindings);
            Assert.Equal(0, envelope.Masthead.SessionCount);
            Assert.Null(envelope.Masthead.SpanStart);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Mockup parity item #8: the masthead's sixth mockup cell — every <c>Agent</c> row in
    /// the corpus, regardless of repository, the same corpus-wide-then-filter shape every other
    /// masthead counter already uses.</summary>
    [Fact]
    public async Task The_masthead_serves_the_corpus_wide_subagent_count()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1", "org/majority"));
            context.Sessions.Add(ASession("s2", "org/minority"));
            context.Agents.Add(new Agent
            {
                SessionId = "s1",
                AgentId = "a1",
                SpawningToolCallId = "tc1",
                Name = "general-purpose",
                DisplayName = "General purpose",
                StartedAt = "2026-08-16T10:00:01Z",
                Outcome = AgentOutcome.Completed,
            });
            context.Agents.Add(new Agent
            {
                SessionId = "s2",
                AgentId = "a2",
                SpawningToolCallId = "tc2",
                Name = "general-purpose",
                DisplayName = "General purpose",
                StartedAt = "2026-08-16T10:00:01Z",
                Outcome = AgentOutcome.Completed,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            // Corpus-wide, not scoped to the selected repository (org/majority): both agents count,
            // even though "s2" belongs to org/minority, the repository not selected here.
            Assert.Equal(2, envelope!.Masthead.SubagentCount);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task Four_repeated_reads_of_one_path_serve_as_a_ranked_finding()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            for (var i = 0; i < 4; i++)
            {
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "s1",
                    ToolCallId = $"tc{i}",
                    ToolName = "view",
                    Path = "/repeated.cs",
                    StartedAt = "2026-08-16T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
            }
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var finding = Assert.Single(envelope!.RankedFindings);
            Assert.Equal(FindingClass.Waste, finding.Class);
            Assert.Contains(finding.Evidence, item => item.Field == "data.path" && item.Value == "/repeated.cs");
            Assert.Equal(1, envelope.Masthead.SessionCount);
            Assert.Equal(4, envelope.Masthead.ToolCallCount);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_failed_hook_pair_serves_its_error_text_read_from_raw()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            const string startPayload = """{"id":"h1","data":{"hookInvocationId":"inv-1","hookType":"sessionStart"}}""";
            const string endPayload = """{"id":"h2","data":{"hookInvocationId":"inv-1","hookType":"sessionStart","success":false,"error":{"message":"ParserError: bad token"}}}""";
            context.RawEvents.Add(new RawEvent(
                "s1", 0, "hook.start", "2026-08-16T10:00:01Z", "0.0.339",
                "events.jsonl", 0, "hash-0", startPayload));
            context.RawEvents.Add(new RawEvent(
                "s1", 1, "hook.end", "2026-08-16T10:00:02Z", "0.0.339",
                "events.jsonl", 100, "hash-1", endPayload));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var finding = Assert.Single(envelope!.RankedFindings);
            Assert.Contains(finding.Evidence, item => item.Field == "data.error" && item.Value == "ParserError: bad token");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_report_intent_call_that_returns_to_an_earlier_phase_serves_a_phase_churn_finding()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            context.SaveChanges();
        }

        using (var context = temporary.Store.Open())
        {
            var events = new[]
            {
                Intent("s1", 0, "2026-08-16T10:00:01Z", "explore"),
                Intent("s1", 1, "2026-08-16T10:00:02Z", "implement"),
                Intent("s1", 2, "2026-08-16T10:00:03Z", "explore"),
            };
            context.RawEvents.AddRange(events);
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Contains(envelope!.RankedFindings, f => f.Evidence.Any(e => e.Field == "returns" && e.Value == "1"));
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    [Fact]
    public async Task The_digest_scopes_findings_to_the_repository_with_the_most_sessions()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("majority-1", "org/majority"));
            context.Sessions.Add(ASession("majority-2", "org/majority"));
            context.Sessions.Add(ASession("minority-1", "org/minority"));

            for (var i = 0; i < 4; i++)
            {
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "minority-1",
                    ToolCallId = $"tc{i}",
                    ToolName = "view",
                    Path = "/minority-only.cs",
                    StartedAt = "2026-08-16T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
            }
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("org/majority", envelope!.Masthead.RepositoryScope.SelectedRepository);
            Assert.Equal(
                new[] { "org/majority", "org/minority" },
                envelope.Masthead.RepositoryScope.AvailableRepositories);
            Assert.Empty(envelope.RankedFindings);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Proves the scoping is a real filter, not just a correct default pick: a qualifying
    /// finding in the minority repository must not leak into the majority repository's ranked list
    /// even when both repositories genuinely have one.</summary>
    [Fact]
    public async Task A_finding_in_the_non_selected_repository_never_appears_in_the_ranked_list()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("majority-1", "org/majority"));
            context.Sessions.Add(ASession("majority-2", "org/majority"));
            context.Sessions.Add(ASession("minority-1", "org/minority"));

            for (var i = 0; i < 4; i++)
            {
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "majority-1",
                    ToolCallId = $"maj-tc{i}",
                    ToolName = "view",
                    Path = "/majority-only.cs",
                    StartedAt = "2026-08-16T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "minority-1",
                    ToolCallId = $"min-tc{i}",
                    ToolName = "view",
                    Path = "/minority-only.cs",
                    StartedAt = "2026-08-16T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
            }
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("org/majority", envelope!.Masthead.RepositoryScope.SelectedRepository);
            var finding = Assert.Single(envelope.RankedFindings);
            Assert.Contains(finding.Evidence, item => item.Field == "data.path" && item.Value == "/majority-only.cs");
            Assert.DoesNotContain(finding.Evidence, item => item.Value == "/minority-only.cs");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Piece 2 of the per-finding session strip (issue tracked in the mockup-parity
    /// backlog): <c>Masthead.RepositoryScope.SessionIds</c> is what a session strip needs and
    /// <c>Recurrence</c> alone cannot give it — every session in the currently selected repository,
    /// in real chronological order, not session id text (random UUIDs here) or insertion order.
    /// Session ids are deliberately chosen so alphabetical order and chronological order disagree
    /// (`zz-first` is earliest, `aa-third` is latest) — a regression to sorting by session id text
    /// (the exact PR #108/#112 defect this ordering convention exists to avoid) would produce the
    /// reverse of the asserted order, so this test cannot pass by coincidence the way sorting ids
    /// that already happen to agree both ways could.</summary>
    [Fact]
    public async Task The_masthead_serves_the_selected_repositorys_own_sessions_in_real_chronological_order()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("zz-first", "org/majority", "2026-08-16T10:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("aa-third", "org/majority", "2026-08-16T12:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("mm-second", "org/majority", "2026-08-16T11:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("only-one", "org/minority", "2026-08-16T09:00:00Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal("org/majority", envelope!.Masthead.RepositoryScope.SelectedRepository);
            Assert.Equal(
                new[] { "zz-first", "mm-second", "aa-third" },
                envelope.Masthead.RepositoryScope.SessionIds);
            Assert.DoesNotContain("only-one", envelope.Masthead.RepositoryScope.SessionIds);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The date-range filter (mirroring the pager task's own design decision, recorded in
    /// <c>Api/CLAUDE.md</c>): a <c>from</c>/<c>to</c> query pair re-scopes the whole analysis to
    /// sessions whose own <see cref="Session.StartedAt"/> falls in range — never a display filter
    /// over findings already computed against every session. A finding whose only occurrence is
    /// outside the window must not appear at all, the same way a finding outside the selected
    /// repository never appears (<see cref="A_finding_in_the_non_selected_repository_never_appears_in_the_ranked_list"/>).</summary>
    [Fact]
    public async Task A_date_range_filter_excludes_findings_whose_only_occurrence_is_outside_it()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("in-range", "org/majority", "2026-06-15T10:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("out-of-range", "org/majority", "2026-01-01T10:00:00Z"));

            for (var i = 0; i < 4; i++)
            {
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "in-range",
                    ToolCallId = $"in-tc{i}",
                    ToolName = "view",
                    Path = "/in-range.cs",
                    StartedAt = "2026-06-15T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
                context.ToolCalls.Add(new ToolCall
                {
                    SessionId = "out-of-range",
                    ToolCallId = $"out-tc{i}",
                    ToolName = "view",
                    Path = "/out-of-range.cs",
                    StartedAt = "2026-01-01T10:00:01Z",
                    OwnerKind = OwnerKind.Main,
                });
            }
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(
                $"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=2026-06-01&{ApiHost.ToParameter}=2026-06-30",
                ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var finding = Assert.Single(envelope!.RankedFindings);
            Assert.Contains(finding.Evidence, item => item.Field == "data.path" && item.Value == "/in-range.cs");
            Assert.DoesNotContain(finding.Evidence, item => item.Value == "/out-of-range.cs");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The date filter's boundaries are inclusive of the whole named day — a session
    /// starting anywhere within <c>to</c>'s own calendar day is still in range, not excluded because
    /// its timestamp carries a time-of-day later than midnight.</summary>
    [Fact]
    public async Task A_session_starting_late_on_the_to_boundarys_own_day_is_still_in_range()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("late-on-boundary", "org/majority", "2026-06-30T23:59:00Z"));
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "late-on-boundary",
                ToolCallId = "tc0",
                ToolName = "view",
                Path = "/late.cs",
                StartedAt = "2026-06-30T23:59:01Z",
                OwnerKind = OwnerKind.Main,
            });
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "late-on-boundary",
                ToolCallId = "tc1",
                ToolName = "view",
                Path = "/late.cs",
                StartedAt = "2026-06-30T23:59:02Z",
                OwnerKind = OwnerKind.Main,
            });
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "late-on-boundary",
                ToolCallId = "tc2",
                ToolName = "view",
                Path = "/late.cs",
                StartedAt = "2026-06-30T23:59:03Z",
                OwnerKind = OwnerKind.Main,
            });
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "late-on-boundary",
                ToolCallId = "tc3",
                ToolName = "view",
                Path = "/late.cs",
                StartedAt = "2026-06-30T23:59:04Z",
                OwnerKind = OwnerKind.Main,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(
                $"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=2026-06-01&{ApiHost.ToParameter}=2026-06-30",
                ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Single(envelope!.RankedFindings);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Mirrors <see cref="MastheadCounters"/>'s own established rule that it ignores
    /// repository selection (<c>Api/CLAUDE.md</c>'s "corpus-wide regardless of repository" remark) —
    /// a date filter is the same kind of ranking-scope lens, not a corpus-wide fact, so the masthead's
    /// session count must not shrink under it.</summary>
    [Fact]
    public async Task Masthead_counters_stay_corpus_wide_when_a_date_filter_is_applied()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("in-range", "org/majority", "2026-06-15T10:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("out-of-range", "org/majority", "2026-01-01T10:00:00Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(
                $"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=2026-06-01&{ApiHost.ToParameter}=2026-06-30",
                ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(2, envelope!.Masthead.SessionCount);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The per-finding session strip (<c>RepositoryScope.SessionIds</c>) is documented as
    /// "the exact set every check ran over" — under a date filter that set is narrower than the
    /// repository selection alone, so this field must follow the filter rather than stay at the
    /// repository-wide list, or the strip would show positions for sessions no check considered.</summary>
    [Fact]
    public async Task RepositoryScope_session_ids_follow_the_date_filter()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("in-range", "org/majority", "2026-06-15T10:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("out-of-range", "org/majority", "2026-01-01T10:00:00Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(
                $"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=2026-06-01&{ApiHost.ToParameter}=2026-06-30",
                ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(new[] { "in-range" }, envelope!.Masthead.RepositoryScope.SessionIds);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>An inverted range is a real, honest refusal (matching this project's other
    /// caller-error responses, e.g. <see cref="ApiHost.MonitorComparisonRoute"/>'s missing-parameter
    /// 400) rather than a silently empty digest that reads as "no findings" instead of "bad request".</summary>
    [Fact]
    public async Task An_inverted_date_range_answers_400()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync(
                $"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=2026-06-30&{ApiHost.ToParameter}=2026-06-01",
                Cancellation);

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Code review gap: <c>ArgumentException</c> is thrown by <see cref="ApiHost.GetDigest"/>
    /// itself and only surfaces as 400 through the route's own <c>catch</c> — this proves the throw
    /// path directly, so it is not dead code by test coverage even though no HTTP request can reach
    /// it without going through the route handler that already catches it.</summary>
    [Fact]
    public void GetDigest_throws_ArgumentException_for_an_inverted_range()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        var ex = Assert.Throws<ArgumentException>(() => ApiHost.GetDigest(
            temporary.Store, from: new DateOnly(2026, 6, 30), to: new DateOnly(2026, 6, 1)));

        Assert.Equal("from", ex.ParamName);
    }

    /// <summary>Code review gap: a malformed <c>from</c> query value must not reach <see cref="ApiHost.GetDigest"/>
    /// at all — ASP.NET Core's own <c>DateOnly?</c> minimal-API binding refuses it with 400 before the
    /// route delegate runs, an assumption this test makes explicit rather than leaving untested.</summary>
    [Fact]
    public async Task A_malformed_from_value_answers_400_before_reaching_GetDigest()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var response = await client.GetAsync($"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=banana", Cancellation);

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Both bounds are independent (<c>Api/CLAUDE.md</c>'s own remarks) — code review flagged
    /// that only the both-supplied case was exercised over HTTP before this test.</summary>
    [Fact]
    public async Task Only_from_supplied_excludes_sessions_that_started_earlier()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("in-range", "org/majority", "2026-06-15T10:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("out-of-range", "org/majority", "2026-01-01T10:00:00Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(
                $"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=2026-06-01", ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(new[] { "in-range" }, envelope!.Masthead.RepositoryScope.SessionIds);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The <c>to</c>-only counterpart of the test above.</summary>
    [Fact]
    public async Task Only_to_supplied_excludes_sessions_that_started_later()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("in-range", "org/majority", "2026-01-01T10:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("out-of-range", "org/majority", "2026-06-15T10:00:00Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(
                $"{ApiHost.DigestRoute}?{ApiHost.ToParameter}=2026-02-01", ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(new[] { "in-range" }, envelope!.Masthead.RepositoryScope.SessionIds);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The <c>from</c> boundary's own inclusivity, mirroring the existing <c>to</c>-boundary
    /// test (<see cref="A_session_starting_late_on_the_to_boundarys_own_day_is_still_in_range"/>) —
    /// code review flagged that only the <c>to</c> side had a boundary test.</summary>
    [Fact]
    public async Task A_session_starting_early_on_the_from_boundarys_own_day_is_still_in_range()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("early-on-boundary", "org/majority", "2026-06-01T00:00:01Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(
                $"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=2026-06-01", ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(new[] { "early-on-boundary" }, envelope!.Masthead.RepositoryScope.SessionIds);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Code review gap: a range matching zero sessions in the selected repository must still
    /// serve an honest, non-crashing digest — an empty session strip, corpus-wide masthead unaffected,
    /// and (per <c>DigestPage.tsx</c>'s own fix) the frontend renders a distinct message for this case
    /// rather than "every check ran and found nothing".</summary>
    [Fact]
    public async Task A_range_matching_zero_sessions_serves_an_empty_but_honest_scope()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("s1", "org/majority", "2026-06-15T10:00:00Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(
                $"{ApiHost.DigestRoute}?{ApiHost.FromParameter}=2026-01-01&{ApiHost.ToParameter}=2026-01-31",
                ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Empty(envelope!.Masthead.RepositoryScope.SessionIds);
            Assert.Empty(envelope.RankedFindings);
            // The masthead is corpus-wide, unaffected by the filter.
            Assert.Equal(1, envelope.Masthead.SessionCount);
            // Every check still reports Ran (a check runs unconditionally, regardless of population
            // size — pre-existing behaviour this task does not change), so SilentChecks is non-empty
            // here: each entry honestly states its own population of 0. This is exactly why
            // DigestPage.tsx renders its own "no sessions in range" message ahead of the ranked list,
            // clean-checks grid and inferred-findings section for this case — see
            // DigestPage.test.tsx's own coverage of that branch — rather than this endpoint trying to
            // suppress a check that genuinely did run, just over an empty population.
            Assert.All(envelope.SilentChecks, check => Assert.Equal(0, check.Population));
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>No <c>from</c>/<c>to</c> supplied at all must behave exactly as before this feature —
    /// the pre-existing regression every other test in this file already proves implicitly, stated
    /// explicitly here for the date-filter code path in particular.</summary>
    [Fact]
    public async Task No_date_filter_supplied_serves_every_session_in_the_selected_repository()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASessionStartedAt("s1", "org/majority", "2026-06-15T10:00:00Z"));
            context.Sessions.Add(ASessionStartedAt("s2", "org/majority", "2026-01-01T10:00:00Z"));
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(2, envelope!.Masthead.RepositoryScope.SessionIds.Count);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Code review Minor (both reviews): an offset-less timestamp must still be read as
    /// UTC for the date filter's own boundary comparison, not the parsing machine's local time — see
    /// <c>ApiHost.ParseTimestampAsUtc</c>'s own remarks. Asserted directly against the internal
    /// method (<c>InternalsVisibleTo</c>, <c>AecoPostMortem.Api.csproj</c>) rather than through the
    /// full HTTP path: a differential test built on a real timestamp would only fail on a host whose
    /// own local offset happens to shift the date across the tested boundary, making the test's own
    /// pass/fail depend on the CI machine's ambient timezone — exactly the non-determinism PRD §3.8
    /// exists to rule out. Asserting <see cref="DateTimeOffset.Offset"/> is exactly zero is
    /// deterministic on every machine.</summary>
    [Fact]
    public void An_offsetless_timestamp_is_read_as_UTC_not_the_parsing_machines_local_time()
    {
        // No trailing 'Z' or offset — DateTimeStyles.RoundtripKind would read this as the test
        // machine's own local time; DateTimeStyles.AssumeUniversal must not.
        var parsed = ApiHost.ParseTimestampAsUtc("2026-06-15T10:00:00");

        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc), parsed.UtcDateTime);
    }

    static Session ASessionStartedAt(string sessionId, string repository, string startedAt) => new()
    {
        SessionId = sessionId,
        StartedAt = startedAt,
        EndedAt = startedAt,
        CopilotVersion = "0.0.339",
        EventSchemaVersion = "1",
        SourceFile = $@"~/.copilot/session-state/{sessionId}/events.jsonl",
        Cwd = @"C:\repo",
        Repository = repository,
    };

    static RawEvent SystemMessage(string sessionId, string content, long sequence = 0) => new(
        sessionId,
        sequence,
        "system.message",
        "2026-08-16T10:00:00Z",
        "0.0.339",
        $"events-{sessionId}.jsonl",
        sequence,
        RawPayload.ContentHashOfText(content),
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "system.message",
            ["data"] = new Dictionary<string, string> { ["content"] = content },
        }));

    const string BannedToolPrompt = """
        <custom_instruction>
        CLAUDE.md
        - Never use grep.
        </custom_instruction>
        """;

    /// <summary>Piece 3's second slice: a banned tool actually called serves a
    /// <see cref="FindingClass.RuleAdherenceToolChoice"/> finding, wired the same way the other six
    /// checks already are.</summary>
    [Fact]
    public async Task A_banned_tool_actually_called_serves_a_rule_adherence_finding()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            context.RawEvents.Add(SystemMessage("s1", BannedToolPrompt));
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc1",
                ToolName = "grep",
                StartedAt = "2026-08-16T10:00:01Z",
                OwnerKind = OwnerKind.Main,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var finding = Assert.Single(
                envelope!.RankedFindings, f => f.Class == FindingClass.RuleAdherenceToolChoice);
            Assert.Contains(finding.Evidence, item => item.Field == "named_tool" && item.Value == "grep");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>Mockup parity item #15: the masthead's rule-coverage figure is real, and agrees with
    /// what <c>/api/rules-inventory</c> independently serves for the same (default) rule-set
    /// version — the "one served figure, never recounted differently on a second surface" guarantee,
    /// checked end to end rather than only at the unit level.</summary>
    [Fact]
    public async Task The_masthead_serves_a_real_analyzed_coverage_figure_matching_the_rules_inventory()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            context.RawEvents.Add(SystemMessage("s1", BannedToolPrompt));
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc1",
                ToolName = "grep",
                StartedAt = "2026-08-16T10:00:01Z",
                OwnerKind = OwnerKind.Main,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var digestEnvelope = await client.GetFromJsonAsync<DigestEnvelope>(
                ApiHost.DigestRoute, ClientOptions, Cancellation);
            var inventoryEnvelope = await client.GetFromJsonAsync<RulesInventoryEnvelope>(
                ApiHost.RulesInventoryRoute, ClientOptions, Cancellation);

            Assert.NotNull(digestEnvelope);
            Assert.NotNull(inventoryEnvelope);

            var coverage = Assert.IsType<RuleCoverageStatusEnvelope.AnalyzedCoverage>(
                digestEnvelope!.Masthead.RuleCoverage);
            Assert.Equal(1, coverage.Counts.Watched);
            Assert.Equal(1, coverage.Counts.Total);

            Assert.Equal(inventoryEnvelope!.StatusCounts.Watched, coverage.Counts.Watched);
            Assert.Equal(inventoryEnvelope.StatusCounts.CheckableNotYetBuilt, coverage.Counts.CheckableNotYetBuilt);
            Assert.Equal(inventoryEnvelope.StatusCounts.NotCheckable, coverage.Counts.NotCheckable);
            Assert.Equal(inventoryEnvelope.StatusCounts.NotARule, coverage.Counts.NotARule);
            Assert.Equal(inventoryEnvelope.StatusCounts.Total, coverage.Counts.Total);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    /// <summary>The Release-1 "not yet" state still holds for a store with no rule-set version to
    /// select at all — an empty store never fabricates a zero-of-everything analyzed figure.</summary>
    [Fact]
    public async Task An_empty_store_serves_not_yet_analyzed_rule_coverage()
    {
        using var temporary = new TemporaryStore();
        temporary.Store.Open().Dispose();

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            Assert.Equal(RuleCoverageStatusEnvelope.NotYetAnalyzed, envelope!.Masthead.RuleCoverage);
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    const string UseAAfterBPrompt = """
        <custom_instruction>
        CLAUDE.md
        - Use rg after glob.
        </custom_instruction>
        """;

    /// <summary>Piece 3's fourth slice: a later-tool call with no earlier prerequisite call serves a
    /// <see cref="FindingClass.RuleAdherenceToolChoice"/> finding, wired the same way the other eight
    /// checks already are.</summary>
    [Fact]
    public async Task A_later_tool_call_with_no_earlier_prerequisite_serves_a_rule_adherence_finding()
    {
        using var temporary = new TemporaryStore();
        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(ASession("s1"));
            context.RawEvents.Add(SystemMessage("s1", UseAAfterBPrompt));
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc1",
                ToolName = "rg",
                StartedAt = "2026-08-16T10:00:01Z",
                OwnerKind = OwnerKind.Main,
            });
            context.ToolCalls.Add(new ToolCall
            {
                SessionId = "s1",
                ToolCallId = "tc2",
                ToolName = "glob",
                StartedAt = "2026-08-16T10:00:02Z",
                OwnerKind = OwnerKind.Main,
            });
            context.SaveChanges();
        }

        await using var app = ApiHost.Build(temporary.Store, MissingCopilotRoot(temporary), port: 0);
        await app.StartAsync(Cancellation);
        try
        {
            using var client = HttpClientFor(app);
            var envelope = await client.GetFromJsonAsync<DigestEnvelope>(ApiHost.DigestRoute, ClientOptions, Cancellation);

            Assert.NotNull(envelope);
            var finding = Assert.Single(
                envelope!.RankedFindings, f => f.Class == FindingClass.RuleAdherenceToolChoice);
            Assert.Contains(finding.Evidence, item => item.Field == "later_tool" && item.Value == "rg");
            Assert.Contains(finding.Evidence, item => item.Field == "earlier_tool" && item.Value == "glob");
        }
        finally
        {
            await app.StopAsync(Cancellation);
        }
    }

    static RawEvent Intent(string sessionId, long sequence, string timestamp, string intent)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = $"e{sequence}",
            data = new
            {
                toolCallId = $"tc{sequence}",
                toolName = "report_intent",
                arguments = new { intent },
            },
        });
        return new RawEvent(
            sessionId, sequence, "tool.execution_start", timestamp, "0.0.339",
            $"~/.copilot/session-state/{sessionId}/events.jsonl", sequence, $"hash-{sequence}", payload);
    }

    static string MissingCopilotRoot(TemporaryStore temporary) =>
        System.IO.Path.Combine(temporary.Folder, "no-such-copilot-root");

    static HttpClient HttpClientFor(WebApplication app) =>
        new() { BaseAddress = new Uri(ListeningAddress(app), UriKind.Absolute) };

    static string ListeningAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
}
