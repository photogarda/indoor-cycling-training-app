using System.Globalization;
using Avalonia.Data.Converters;
using Trainer.Core.Models;

namespace Trainer.Desktop.Infrastructure;

public sealed class StatusBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WorkoutStatus s ? Palette.Status(s) : Palette.Planned;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class KindBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WorkoutKind k ? Palette.Kind(k) : Palette.Z2;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PhaseBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Phase p ? Palette.Phase(p) : Palette.Planned;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True for non-null, non-empty, non-zero values. Used for IsVisible.</summary>
public sealed class HasValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value switch { null => false, bool x => x, string s => s.Length > 0, int i => i != 0, _ => true };
        return parameter is "not" ? !b : b;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class Format
{
    public static string Duration(TimeSpan ts) => ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}" : $"{ts.Minutes} min";
    public static string Duration(int seconds) => Duration(TimeSpan.FromSeconds(seconds));
}
