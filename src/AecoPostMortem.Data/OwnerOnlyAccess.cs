using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AecoPostMortem.Data;

/// <summary>
/// FR-11's owner-only permissions, and §3.8's secret hygiene behind it: the store holds prompt and
/// patch text, so nobody but the account that created it may read it.
/// </summary>
/// <remarks>
/// Applied on every open rather than only at creation. A store copied between machines, restored
/// from a backup, or created by an earlier build arrives with whatever permissions the copy gave
/// it, and re-applying costs nothing next to leaving that unchecked.
/// </remarks>
public static class OwnerOnlyAccess
{
    /// <summary>Restrict a file to its owner: <c>0600</c> on Unix, a single-entry DACL on Windows.</summary>
    public static void ApplyToFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindows(new FileInfo(path));
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Restrict a directory to its owner: <c>0700</c> on Unix, a single inheritable entry on
    /// Windows. The directory matters as much as the file — SQLite writes its rollback journal
    /// beside the database, and a journal holds the same text the database does.
    /// </summary>
    public static void ApplyToFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindows(new DirectoryInfo(path));
        }
        else
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [SupportedOSPlatform("windows")]
    static void ApplyWindows(FileInfo file)
    {
        var security = new FileSecurity();
        Restrict(security, InheritanceFlags.None);
        file.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    static void ApplyWindows(DirectoryInfo folder)
    {
        var security = new DirectorySecurity();
        Restrict(security, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
        folder.SetAccessControl(security);
    }

    /// <summary>
    /// Break inheritance without copying the inherited entries forward, then grant the current
    /// account and nobody else. Windows has no mode bits; discarding the inherited rules is what
    /// makes "owner-only" mean the same thing it means on Unix, and preserving them would keep
    /// exactly the Administrators and SYSTEM entries the requirement excludes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    static void Restrict(FileSystemSecurity security, InheritanceFlags inheritance)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User
                    ?? throw new InvalidOperationException(
                        "The current Windows identity has no user SID, so the store's access rules "
                        + "cannot be restricted to it.");

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}
