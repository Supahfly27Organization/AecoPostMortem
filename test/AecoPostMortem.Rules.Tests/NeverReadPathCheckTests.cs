namespace AecoPostMortem.Rules.Tests;

/// <summary>Piece 3's NeverReadPath adherence check: a prohibition names a path, and the only
/// adherence-worthy question is whether any real tool call touched a path that matches it — on a
/// path-segment boundary, never a bare substring, since real observed paths are absolute and a
/// rule's own operand is typically relative (issue: matching must not confuse "Data" with
/// "DataAccessLayer").</summary>
public sealed class NeverReadPathCheckTests
{
    [Fact]
    public void A_path_accessed_under_a_banned_directory_is_reported_with_its_access_count()
    {
        var mentions = new[]
        {
            new NeverReadPathMention
            {
                SourceText = "Never read `src/AecoPostMortem.Data/Migrations/`.",
                NamedPath = "src/AecoPostMortem.Data/Migrations/",
            },
        };
        ReadEvent[] events =
        [
            new ReadEvent { SessionId = "s1", Path = @"F:\git\AecoPostMortem\src\AecoPostMortem.Data\Migrations\0001_Init.cs" },
            new ReadEvent { SessionId = "s1", Path = @"F:\git\AecoPostMortem\src\AecoPostMortem.Data\Migrations\0002_Next.cs" },
        ];

        var results = NeverReadPathCheck.Run(mentions, events);

        var violation = Assert.Single(results);
        Assert.Equal("src/AecoPostMortem.Data/Migrations/", violation.NamedPath);
        Assert.Equal(2, violation.AccessCount);
        Assert.Equal(["s1"], violation.SessionIds);
    }

    [Fact]
    public void A_path_never_accessed_produces_no_result()
    {
        var mentions = new[]
        {
            new NeverReadPathMention { SourceText = "Never read `src/Secrets/`.", NamedPath = "src/Secrets/" },
        };
        ReadEvent[] events =
        [
            new ReadEvent { SessionId = "s1", Path = @"F:\git\AecoPostMortem\src\AecoPostMortem.Data\ToolCall.cs" },
        ];

        var results = NeverReadPathCheck.Run(mentions, events);

        Assert.Empty(results);
    }

    [Fact]
    public void A_segment_that_is_only_a_substring_of_a_directory_name_does_not_match()
    {
        // "Data" must not match "DataAccessLayer" — a bare substring match would confuse the two.
        var mentions = new[]
        {
            new NeverReadPathMention { SourceText = "Never read `Data/`.", NamedPath = "Data/" },
        };
        ReadEvent[] events =
        [
            new ReadEvent { SessionId = "s1", Path = @"F:\git\UpFront\UpFront.Auth\UpFront.Auth.DataAccessLayer\Program.cs" },
        ];

        var results = NeverReadPathCheck.Run(mentions, events);

        Assert.Empty(results);
    }

    [Fact]
    public void Access_across_two_sessions_reports_both_session_ids()
    {
        var mentions = new[]
        {
            new NeverReadPathMention { SourceText = "Never read `Migrations/`.", NamedPath = "Migrations/" },
        };
        ReadEvent[] events =
        [
            new ReadEvent { SessionId = "s1", Path = @"F:\git\AecoPostMortem\src\AecoPostMortem.Data\Migrations\0001_Init.cs" },
            new ReadEvent { SessionId = "s2", Path = @"F:\git\AecoPostMortem\src\AecoPostMortem.Data\Migrations\0002_Next.cs" },
        ];

        var results = NeverReadPathCheck.Run(mentions, events);

        var violation = Assert.Single(results);
        Assert.Equal(2, violation.AccessCount);
        Assert.Equal(["s1", "s2"], violation.SessionIds);
    }

    [Fact]
    public void Matching_is_case_insensitive_because_real_observed_paths_are_windows_filesystem_paths()
    {
        // Windows filesystem paths are case-insensitive; the same precedent this codebase already
        // follows for path-prefix matching (Ingestion.SessionExclusion.Normalize/IsExcluded, which
        // uses OrdinalIgnoreCase for exactly this reason).
        var mentions = new[]
        {
            new NeverReadPathMention { SourceText = "Never read `UpFront.Data/Migrations/`.", NamedPath = "UpFront.Data/Migrations/" },
        };
        ReadEvent[] events =
        [
            new ReadEvent { SessionId = "s1", Path = @"F:\git\upfront\upfront.data\migrations\0001_Init.cs" },
        ];

        var results = NeverReadPathCheck.Run(mentions, events);

        var violation = Assert.Single(results);
        Assert.Equal(1, violation.AccessCount);
    }

    [Fact]
    public void A_single_file_operand_matches_only_that_exact_file_at_the_end_of_the_path()
    {
        var mentions = new[]
        {
            new NeverReadPathMention { SourceText = "Never edit `appsettings.json`.", NamedPath = "appsettings.json" },
        };
        ReadEvent[] events =
        [
            new ReadEvent { SessionId = "s1", Path = @"F:\git\UpFront\UpFront.Auth\appsettings.Development.json" },
            new ReadEvent { SessionId = "s2", Path = @"F:\git\UpFront\UpFront.Auth\appsettings.json" },
        ];

        var results = NeverReadPathCheck.Run(mentions, events);

        var violation = Assert.Single(results);
        Assert.Equal(1, violation.AccessCount);
        Assert.Equal(["s2"], violation.SessionIds);
    }
}
