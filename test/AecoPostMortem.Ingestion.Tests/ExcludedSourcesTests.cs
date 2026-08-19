using System.Globalization;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-10: Copilot's global session-store database is never opened. It is live-written and
/// WAL-dependent, covers a measured 7 of 40 sessions, and offers nothing any finding class needs —
/// recorded so it is not rediscovered as an oversight.
/// </summary>
public sealed class ExcludedSourcesTests
{
    [Fact]
    public void The_global_session_store_database_is_recognised_by_name()
    {
        Assert.True(ExcludedSources.IsExcluded(@"C:\Users\op\.copilot\session-store.db"));
        Assert.True(ExcludedSources.IsExcluded("/home/op/.copilot/session-store.db"));
    }

    /// <summary>The per-session <c>session.db</c> (todo rows) is a different file with a different
    /// name — FR-1's classify-only decision, not FR-10's exclusion. This type must not conflate the
    /// two.</summary>
    [Fact]
    public void The_per_session_database_is_a_different_file_and_is_not_excluded_by_this_rule()
    {
        Assert.False(ExcludedSources.IsExcluded(
            @"C:\Users\op\.copilot\session-state\session-1\session.db"));
    }

    [Fact]
    public void An_ordinary_source_file_is_not_excluded()
    {
        Assert.False(ExcludedSources.IsExcluded(
            @"C:\Users\op\.copilot\session-state\session-1\events.jsonl"));
    }

    /// <summary>Acceptance criterion 2's second half: "the coverage report states it was skipped by
    /// design." This is the fact a coverage report draws on — stated here as a value, not left to a
    /// caller to phrase.</summary>
    [Fact]
    public void The_skip_reason_states_it_was_skipped_by_design()
    {
        var reason = ExcludedSources.SkipReason;

        Assert.Contains("by design", reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Acceptance criterion 2's first half: "that file is never opened." The file is held under an
    /// exclusive lock by the test itself first — the way a live Copilot process holds it — so that
    /// any attempt to actually open it at the OS level would fail. <see cref="SourceFiles.OpenRead"/>
    /// refuses before ever reaching the OS call, which is what proves it was never opened rather than
    /// opened-and-happened-to-succeed.
    /// </summary>
    [Fact]
    public void SourceFiles_OpenRead_refuses_the_session_store_database_without_touching_it()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "AecoPostMortem.Tests",
            Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture),
            "session-store.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        try
        {
            using var exclusivelyLocked = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                });

            var ex = Assert.Throws<InvalidOperationException>(() => SourceFiles.OpenRead(path));
            Assert.Contains("by design", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
