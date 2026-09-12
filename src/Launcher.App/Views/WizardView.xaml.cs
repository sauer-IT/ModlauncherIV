using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ModlauncherIV.App;

public partial class WizardView : UserControl
{
    public WizardView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WizardViewModel old)
        {
            old.PropertyChanged -= OnWizardChanged;
        }

        if (e.NewValue is WizardViewModel now)
        {
            now.PropertyChanged += OnWizardChanged;
        }
    }

    /// <summary>
    /// Every step starts at the top.
    ///
    /// One ScrollViewer holds all seven pages, so it keeps the offset from the
    /// page before. Coming out of a long one, the next page opens halfway down -
    /// which looks like a page that begins in the middle of a sentence, and on
    /// the mod list it hid both the version choice and everything installable
    /// behind what is not.
    /// </summary>
    private void OnWizardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WizardViewModel.Current))
        {
            Page.ScrollToTop();
        }
    }
}
