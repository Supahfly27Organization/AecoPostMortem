namespace AecoPostMortem.Data.Tests;

/// <summary>
/// FR-12: identical system-prompt text is stored once, referenced by its own content hash. A
/// measured 337 system messages at a measured median 54,335 characters are mostly near-duplicates
/// (data map Part 6), so naive per-event storage would store the same prompt hundreds of times.
/// </summary>
public sealed class SystemPromptTextAppendTests
{
    /// <summary>Acceptance criterion 1: "Identical system prompts are stored once ... referenced by
    /// content hash." Two sessions carrying the same text collapse to one row.</summary>
    [Fact]
    public void Identical_prompt_text_from_different_sessions_is_stored_once()
    {
        using var temporary = new TemporaryStore();
        const string prompt = "You are a coding agent. Follow AGENTS.md.";
        var hash = RawPayload.ContentHashOfText(prompt);

        using var context = temporary.Store.Open();

        var inserted = SystemPromptTextBatch.Append(context, [
            new SystemPromptText(hash, prompt),
            new SystemPromptText(hash, prompt),
        ]);

        Assert.Equal(1, inserted);
        Assert.Equal(1, context.Set<SystemPromptText>().Count());
    }

    /// <summary>Re-running the append over the same text adds nothing, mirroring FR-5's idempotency
    /// for RAW itself.</summary>
    [Fact]
    public void Re_appending_the_same_text_adds_nothing()
    {
        using var temporary = new TemporaryStore();
        const string prompt = "Repo Rule 6: nothing in Rules may name a tool.";
        var hash = RawPayload.ContentHashOfText(prompt);

        using var context = temporary.Store.Open();

        Assert.Equal(1, SystemPromptTextBatch.Append(context, [new SystemPromptText(hash, prompt)]));
        Assert.Equal(0, SystemPromptTextBatch.Append(context, [new SystemPromptText(hash, prompt)]));
        Assert.Equal(1, context.Set<SystemPromptText>().Count());
    }

    /// <summary>Distinct prompt text is distinct storage — dedup is by content, not a blanket
    /// collapse.</summary>
    [Fact]
    public void Distinct_prompt_text_is_stored_separately()
    {
        using var temporary = new TemporaryStore();
        const string first = "prompt one";
        const string second = "prompt two";

        using var context = temporary.Store.Open();

        var inserted = SystemPromptTextBatch.Append(context, [
            new SystemPromptText(RawPayload.ContentHashOfText(first), first),
            new SystemPromptText(RawPayload.ContentHashOfText(second), second),
        ]);

        Assert.Equal(2, inserted);
        Assert.Equal(2, context.Set<SystemPromptText>().Count());
    }

    /// <summary>Round trip: the stored text is byte-identical to what was appended, at the largest
    /// measured system prompt size (59,982 characters, data map Part 6).</summary>
    [Fact]
    public void Stored_text_round_trips_at_the_largest_measured_size()
    {
        using var temporary = new TemporaryStore();
        var prompt = new string('x', 59_982);
        var hash = RawPayload.ContentHashOfText(prompt);

        using (var context = temporary.Store.Open())
        {
            SystemPromptTextBatch.Append(context, [new SystemPromptText(hash, prompt)]);
        }

        using var reopened = temporary.Store.Open();
        var stored = reopened.Set<SystemPromptText>().Single();

        Assert.Equal(hash, stored.ContentHash);
        Assert.Equal(prompt, stored.Text);
    }

    /// <summary>More rows than fit in one statement, walking the same batching seam
    /// <c>RawEventAppendTests.Every_row_lands_however_the_batches_fall</c> covers for RAW.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(SystemPromptTextBatch.DefaultRowsPerStatement)]
    [InlineData(SystemPromptTextBatch.DefaultRowsPerStatement + 1)]
    public void Every_row_lands_however_the_batches_fall(int count)
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        var texts = Enumerable.Range(0, count)
            .Select(n => $"prompt-{n}")
            .Select(text => new SystemPromptText(RawPayload.ContentHashOfText(text), text))
            .ToArray();

        Assert.Equal(count, SystemPromptTextBatch.Append(context, texts));
        Assert.Equal(count, context.Set<SystemPromptText>().Count());
    }
}
