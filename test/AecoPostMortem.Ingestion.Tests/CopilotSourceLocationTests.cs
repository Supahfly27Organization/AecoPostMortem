namespace AecoPostMortem.Ingestion.Tests;

/// <summary>The default per-user location of Copilot's session-state root (PRD §3.2), resolved the
/// same way <c>AecoPostMortem.Data.StoreLocation</c> resolves the store's own path.</summary>
public sealed class CopilotSourceLocationTests
{
    [Fact]
    public void The_default_root_ends_in_dot_copilot_session_state()
    {
        var root = CopilotSourceLocation.DefaultSessionStateRoot;

        Assert.EndsWith(
            Path.Combine(".copilot", "session-state"),
            root,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_root_is_rooted_under_the_users_home_directory()
    {
        var root = CopilotSourceLocation.DefaultSessionStateRoot;
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);

        Assert.StartsWith(home, root, StringComparison.Ordinal);
    }
}
