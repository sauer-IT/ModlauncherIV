using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ModlauncherIV.App;
using ModlauncherIV.Core.Catalog;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.UiSmoke;

/// <summary>
/// Builds every page of the launcher once, without showing anything.
///
/// The reason this exists: a mistyped resource key, a template that does not
/// parse, a binding to a property that was renamed - none of those are build
/// errors. They surface when a person opens that one page, which in a wizard
/// can be five clicks and a download in. Measuring the page makes WPF do all
/// of that work, and WPF says what went wrong through the binding trace, so
/// this listens to it and treats a single line there as a failure.
/// </summary>
internal static class Program
{
    private static readonly List<string> Failures = [];
    private static readonly Collector Trace = new();

    [STAThread]
    private static int Main(string[] args)
    {
        var catalogDirectory = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "catalog");

        // Application.Current has to exist before anything is built: the views
        // take their styles, converters and step-to-view mapping from its
        // resources, and without it every StaticResource in them fails.
        var app = new ModlauncherIV.App.App();
        app.InitializeComponent();

        // Anything that goes wrong off this thread - a view model that starts
        // work of its own - would otherwise take the process down with no line
        // saying why, and a test that dies silently reads like a test that
        // never ran.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.WriteLine($"  UNHANDLED  {e.ExceptionObject}");

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(Trace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        var session = BuildSession(catalogDirectory);

        Console.WriteLine();
        Console.WriteLine($"  catalog: {session.Catalog?.Recipes.Count ?? 0} recipe(s) from {catalogDirectory}");
        Console.WriteLine();

        // The home page and the wizard frame. Everything else is a step, and
        // which view belongs to a step is decided by the DataTemplates in
        // App.xaml - so going through the steps tests that mapping as well.
        // Entered, not merely constructed: the lists on it - installed mods,
        // external tools, servers - are filled there, and an empty page tests
        // none of the templates that draw them.
        var home = new HomeViewModel(session, () => { });
        home.EnterAsync().GetAwaiter().GetResult();
        Check("home", home);
        Check("wizard frame", new WizardViewModel(session, canGoHome: true));

        Check("welcome", new WelcomeStep(session));
        Check("install", new InstallStep(session));
        Check("choice", Entered(new ChoiceStep(session)));
        Check("plan", new PlanStep(session));
        Check("acquire", new AcquireStep(session));
        Check("run", new RunStep(session));
        Check("done", new DoneStep(session));

        // The mod list is the page this test was written for. Its content is
        // built rather than bound straight through, and empty groups or a
        // filter that matches nothing are states a person will produce.
        CheckChoiceList(session);
        CheckVersions(session);
        CheckServerBook();
        CheckListingProtocol();
        CheckOnlineWindow();

        // Only when asked for: this one talks to somebody else's server, and a
        // test suite that fails because a stranger's host is down is a test
        // suite people learn to ignore.
        if (args.Contains("--live"))
        {
            AskTheRealList();
        }

        Console.WriteLine();
        Console.WriteLine(Failures.Count == 0
            ? "  every view builds."
            : $"  {Failures.Count} view(s) with problems.");
        Console.WriteLine();

        return Failures.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// A step that fills itself on entry is worth seeing filled. Only for the
    /// ones where entering does nothing but read - acquire downloads and run
    /// installs, and neither belongs in a test of the drawing.
    /// </summary>
    private static ChoiceStep Entered(ChoiceStep step)
    {
        step.EnterAsync().GetAwaiter().GetResult();
        return step;
    }

    private static void Check(string name, object viewModel)
    {
        Trace.Lines.Clear();

        string? error = null;

        try
        {
            // A ContentControl, because that is how the shell shows it: the
            // view is picked by the type of what is put in.
            var host = new ContentControl { Content = viewModel };

            host.Measure(new Size(940, 4000));
            host.Arrange(new Rect(0, 0, 940, 4000));
            host.UpdateLayout();

            // Bindings are resolved on a lower priority than layout. Without
            // this the trace would still be empty here.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
        catch (Exception e)
        {
            error = e.Message;
        }

        if (error is null && Trace.Lines.Count == 0)
        {
            Console.WriteLine($"  PASS  {name}");
            return;
        }

        Console.WriteLine($"  FAIL  {name}");
        Failures.Add(name);

        if (error is not null)
        {
            Console.WriteLine($"          {error}");
        }

        foreach (var line in Trace.Lines.Take(6))
        {
            Console.WriteLine($"          {line}");
        }
    }

    /// <summary>
    /// What the grouping actually produced. Not drawing - counting: a list that
    /// renders beautifully and silently drops half the catalog is worse than
    /// one that throws.
    /// </summary>
    private static void CheckChoiceList(Session session)
    {
        var step = new ChoiceStep(session);
        step.EnterAsync().GetAwaiter().GetResult();

        // Without this the rest below would pass on an empty list and say
        // nothing. An unsigned or half-signed catalog loads no recipes at all,
        // and that has shipped before.
        Report("choice: the catalog loaded at all", step.Recipes.Count > 0);

        var grouped = step.Groups.Sum(g => g.Items.Count);
        Report("choice: every recipe lands in a group", grouped == step.Recipes.Count);
        Report("choice: the groups have names", step.Groups.All(g => !string.IsNullOrWhiteSpace(g.Name)));

        Report(
            "choice: what does not fit the version is marked, not hidden",
            step.Recipes.Where(r => !r.Available).All(r => !string.IsNullOrWhiteSpace(r.UnavailableBecause)));

        var unfit = step.Recipes.FirstOrDefault(r => !r.Available);
        if (unfit is not null)
        {
            unfit.Selected = true;
            Report("choice: and cannot be ticked", !unfit.Selected);
        }

        step.Filter = "trainer";
        var narrowed = step.Groups.Sum(g => g.Items.Count);
        Report("choice: the filter narrows", narrowed is > 0 && narrowed < step.Recipes.Count);

        step.Filter = "qqqq-nothing-is-called-that";
        Report("choice: a filter matching nothing says so", step.NothingMatches);

        step.Filter = string.Empty;
        Report("choice: clearing it brings everything back", step.Groups.Sum(g => g.Items.Count) == step.Recipes.Count);

        // Printed rather than only counted: whether a list reads well is not
        // something an assertion can tell you, and this is the one place the
        // page can be looked at without opening a window.
        Console.WriteLine();
        Console.WriteLine($"  the list, as it stands for {step.Target?.Raw}:");

        foreach (var group in step.Groups)
        {
            Console.WriteLine();
            Console.WriteLine($"    {group.Name}  ({group.Count})");

            foreach (var item in group.Items)
            {
                var marks = new List<string>();
                if (item.Installed) { marks.Add("installed"); }
                if (item.ByHand) { marks.Add("by hand"); }
                if (!item.Available) { marks.Add(item.UnavailableBecause); }

                var suffix = marks.Count > 0 ? "  [" + string.Join(", ", marks) + "]" : string.Empty;
                Console.WriteLine($"      {item.Name,-38} {item.SizeText,9}{suffix}");
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Which versions the page offers, from two places the game can stand in.
    ///
    /// The one that matters is somebody already on 1.0.7.0 looking at four mods
    /// that want 1.0.8.0: it has to be in the list, and it has to say what to do
    /// rather than simply not be there.
    /// </summary>
    private static void CheckVersions(Session session)
    {
        // With the version in place unreadable, both downgrade targets are
        // offered: nothing can be said about what is reachable, and greying
        // everything out would strand the user on this page.
        var unknown = new ChoiceStep(session);
        unknown.EnterAsync().GetAwaiter().GetResult();

        Report("versions: 1.0.7.0 is offered", unknown.Versions.Any(v => v.Raw == "1.0.7.0"));
        Report("versions: 1.0.8.0 is offered", unknown.Versions.Any(v => v.Raw == "1.0.8.0"));

        Report(
            "versions: nothing is offered that no recipe produces",
            unknown.Versions.All(v => v.IsCurrent || v.Raw is "1.0.7.0" or "1.0.8.0"));

        // And now the case this was written for: the game is on 1.0.7.0, four
        // mods want 1.0.8.0, and there is no edge between the two downgrades.
        var step = new ChoiceStep(On(session, "1.0.7.0"));
        step.EnterAsync().GetAwaiter().GetResult();

        var eight = step.Versions.FirstOrDefault(v => v.Raw == "1.0.8.0");

        Report("versions: on 1.0.7.0 the 1.0.8.0 entry is still shown", eight is not null);
        Report("versions: and is not silently selectable", eight is { Reachable: false });
        Report(
            "versions: and says what stands in the way",
            eight is not null && eight.Reason.Contains("1.0.8.0") && eight.Reason.Contains("1.0.7.0"));

        Report("versions: keeping what is there stays the preselection", step.Target?.Raw == "1.0.7.0");

        Console.WriteLine();
        Console.WriteLine("  the version list on a 1.0.7.0 installation:");

        foreach (var version in step.Versions)
        {
            var mark = version.Reachable ? " " : "-";
            Console.WriteLine($"    {mark} {version.Label,-28} {version.Reason}");
        }
    }

    /// <summary>
    /// The list of servers the launcher keeps.
    ///
    /// The address is handed to another program on a command line, so what may
    /// be stored matters more here than anywhere else on the page - including
    /// what a hand-edited file may put back in.
    /// </summary>
    private static void CheckServerBook()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mliv-servers-{Guid.NewGuid():N}.json");

        try
        {
            Report("servers: an empty list to start with", ServerBook.Load(file).Count == 0);

            ServerBook.Add("Jacob's", "192.99.32.215:22000", file);
            var saved = ServerBook.Load(file);
            Report("servers: one is kept", saved.Count == 1 && saved[0].Address == "192.99.32.215:22000");
            Report("servers: with its name", saved[0].Name == "Jacob's");

            // The same address twice is one server with a new name, not two.
            ServerBook.Add("Jacob", "192.99.32.215:22000", file);
            Report("servers: the same address does not double up", ServerBook.Load(file).Count == 1);
            Report("servers: but takes the new name", ServerBook.Load(file)[0].Name == "Jacob");

            ServerBook.Add("", "play.example.org:22005", file);
            Report("servers: a second one is added", ServerBook.Load(file).Count == 2);

            Report("servers: a host name with a port is an address", ServerBook.IsAddress("play.example.org:22005"));
            Report("servers: an empty one is not", !ServerBook.IsAddress("  "));
            Report("servers: and neither is one with a space", !ServerBook.IsAddress("1.2.3.4 --do-something"));
            Report("servers: nor one with quotes", !ServerBook.IsAddress("\"1.2.3.4\""));
            Report("servers: nor one longer than the limit", !ServerBook.IsAddress(new string('a', 65)));

            // What a hand-edited file puts back in is foreign data, even though
            // we wrote the file.
            File.WriteAllText(file, """
                [
                  { "Name": "fine", "Address": "10.0.0.5:22000" },
                  { "Name": "smuggled", "Address": "10.0.0.6:22000 --flag" }
                ]
                """);

            var loaded = ServerBook.Load(file);
            Report("servers: a tampered file keeps what is an address", loaded.Count == 1);
            Report("servers: and drops what is not", loaded.All(s => !s.Address.Contains(' ')));

            File.WriteAllText(file, "this is not json");
            Report("servers: a broken file costs the list, not the page", ServerBook.Load(file).Count == 0);

            ServerBook.Add("back", "10.0.0.5:22000", file);
            ServerBook.Remove("10.0.0.5:22000", file);
            Report("servers: one can be forgotten again", ServerBook.Load(file).Count == 0);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// The master list's wire format, against bytes built here.
    ///
    /// Without this the only test of the parser would be somebody else's server
    /// being up. The frames below are written the way the client's own page
    /// writes them - a 7-bit length in front of everything - so a change to the
    /// reader that breaks the shape shows up here rather than on a black list
    /// in front of a user.
    /// </summary>
    private static void CheckListingProtocol()
    {
        var servers = new List<LiveServer>();

        var type = ServerListing.Read(
            ServerAdd(flags: 2, "Jacob's Freeroam", "Freeroam", max: 64, current: 12, "1.2.3.4:22000", "IVC"),
            servers);

        Report("listing: a server entry is read", servers.Count == 1);
        Report("listing: it is a ServerAdd", type == 0);

        if (servers.Count == 1)
        {
            var server = servers[0];
            Report("listing: the address", server.Address == "1.2.3.4:22000");
            Report("listing: the name", server.Name == "Jacob's Freeroam");
            Report("listing: how full it is", server is { Players: 12, MaxPlayers: 64 });
            Report("listing: and that it is official", server.Official && !server.Locked);
        }

        // The address comes from a stranger and goes on a command line.
        servers.Clear();
        ServerListing.Read(
            ServerAdd(0, "bad", "mode", 10, 1, "1.2.3.4:22000 --flag", "IVC"),
            servers);

        Report("listing: an address with a space is dropped", servers.Count == 0);

        // A message that stops in the middle must cost that message, nothing else.
        var truncated = ServerAdd(0, "half", "mode", 10, 1, "1.2.3.4:22000", "IVC");
        servers.Clear();
        ServerListing.Read(truncated[..(truncated.Length / 2)], servers);
        Report("listing: a truncated message costs only itself", servers.Count == 0);

        var join = ServerListing.JoinMessage();
        Report("listing: the join message asks to join", join.Array is not null && join.Array[0] == 0);
        Report("listing: and names the games", join.Count > 4);
    }

    /// <summary>
    /// The page that opens on "Play online" - built for real, with a list that
    /// was not fetched from anybody.
    ///
    /// It is a Window rather than a page in the shell, so it is shown, off the
    /// side of the screen, and closed again: showing is what makes WPF build
    /// and bind it, which is the whole point of this test.
    /// </summary>
    private static void CheckOnlineWindow()
    {
        var connected = new ConnectedInstall(
            Path.Combine(Path.GetTempPath(), "mliv-ui-smoke", "Launcher.exe"),
            Path.Combine(Path.GetTempPath(), "mliv-ui-smoke", "GTAIV.exe"),
            "1.9.0");

        IReadOnlyList<LiveServer> live =
        [
            new("1.2.3.4:22000", "Busy Freeroam", "Freeroam", 30, 64, false, true, ["IVC"]),
            new("5.6.7.8:22000", "Quiet one", "Racing", 1, 32, true, false, ["IVC"]),
        ];

        var model = new OnlineViewModel(
            connected,
            _ => { },
            () => { },
            () => Task.FromResult((live, (string?)null)));

        model.LoadAsync().GetAwaiter().GetResult();

        Report("online: the servers that are up are listed", model.Servers.Count == 2);
        Report("online: busiest first", model.Servers.Count == 2 && model.Servers[0].Address == "1.2.3.4:22000");
        Report("online: with how full they are", model.Servers.Count > 0 && model.Servers[0].Detail.Contains("30/64"));
        Report("online: and the list says how many", model.Status.Contains('2'));

        // A refusal has to leave a way forward rather than an empty page.
        var refused = new OnlineViewModel(
            connected,
            _ => { },
            () => { },
            () => Task.FromResult(((IReadOnlyList<LiveServer>)[], (string?)"nothing answered")));

        refused.LoadAsync().GetAwaiter().GetResult();

        Report("online: a refusal is said out loud", refused.ListingFailed);
        Report("online: and names what went wrong", refused.ListingError.Contains("nothing answered"));

        Trace.Lines.Clear();
        string? error = null;

        try
        {
            var window = new OnlineWindow(model)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000,
            };

            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.Close();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
        catch (Exception e)
        {
            error = e.Message;
        }

        if (error is null && Trace.Lines.Count == 0)
        {
            Console.WriteLine("  PASS  online: the window builds");
        }
        else
        {
            Console.WriteLine("  FAIL  online: the window builds");
            Failures.Add("online window");

            if (error is not null)
            {
                Console.WriteLine($"          {error}");
            }

            foreach (var line in Trace.Lines.Take(6))
            {
                Console.WriteLine($"          {line}");
            }
        }
    }

    /// <summary>Builds one ServerAdd frame the way the master list writes them.</summary>
    private static byte[] ServerAdd(
        int flags, string name, string mode, int max, int current, string address, params string[] games)
    {
        var body = new List<byte>();

        void Number(int value)
        {
            var remaining = (uint)value;
            while (remaining >= 0x80)
            {
                body.Add((byte)(remaining | 0x80));
                remaining >>= 7;
            }

            body.Add((byte)remaining);
        }

        void Text(string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            Number(bytes.Length);
            body.AddRange(bytes);
        }

        Number(0); // ServerAdd
        Number(flags);
        Text(name);
        Text(mode);
        Number(max);
        Number(current);
        Text(address);
        Number(games.Length);

        foreach (var game in games)
        {
            Text(game);
        }

        return body.ToArray();
    }

    /// <summary>
    /// The real thing, only with --live. Reports what came back rather than
    /// passing or failing on it: whether a stranger's server is up today says
    /// nothing about this code.
    /// </summary>
    private static void AskTheRealList()
    {
        Console.WriteLine();
        Console.WriteLine($"  asking {ServerListing.Endpoint} ...");

        var (servers, error) = ServerListing
            .FetchAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();

        if (error is not null)
        {
            Console.WriteLine($"    no list: {error}");
            Console.WriteLine("    the launcher falls back to the client's own browser here.");
            return;
        }

        Console.WriteLine($"    {servers.Count} server(s) up:");

        foreach (var server in servers.Take(15))
        {
            var marks = string.Join(" ", new[]
            {
                server.Locked ? "locked" : null,
                server.Official ? "official" : null,
                string.Join("/", server.Games),
            }.Where(m => !string.IsNullOrEmpty(m)));

            Console.WriteLine(
                $"      {server.Address,-24} {server.Players,3}/{server.MaxPlayers,-3} {server.Name,-32} {marks}");
        }
    }

    /// <summary>The same session, with the game standing on another version.</summary>
    private static Session On(Session session, string version) => new()
    {
        Install = session.Install! with { Version = KnownVersions.Resolve(version) },
        Found = session.Found,
        Environment = session.Environment,
        Catalog = session.Catalog,
        CacheRoot = session.CacheRoot,
    };

    private static void Report(string label, bool ok)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");

        if (!ok)
        {
            Failures.Add(label);
        }
    }

    /// <summary>
    /// A game directory that is not one, so that nothing real is touched. The
    /// version comes out unknown, which is a state the pages have to survive
    /// anyway - it is what a tester with an unrecognised build sees.
    /// </summary>
    private static Session BuildSession(string catalogDirectory)
    {
        var root = Path.Combine(Path.GetTempPath(), "mliv-ui-smoke");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, InstallInspector.ExecutableName), "not a game");

        var install = new InstallInspector().Inspect(
            new InstallCandidate(root, GamePlatform.RockstarLauncher, "ui smoke test"));

        return new Session
        {
            Install = install,
            Found = [install],
            Environment = SystemEnvironmentProbe.Probe(),
            Catalog = RecipeCatalog.LoadFrom(catalogDirectory, CatalogTrust.RequireSignature),
            CacheRoot = Path.Combine(root, "cache"),
        };
    }

    /// <summary>Collects what WPF says about bindings while a page is built.</summary>
    private sealed class Collector : TraceListener
    {
        public List<string> Lines { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Lines.Add(message.Trim());
            }
        }
    }
}
