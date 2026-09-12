using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ModlauncherIV.App;

public partial class AcquireView : UserControl
{
    public AcquireView() => InitializeComponent();

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AcquireStep step)
        {
            return;
        }

        // Der Ordner existiert nicht zwingend schon - der Explorer bekäme sonst
        // einen Pfad ins Leere und meldete einen Fehler, der wie unserer aussieht.
        Directory.CreateDirectory(step.CacheFolder);

        Process.Start(new ProcessStartInfo
        {
            FileName = step.CacheFolder,
            UseShellExecute = true,
        });
    }

    private async void OnRetry(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AcquireStep step)
        {
            return;
        }

        var button = (Button)sender;
        button.IsEnabled = false;

        try
        {
            await step.RetryAsync().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Beschaffung fehlgeschlagen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
