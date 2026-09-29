using System.Globalization;
using System.Windows.Data;

namespace Wallup.Views;

/// <summary>
/// Binds a group of radio buttons to one enum setting: each button is checked when the
/// setting equals the name in its ConverterParameter, and checking it sets that value.
/// </summary>
internal sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter as string;

    // Unchecking is the other button being checked, which sets the value on its own.
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Enum.Parse(targetType, (string)parameter) : Binding.DoNothing;
}
