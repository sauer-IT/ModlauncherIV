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
        Check("home", new HomeViewModel(session, () => { }));
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
