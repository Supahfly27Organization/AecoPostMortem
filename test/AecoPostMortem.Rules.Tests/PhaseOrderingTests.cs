namespace AecoPostMortem.Rules.Tests;

/// <summary>Scenario 1 of issue #29 (FR-19): the phase vocabulary and its ordering come from the
/// corpus, never from a list named in source code — the same discipline
/// <see cref="ToolVocabularyTests"/> proves for tool names.</summary>
public sealed class PhaseOrderingTests
{
    [Fact]
    public void The_vocabulary_contains_exactly_the_phases_declared()
    {
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "explore", Sequence = 1 },
            new() { SessionId = "s1", Phase = "implement", Sequence = 2 },
            new() { SessionId = "s2", Phase = "explore", Sequence = 3 },
        ];

        var vocabulary = PhaseOrdering.Derive(intents);

        Assert.Equal(new HashSet<string> { "explore", "implement" }, vocabulary.ToHashSet());
    }

    [Fact]
    public void The_ordering_follows_each_phases_first_declaration_by_sequence_not_by_list_order()
    {
        // Deliberately out of Sequence order in the list itself: "test" is listed first but was
        // declared last (Sequence 30), so the derived ordering must follow Sequence, not
        // enumeration order.
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "test", Sequence = 30 },
            new() { SessionId = "s1", Phase = "explore", Sequence = 10 },
            new() { SessionId = "s2", Phase = "implement", Sequence = 20 },
        ];

        var ordering = PhaseOrdering.Derive(intents);

        Assert.Equal(["explore", "implement", "test"], ordering);
    }

    [Fact]
    public void A_phases_ordering_position_is_its_earliest_declaration_across_sessions()
    {
        // "explore" is declared late in s1 (Sequence 40) but early in s2 (Sequence 5) — its earlier
        // declaration in s2 must win. It is still Sequence 5, after "implement"'s Sequence 1, so
        // "implement" orders first.
        DeclaredIntent[] intents =
        [
            new() { SessionId = "s1", Phase = "implement", Sequence = 1 },
            new() { SessionId = "s1", Phase = "explore", Sequence = 40 },
            new() { SessionId = "s2", Phase = "explore", Sequence = 5 },
        ];

        var ordering = PhaseOrdering.Derive(intents);

        Assert.Equal(["implement", "explore"], ordering);
    }

    [Fact]
    public void An_empty_corpus_produces_an_empty_ordering()
    {
        var ordering = PhaseOrdering.Derive([]);

        Assert.Empty(ordering);
    }
}
