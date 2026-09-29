using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wallup.Views;

/// <summary>Shows a list's "nothing here" note only while the list has no items.</summary>
internal sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
