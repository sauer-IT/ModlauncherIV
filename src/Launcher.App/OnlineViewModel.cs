using System.Collections.ObjectModel;

namespace ModlauncherIV.App;

/// <summary>One line in the server list.</summary>
/// <param name="Name">Server name, or the address when there is nothing better.</param>
/// <param name="Address">host:port.</param>
/// <param name="Detail">Players and game mode, where they are known.</param>
/// <param name="Origin">"up now", "saved here" or "last played".</param>
/// <param name="Saved">In the launcher's own list - only those can be forgotten.</param>
/// <param name="Game">Which game it serves, in the client's spelling.</param>
public sealed record ServerRow(
    string Name,
    string Address,
    string Detail,
    string Origin,
    bool Saved,
    string Game,
    RelayCommand ConnectCommand,
    RelayCommand KeepCommand,
    RelayCommand ForgetCommand);

/// <summary>
/// The page that opens on "Play online": where do you want to go?
///
/// It used to hand straight over to the client, which then asked the question
/// itself one window later. The list belongs at the moment of deciding, not on
/// the home page where it sat between things about the installation.
///
/// Three sources, one list. What is up now comes from GTA Connected's own
/// master list, which is the only one that knows; what was kept here comes from
/// the launcher; what was played last comes out of the client's own history. If
/// the master list cannot be reached - somebody else's server, somebody else's
/// protocol - the honest answer is the client's own browser, one button away.
/// </summary>
public sealed class OnlineViewModel : Observable
{
    private readonly ConnectedInstall _connected;
    private readonly Action<string, string> _connect;
    private readonly Action _openBrowser;
    private readonly Func<Task<(IReadOnlyList<LiveServer> Servers, string? Error)>> _listing;

    private string _status = string.Empty;
    private string _listingError = string.Empty;
    private bool _busy;
    private string _newAddress = string.Empty;
    private string _newName = string.Empty;
    private string _serverError = string.Empty;

    /// <param name="listing">
    /// Where the live list comes from. Left out, it is the real one - ten
    /// seconds at the outside, because somebody has just clicked "play". Given,
    /// it is a test: what this page does with a list, with an empty one and with
    /// a refusal is the part worth checking, and none of it should depend on a
    /// stranger's server being up.
    /// </param>
    public OnlineViewModel(
        ConnectedInstall connected,
        Action<string, string> connect,
        Action openBrowser,
        Func<Task<(IReadOnlyList<LiveServer> Servers, string? Error)>>? listing = null)
    {
        _connected = connected;
        _connect = connect;
        _openBrowser = openBrowser;
        _listing = listing ?? (() => ServerListing.FetchAsync(TimeSpan.FromSeconds(10)));

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !_busy);
        AddServerCommand = new RelayCommand(AddServer, () => !string.IsNullOrWhiteSpace(NewAddress));
        BrowserCommand = new RelayCommand(openBrowser);
    }

    public ObservableCollection<ServerRow> Servers { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand AddServerCommand { get; }

    /// <summary>Hands over to the client's own server browser.</summary>
    public RelayCommand BrowserCommand { get; }

    /// <summary>Where the list stands: asking, how many are up, or why not.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>
    /// Set when the master list could not be read. Shown next to the way out,
    /// because a reason without a way out is just bad news.
    /// </summary>
    public string ListingError
    {
        get => _listingError;
        private set
        {
            if (Set(ref _listingError, value))
            {
                Raise(nameof(ListingFailed));
            }
        }
    }

    public bool ListingFailed => !string.IsNullOrWhiteSpace(ListingError);

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

    public string NewName
    {
        get => _newName;
        set => Set(ref _newName, value ?? string.Empty);
    }

    public string ServerError
    {
        get => _serverError;
        private set => Set(ref _serverError, value);
    }

    /// <summary>
    /// Who you will be and what will start, in one line.
    ///
    /// Both come out of the client's own settings, and both are worth seeing
    /// before a server is joined: the name is what other people will see, and
    /// the path need not be the installation this launcher looks after.
    /// </summary>
    public string Who =>
        $"as {(string.IsNullOrWhiteSpace(_connected.PlayerName) ? "nobody yet" : _connected.PlayerName)}"
        + $"  ·  starting {_connected.GamePath ?? "an unknown game"}";

    /// <summary>What the client still needs before it can join anything.</summary>
    public string Missing => _connected.Missing ?? string.Empty;

    public bool NotReady => !_connected.Ready;

    /// <summary>Fills the list. Called when the window opens.</summary>
    public async Task LoadAsync()
    {
        _busy = true;
        RefreshCommand.RaiseCanExecuteChanged();

        Status = "Asking GTA Connected's server list ...";
        ListingError = string.Empty;

        var (live, error) = await _listing().ConfigureAwait(true);

        Fill(live);

        if (error is not null)
        {
            ListingError = error;
            Status = "The servers below are the ones kept here and last played.";
        }
        else
        {
            Status = live.Count switch
            {
                0 => "No server is up at the moment - at least none this list knows.",
                1 => "1 server up.",
                _ => $"{live.Count} servers up.",
            };
        }

        _busy = false;
        RefreshCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Builds the list: what is up, then what was kept, then what was played
    /// last - and each address only once, with the best thing known about it.
    /// </summary>
    private void Fill(IReadOnlyList<LiveServer> live)
    {
        Servers.Clear();

        var saved = ServerBook.Load();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var server in live)
        {
            if (!seen.Add(server.Address))
            {
                continue;
            }

            var kept = saved.FirstOrDefault(s => Same(s.Address, server.Address));

            var detail = $"{server.Players}/{server.MaxPlayers} players";
            if (!string.IsNullOrWhiteSpace(server.GameMode))
            {
                detail += $"  ·  {server.GameMode}";
            }

            if (server.Locked)
            {
                detail += "  ·  password";
            }

            Servers.Add(Row(
                string.IsNullOrWhiteSpace(server.Name) ? server.Address : server.Name,
                server.Address,
                detail,
                server.Official ? "up now  ·  official" : "up now",
                kept is not null,
                GtaConnected.GameFromListing(server.Games)));
        }

        foreach (var server in saved)
        {
            if (!seen.Add(server.Address))
            {
                continue;
            }

            Servers.Add(Row(
                string.IsNullOrWhiteSpace(server.Name) ? server.Address : server.Name,
                server.Address,
                "not in the list of servers that are up",
                "saved here",
                saved: true));
        }

        foreach (var address in _connected.RecentServers())
        {
            if (!seen.Add(address))
            {
                continue;
            }

            Servers.Add(Row(address, address, "not in the list of servers that are up", "last played", saved: false));
        }
    }

    private ServerRow Row(
        string name, string address, string detail, string origin, bool saved,
        string game = GtaConnected.GtaIV) => new(
        name,
        address,
        detail,
        origin,
        saved,
        game,
        new RelayCommand(() => _connect(address, game)),
        new RelayCommand(() => Keep(address, name), () => !saved),
        new RelayCommand(() => Forget(address), () => saved));

    private void Keep(string address, string name)
    {
        ServerBook.Add(name, address);
        Refill();
    }

    private void Forget(string address)
    {
        ServerBook.Remove(address);
        Refill();
    }

    /// <summary>
    /// Rebuilds the list without asking the master list again. Keeping or
    /// forgetting a server changes what the launcher knows, not what is up.
    /// </summary>
    private void Refill()
    {
        // The rows that came from the master list keep what it said about them -
        // player counts and game modes are not ours to rebuild - and only their
        // Keep/Forget changes.
        var up = Servers.Where(IsUp).ToArray();
        var saved = ServerBook.Load();
        var seen = new HashSet<string>(up.Select(r => r.Address), StringComparer.OrdinalIgnoreCase);

        Servers.Clear();

        foreach (var row in up)
        {
            Servers.Add(Row(
                row.Name, row.Address, row.Detail, row.Origin,
                saved.Any(s => Same(s.Address, row.Address)), row.Game));
        }

        foreach (var server in saved.Where(s => seen.Add(s.Address)))
        {
            Servers.Add(Row(
                string.IsNullOrWhiteSpace(server.Name) ? server.Address : server.Name,
                server.Address,
                "not in the list of servers that are up",
                "saved here",
                saved: true));
        }

        foreach (var address in _connected.RecentServers().Where(a => seen.Add(a)))
        {
            Servers.Add(Row(address, address, "not in the list of servers that are up", "last played", false));
        }
    }

    private static bool IsUp(ServerRow row) => row.Origin.StartsWith("up now", StringComparison.Ordinal);

    private void AddServer()
    {
        if (!ServerBook.IsAddress(NewAddress))
        {
            ServerError = "That is not an address. Expected something like 192.99.32.215:22000.";
            return;
        }

        ServerError = string.Empty;
        Keep(NewAddress.Trim(), NewName.Trim());

        NewAddress = string.Empty;
        NewName = string.Empty;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
