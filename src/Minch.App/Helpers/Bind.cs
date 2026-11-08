using Microsoft.UI.Xaml;
using Minch.Core.Formatting;

namespace Minch.App.Helpers;

/// <summary>Small pure functions for x:Bind function bindings.</summary>
public static class Bind
{
    public static bool Not(bool value) => !value;
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);
    public static Visibility VisibleIfText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfNone(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfBoth(bool a, bool b) => a && b ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Checked(bool? value) => value == true ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfAndNot(bool a, bool b) => a && !b ? Visibility.Visible : Visibility.Collapsed;
    public static double Opacity(bool dim) => dim ? 0.55 : 1.0;
    public static string Percent(double fraction) => $"{Math.Round(Math.Clamp(fraction, 0, 1) * 100)}%";
    public static string Speed(long bytesPerSecond) => TransferFormat.Speed(bytesPerSecond) ?? "";
    public static string FavoriteGlyph(bool fav) => fav ? "" : "";
    public static string FavoriteLabel(bool fav) => fav ? "Remove from favorites" : "Add to favorites";
    public static string ExpandGlyph(bool expanded) => expanded ? "" : "";
}

/// <summary>Lets x:Bind TwoWay bind ComboBox.SelectedItem (object) to a typed VM property.</summary>
public sealed partial class ObjectPassthroughConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language) => value;
    public object? ConvertBack(object? value, Type targetType, object? parameter, string language) => value;
}
