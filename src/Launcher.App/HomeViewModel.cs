using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Net.Http;
using ModlauncherIV.Core.Acquisition;
using ModlauncherIV.Core.Backup;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;
using ModlauncherIV.Core.Execution;
using ModlauncherIV.Core.Planning;
using ModlauncherIV.Core.Verification;

namespace ModlauncherIV.App;

/// <summary>
/// A program next to the game that this launcher did not install.
///
/// Deliberately not a recipe, and deliberately shown apart from them. A recipe
/// is something written into the game directory, with a snapshot behind it and a
/// way back out. This is a second program that starts the same game, installs
/// itself somewhere else entirely, and is none of our business beyond being
/// worth knowing about. Calling it a mod would make the list say something
/// untrue about what the launcher can take back.
/// </summary>
public sealed record ExternalTool(
    string Name,
    string Version,
    string State,
    bool Fine,
    bool Installed,
    string ActionLabel,
    RelayCommand ActionCommand);

/// <summary>An installed recipe, the way it appears on the home page.</summary>
/// <param name="Version">The release that is installed, not what the catalog has.</param>
/// <param name="State">Files counted, or what is wrong with them.</param>
/// <param name="Intact">False when files are changed or gone.</param>
/// <param name="Outdated">
/// True when the catalog has a different release than the one installed. It
/// happens more often than it sounds: the trainer's release changes with every
/// build of it, and nothing on this page used to say so - the wizard knew, five
/// clicks away, and the home page showed a version number that meant nothing
/// without the other one beside it.
/// </param>
/// <param name="Available">What the catalog has, when that is something else.</param>
public sealed record InstalledMod(
    string RecipeId,
    string Name,
    string Version,
    string State,
    bool Intact,
    bool Outdated,
    string Available,
    RelayCommand RemoveCommand,
    RelayCommand UpdateCommand);

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
    private readonly Action _openWizard;
    private string _status = string.Empty;
    private bool _healthy = true;
    private bool _busy;

    public HomeViewModel(Session session, Action openWizard)
    {
        _session = session;
        _openWizard = openWizard;

        PlayCommand = new RelayCommand(Play, () => CanPlay);
        OnlineCommand = new RelayCommand(PlayOnline, () => _connected is not null);
        WizardCommand = new RelayCommand(openWizard);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => !_busy);
        FolderCommand = new RelayCommand(OpenFolder, () => _session.Install is not null);
        LogCommand = new RelayCommand(Diary.Show);
    }

    private readonly ConnectedInstall? _connected = GtaConnected.Find();

    public RelayCommand PlayCommand { get; }

    /// <summary>Starts GTA Connected. Only there when it is installed.</summary>
    public RelayCommand OnlineCommand { get; }

    public bool HasOnline => _connected is not null;

    public RelayCommand WizardCommand { get; }

    public AsyncRelayCommand VerifyCommand { get; }

    public RelayCommand FolderCommand { get; }

    /// <summary>
    /// Opens the log.
    ///
    /// On the page rather than only in the handout: when something goes wrong,
    /// the person it went wrong for is standing in front of this window, and
    /// "look under %LOCALAPPDATA%" is a sentence that loses people.
    /// </summary>
    public RelayCommand LogCommand { get; }

    public ObservableCollection<InstalledMod> Mods { get; } = [];

    /// <summary>Programs beside the game that the launcher only found.</summary>
    public ObservableCollection<ExternalTool> External { get; } = [];

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

        Diary.Info($"Starting the game: {install.ExecutablePath}");

        Process.Start(new ProcessStartInfo
        {
            FileName = install.ExecutablePath,
            WorkingDirectory = install.Path,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Hands one mod to the wizard to be brought up to date.
    ///
    /// Not installed from here, deliberately. Bringing a recipe up to date is an
    /// ordinary run: dependencies may have moved with it, files it no longer
    /// installs have to be cleaned up, and there is a plan to read and a
    /// question to answer before anything is written. All of that lives in the
    /// wizard. What this saves is finding the right tick box.
    /// </summary>
    private void Update(string recipeId)
    {
        _session.Wanted.Clear();
        _session.Wanted.Add(recipeId);

        Diary.Info($"Update wanted for {recipeId}; handing over to the wizard.");

        _openWizard();
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
            log: new ExecutionLog(Diary.Line),
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

        // The question "which server?" belongs here, at the moment of deciding,
        // rather than on the home page where it sat among things about the
        // installation. The warnings stay with the handover itself: they are
        // about what is about to start, and the same ones apply whether a
        // server was picked here or is about to be picked over there.
        var window = new OnlineWindow(new OnlineViewModel(
            connected,
            Connect,
            () =>
            {
                if (WarnBeforeOnline(connected))
                {
                    Launch(connected, string.Empty);
                }
            }))
        {
            Owner = Application.Current?.MainWindow,
        };

        window.ShowDialog();
    }

    /// <summary>
    /// Says what is about to be invisible, and asks. False means: do not start.
    /// </summary>
    private bool WarnBeforeOnline(ConnectedInstall connected)
    {
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

        if (warnings.Count == 0)
        {
            return true;
        }

        return MessageBox.Show(
            string.Join("\n\n", warnings) + "\n\nStart anyway?",
            SelfInstall.ProgramName,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    /// <summary>
    /// Straight onto one server, past its own list.
    ///
    /// The same warnings as the plain handover - they are about the game
    /// directory, and that does not change because a server was named. Saying
    /// them once here and once there is deliberate: skipping them on the shorter
    /// path would mean the shorter path is the one that warns about nothing.
    /// </summary>
    private void Connect(string server, string game)
    {
        if (_connected is not { } connected || !WarnBeforeOnline(connected))
        {
            return;
        }

        Launch(connected, GtaConnected.ConnectArguments(server, game));
    }

    private static void Launch(ConnectedInstall connected, string arguments)
    {
        Diary.Info($"Handing over to GTA Connected: {connected.LauncherPath} {arguments}".TrimEnd());

        Process.Start(new ProcessStartInfo
        {
            FileName = connected.LauncherPath,
            Arguments = arguments,
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
        // Read the game again first. This page is shown after every removal and
        // after every run of the wizard, and the version line on it was the one
        // from startup - so taking a downgrade back left the page claiming a
        // version the game no longer had.
        _session.Reinspect();
        Raise(nameof(Version));
        Raise(nameof(Platform));
        Raise(nameof(GamePath));

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
            var result = await Task.Run(() => InstallVerifier.Verify(install, ledger, _session.Catalog?.Recipes)).ConfigureAwait(true);

            Mods.Clear();

            foreach (var entry in ledger.Entries.OrderBy(e => e.InstalledAt))
            {
                var broken = result.Files.Count(f =>
                    string.Equals(f.RecipeId, entry.RecipeId, StringComparison.OrdinalIgnoreCase) &&
                    f.State is OwnedFileState.Modified or OwnedFileState.Missing);

                var id = entry.RecipeId;

                // The name the catalog uses today, not the one that was stored
                // when it was installed. A recipe that has been renamed since -
                // and every one of them was, when this stopped being German -
                // would otherwise keep its old name on this page until somebody
                // reinstalled it. The stored name still answers for a recipe
                // that has left the catalog, which is the only case left.
                var current = _session.Catalog?.Recipes
                    .FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

                var outdated = current is not null
                               && !string.Equals(current.Version, entry.RecipeVersion, StringComparison.OrdinalIgnoreCase);

                Mods.Add(new InstalledMod(
                    id,
                    current?.Name ?? entry.RecipeName,
                    entry.RecipeVersion,
                    broken == 0 ? $"{entry.Files.Count} file(s)" : $"{broken} file(s) changed or gone",
                    broken == 0,
                    outdated,
                    outdated ? current!.Version : string.Empty,
                    new RelayCommand(() => Remove(id), () => !_busy),
                    new RelayCommand(() => Update(id), () => !_busy)));
            }

            FillExternal(install);
            Describe(result, ledger);
        }
        finally
        {
            _busy = false;
            VerifyCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// What else is on this machine that starts this game.
    ///
    /// The state line answers the one question that matters and cannot be seen:
    /// whether it is aimed at the installation on this page. With two copies of
    /// the game it may not be, and then nothing listed above applies to what it
    /// starts.
    /// </summary>
    private void FillExternal(GameInstall install)
    {
        External.Clear();

        foreach (var tool in ToolCatalog.LoadFrom(AppPaths.CatalogDirectory, _session.Catalog!))
        {
            var found = Tools.Find(tool);

            if (found.Installed)
            {
                var fine = tool.Id != "gta-connected" || GtaConnected.Find() is not { } c
                           || c.PointsAt(install.Path);

                External.Add(new ExternalTool(
                    tool.Name,
                    tool.Version,
                    fine ? "installed - starts this installation" : "installed, but aimed elsewhere",
                    fine,
                    true,
                    "Start",
                    new RelayCommand(() => Start(found), () => !_busy)));

                continue;
            }

            External.Add(new ExternalTool(
                tool.Name,
                tool.Version,
                "not installed",
                true,
                false,
                "Install",
                new RelayCommand(() => InstallTool(tool), () => !_busy)));
        }
    }

    private void Start(FoundTool found)
    {
        if (found.ExecutablePath is not { } exe)
        {
            return;
        }

        if (found.Tool.Id == "gta-connected")
        {
            PlayOnline();
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Fetches a tool's installer and hands it over.
    ///
    /// The download is checked against the checksum in the signed catalog, the
    /// same way every recipe source is - that part is the same problem and uses
    /// the same code. What is different comes after: the installer is somebody
    /// else's program, it writes where it likes, and nothing here can take that
    /// back. So it is said plainly first, once, and then it is the user's call.
    /// </summary>
    private async void InstallTool(CatalogTool tool)
    {
        var answer = MessageBox.Show(
            $"{tool.Name} {tool.Version}\n\n{tool.Description}\n\n"
            + $"The launcher downloads the installer ({tool.Installer.SizeBytes / 1024 / 1024} MB), "
            + "checks it against the checksum in the signed catalog, and then starts it.\n\n"
            + "From that point it is that installer's own program: it writes where it likes, "
            + "outside the game directory, and the launcher cannot undo it. Uninstalling it "
            + "later is done through Windows, not here.\n\nGo ahead?",
            SelfInstall.ProgramName,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        _busy = true;
        Status = $"Fetching {tool.Name} ...";

        try
        {
            using var http = new HttpClient();
            var acquirer = new SourceAcquirer(http, _session.CacheRoot, new ExecutionLog(Diary.Line));

            var result = await acquirer.AcquireAsync(tool.Installer).ConfigureAwait(true);

            if (!result.Ok)
            {
                MessageBox.Show(
                    $"{tool.Name} could not be fetched:\n\n{result.Error}",
                    SelfInstall.ProgramName, MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }

            var installer = Tools.Unpack(tool, _session.CacheRoot);

            Process.Start(new ProcessStartInfo
            {
                FileName = installer,
                WorkingDirectory = _session.CacheRoot,
                UseShellExecute = true,
            });

            Status = $"{tool.Name}: the installer is running. Check again when it is done.";
        }
        catch (Exception e) when (e is IOException
                                       or UnauthorizedAccessException
                                       or HttpRequestException
                                       or InvalidDataException
                                       or FileNotFoundException)
        {
            MessageBox.Show(
                $"{tool.Name} could not be installed:\n\n{e.Message}",
                SelfInstall.ProgramName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            VerifyCommand.RaiseCanExecuteChanged();
        }
    }

    private void Describe(VerificationResult result, InstallLedger ledger)
    {
        Diary.Info($"Counter-check: {ledger.Entries.Count} recipe(s), "
                   + $"{result.ModifiedCount} changed, {result.MissingCount} missing"
                   + (result.VersionReverted ? $", version reverted to {result.CurrentVersion}" : string.Empty));

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
            // The same fact, two causes, and only one of them is somebody
            // else's doing. Saying "the platform reset the game" to a person who
            // has just taken the downgrade back themselves, in this window, is
            // how a program teaches people to stop reading its messages.
            Status = result.DowngradeRemoved
                ? $"The game is on {result.CurrentVersion} again - the downgrade was taken back. "
                  + $"What is still installed was made for {result.ExpectedVersion} and will not load like this."
                : $"The platform reset the game to {result.CurrentVersion}. "
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
