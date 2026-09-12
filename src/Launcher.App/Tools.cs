using System.IO;
using System.IO.Compression;
using ModlauncherIV.Core.Catalog;
using Microsoft.Win32;

namespace ModlauncherIV.App;

/// <summary>Where a tool from the catalog was found, or that it was not.</summary>
/// <param name="Tool">The catalog entry.</param>
/// <param name="ExecutablePath">Its startable file, or null when absent.</param>
public sealed record FoundTool(CatalogTool Tool, string? ExecutablePath)
{
    public bool Installed => ExecutablePath is not null;
}

/// <summary>
/// Finds and starts the programs the catalog knows about but does not install.
///
/// The detection rule comes out of the signed catalog rather than out of this
/// file. A registry path is as much a part of "which program is this" as a
/// checksum is, and leaving it in code would mean a tool could never be added
/// without a new build.
/// </summary>
public static class Tools
{
    public static FoundTool Find(CatalogTool tool)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(tool.Detect.RegistryKey);

            if (key?.GetValue(tool.Detect.RegistryValue) is string directory)
            {
                var exe = Path.Combine(directory, tool.Detect.Executable);

                // The key outlives an uninstall, the file does not. Asking for
                // the file is the difference between "installed" and "was
                // installed once".
                if (File.Exists(exe))
                {
                    return new FoundTool(tool, exe);
                }
            }
        }
        catch (Exception e) when (e is IOException
                                       or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
            // Unreadable is the same as absent here: nothing can be started.
        }

        return new FoundTool(tool, null);
    }

    /// <summary>
    /// The installer, out of the acquired file and ready to run.
    ///
    /// When the download is an archive the named entry is unpacked beside it -
    /// into the working directory, never into the game. Nothing here writes
    /// anywhere the launcher would have to be able to undo.
    /// </summary>
    public static string Unpack(CatalogTool tool, string cacheRoot)
    {
        var acquired = Path.Combine(cacheRoot, tool.Installer.FileName);

        if (tool.InstallerEntry.Length == 0)
        {
            return acquired;
        }

        var target = Path.Combine(cacheRoot, tool.InstallerEntry);

        using var zip = ZipFile.OpenRead(acquired);

        var entry = zip.GetEntry(tool.InstallerEntry)
            ?? throw new FileNotFoundException(
                $"{tool.InstallerEntry} is not in {tool.Installer.FileName}.", acquired);

        entry.ExtractToFile(target, overwrite: true);

        return target;
    }
}
