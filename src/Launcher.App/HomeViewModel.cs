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

/// <summary>
/// One server to go straight to.
/// </summary>
/// <param name="Name">What it is called here, or the address again.</param>
/// <param name="Address">host:port.</param>
/// <param name="Origin">"saved here" or "last played" - where it came from.</param>
/// <param name="Saved">
/// Whether it is in the launcher's own list. Only those can be forgotten; the
/// client's history belongs to the client.
/// </param>
public sealed record OnlineServer(
    string Name,
    string Address,
    string Origin,
    bool Saved,
    RelayCommand ConnectCommand,
    RelayCommand KeepCommand,
    RelayCommand ForgetCommand);

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
    private string _newAddress = string.Empty;
    private string _newName = string.Empty;
    private string _serverError = string.Empty;

    public HomeViewModel(Session session, Action openWizard)
    {
        _session = session;

        PlayCommand = new RelayCommand(Play, () => CanPlay);
        OnlineCommand = new RelayCommand(PlayOnline, () => _connected is not null);
        WizardCommand = new RelayCommand(openWizard);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => !_busy);
        FolderCommand = new RelayCommand(OpenFolder, () => _session.Install is not null);
        AddServerCommand = new RelayCommand(AddServer, () => !string.IsNullOrWhiteSpace(NewAddress));
    }

    private readonly ConnectedInstall? _connected = GtaConnected.Find();

    public RelayCommand PlayCommand { get; }

    /// <summary>Starts GTA Connected. Only there when it is installed.</summary>
    public RelayCommand OnlineCommand { get; }

    public bool HasOnline => _connected is not null;

    public RelayCommand WizardCommand { get; }

    public AsyncRelayCommand VerifyCommand { get; }

    public RelayCommand FolderCommand { get; }

    /// <summary>Puts what was typed into the list of servers.</summary>
    public RelayCommand AddServerCommand { get; }

    public ObservableCollection<InstalledMod> Mods { get; } = [];

    /// <summary>Programs beside the game that the launcher only found.</summary>
    public ObservableCollection<ExternalTool> External { get; } = [];

    /// <summary>The servers to choose from: kept here, and last played.</summary>
    public ObservableCollection<OnlineServer> Servers { get; } = [];

    /// <summary>Address being typed into the add row.</summary>
    public string NewAddress
    {
        get => _newAddress;
        set
        {
            if (Set(ref _newAddress, value ?? string.Empty))
            {
                ServerError = string.Empty;
                AddServerCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Optional name for it. An address is a poor thing to recognise.</summary>
    public string NewName
    {
        get => _newName;
        set => Set(ref _newName, value ?? string.Empty);
    }

    /// <summary>Why the address was not taken. Empty when there is nothing to say.</summary>
    public string ServerError
    {
        get => _serverError;
        private set => Set(ref _serverError, value);
    }

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
        if (_connected is { } connected && WarnBeforeOnline(connected))
        {
            Launch(connected, string.Empty);
        }
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
    private void Connect(string server)
    {
        if (_connected is not { } connected || !WarnBeforeOnline(connected))
        {
            return;
        }

        Launch(connected, GtaConnected.ConnectArguments(server));
    }

    private static void Launch(ConnectedInstall connected, string arguments)
    {
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

                // The name the catalog uses today, not the one that was stored
                // when it was installed. A recipe that has been renamed since -
                // and every one of them was, when this stopped being German -
                // would otherwise keep its old name on this page until somebody
                // reinstalled it. The stored name still answers for a recipe
                // that has left the catalog, which is the only case left.
                var current = _session.Catalog?.Recipes
                    .FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

                Mods.Add(new InstalledMod(
                    id,
                    current?.Name ?? entry.RecipeName,
                    entry.RecipeVersion,
                    broken == 0 ? $"{entry.Files.Count} file(s)" : $"{broken} file(s) changed or gone",
                    broken == 0,
                    new RelayCommand(() => Remove(id), () => !_busy)));
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
    /// The list to pick from: what was kept here first, then where the client
    /// was last.
    ///
    /// Two sources, one list, because to the person looking for a server the
    /// difference does not matter - and the row says which it is anyway. The
    /// history belongs to GTA Connected and is read from its own file, so a
    /// server dropped over there disappears here too. The saved ones are ours,
    /// because a history is not a choice: it forgets, it is ordered by
    /// accident, and a server nobody has been on yet is never in it.
    /// </summary>
    private void FillServers()
    {
        Servers.Clear();

        var saved = ServerBook.Load();

        foreach (var server in saved)
        {
            var address = server.Address;

            Servers.Add(new OnlineServer(
                string.IsNullOrWhiteSpace(server.Name) ? address : server.Name,
                address,
                "saved here",
                Saved: true,
                new RelayCommand(() => Connect(address)),
                new RelayCommand(() => { }, () => false),
                new RelayCommand(() => Forget(address))));
        }

        if (_connected is not { } connected)
        {
            return;
        }

        foreach (var address in connected.RecentServers())
        {
            // Already kept? Then it is one entry, not two.
            if (saved.Any(s => string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var target = address;

            Servers.Add(new OnlineServer(
                target,
                target,
                "last played",
                Saved: false,
                new RelayCommand(() => Connect(target)),
                new RelayCommand(() => Keep(target)),
                new RelayCommand(() => { }, () => false)));
        }
    }

    /// <summary>Takes a server into the launcher's own list.</summary>
    private void Keep(string address, string name = "")
    {
        ServerBook.Add(name, address);
        FillServers();
    }

    private void Forget(string address)
    {
        ServerBook.Remove(address);
        FillServers();
    }

    /// <summary>
    /// Adds what was typed in. The address is checked before it is kept, not
    /// when it is used: this ends up on a command line, and the answer to a bad
    /// one belongs next to the box it was typed into.
    /// </summary>
    private void AddServer()
    {
        if (!ServerBook.IsAddress(NewAddress))
        {
            ServerError = "That is not an address. Expected something like 192.99.32.215:22000.";
            return;
        }

        ServerError = string.Empty;
        Keep(NewAddress.Trim(), NewName);

        NewAddress = string.Empty;
        NewName = string.Empty;
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
        Servers.Clear();

        FillServers();

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
            var acquirer = new SourceAcquirer(http, _session.CacheRoot, new ExecutionLog());

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
