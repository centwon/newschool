using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace NewSchool.Converters;

/// <summary>
/// Boolean을 색상으로 변환 (휴일 - 빨간색)
/// </summary>
public partial class BoolToVacationColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isVacation && isVacation)
        {
            return new SolidColorBrush(Colors.Red);
        }
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Boolean을 배경색으로 변환 (오늘 - 연한 분홍색)
/// </summary>
public partial class BoolToTodayBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isToday && isToday)
        {
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 182, 193)); // LightPink
        }
        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 240, 240, 240)); // SubtleFillColorSecondary
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 빈 문자열을 Visibility로 변환 (비어있으면 Collapsed)
/// </summary>
public partial class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string str && !string.IsNullOrWhiteSpace(str))
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
