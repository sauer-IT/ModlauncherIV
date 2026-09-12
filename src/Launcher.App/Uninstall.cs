using System.Diagnostics;
using System.IO;
using System.Windows;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
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
        // From the ledgers, not from detection. Detection answers "what is on
        // this machine now" and says nothing when it finds two installations -
        // and "nothing found" used to mean "nothing is owed", which then offered
        // to delete the snapshots of a game that was still full of mods. That is
        // precisely the thing the order of steps here exists to prevent.
        var owing = LedgerStore.All().Where(l => l.Entries.Count > 0).ToArray();

        if (owing.Length == 0)
        {
            return true;
        }

        var what = string.Join("\n", owing.Select(l =>
            $"  {l.Entries.Count} recipe(s) in {l.GameRoot}"));

        var answer = MessageBox.Show(
            $"The launcher has changed {(owing.Length == 1 ? "an installation" : $"{owing.Length} installations")}:\n\n"
            + what
            + "\n\nTake it all back out and leave the game as it was found?\n\n"
            + "No keeps the mods. They then stay without anything left that knows "
            + "how to remove them.",
            SelfInstall.ProgramName,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            Diary.Info("Uninstall: the mods were left in place at the user's request.");
            return false;
        }

        var catalog = RecipeCatalog.LoadFrom(AppPaths.CatalogDirectory, CatalogTrust.RequireSignature);
        var clean = true;
        var taken = 0;

        foreach (var ledger in owing)
        {
            // A folder that is not there cannot be put back - an external disk,
            // or a game uninstalled in the meantime. Its snapshots are then the
            // only record of what was changed, and they stay.
            if (!Directory.Exists(ledger.GameRoot))
            {
                done.Add($"Not reachable, left alone: {ledger.GameRoot}");
                Diary.Warn($"Uninstall: {ledger.GameRoot} does not exist; its snapshots are kept.");
                clean = false;
                continue;
            }

            var failures = Restore(ledger.GameRoot, catalog.Recipes);

            if (failures.Count > 0)
            {
                done.Add($"{ledger.GameRoot} restored, except: {string.Join(", ", failures)}");
                clean = false;
                continue;
            }

            taken += ledger.Entries.Count;
        }

        if (taken > 0)
        {
            done.Add($"{taken} recipe(s) taken back out of the game");
        }

        return clean;
    }

    /// <summary>
    /// Takes everything back out of one installation. Returns what would not
    /// come out - newest first, which is the order that makes dependencies come
    /// apart by themselves, the same order "remove --all" uses.
    /// </summary>
    private static List<string> Restore(string gameRoot, IReadOnlyList<Recipe> recipes)
    {
        var ledgerStore = new LedgerStore(gameRoot);
        var uninstaller = new Uninstaller(new SnapshotStore(gameRoot), ledgerStore);

        var context = new RecipeContext(
            gameRoot: gameRoot,
            sourceRoot: AppPaths.Cache,
            log: new ExecutionLog(Diary.Line),
            dryRun: false);

        var failures = new List<string>();

        foreach (var id in uninstaller.InstalledNewestFirst())
        {
            var plan = uninstaller.Plan(id, context, recipes);

            if (plan is null || !plan.CanRun || !uninstaller.Remove(plan, context).Success)
            {
                failures.Add(id);
            }
        }

        return failures;
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
