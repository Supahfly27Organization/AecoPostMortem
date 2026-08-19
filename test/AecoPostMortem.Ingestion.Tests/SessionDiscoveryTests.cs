using System.Globalization;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>FR-1: every session directory under the Copilot session-state root is found, and its
/// sibling files are classified without being read.</summary>
public sealed class SessionDiscoveryTests
{
    [Fact]
    public void A_missing_root_directory_is_reported_not_thrown()
    {
        var missingRoot = Path.Combine(
            Path.GetTempPath(),
            "AecoPostMortem.Tests",
            Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));

        var result = SessionDiscovery.Discover(missingRoot);

        Assert.False(result.RootFound);
        Assert.Empty(result.Sessions);
    }

    [Fact]
    public void A_session_directory_holding_events_jsonl_is_classified()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, """{"type":"session.start"}""");

        var result = SessionDiscovery.Discover(workspace.SessionStateRoot);

        Assert.True(result.RootFound);
        var session = Assert.Single(result.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.True(session.HasEvents);
        Assert.NotNull(session.EventsFile);
    }

    [Fact]
    public void Sibling_session_db_rewind_snapshots_and_workspace_yaml_are_classified_alongside_events()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, """{"type":"session.start"}""");
        workspace.WriteSiblingFiles(
            "session-1",
            sessionDb: true,
            rewindSnapshotsIndex: true,
            workspaceYaml: true);

        var result = SessionDiscovery.Discover(workspace.SessionStateRoot);

        var session = Assert.Single(result.Sessions);
        Assert.NotNull(session.SessionDatabaseFile);
        Assert.NotNull(session.RewindSnapshotsIndexFile);
        Assert.NotNull(session.WorkspaceFile);
    }

    [Fact]
    public void A_session_directory_with_no_events_jsonl_is_classified_and_skipped()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.CreateSessionDirectory("session-no-events");

        var result = SessionDiscovery.Discover(workspace.SessionStateRoot);

        var session = Assert.Single(result.Sessions);
        Assert.Equal("session-no-events", session.SessionId);
        Assert.False(session.HasEvents);
        Assert.Null(session.EventsFile);
    }

    [Fact]
    public void Multiple_session_directories_are_all_classified()
    {
        using var workspace = new IngestionTestWorkspace();
        workspace.WriteEventsFile("session-1", trailingNewline: true, """{"type":"session.start"}""");
        workspace.WriteEventsFile("session-2", trailingNewline: true, """{"type":"session.start"}""");
        workspace.CreateSessionDirectory("session-3-no-events");

        var result = SessionDiscovery.Discover(workspace.SessionStateRoot);

        Assert.Equal(3, result.Sessions.Count);
    }
}
