using System.Windows;
using System.Windows.Threading;

namespace ModlauncherIV.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ein Programm, das gerade hunderte Dateien im Spielverzeichnis
        // austauscht, darf nicht wortlos verschwinden. Wer hier landet, soll
        // wenigstens wissen, woran er ist - und dass die Sicherungen liegen.
        DispatcherUnhandledException += OnUnhandled;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Der Assistent ist auf einen unerwarteten Fehler gestoßen:\n\n{e.Exception.Message}\n\n"
            + "Am Spiel wurde nichts halb verändert — jedes Rezept räumt bei einem "
            + "Fehlschlag selbst auf. Die Sicherungen liegen unter %LOCALAPPDATA%\\ModlauncherIV.",
            "Modlauncher IV",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
