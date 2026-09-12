using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Execution;
using ModlauncherIV.Core.Planning;
using ModlauncherIV.Core.Verification;

namespace ModlauncherIV.App;

/// <summary>An installed recipe, the way it appears on the home page.</summary>
public sealed record InstalledMod(
    string RecipeId,
    string Name,
    string Version,
    string State,
    bool Intact,
    RelayCommand RemoveCommand);

/// <summary>
/// The home page.
///
/// Anyone whose game is already set up wants to play - not click through seven
/// steps only to learn at the end that there is nothing to do. The wizard stays
/// exactly one click away.
/// </summary>
public sealed class HomeViewModel : Observable
{
    private readonly Session _session;
    private string _status = string.Empty;
    private bool _healthy = true;
    private bool _busy;

    public HomeViewModel(Session session, Action openWizard)
    {
        _session = session;

        PlayCommand = new RelayCommand(Play, () => CanPlay);
        OnlineCommand = new RelayCommand(PlayOnline, () => _connected is not null);
        WizardCommand = new RelayCommand(openWizard);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => !_busy);
        FolderCommand = new RelayCommand(OpenFolder, () => _session.Install is not null);
    }

    private readonly ConnectedInstall? _connected = GtaConnected.Find();

    public RelayCommand PlayCommand { get; }

    /// <summary>Starts GTA Connected. Only there when it is installed.</summary>
    public RelayCommand OnlineCommand { get; }

    public bool HasOnline => _connected is not null;

    public RelayCommand WizardCommand { get; }

    public AsyncRelayCommand VerifyCommand { get; }

    public RelayCommand FolderCommand { get; }

    public ObservableCollection<InstalledMod> Mods { get; } = [];

    /// <summary>The offer to put itself on the desktop. Disappears once done.</summary>
    public SetupBanner Setup { get; } = new();

    public string GamePath => _session.Install?.Path ?? "no installation";

    public string Version => _session.Install is { } install
        ? $"{install.Version.Raw} — {install.Version.DisplayName}"
        : string.Empty;

    public string Platform => _session.Install?.Platform switch
    {
        GamePlatform.Steam => "Steam",
        GamePlatform.RockstarLauncher => "Rockstar Games Launcher",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Retail => "Disc",
        _ => "unknown origin",
    };

    /// <summary>The sentence above the play button. Says whether something is wrong.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>False when the counter-check found something. Colours the status.</summary>
    public bool Healthy
    {
        get => _healthy;
        private set => Set(ref _healthy, value);
    }

    public bool CanPlay => _session.Install is not null && File.Exists(_session.Install.ExecutablePath);

    /// <summary>First display. The counter-check runs along with it.</summary>
    public Task EnterAsync() => VerifyAsync();

    /// <summary>
    /// Starts the game directly, bypassing the platform launcher.
    ///
    /// That is not convenience but the heart of the matter: Steam, Epic and the
    /// Rockstar Games Launcher check the files on start and put the current
    /// version back. Anyone starting through the launcher after a downgrade has
    /// lost that downgrade again.
    /// </summary>
    private void Play()
    {
        if (_session.Install is not { } install || !File.Exists(install.ExecutablePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = install.ExecutablePath,
            WorkingDirectory = install.Path,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Takes one recipe back out.
    ///
    /// Not "delete the files": the snapshot also knows which files existed
    /// beforehand and with what content, so an overwritten file gets its old
    /// content back rather than disappearing. That is the whole reason a
    /// downgrade can be undone at all.
    ///
    /// Asks first, and says what it is about to do. Everything else on this page
    /// only reads; this is the one button that takes something away.
    /// </summary>
    private void Remove(string recipeId)
    {
        if (_session.Install is not { } install)
        {
            return;
        }

        var uninstaller = new Uninstaller(new SnapshotStore(install.Path), new LedgerStore(install.Path));

        var context = new RecipeContext(
            gameRoot: install.Path,
            sourceRoot: _session.CacheRoot,
            log: new ExecutionLog(),
            dryRun: false);

        var plan = uninstaller.Plan(recipeId, context, _session.Catalog?.Recipes ?? []);

        if (plan is null)
        {
            MessageBox.Show($"{recipeId} is not installed.", SelfInstall.ProgramName,
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Blockers before the question, not after it. Being asked "are you sure"
        // and then told it was never possible is the wrong order.
        if (!plan.CanRun)
        {
            var why = string.Join("\n", plan.Issues
                .Where(i => i.Severity == IssueSeverity.Fatal)
                .Select(i => "- " + i.Message));

            MessageBox.Show($"{recipeId} cannot be removed:\n\n{why}", SelfInstall.ProgramName,
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var files = plan.Snapshot!.Entries.Count;

        var answer = MessageBox.Show(
            $"Remove {plan.Entry.RecipeName}?\n\n"
            + $"{files} path(s) go back to the state before it was installed. "
            + "Files it created are deleted, files it overwrote get their old content back.",
            SelfInstall.ProgramName,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        var outcome = uninstaller.Remove(plan, context);

        if (!outcome.Success)
        {
            MessageBox.Show(
                "Removal failed:\n\n" + string.Join("\n", outcome.Errors),
                SelfInstall.ProgramName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // Either way: the page has to show what is actually there now.
        _ = VerifyAsync();
    }

    /// <summary>
    /// Hands over to GTA Connected.
    ///
    /// It starts the game itself, so this is a handover rather than a launch:
    /// nothing of ours runs afterwards.
    ///
    /// Two things get said first, because both are invisible until they have
    /// already gone wrong. Its own registry key records which GTAIV.exe it will
    /// start, and that need not be the installation this launcher looks after.
    /// And the ASI loader does not care what the game is being used for - every
    /// plugin in plugins\ loads in multiplayer too, the trainer included. A
    /// trainer on a server is both a good way to be thrown off it and a good way
    /// to crash.
    /// </summary>
    private void PlayOnline()
    {
        if (_connected is not { } connected)
        {
            return;
        }

        var warnings = new List<string>();

        if (_session.Install is { } install && !connected.PointsAt(install.Path))
        {
            warnings.Add(
                $"GTA Connected is set to start\n  {connected.GamePath}\n\n"
                + $"This launcher looks after\n  {install.Path}\n\n"
                + "Nothing installed here applies to what actually starts.");
        }

        var plugins = _session.Install is { } i ? Path.Combine(i.Path, "plugins") : null;

        if (plugins is not null && Directory.Exists(plugins) &&
            Directory.EnumerateFiles(plugins, "*.asi").Any())
        {
            warnings.Add(
                "Plugins from plugins\\ load in multiplayer as well - the trainer "
                + "among them. On a server that is a good way to be thrown off it, "
                + "and a good way to crash. Remove them here first if you would "
                + "rather play clean.");
        }

        if (warnings.Count > 0)
        {
            var answer = MessageBox.Show(
                string.Join("\n\n", warnings) + "\n\nStart anyway?",
                SelfInstall.ProgramName,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = connected.LauncherPath,
            WorkingDirectory = Path.GetDirectoryName(connected.LauncherPath) ?? string.Empty,
            UseShellExecute = true,
        });
    }

    private void OpenFolder()
    {
        if (_session.Install is { } install && Directory.Exists(install.Path))
        {
            Process.Start(new ProcessStartInfo { FileName = install.Path, UseShellExecute = true });
        }
    }

    private async Task VerifyAsync()
    {
        if (_session.Install is not { } install)
        {
            Status = "No installation found.";
            Healthy = false;
            return;
        }

        _busy = true;
        VerifyCommand.RaiseCanExecuteChanged();
        Status = "Checking the installation ...";

        try
        {
            var ledger = _session.Ledger;

            // Checksums over hundreds of files — that does not belong on the
            // thread that draws the window.
            var result = await Task.Run(() => InstallVerifier.Verify(install, ledger)).ConfigureAwait(true);

            Mods.Clear();

            foreach (var entry in ledger.Entries.OrderBy(e => e.InstalledAt))
            {
                var broken = result.Files.Count(f =>
                    string.Equals(f.RecipeId, entry.RecipeId, StringComparison.OrdinalIgnoreCase) &&
                    f.State is OwnedFileState.Modified or OwnedFileState.Missing);

                var id = entry.RecipeId;

                Mods.Add(new InstalledMod(
                    id,
                    entry.RecipeName,
                    entry.RecipeVersion,
                    broken == 0 ? $"{entry.Files.Count} file(s)" : $"{broken} file(s) changed or gone",
                    broken == 0,
                    new RelayCommand(() => Remove(id), () => !_busy)));
            }

            Describe(result, ledger);
        }
        finally
        {
            _busy = false;
            VerifyCommand.RaiseCanExecuteChanged();
        }
    }

    private void Describe(VerificationResult result, InstallLedger ledger)
    {
        if (ledger.Entries.Count == 0)
        {
            Status = "The launcher has not changed anything here yet.";
            Healthy = true;
            return;
        }

        // The version first: if it was patched back, every other finding is just
        // a consequence, and the cause would otherwise sit down in a list.
        if (result.VersionReverted)
        {
            Status = $"The platform reset the game to {result.CurrentVersion}. "
                   + $"{result.ExpectedVersion} was installed. The mods will not load like this.";

            Healthy = false;
            return;
        }

        if (!result.IsIntact)
        {
            Status = $"{result.ModifiedCount + result.MissingCount} file(s) are no longer the way "
                   + "the launcher left them.";

            Healthy = false;
            return;
        }

        Status = "All set.";
        Healthy = true;
    }

    /// <summary>
    /// Whether this installation is set up enough to show the home page.
    ///
    /// The bar is deliberately low: a single installed recipe already means
    /// somebody has been here and knows what they are doing. Whoever has an
    /// untouched game should see the wizard instead of a play button that does
    /// nothing for them.
    /// </summary>
    public static bool LooksConfigured(Session session) =>
        session.Install is not null && session.Ledger.Entries.Count > 0;
}
