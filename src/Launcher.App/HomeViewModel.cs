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
using ModlauncherIV.Core.Protection;
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
/// <param name="AvailableLabel">Whether that is newer, older, or merely different.</param>
/// <param name="CanUpdate">
/// Only when the catalog's release is later, or nothing can tell. An older one
/// in the catalog is shown, never offered.
/// </param>
/// <param name="Mismatched">
/// True when this was made for a different game version than the one installed
/// now.
///
/// The state nothing used to mention. Every file is exactly where it was put,
/// so the counter-check is happy and the page looked healthy - while the mod
/// itself does not load, or worse. It is what a version change leaves behind,
/// and the planner warns before one; this is the same thing said afterwards,
/// for somebody who is already standing in it.
/// </param>
/// <param name="MadeFor">The versions it was made for, when they are not this one.</param>
public sealed record InstalledMod(
    string RecipeId,
    string Name,
    string Version,
    string State,
    bool Intact,
    bool Outdated,
    string Available,
    bool Mismatched,
    string MadeFor,
    RelayCommand RemoveCommand,
    RelayCommand UpdateCommand,
    string AvailableLabel = "newer in the catalog:",
    bool CanUpdate = true);

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
        LockCommand = new RelayCommand(Lock, () => CanLock && !_busy);
    }

    /// <summary>
    /// Sets the platform's switch against automatic updates, where there is one.
    ///
    /// Only Steam has a file for it. Everything else gets a sentence instead,
    /// because a button that claims to lock something it cannot would be worse
    /// than none.
    /// </summary>
    private void Lock()
    {
        if (_session.Install is not { } install)
        {
            return;
        }

        var locked = UpdateGuard.TryLock(install, out var message);

        Diary.Info($"Update lock: {message}");

        MessageBox.Show(
            message,
            SelfInstall.ProgramName,
            MessageBoxButton.OK,
            locked ? MessageBoxImage.Information : MessageBoxImage.Warning);

        DescribeGuard(install);
    }

    /// <summary>Reads the platform's state and puts it into one readable line.</summary>
    private void DescribeGuard(GameInstall install)
    {
        var guard = UpdateGuard.Check(install);

        Guard = guard.State switch
        {
            GuardState.Locked => guard.Summary,
            GuardState.Unlocked => $"{guard.Summary} {guard.Detail}".Trim(),

            // For the ones without a switch the summary alone is a statement of
            // fact; what to do about it is the first line of the advice.
            _ => string.Join(
                "  ",
                new[] { guard.Summary, guard.Instructions.FirstOrDefault() }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
        };

        GuardFine = guard.State == GuardState.Locked;
        CanLock = guard.State == GuardState.Unlocked;

        Raise(nameof(Guard));
        Raise(nameof(GuardFine));
        Raise(nameof(CanLock));
        LockCommand.RaiseCanExecuteChanged();
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

    /// <summary>
    /// Whether the platform can put this installation back, in one line.
    ///
    /// It used to be said once, on the last page of the wizard, and then never
    /// again. The risk is not a one-off: Steam resets its own manifest now and
    /// then, the Rockstar launcher checks the installation on every start, and
    /// the person this happens to opens this page weeks later wondering why the
    /// mods went quiet. It belongs where they live, not where they passed once.
    /// </summary>
    public string Guard { get; private set; } = string.Empty;

    /// <summary>False when something out there may undo the downgrade.</summary>
    public bool GuardFine { get; private set; } = true;

    /// <summary>Steam is the only one with a switch we can actually set.</summary>
    public bool CanLock { get; private set; }

    public RelayCommand LockCommand { get; }

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
    /// Brings one mod up to date, right here, when that is all there is to it.
    ///
    /// It used to open the wizard every time, which meant clicking through
    /// version, mods, plan and files to install the one thing the button had
    /// already named. Most updates are exactly that: one recipe, a newer release,
    /// nothing else moving. Those run from this click - through the same
    /// planner, the same checksum-checked files and the same snapshot as the
    /// wizard, so nothing about safety is skipped, only the pages.
    ///
    /// The wizard still gets it when there is more to it than that: a
    /// dependency that has to come along, or a plan the planner cannot make.
    /// Those are the cases with something to read before anything is written.
    /// </summary>
    private async void Update(string recipeId)
    {
        if (_busy || _session.Install is not { } install)
        {
            return;
        }

        var journey = JourneyPlanner.Plan(
            new JourneyRequest(null, [recipeId]),
            _session.Catalog?.Recipes ?? [],
            install.Version.Raw,
            _session.Ledger);

        if (SingleUpdate(journey, recipeId) is not { } step)
        {
            HandToWizard(recipeId, "more than this one recipe is involved");
            return;
        }

        _busy = true;
        Status = $"Updating {step.Recipe.Name} ...";
        Healthy = true;
        Diary.Info($"Update of {recipeId} {step.InstalledVersion} -> {step.Recipe.Version}, straight from the home page.");

        var fetched = false;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var acquirer = new SourceAcquirer(
                http, _session.CacheRoot, new ExecutionLog(Diary.Line), AppPaths.BundledDirectory,
                [AppPaths.Downloads]);

            var missing = new List<string>();

            foreach (var source in step.Recipe.RequiredFiles)
            {
                var result = await acquirer.AcquireAsync(source).ConfigureAwait(true);
                if (!result.Ok)
                {
                    missing.Add(source.FileName);
                }
            }

            // A file that cannot be had is the wizard's page to explain: it
            // says which file, where it goes, and lets you try again.
            if (missing.Count > 0)
            {
                Diary.Warn($"Update of {recipeId}: could not get {string.Join(", ", missing)}.");
                return;
            }

            fetched = true;

            var outcome = await Task.Run(() => RunStep.ApplyOne(install.Path, step, install.Version.Raw))
                .ConfigureAwait(true);

            if (outcome.Success)
            {
                Diary.Info($"{recipeId} {step.Recipe.Version} installed, snapshot {outcome.SnapshotId}.");
                return;
            }

            Diary.Error($"{recipeId} update failed: {string.Join(" ", outcome.Errors)}");

            MessageBox.Show(
                $"{step.Recipe.Name} could not be updated:\n\n"
                + string.Join("\n", outcome.Errors.Select(e => "- " + e))
                + (outcome.RolledBack
                    ? "\n\nThe previous state was restored."
                    : "\n\nNothing was changed."),
                SelfInstall.ProgramName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Diary.Error($"Update of {recipeId}: {e.Message}");

            MessageBox.Show(
                $"{step.Recipe.Name} could not be updated:\n\n{e.Message}",
                SelfInstall.ProgramName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;

            if (fetched)
            {
                await VerifyAsync().ConfigureAwait(true);
            }
        }

        if (!fetched)
        {
            HandToWizard(recipeId, "a file it needs is not there");
        }
    }

    /// <summary>
    /// The one step to run when updating <paramref name="recipeId"/> is nothing
    /// more than that; null when the wizard should have it.
    /// </summary>
    public static JourneyStep? SingleUpdate(Journey journey, string recipeId)
    {
        if (!journey.IsPossible || journey.Remaining is not [var only])
        {
            return null;
        }

        return only.State == JourneyStepState.NeedsUpdate
               && string.Equals(only.Recipe.Id, recipeId, StringComparison.OrdinalIgnoreCase)
            ? only
            : null;
    }

    private void HandToWizard(string recipeId, string why)
    {
        _session.Wanted.Clear();
        _session.Wanted.Add(recipeId);

        Diary.Info($"Update wanted for {recipeId}; {why}, handing over to the wizard.");

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
    /// plugin in plugins\ was thought to load in multiplayer too. It does not -
    /// the client loads what it loads - so what is left to say is the one thing
    /// that is true and invisible: which game it will start. A trainer on a
    /// server with other people on it would be cheating and a good way
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

        // What used to stand here was that plugins come along into multiplayer,
        // the trainer among them. That was reasoned, not tried, and it is wrong:
        // the client decides what is loaded into the game it starts, and none of
        // this goes with it. The warning is gone rather than corrected - there
        // is nothing here to warn about, and a warning that is not true is worse
        // than none at all.

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

        if (_session.Install is { } reinspected)
        {
            DescribeGuard(reinspected);
        }

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

                // Which way round the catalog differs, not merely that it does.
                // "Different" used to be shown as "newer", and the Update button
                // then installed an older trainer over a newer one - measured on
                // this machine: 04cbb02b in the game, 0e3aca81 in the launcher,
                // one click, and the keyboard fix was gone.
                var order = current is null
                    ? ReleaseOrder.Same
                    : ReleaseVersion.Compare(entry.RecipeVersion, current.Version);

                var outdated = order is ReleaseOrder.Newer or ReleaseOrder.Older or ReleaseOrder.Unordered;
                var canUpdate = order is ReleaseOrder.Newer or ReleaseOrder.Unordered;
                var availableLabel = order switch
                {
                    ReleaseOrder.Newer => "newer in the catalog:",
                    ReleaseOrder.Older => "older in the catalog:",
                    _ => "different in the catalog:",
                };

                // Made for another game version than the one that is installed.
                // Its files are all present, so nothing else on this page would
                // ever mention it - and the mod is silent, or worse.
                var mismatched = current is not null
                                 && current.AppliesTo.Count > 0
                                 && !current.IsVersionTransition
                                 && !current.Matches(install.Version.Raw);

                Mods.Add(new InstalledMod(
                    id,
                    current?.Name ?? entry.RecipeName,
                    entry.RecipeVersion,
                    broken == 0 ? $"{entry.Files.Count} file(s)" : $"{broken} file(s) changed or gone",
                    broken == 0,
                    outdated,
                    outdated ? current!.Version : string.Empty,
                    mismatched,
                    mismatched ? string.Join(", ", current!.AppliesTo) : string.Empty,
                    new RelayCommand(() => Remove(id), () => !_busy),
                    new RelayCommand(() => Update(id), () => !_busy),
                    availableLabel,
                    outdated && canUpdate));
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

        // Mods for another version of the game are not a file problem - every
        // one of their files is present - so the counter-check has nothing to
        // say about them. Said here, because a page reporting "everything is
        // fine" over mods that cannot load is worse than one that says nothing.
        var mismatched = Mods.Where(m => m.Mismatched).ToArray();

        if (mismatched.Length > 0)
        {
            var names = string.Join(", ", mismatched.Select(m => m.Name));

            Status = mismatched.Length == 1
                ? $"{names} was made for {mismatched[0].MadeFor} and the game is {result.CurrentVersion}. "
                  + "It will not load - take it back, or put that version back."
                : $"{mismatched.Length} mods were made for another version than {result.CurrentVersion}: {names}. "
                  + "They will not load - take them back, or put that version back.";

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
