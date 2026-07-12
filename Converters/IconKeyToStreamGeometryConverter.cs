using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MajdataEdit_Neo.Converters;

public class IconKeyToStreamGeometryConverter : IValueConverter
{
    public static readonly IconKeyToStreamGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current != null)
        {
            if (Application.Current.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var resource))
            {
                return resource;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

