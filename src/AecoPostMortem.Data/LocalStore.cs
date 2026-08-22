using System.Globalization;
using AecoPostMortem.Data.Execution;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data;

/// <summary>
/// The store as a file the operator owns (FR-11): where it is, how it is created, how big it is and
/// how it is erased. Everything about the store that is not schema lives here.
/// </summary>
public sealed class LocalStore
{
    /// <summary>
    /// SQLite's transient companions. They exist only while a connection is open, but a process
    /// killed mid-transaction leaves one behind, and a purge that left a journal behind would leave
    /// the operator's prompt text on disk after reporting the store deleted.
    /// </summary>
    static readonly string[] SidecarSuffixes = ["-journal", "-wal", "-shm"];

    /// <summary>The magic every SQLite database begins with. The real header runs 16 bytes and ends
    /// in a NUL; matching the 15 printable ones identifies the format without putting a NUL in
    /// source.</summary>
    static ReadOnlySpan<byte> SqliteMagic => "SQLite format 3"u8;

    public LocalStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FilePath = Path.GetFullPath(filePath);
        Folder = Path.GetDirectoryName(FilePath)
                 ?? throw new ArgumentException(
                     $"'{filePath}' has no containing directory, so it cannot be a store path.",
                     nameof(filePath));
    }

    /// <summary>The store at FR-11's documented per-user path — what the product uses.</summary>
    public static LocalStore AtDefaultLocation() => new(StoreLocation.Default);

    public string FilePath { get; }

    public string Folder { get; }

    public bool Exists => File.Exists(FilePath);

    /// <summary>
    /// The store's footprint on disk, sidecars included, or zero when there is no store. Queryable
    /// because the coverage report states it, and a figure the operator is shown should be the size
    /// of everything a purge would delete rather than of one file among several.
    /// </summary>
    public long SizeInBytes => ExistingFiles().Sum(file => new FileInfo(file).Length);

    /// <summary>
    /// The connection the store is reached through. <c>Pooling=False</c> is deliberate: a pooled
    /// handle outlives the context that opened it, and the purge has to be able to delete the file
    /// immediately afterwards. There is one local file and one process, so the pool buys nothing to
    /// weigh against that.
    /// </summary>
    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = FilePath,
        Pooling = false,
    }.ToString();

    /// <summary>
    /// Open the store, creating it if it is not there. The schema is created and advanced by
    /// applying the migrations (FR-11), so the operator never runs a database command and a store
    /// written by an older build is brought forward rather than rejected.
    /// The derived tables are created from the model at the same time, and recreated when the
    /// model's version moves — they are re-derivable from RAW, so they are rebuilt rather than
    /// migrated (PRD §3.8).
    /// </summary>
    /// <exception cref="InvalidOperationException">The path holds a file this product did not
    /// create. Overwriting it would destroy whatever it is, so the open fails instead.</exception>
    public PostMortemContext Open()
    {
        Directory.CreateDirectory(Folder);
        OwnerOnlyAccess.ApplyToFolder(Folder);

        GuardAgainstAForeignFile();

        var context = new PostMortemContext(Options());
        context.Database.Migrate();
        DerivedSchema.EnsureCurrent(context);

        OwnerOnlyAccess.ApplyToFile(FilePath);

        return context;
    }

    /// <summary>The options <see cref="Open"/> builds the context from, exposed so a caller that
    /// already holds an open store can build a second context against the same file.</summary>
    public DbContextOptions<PostMortemContext> Options() =>
        new DbContextOptionsBuilder<PostMortemContext>()
            .UseSqlite(ConnectionString)
            .Options;

    /// <summary>
    /// Delete the store outright (FR-11). Total, because there is no export path and no partial
    /// erasure worth offering; idempotent, because "there is nothing to purge" is an answer rather
    /// than an error.
    /// </summary>
    public PurgeOutcome Purge()
    {
        // Microsoft.Data.Sqlite pools connections by default, and a pooled connection holds the
        // file open — on Windows that is a delete that fails rather than a handle quietly reused.
        // Pooling is off in ConnectionString; this closes anything another caller left pooled.
        SqliteConnection.ClearAllPools();

        var deleted = new List<string>();
        long reclaimed = 0;

        foreach (var file in ExistingFiles())
        {
            reclaimed += new FileInfo(file).Length;
            File.Delete(file);
            deleted.Add(file);
        }

        return new PurgeOutcome(deleted, reclaimed);
    }

    IEnumerable<string> ExistingFiles()
    {
        if (File.Exists(FilePath))
        {
            yield return FilePath;
        }

        foreach (var suffix in SidecarSuffixes)
        {
            var sidecar = FilePath + suffix;
            if (File.Exists(sidecar))
            {
                yield return sidecar;
            }
        }
    }

    /// <summary>
    /// A store path that collides with something else fails loudly rather than overwriting. An
    /// empty file is adopted — that is what SQLite itself does with one, and it is what a half-made
    /// store from an interrupted first run looks like.
    /// </summary>
    void GuardAgainstAForeignFile()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists || file.Length == 0)
        {
            return;
        }

        // FileShare.ReadWrite | FileShare.Delete, not FileInfo.OpenRead(): OpenRead opens with
        // FileShare.Read, which *denies* write sharing, so this check threw
        // "The process cannot access the file ... because it is being used by another process"
        // whenever any SQLite connection had the store open — i.e. whenever a second request
        // overlapped a first. Measured against a live host before the fix: 8 concurrent
        // `/api/app-state` requests answered 3x500, and `/api/monitor-comparison` 20x500 of 24.
        // This is a read of the first 15 bytes; it has no business excluding anyone.
        Span<byte> magic = stackalloc byte[15];
        using (var stream = new FileStream(
            FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) < magic.Length
                || !magic.SequenceEqual(SqliteMagic))
            {
                throw new InvalidOperationException(
                    $"'{FilePath}' is not a SQLite database, so it is not this product's store. "
                    + "Move it aside or point the store somewhere else; it will not be overwritten.");
            }
        }

        if (CountOfTables() > 0 && !MigrationHistoryExists())
        {
            throw new InvalidOperationException(
                $"'{FilePath}' is a SQLite database this product did not create — it carries "
                + "tables but no migration history. Move it aside or point the store somewhere "
                + "else; it will not be overwritten.");
        }
    }

    long CountOfTables() =>
        Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table'", parameter: null);

    bool MigrationHistoryExists() =>
        Scalar(
            "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name",
            "__EFMigrationsHistory") > 0;

    long Scalar(string sql, string? parameter)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameter is not null)
        {
            command.Parameters.AddWithValue("$name", parameter);
        }

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}

/// <summary>What a purge deleted. An empty <see cref="Deleted"/> is the "nothing to purge" answer,
/// reported rather than thrown.</summary>
public sealed record PurgeOutcome(IReadOnlyList<string> Deleted, long BytesReclaimed)
{
    public bool DeletedAnything => Deleted.Count > 0;
}
