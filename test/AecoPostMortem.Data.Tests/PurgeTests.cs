namespace AecoPostMortem.Data.Tests;

/// <summary>
/// FR-11's erasure. The store holds prompt and patch text and there is no export path (§3.8), so
/// purge is total and it is the operator's only lever over what has accumulated.
/// </summary>
public sealed class PurgeTests
{
    [Fact]
    public void Purge_deletes_the_store_entirely()
    {
        using var temporary = new TemporaryStore();

        using (var context = temporary.Store.Open())
        {
            RawEventBatch.Append(context, [Events.From("""{"type":"session.start"}""")]);
        }

        var outcome = temporary.Store.Purge();

        Assert.True(outcome.DeletedAnything);
        Assert.True(outcome.BytesReclaimed > 0);
        Assert.False(temporary.Store.Exists);
        Assert.Equal(0, temporary.Store.SizeInBytes);
        Assert.Empty(Directory.GetFiles(temporary.Folder));
    }

    [Fact]
    public void Purging_again_reports_nothing_to_purge_without_failing()
    {
        using var temporary = new TemporaryStore();

        using (temporary.Store.Open())
        {
        }

        temporary.Store.Purge();
        var second = temporary.Store.Purge();

        Assert.False(second.DeletedAnything);
        Assert.Empty(second.Deleted);
        Assert.Equal(0, second.BytesReclaimed);
    }

    [Fact]
    public void Purging_a_store_that_was_never_created_is_not_an_error()
    {
        using var temporary = new TemporaryStore();

        var outcome = temporary.Store.Purge();

        Assert.False(outcome.DeletedAnything);
    }

    /// <summary>A journal left behind by a process that died mid-transaction holds the same text the
    /// database does, so purge has to take it too.</summary>
    [Fact]
    public void A_stray_journal_beside_the_store_is_purged_with_it()
    {
        using var temporary = new TemporaryStore();

        using (temporary.Store.Open())
        {
        }

        var journal = temporary.Store.FilePath + "-journal";
        File.WriteAllText(journal, "a transaction that never finished");

        var outcome = temporary.Store.Purge();

        Assert.False(File.Exists(journal));
        Assert.Contains(journal, outcome.Deleted);
    }

    /// <summary>Nothing else in the store's directory is collateral: purge deletes the store, not
    /// whatever else the operator keeps there.</summary>
    [Fact]
    public void Purge_leaves_files_that_are_not_the_store_alone()
    {
        using var temporary = new TemporaryStore();

        using (temporary.Store.Open())
        {
        }

        var unrelated = Path.Combine(temporary.Folder, "notes.txt");
        File.WriteAllText(unrelated, "the operator's own file");

        temporary.Store.Purge();

        Assert.True(File.Exists(unrelated));
    }
}
