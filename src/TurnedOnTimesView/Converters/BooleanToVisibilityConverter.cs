using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TurnedOnTimesView.Converters;

/// <summary>
/// 布尔值到可见性的转换器
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 反向转换（true -> Hidden, false -> Visible）
    /// </summary>
    public bool IsReversed { get; set; }

    /// <summary>
    /// 使用 Hidden 而不是 Collapsed
    /// </summary>
    public bool UseHidden { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
            return Visibility.Collapsed;

        var shouldShow = IsReversed ? !boolValue : boolValue;
        
        if (shouldShow)
            return Visibility.Visible;
        
        return UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Visibility visibility)
            return false;

        var isVisible = visibility == Visibility.Visible;
        return IsReversed ? !isVisible : isVisible;
    }
}

/// <summary>
/// 静态实例，方便在XAML中使用
/// </summary>
public static class BooleanToVisibilityConverters
{
    /// <summary>
    /// 标准转换器：true -> Visible, false -> Collapsed
    /// </summary>
    public static BooleanToVisibilityConverter Default { get; } = new();

    /// <summary>
    /// 反向转换器：true -> Collapsed, false -> Visible
    /// </summary>
    public static BooleanToVisibilityConverter Reversed { get; } = new() { IsReversed = true };

    /// <summary>
    /// Hidden转换器：true -> Visible, false -> Hidden
    /// </summary>
    public static BooleanToVisibilityConverter Hidden { get; } = new() { UseHidden = true };
}