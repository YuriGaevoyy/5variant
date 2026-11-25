using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DeliveryService.Core;

namespace DeliveryService.Desktop;

/// <summary>Цветовая кодировка этапов: янтарный — в пути, спокойные тона для остальных.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            ShipmentStatus.InTransit => "#F2A93B",
            ShipmentStatus.Delivered => "#2E7D6B",
            ShipmentStatus.Accepted => "#3B7EA1",
            ShipmentStatus.Returned => "#9A6B72",
            _ => "#64798C"
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ShipmentStatus status ? StatusNames.Russian(status) : value.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class EmptyTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count == 0 ? "Нет отправлений по текущему фильтру" : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = value is true;
        if (parameter as string == "Invert")
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
