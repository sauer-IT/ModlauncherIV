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
            Title = "Ordner mit GTAIV.exe auswählen",
            Multiselect = false,
        };

        if (dialog.ShowDialog() == true)
        {
            step.AddManually(dialog.FolderName);
        }
    }
}
