namespace AecoPostMortem.Cli;

/// <summary>
/// Finds the built web app's static output (<c>web/dist</c>, produced by
/// <c>scripts/build-web.ps1</c>), so <c>serve</c> can hand it to <c>ApiHost.Build</c> when it is
/// there. <c>dotnet build</c> and <c>dotnet test</c> never run that script (`web/CLAUDE.md`), so a
/// machine that has only built the .NET solution has no web shell to serve — <c>serve</c> still
/// answers the API, it just has nothing to hand back for "/" (<c>ApiHost</c>'s own fallback).
/// </summary>
static class ServeWebRoot
{
    const string RelativePath = "web/dist";

    /// <summary>
    /// Walks upward from the running executable looking for <c>web/dist/index.html</c>, bounded so
    /// a machine with no repository checkout at all does not walk to the filesystem root. Returns
    /// <see langword="null"/> rather than throwing when nothing is found — an absent web shell is a
    /// state <see cref="AecoPostMortem.Api.ApiHost"/> already handles, not a failure this method
    /// should report.
    /// </summary>
    public static string? Resolve()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, RelativePath);
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }
        }

        return null;
    }
}
