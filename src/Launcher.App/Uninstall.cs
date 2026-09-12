using System.Diagnostics;
using System.IO;
using System.Windows;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Execution;

namespace ModlauncherIV.App;

/// <summary>
/// Takes the launcher off the machine again.
///
/// Reached from "Apps &amp; features" through <c>--uninstall</c>, which is what
/// the registry entry written at install time points at.
///
/// The order is the whole design. Mods first, snapshots second, everything else
/// after: the snapshots are the only way the game gets back to how it was, so
/// deleting them before taking the mods out would leave a modded game with no
/// way back and no program left to do it with. Anyone who has ever run an
/// uninstaller that cleaned up its own backups first knows what that costs.
/// </summary>
public static class Uninstall
{
    public const string Switch = "--uninstall";

    public static void Run()
    {
        var answer = MessageBox.Show(
            $"Remove {SelfInstall.ProgramName}?\n\n"
            + "You will be asked separately what should happen to the game and to "
            + "the backups.",
            SelfInstall.ProgramName,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        var done = new List<string>();

        var restored = OfferToRestoreGame(done);

        // Only offered once the game is back, and only then is throwing them
        // away harmless. Left alone otherwise, even at the price of a folder
        // nobody cleans up: they are what the game is still owed.
        if (restored)
        {
            OfferToRemoveState(done);
        }

        RemoveShortcuts(done);
        SelfInstall.UnregisterUninstall();
        done.Add("Removed from the list of installed programs");

        // The running EXE cannot delete itself. Rather than leave a detached
        // command behind to do it - which is what such a thing looks like from
        // the outside, and what every scanner treats it as - the folder is
        // opened and the one remaining file named.
        var folder = SelfInstall.TargetDirectory;
        var leftover = Directory.Exists(folder);

        MessageBox.Show(
            string.Join("\n", done.Select(d => "- " + d))
            + (leftover
                ? $"\n\nWhat is left is the program itself:\n  {SelfInstall.TargetPath}\n\n"
                  + "It cannot delete itself while it is running. The folder opens now; "
                  + "delete it there."
                : string.Empty),
            SelfInstall.ProgramName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        if (leftover)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
            {
            }
        }
    }

    /// <summary>
    /// Offers to put the game back first. True when nothing is installed any
    /// more - either because it was taken back, or because there was nothing.
    /// </summary>
    private static bool OfferToRestoreGame(List<string> done)
    {
        var session = new Session();
        Detection.FillAsync(session).GetAwaiter().GetResult();

        if (session.Install is not { } install)
        {
            return true;
        }

        var ledgerStore = new LedgerStore(install.Path);
        var installed = ledgerStore.Load().Entries.Count;

        if (installed == 0)
        {
            return true;
        }

        var answer = MessageBox.Show(
            $"{installed} recipe(s) are installed in\n  {install.Path}\n\n"
            + "Take them back out and leave the game as it was found?\n\n"
            + "No keeps the mods. They then stay without anything left that knows "
            + "how to remove them.",
            SelfInstall.ProgramName,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return false;
        }

        var uninstaller = new Uninstaller(new SnapshotStore(install.Path), ledgerStore);

        var context = new RecipeContext(
            gameRoot: install.Path,
            sourceRoot: session.CacheRoot,
            log: new ExecutionLog(Diary.Line),
            dryRun: false);

        var failures = new List<string>();

        // Newest first, which is the order that makes dependencies come apart by
        // themselves - the same order "remove --all" uses.
        foreach (var id in uninstaller.InstalledNewestFirst())
        {
            var plan = uninstaller.Plan(id, context, session.Catalog?.Recipes ?? []);

            if (plan is null || !plan.CanRun)
            {
                failures.Add(id);
                continue;
            }

            if (!uninstaller.Remove(plan, context).Success)
            {
                failures.Add(id);
            }
        }

        if (failures.Count > 0)
        {
            done.Add($"Game restored, except: {string.Join(", ", failures)}");
            return false;
        }

        done.Add($"{installed} recipe(s) taken back out of the game");
        return true;
    }

    /// <summary>
    /// Offers to throw away the ledger, the snapshots and the downloads.
    ///
    /// Separately, because it is the one step with nothing behind it. The
    /// download cache is only time; the snapshots are the game's earlier self.
    /// </summary>
    private static void OfferToRemoveState(List<string> done)
    {
        if (!Directory.Exists(AppPaths.Root))
        {
            return;
        }

        long bytes = 0;
        try
        {
            bytes = new DirectoryInfo(AppPaths.Root)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        var answer = MessageBox.Show(
            $"Also delete the backups and downloads?\n  {AppPaths.Root}\n"
            + $"  {bytes / 1024 / 1024} MB\n\n"
            + "This holds the snapshots, the record of what was installed, the "
            + "signing key and everything downloaded so far. The game has been put "
            + "back, so none of it is owed anything any more - but a download of "
            + "several hundred megabytes would have to happen again.",
            SelfInstall.ProgramName,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            done.Add($"Backups and downloads kept in {AppPaths.Root}");
            return;
        }

        try
        {
            Directory.Delete(AppPaths.Root, recursive: true);
            done.Add("Backups and downloads deleted");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            done.Add($"Backups could not be deleted: {e.Message}");
        }
    }

    private static void RemoveShortcuts(List<string> done)
    {
        foreach (var link in new[] { SelfInstall.DesktopShortcut, SelfInstall.StartMenuShortcut })
        {
            try
            {
                if (File.Exists(link))
                {
                    File.Delete(link);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        done.Add("Shortcuts removed");
    }
}
