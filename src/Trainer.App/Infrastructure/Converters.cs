using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Trainer.Core.Models;

namespace Trainer.App.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value switch { bool x => x, null => false, string s => s.Length > 0, int i => i != 0, _ => true };
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WorkoutStatus s ? Palette.Status(s) : Palette.Planned;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class KindBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WorkoutKind k ? Palette.Kind(k) : Palette.Z2;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PhaseBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Phase p ? Palette.Phase(p) : Palette.Planned;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Seconds or TimeSpan → "1:30".</summary>
public sealed class DurationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ts = value switch { int s => TimeSpan.FromSeconds(s), TimeSpan t => t, double h => TimeSpan.FromHours(h), _ => TimeSpan.Zero };
        return Format.Duration(ts);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class Format
{
    public static string Duration(TimeSpan ts) => ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}" : $"{ts.Minutes} min";
    public static string Duration(int seconds) => Duration(TimeSpan.FromSeconds(seconds));
}
