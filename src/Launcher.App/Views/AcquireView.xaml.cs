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

        // The folder does not necessarily exist yet - Explorer would otherwise
        // get a path into nowhere and report an error that looks like ours.
        Directory.CreateDirectory(step.CacheFolder);

        Process.Start(new ProcessStartInfo
        {
            FileName = step.CacheFolder,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Opens the page a file has to be downloaded from by hand.
    ///
    /// The address comes out of the signed catalog and is checked again here:
    /// https only. A button that starts a program with whatever a text field
    /// said would be the one place in this launcher where that is possible.
    /// </summary>
    private void OnOpenPage(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SourceRow row
            || !Uri.TryCreate(row.Page, UriKind.Absolute, out var page)
            || page.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        Diary.Info($"Opening the download page for {row.FileName}: {page}");

        Process.Start(new ProcessStartInfo
        {
            FileName = page.AbsoluteUri,
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
            MessageBox.Show(error.Message, "Acquisition failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
