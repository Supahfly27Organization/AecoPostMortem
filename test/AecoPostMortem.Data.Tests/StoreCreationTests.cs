using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// FR-11: one local file, in a documented per-user place, created by the product with owner-only
/// permissions and without the operator running a database command.
/// </summary>
public sealed class StoreCreationTests
{
    [Fact]
    public void The_documented_location_is_per_user_and_not_the_working_directory()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        Assert.True(Path.IsPathFullyQualified(StoreLocation.Default));
        Assert.StartsWith(localApplicationData, StoreLocation.Default, StringComparison.Ordinal);
        Assert.Equal(StoreLocation.FileName, Path.GetFileName(StoreLocation.Default));
        Assert.Equal(StoreLocation.FolderName, Path.GetFileName(Path.GetDirectoryName(StoreLocation.Default)));
    }

    [Fact]
    public void The_first_write_creates_the_store_and_applies_the_migrations_with_no_command_from_the_operator()
    {
        using var temporary = new TemporaryStore();

        Assert.False(temporary.Store.Exists);

        using var context = temporary.Store.Open();

        Assert.True(temporary.Store.Exists);
        Assert.Empty(context.Database.GetPendingMigrations());
        Assert.Equal(context.Database.GetMigrations(), context.Database.GetAppliedMigrations());
    }

    [Fact]
    public void The_store_is_readable_only_by_the_account_that_created_it()
    {
        using var temporary = new TemporaryStore();
        using (temporary.Store.Open())
        {
        }

        AssertOwnerOnly(temporary.Store.FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>The directory matters too: SQLite writes its rollback journal beside the database,
    /// and the journal holds the same prompt and patch text (§3.8, secret hygiene).</summary>
    [Fact]
    public void The_stores_directory_is_readable_only_by_the_account_that_created_it()
    {
        using var temporary = new TemporaryStore();
        using (temporary.Store.Open())
        {
        }

        AssertOwnerOnly(
            temporary.Store.Folder,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void The_size_of_the_store_is_queryable()
    {
        using var temporary = new TemporaryStore();

        Assert.Equal(0, temporary.Store.SizeInBytes);

        using (var context = temporary.Store.Open())
        {
            RawEventBatch.Append(context, [Events.From("""{"type":"session.start"}""")]);
        }

        Assert.True(
            temporary.Store.SizeInBytes > 0,
            "The coverage report states the store's size, so a populated store must not report zero.");
    }

    [Fact]
    public void A_path_holding_something_that_is_not_a_store_fails_loudly_rather_than_overwriting_it()
    {
        using var temporary = new TemporaryStore();
        Directory.CreateDirectory(temporary.Folder);
        File.WriteAllText(temporary.Store.FilePath, "the operator's notes, not a database");

        var failure = Assert.Throws<InvalidOperationException>(() => temporary.Store.Open());

        Assert.Contains(temporary.Store.FilePath, failure.Message, StringComparison.Ordinal);
        Assert.Equal("the operator's notes, not a database", File.ReadAllText(temporary.Store.FilePath));
    }

    [Fact]
    public void A_SQLite_database_this_product_did_not_create_fails_loudly_too()
    {
        using var temporary = new TemporaryStore();
        Directory.CreateDirectory(temporary.Folder);

        using (var foreign = new SqliteConnection(temporary.Store.ConnectionString))
        {
            foreign.Open();
            using var command = foreign.CreateCommand();
            command.CommandText = "CREATE TABLE somebody_elses_table(id INTEGER PRIMARY KEY)";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => temporary.Store.Open());
    }

    /// <summary>
    /// Owner-only means the same thing on both platforms and is checked as each platform states it:
    /// mode bits on Unix, an unprotected-inheritance DACL with a single entry on Windows.
    /// </summary>
    static void AssertOwnerOnly(string path, UnixFileMode expectedMode)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();

            FileSystemSecurity security = Directory.Exists(path)
                ? new DirectoryInfo(path).GetAccessControl()
                : new FileInfo(path).GetAccessControl();

            Assert.True(
                security.AreAccessRulesProtected,
                $"{path} still inherits access rules, so it is not owner-only.");

            var rules = security
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToArray();

            var rule = Assert.Single(rules);
            Assert.Equal(identity.User, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        }
        else
        {
            Assert.Equal(expectedMode, File.GetUnixFileMode(path));
        }
    }
}
