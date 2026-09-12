using System.Windows;
using System.Windows.Threading;

namespace ModlauncherIV.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Diary.Start();

        // The uninstall path, as "Apps & features" invokes it. Before anything
        // else is built: there is no installation to show once this has run, and
        // the main window would have nothing to say.
        if (e.Args.Any(a => string.Equals(a, Uninstall.Switch, StringComparison.OrdinalIgnoreCase)))
        {
            Diary.Info("Started to uninstall itself.");
            Uninstall.Run();
            Shutdown();
            return;
        }

        // A program in the middle of swapping hundreds of files in the game
        // directory must not vanish without a word. Whoever ends up here should
        // at least know where they stand — and that the backups are there.
        DispatcherUnhandledException += OnUnhandled;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Diary.Crash(e.Exception, "the window");

        MessageBox.Show(
            $"The wizard hit an unexpected error:\n\n{e.Exception.Message}\n\n"
            + "Nothing in the game was left half-changed — every recipe cleans up "
            + "after itself on failure. The backups are under %LOCALAPPDATA%\\ModlauncherIV.\n\n"
            + $"What led up to this is written down here, and it is the useful thing to send:\n{Diary.File}",
            "Modlauncher IV",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
