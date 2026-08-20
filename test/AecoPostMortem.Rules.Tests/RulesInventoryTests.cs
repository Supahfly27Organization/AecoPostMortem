using System.Reflection;
using System.Runtime.CompilerServices;

namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-40 (issue #35, S-22): every extracted statement listed with exactly one status, its source
/// file, the sessions carrying it, its in-force window, and — for a statement gone from the most
/// recent rule-set version — its retirement date, all scoped to one rule-set version at a time.
/// The measured 4 / 9 / 9 / 21 status split makes "Not a rule" the largest bucket, and a measured
/// 34 of 43 statements are absent from the most recent session, which is why a union across
/// versions is the one thing this surface must never render.
/// </summary>
public sealed class RulesInventoryTests
{
    static InstructionBlock Block(string sourceFile, params string[] statements) => new()
    {
        SourceFile = sourceFile,
        Statements = statements
            .Select(text => new RuleStatement { SourceFile = sourceFile, Text = text })
            .ToArray(),
    };

    static SessionRuleSet Session(
        string sessionId, string? repository, string startedAt, params InstructionBlock[] blocks) =>
        new()
        {
            SessionId = sessionId,
            Repository = repository,
            StartedAt = startedAt,
            Blocks = blocks,
        };

    /// <summary>Stands in for S-25's shape matcher, which does not exist yet: every statement is a
    /// rule nobody has built a shape for.</summary>
    static RuleStatementStatus AllCheckableNotYetBuilt(RuleStatement statement) =>
        RuleStatementStatus.CheckableNotYetBuilt;

    static RuleSetVersionId VersionOf(SessionRuleSet session) => new()
    {
        Repository = session.Repository,
        Hash = RuleSetVersionHasher.ComputeHash(session.Blocks),
    };

    // ---- Scenario 1: every statement carries exactly one status ----

    [Fact]
    public void The_status_vocabulary_is_closed_to_the_four_FR_40_names()
    {
        var shapes = typeof(RuleStatementStatus).Assembly
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(RuleStatementStatus)))
            .ToArray();

        Assert.Equal(4, shapes.Length);
        Assert.All(shapes, shape => Assert.True(shape.IsSealed));
    }

    [Fact]
    public void Nothing_outside_the_status_type_can_add_a_fifth_status()
    {
        // A record also gets a compiler-generated protected copy constructor, which is not a way in:
        // a record declared elsewhere still has to chain to the parameterless constructor to derive,
        // and that one is private, visible only to the four nested shapes.
        var parameterless = typeof(RuleStatementStatus)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 0);

        Assert.True(parameterless.IsPrivate);
    }

    [Fact]
    public void A_not_checkable_status_cannot_be_constructed_without_its_reason()
    {
        var reason = typeof(RuleStatementStatus.NotCheckableStatus)
            .GetProperty(nameof(RuleStatementStatus.NotCheckableStatus.Reason))!;

        Assert.NotNull(reason.GetCustomAttribute<RequiredMemberAttribute>());
    }

    /// <summary>`required` proves the reason must be assigned, not that it says anything. FR-40's
    /// status is "Not checkable **with a stated reason**", so the empty string has to be refused on
    /// every construction path, including the object initialiser and `with`.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_not_checkable_status_refuses_a_reason_that_states_nothing(string blank)
    {
        Assert.Throws<ArgumentException>(
            () => new RuleStatementStatus.NotCheckableStatus { Reason = blank });

        var stated = (RuleStatementStatus.NotCheckableStatus)
            RuleStatementStatus.NotCheckable("The logs record no such event.");

        Assert.Throws<ArgumentException>(() => stated with { Reason = blank });
    }

    [Fact]
    public void A_retired_rule_refuses_a_removal_date_that_states_nothing()
    {
        Assert.Throws<ArgumentException>(() => new RuleRetirement.RetiredRule { RetiredAt = " " });
    }

    [Fact]
    public void Every_row_carries_exactly_one_status()
    {
        SessionRuleSet[] sessions =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A.", "Rule B."))];

        var inventory = RulesInventory.Build(
            sessions,
            VersionOf(sessions[0]),
            statement => statement.Text == "Rule A."
                ? RuleStatementStatus.Watched
                : RuleStatementStatus.NotCheckable("No shape can express it."));

        Assert.Equal(2, inventory.Rows.Count);
        Assert.Equal(RuleStatementStatus.Watched, inventory.Rows.Single(r => r.Statement.Text == "Rule A.").Status);
        var notCheckable = Assert.IsType<RuleStatementStatus.NotCheckableStatus>(
            inventory.Rows.Single(r => r.Statement.Text == "Rule B.").Status);
        Assert.Equal("No shape can express it.", notCheckable.Reason);
    }

    [Fact]
    public void The_status_counts_are_derived_from_the_rows_rather_than_stored_alongside_them()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "A.", "B.", "C.", "D.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), statement => statement.Text switch
        {
            "A." => RuleStatementStatus.Watched,
            "B." => RuleStatementStatus.CheckableNotYetBuilt,
            "C." => RuleStatementStatus.NotCheckable("The logs do not record it."),
            _ => RuleStatementStatus.NotARule,
        });

        Assert.Equal(1, inventory.StatusCounts.Watched);
        Assert.Equal(1, inventory.StatusCounts.CheckableNotYetBuilt);
        Assert.Equal(1, inventory.StatusCounts.NotCheckable);
        Assert.Equal(1, inventory.StatusCounts.NotARule);
        Assert.Equal(inventory.Rows.Count, inventory.StatusCounts.Total);

        Assert.Null(typeof(RulesInventory).GetProperty(nameof(RulesInventory.StatusCounts))!.SetMethod);
    }

    [Fact]
    public void A_classifier_that_returns_no_status_is_refused_rather_than_rendered_blank()
    {
        SessionRuleSet[] sessions =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A."))];

        Assert.Throws<InvalidOperationException>(
            () => RulesInventory.Build(sessions, VersionOf(sessions[0]), _ => null!));
    }

    // ---- Scenario 2: each row carries its origin and its reach ----

    [Fact]
    public void Each_row_shows_the_source_file_it_came_from_and_every_session_carrying_it()
    {
        var blocks = Block("AGENTS.md", "Rule A.");
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", blocks),
            Session("s2", "repo-a", "2026-01-02T00:00:00Z", blocks),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        var row = Assert.Single(inventory.Rows);
        Assert.Equal("AGENTS.md", row.SourceFile);
        Assert.Equal(["s1", "s2"], row.SessionIds);
    }

    [Fact]
    public void The_same_wording_headed_by_two_source_files_stays_two_rows()
    {
        SessionRuleSet[] sessions =
        [
            Session(
                "s1",
                "repo-a",
                "2026-01-01T00:00:00Z",
                Block("CLAUDE.md", "Prefer the index."),
                Block("AGENTS.md", "Prefer the index.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal(2, inventory.Rows.Count);
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], inventory.Rows.Select(row => row.SourceFile));
    }

    // ---- Scenario 3: the in-force window is stated ----

    [Fact]
    public void The_in_force_window_is_the_first_and_last_carrying_sessions_own_timestamps()
    {
        var blocks = Block("CLAUDE.md", "Rule A.");
        SessionRuleSet[] sessions =
        [
            Session("s3", "repo-a", "2026-01-10T00:00:00Z", blocks),
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", blocks),
            Session("s2", "repo-a", "2026-01-05T00:00:00Z", blocks),
        ];

        var row = Assert.Single(
            RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt).Rows);

        Assert.Equal("2026-01-01T00:00:00Z", row.InForceFrom);
        Assert.Equal("2026-01-10T00:00:00Z", row.InForceUntil);
    }

    [Fact]
    public void A_statement_carried_by_one_session_has_a_window_of_that_session_alone()
    {
        SessionRuleSet[] sessions =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A."))];

        var row = Assert.Single(
            RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt).Rows);

        Assert.Equal("2026-01-01T00:00:00Z", row.InForceFrom);
        Assert.Equal("2026-01-01T00:00:00Z", row.InForceUntil);
    }

    // ---- Scenario 4: a repository with no rules is a designed state ----

    [Fact]
    public void A_repository_whose_sessions_carry_no_instruction_block_states_that_no_rules_were_found()
    {
        SessionRuleSet[] sessions = [Session("s1", "repo-a", "2026-01-01T00:00:00Z")];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal(RulesInventoryState.NoInstructionBlocks, inventory.State);
        Assert.Empty(inventory.Rows);
    }

    [Fact]
    public void A_block_that_carried_no_list_item_is_a_different_designed_state_from_no_block_at_all()
    {
        SessionRuleSet[] sessions = [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md"))];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal(RulesInventoryState.BlocksCarriedNoStatements, inventory.State);
        Assert.Empty(inventory.Rows);
    }

    [Fact]
    public void A_version_carrying_statements_is_listed()
    {
        SessionRuleSet[] sessions =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A."))];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal(RulesInventoryState.Listed, inventory.State);
    }

    // ---- Scenario 5: a retired rule stays visible ----

    [Fact]
    public void A_statement_absent_from_the_most_recent_version_is_retired_at_the_date_it_was_removed()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Kept.", "Dropped.")),
            Session("s2", "repo-a", "2026-01-05T00:00:00Z", Block("CLAUDE.md", "Kept.", "Dropped.")),
            Session("s3", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "Kept.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        var dropped = inventory.Rows.Single(row => row.Statement.Text == "Dropped.");
        var retired = Assert.IsType<RuleRetirement.RetiredRule>(dropped.Retirement);
        Assert.Equal("2026-01-09T00:00:00Z", retired.RetiredAt);
        Assert.Equal("2026-01-09T00:00:00Z", dropped.AdherenceFrozenAt);
    }

    /// <summary>
    /// The removal date is the statement's own, not the selected version's. Every session sharing a
    /// hash carries an identical block set, so a row's carrying-session list is the same for every
    /// row in that version — deriving retirement from it would report "the session after this
    /// version ended" for every statement alike, including one that a later version went on
    /// carrying.
    /// </summary>
    [Fact]
    public void A_statement_a_later_version_kept_is_retired_at_that_later_removal_not_at_this_versions_end()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Kept.", "Shared.")),
            Session("s2", "repo-a", "2026-01-05T00:00:00Z", Block("CLAUDE.md", "Kept.", "Shared.", "Extra.")),
            Session("s3", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "Kept.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        var shared = inventory.Rows.Single(row => row.Statement.Text == "Shared.");
        var retired = Assert.IsType<RuleRetirement.RetiredRule>(shared.Retirement);
        Assert.Equal("2026-01-09T00:00:00Z", retired.RetiredAt);
    }

    [Fact]
    public void A_retired_statement_still_appears_in_the_inventory()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Dropped.")),
            Session("s2", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "Kept.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal("Dropped.", Assert.Single(inventory.Rows).Statement.Text);
    }

    [Fact]
    public void A_statement_still_in_the_most_recent_version_is_in_force_with_no_frozen_date()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Kept.", "Dropped.")),
            Session("s2", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "Kept.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[1]), AllCheckableNotYetBuilt);

        var kept = Assert.Single(inventory.Rows);
        Assert.IsType<RuleRetirement.StillInForce>(kept.Retirement);
        Assert.Null(kept.AdherenceFrozenAt);
    }

    // ---- Scenario 6: the inventory is scoped to a version ----

    [Fact]
    public void The_inventory_names_the_single_version_it_is_scoped_to()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Old.")),
            Session("s2", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "New.")),
        ];

        var selected = VersionOf(sessions[0]);
        var inventory = RulesInventory.Build(sessions, selected, AllCheckableNotYetBuilt);

        Assert.Equal(selected, inventory.SelectedVersion);
        Assert.Equal("repo-a", inventory.SelectedVersion.Repository);
    }

    [Fact]
    public void The_inventory_never_unions_statements_across_versions()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Old.")),
            Session("s2", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "New.")),
        ];

        var older = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);
        var newer = RulesInventory.Build(sessions, VersionOf(sessions[1]), AllCheckableNotYetBuilt);

        Assert.Equal("Old.", Assert.Single(older.Rows).Statement.Text);
        Assert.Equal("New.", Assert.Single(newer.Rows).Statement.Text);
    }

    [Fact]
    public void Every_version_in_the_repository_is_offered_even_though_one_renders_at_a_time()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Old.")),
            Session("s2", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "New.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal(2, inventory.AvailableVersions.Count);
        Assert.All(inventory.AvailableVersions, version => Assert.Equal("repo-a", version.Repository));
    }

    /// <summary>Session ids in the real corpus are opaque (random UUIDs), so an order keyed on session
    /// id text would bear no relation to time. <see cref="RuleSetVersioning.Compute"/> orders by each
    /// version's own <see cref="RuleSetVersion.FirstSessionStartedAt"/> instead — proven here with
    /// session ids ("s-zebra" before "s-alpha") that sort backwards from their own chronology, so this
    /// test only passes if the ordering is genuinely time-based. An operator picking a version has to
    /// be able to tell which one is the most recent — that is the version FR-40's retirement rule is
    /// stated against, and the only one in which nothing is retired.</summary>
    [Fact]
    public void Versions_are_offered_in_the_repositorys_own_chronological_order()
    {
        SessionRuleSet[] sessions =
        [
            Session("s-zebra", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Older.")),
            Session("s-alpha", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "Newer.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal(
            ["s-zebra", "s-alpha"],
            inventory.AvailableVersions.Select(version => version.FirstSessionId));
    }

    [Fact]
    public void The_last_offered_version_is_the_one_MostRecentVersion_names()
    {
        SessionRuleSet[] sessions =
        [
            Session("s-zebra", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Older.")),
            Session("s-alpha", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "Newer.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal(
            RulesInventory.MostRecentVersion(sessions, "repo-a"),
            inventory.AvailableVersions[^1].Id);
    }

    [Fact]
    public void Another_repositorys_versions_are_neither_rendered_nor_offered()
    {
        SessionRuleSet[] sessions =
        [
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "A's rule.")),
            Session("s2", "repo-b", "2026-01-02T00:00:00Z", Block("CLAUDE.md", "B's rule.")),
        ];

        var inventory = RulesInventory.Build(sessions, VersionOf(sessions[0]), AllCheckableNotYetBuilt);

        Assert.Equal("A's rule.", Assert.Single(inventory.Rows).Statement.Text);
        Assert.Equal("repo-a", Assert.Single(inventory.AvailableVersions).Repository);
    }

    [Fact]
    public void A_version_the_corpus_does_not_carry_cannot_be_rendered()
    {
        SessionRuleSet[] sessions =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A."))];

        Assert.Throws<UnknownRuleSetVersionException>(() => RulesInventory.Build(
            sessions,
            new RuleSetVersionId { Repository = "repo-a", Hash = "not-a-hash" },
            AllCheckableNotYetBuilt));
    }

    [Fact]
    public void The_most_recent_version_of_a_repository_is_the_one_its_latest_session_carried()
    {
        SessionRuleSet[] sessions =
        [
            Session("s2", "repo-a", "2026-01-09T00:00:00Z", Block("CLAUDE.md", "New.")),
            Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Old.")),
        ];

        var mostRecent = RulesInventory.MostRecentVersion(sessions, "repo-a");

        Assert.Equal(VersionOf(sessions[0]), mostRecent);
    }

    [Fact]
    public void A_repository_the_corpus_does_not_carry_has_no_most_recent_version()
    {
        SessionRuleSet[] sessions =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "Rule A."))];

        Assert.Null(RulesInventory.MostRecentVersion(sessions, "repo-z"));
    }

    [Fact]
    public void Rows_are_ordered_deterministically_regardless_of_input_order()
    {
        SessionRuleSet[] forwards =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "B.", "A."))];
        SessionRuleSet[] backwards =
            [Session("s1", "repo-a", "2026-01-01T00:00:00Z", Block("CLAUDE.md", "A.", "B."))];

        var first = RulesInventory.Build(forwards, VersionOf(forwards[0]), AllCheckableNotYetBuilt);
        var second = RulesInventory.Build(backwards, VersionOf(backwards[0]), AllCheckableNotYetBuilt);

        Assert.Equal(["A.", "B."], first.Rows.Select(row => row.Statement.Text));
        Assert.Equal(
            first.Rows.Select(row => row.Statement.Text),
            second.Rows.Select(row => row.Statement.Text));
    }
}
