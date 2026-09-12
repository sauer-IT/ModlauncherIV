using System.Windows;
using System.Windows.Threading;

namespace ModlauncherIV.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A program in the middle of swapping hundreds of files in the game
        // directory must not vanish without a word. Whoever ends up here should
        // at least know where they stand — and that the backups are there.
        DispatcherUnhandledException += OnUnhandled;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"The wizard hit an unexpected error:\n\n{e.Exception.Message}\n\n"
            + "Nothing in the game was left half-changed — every recipe cleans up "
            + "after itself on failure. The backups are under %LOCALAPPDATA%\\ModlauncherIV.",
            "Modlauncher IV",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
