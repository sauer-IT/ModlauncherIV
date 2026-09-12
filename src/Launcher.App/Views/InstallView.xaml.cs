using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ModlauncherIV.App;

public partial class InstallView : UserControl
{
    public InstallView() => InitializeComponent();

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (DataContext is not InstallStep step)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Pick the folder containing GTAIV.exe",
            Multiselect = false,
        };

        if (dialog.ShowDialog() == true)
        {
            step.AddManually(dialog.FolderName);
        }
    }
}
