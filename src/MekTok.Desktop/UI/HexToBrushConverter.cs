using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MekTok.Desktop.UI;

public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && Color.TryParse(hex, out Color color))
        {
            return new SolidColorBrush(color);
        }

        return new SolidColorBrush(Color.Parse("#848B9B"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

