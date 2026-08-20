namespace AecoPostMortem.Ingestion;

/// <summary>
/// Where Copilot's session-state root lives on this machine (PRD §3.2):
/// <c>~/.copilot/session-state</c>, resolved from the user's home directory the same way
/// <c>AecoPostMortem.Data.StoreLocation</c> resolves the store's own path. This type only names
/// the path — <see cref="SessionDiscovery"/> is what asks whether it is really there.
/// </summary>
public static class CopilotSourceLocation
{
    public const string FolderName = ".copilot";

    public const string SessionStateFolderName = "session-state";

    /// <summary>The documented per-user path Copilot itself writes to.</summary>
    public static string DefaultSessionStateRoot =>
        Path.Combine(UserProfile(), FolderName, SessionStateFolderName);

    /// <summary>
    /// <c>DoNotVerify</c> because the directory legitimately does not exist on a machine that has
    /// never run Copilot (S-48's "no source found" state) — that is a fact to report, not a reason
    /// to throw while resolving the path itself.
    /// </summary>
    static string UserProfile()
    {
        var folder = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);

        return string.IsNullOrEmpty(folder)
            ? throw new InvalidOperationException(
                "This account has no home directory, so the Copilot session-state root cannot be "
                + "resolved. Pass an explicit path instead.")
            : folder;
    }
}
