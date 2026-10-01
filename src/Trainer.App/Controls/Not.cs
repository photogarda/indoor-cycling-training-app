using System.Globalization;
using System.Windows.Data;

namespace Trainer.App.Controls;

/// <summary>Boolean negation for bindings.</summary>
public sealed class Not : IValueConverter
{
    public static readonly Not Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
