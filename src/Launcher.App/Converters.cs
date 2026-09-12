using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ModlauncherIV.App;

/// <summary>True shows it, False hides it and takes up no space.</summary>
public sealed class ShowConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The other way round: true hides it.</summary>
public sealed class HideConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows what actually contains something. Empty strings count as nothing —
/// otherwise an empty note would leave a gap in the layout that looks like
/// something is missing there.
/// </summary>
public sealed class HasContentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => Visibility.Collapsed,
        string s => string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible,
        int n => n > 0 ? Visibility.Visible : Visibility.Collapsed,
        _ => Visibility.Visible,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
