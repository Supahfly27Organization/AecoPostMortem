namespace AecoPostMortem.Data;

/// <summary>
/// Where the store lives (FR-11). One documented per-user path, resolved from the platform's own
/// local-application-data folder so the answer is the same on every machine without a setting to
/// get wrong: <c>%LOCALAPPDATA%\AecoPostMortem\store.db</c> on Windows,
/// <c>~/.local/share/AecoPostMortem/store.db</c> elsewhere.
/// </summary>
public static class StoreLocation
{
    public const string FolderName = "AecoPostMortem";

    public const string FileName = "store.db";

    /// <summary>The store's directory. It carries owner-only permissions too, because SQLite's
    /// transient journal is written beside the database and inherits from here.</summary>
    public static string DefaultFolder => Path.Combine(LocalApplicationData(), FolderName);

    /// <summary>The documented per-user path of the store file.</summary>
    public static string Default => Path.Combine(DefaultFolder, FileName);

    /// <summary>
    /// <c>DoNotVerify</c> because the folder legitimately does not exist before the first ingest;
    /// the default behaviour returns an empty string in that case, which would silently place the
    /// store in the working directory.
    /// </summary>
    static string LocalApplicationData()
    {
        var folder = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        return string.IsNullOrEmpty(folder)
            ? throw new InvalidOperationException(
                "This account has no local application-data folder, so FR-11's per-user store path "
                + "cannot be resolved. Pass an explicit path to the store instead.")
            : folder;
    }
}
