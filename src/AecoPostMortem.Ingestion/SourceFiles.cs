namespace AecoPostMortem.Ingestion;

/// <summary>
/// The only door onto <c>~/.copilot/</c>. One place, so "the product never writes to the source"
/// (PRD §3.8) is a property of the code rather than of everyone who reads a file remembering it.
/// </summary>
public static class SourceFiles
{
    /// <summary>
    /// Open a source file for reading and nothing else.
    /// </summary>
    /// <remarks>
    /// <c>FileShare.ReadWrite | FileShare.Delete</c> is the deliberate part: <c>events.jsonl</c> is
    /// written live by a Copilot session that may still be running, and a reader that took an
    /// exclusive share would make the source fail to write. Read-only means the product neither
    /// writes to the source nor stops it being written to.
    /// </remarks>
    public static FileStream OpenRead(string path) =>
        new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.SequentialScan,
        });
}
