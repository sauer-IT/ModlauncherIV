using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ModlauncherIV.App;

/// <summary>True blendet ein, False blendet aus und nimmt keinen Platz weg.</summary>
public sealed class ShowConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Umgekehrt: True blendet aus.</summary>
public sealed class HideConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Zeigt an, was tatsächlich etwas enthält. Leere Zeichenketten zählen als
/// nichts — sonst hinterließe ein leerer Hinweis eine Lücke im Layout, die
/// aussieht, als fehlte dort etwas.
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
