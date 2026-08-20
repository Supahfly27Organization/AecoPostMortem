using System.Text.Json;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-40's served inventory (S-22, issue #35). The wire shape has to keep the two things the domain
/// makes structural: exactly one status per statement, with "Not checkable" inseparable from its
/// reason, and exactly one rule-set version rendered at a time — a client must not be able to read a
/// union across versions out of this contract because the contract never carries one.
/// </summary>
public sealed class RulesInventoryEnvelopeTests
{
    static InstructionBlock Block(string sourceFile, params string[] statements) => new()
    {
        SourceFile = sourceFile,
        Statements = statements
            .Select(text => new RuleStatement { SourceFile = sourceFile, Text = text })
            .ToArray(),
    };

    static SessionRuleSet Session(string sessionId, string startedAt, params InstructionBlock[] blocks) =>
        new()
        {
            SessionId = sessionId,
            Repository = "supahfly27/UpFront",
            StartedAt = startedAt,
            Blocks = blocks,
        };

    static RuleSetVersionId VersionOf(SessionRuleSet session) => new()
    {
        Repository = session.Repository,
        Hash = RuleSetVersionHasher.ComputeHash(session.Blocks),
    };

    static SessionRuleSet[] TwoVersions() =>
    [
        Session("s1", "2026-05-20T09:00:00Z", Block("CLAUDE.md", "Kept.", "Dropped.")),
        Session("s2", "2026-05-23T09:00:00Z", Block("CLAUDE.md", "Kept.")),
    ];

    [Fact]
    public void Every_row_serialises_its_status_with_a_discriminator_and_its_fixed_label()
    {
        var sessions = TwoVersions();
        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[1]),
            _ => RuleStatementStatus.Watched);

        var json = JsonSerializer.Serialize(RulesInventoryEnvelope.From(inventory));

        Assert.Contains("\"status\":\"watched\"", json, StringComparison.Ordinal);
        Assert.Contains("Watched", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_not_checkable_status_serialises_its_reason_alongside_its_discriminator()
    {
        var sessions = TwoVersions();
        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[1]),
            _ => RuleStatementStatus.NotCheckable("The logs record no such event."));

        var json = JsonSerializer.Serialize(RulesInventoryEnvelope.From(inventory));

        Assert.Contains("\"status\":\"notCheckable\"", json, StringComparison.Ordinal);
        Assert.Contains("The logs record no such event.", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_four_statuses_carry_four_distinct_discriminators()
    {
        var sessions = new[]
        {
            Session("s1", "2026-05-20T09:00:00Z", Block("CLAUDE.md", "A.", "B.", "C.", "D.")),
        };

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), statement => statement.Text switch
        {
            "A." => RuleStatementStatus.Watched,
            "B." => RuleStatementStatus.CheckableNotYetBuilt,
            "C." => RuleStatementStatus.NotCheckable("No shape can express it."),
            _ => RuleStatementStatus.NotARule,
        });

        var json = JsonSerializer.Serialize(RulesInventoryEnvelope.From(inventory));

        Assert.Contains("\"watched\"", json, StringComparison.Ordinal);
        Assert.Contains("\"checkableNotYetBuilt\"", json, StringComparison.Ordinal);
        Assert.Contains("\"notCheckable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"notARule\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_row_carries_its_source_file_its_sessions_and_its_in_force_window()
    {
        var sessions = TwoVersions();
        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[0]),
            _ => RuleStatementStatus.CheckableNotYetBuilt);

        var envelope = RulesInventoryEnvelope.From(inventory);

        var kept = envelope.Rows.Single(row => row.Text == "Kept.");
        Assert.Equal("CLAUDE.md", kept.SourceFile);
        Assert.Equal(["s1"], kept.SessionIds);
        Assert.Equal("2026-05-20T09:00:00Z", kept.InForceFrom);
        Assert.Equal("2026-05-20T09:00:00Z", kept.InForceUntil);
    }

    [Fact]
    public void A_retired_row_serialises_its_frozen_date_and_an_in_force_row_serialises_none()
    {
        var sessions = TwoVersions();
        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[0]),
            _ => RuleStatementStatus.CheckableNotYetBuilt);

        var envelope = RulesInventoryEnvelope.From(inventory);

        var dropped = envelope.Rows.Single(row => row.Text == "Dropped.");
        Assert.Equal("2026-05-23T09:00:00Z", dropped.AdherenceFrozenAt);
        Assert.IsType<RuleRetirementEnvelope.RetiredRule>(dropped.Retirement);

        var kept = envelope.Rows.Single(row => row.Text == "Kept.");
        Assert.Null(kept.AdherenceFrozenAt);
        Assert.IsType<RuleRetirementEnvelope.StillInForce>(kept.Retirement);
    }

    [Fact]
    public void The_envelope_names_the_one_version_it_carries_and_offers_the_others_without_their_rows()
    {
        var sessions = TwoVersions();
        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[0]),
            _ => RuleStatementStatus.CheckableNotYetBuilt);

        var envelope = RulesInventoryEnvelope.From(inventory);

        Assert.Equal(VersionOf(sessions[0]).Hash, envelope.SelectedVersion.Hash);
        Assert.Equal("supahfly27/UpFront", envelope.SelectedVersion.Repository);
        Assert.Equal(2, envelope.AvailableVersions.Count);
        Assert.Equal(["Dropped.", "Kept."], envelope.Rows.Select(row => row.Text));
    }

    [Fact]
    public void The_selected_version_carries_its_own_window_and_sample_size()
    {
        var sessions = TwoVersions();
        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[0]),
            _ => RuleStatementStatus.CheckableNotYetBuilt);

        var selected = RulesInventoryEnvelope.From(inventory).SelectedVersion;

        Assert.Equal("s1", selected.FirstSessionId);
        Assert.Equal("s1", selected.LastSessionId);
        Assert.Equal(1, selected.SessionCount);
    }

    [Fact]
    public void The_status_counts_are_served_so_a_client_never_recomputes_the_breakdown()
    {
        var sessions = new[]
        {
            Session("s1", "2026-05-20T09:00:00Z", Block("CLAUDE.md", "A.", "B.")),
        };

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), statement =>
            statement.Text == "A." ? RuleStatementStatus.Watched : RuleStatementStatus.NotARule);

        var counts = RulesInventoryEnvelope.From(inventory).StatusCounts;

        Assert.Equal(1, counts.Watched);
        Assert.Equal(0, counts.CheckableNotYetBuilt);
        Assert.Equal(0, counts.NotCheckable);
        Assert.Equal(1, counts.NotARule);
        Assert.Equal(2, counts.Total);
    }

    [Fact]
    public void The_no_rules_found_state_is_served_as_a_named_state_not_an_empty_row_list_alone()
    {
        SessionRuleSet[] sessions = [Session("s1", "2026-05-20T09:00:00Z")];

        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[0]),
            _ => RuleStatementStatus.CheckableNotYetBuilt);

        var json = JsonSerializer.Serialize(RulesInventoryEnvelope.From(inventory));

        Assert.Equal(RulesInventoryState.NoInstructionBlocks, RulesInventoryEnvelope.From(inventory).State);
        Assert.Contains("NoInstructionBlocks", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_carrying_no_statement_serialises_a_different_state_from_no_block_at_all()
    {
        SessionRuleSet[] sessions = [Session("s1", "2026-05-20T09:00:00Z", Block("CLAUDE.md"))];

        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[0]),
            _ => RuleStatementStatus.CheckableNotYetBuilt);

        Assert.Equal(
            RulesInventoryState.BlocksCarriedNoStatements,
            RulesInventoryEnvelope.From(inventory).State);
    }
}
