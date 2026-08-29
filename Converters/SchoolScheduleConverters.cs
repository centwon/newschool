using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace NewSchool.Converters;

/// <summary>
/// 휴업일 여부를 글자색으로. true = 강조색(빨강), false = 본문 기본색.
///
/// <para>예전에는 false 에 <c>Colors.Black</c> 을 하드코딩해, 다크 테마에서 어두운 배경에
/// 검은 글씨가 묻혀 학사일정 날짜가 보이지 않았다. 테마 리소스를 찾아 쓰고, 못 찾으면
/// 두 테마에서 모두 읽히는 회색으로 물러난다.</para>
///
/// <para>⚠ 컨버터는 바인딩 소스(<c>IsVacation</c>)가 바뀔 때만 다시 돈다. 앱을 켠 채 테마를
/// 바꾸면 목록이 다시 그려지기 전까지 이전 색이 남는다. 색을 XAML 의 <c>ThemeResource</c> 로
/// 직접 물리려면 목록 템플릿 구조를 바꿔야 해서 여기서는 손대지 않았다.</para>
/// </summary>
public partial class BoolToVacationColorConverter : IValueConverter
{
    // 테마 리소스를 못 찾았을 때만 쓴다. 회색은 밝은 배경·어두운 배경 양쪽에서 읽힌다.
    private static readonly SolidColorBrush FallbackVacation = new(Colors.Red);
    private static readonly SolidColorBrush FallbackText = new(Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool isVacation && isVacation
            ? ThemeBrush("SystemFillColorCriticalBrush", FallbackVacation)
            : ThemeBrush("TextFillColorPrimaryBrush", FallbackText);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    private static Brush ThemeBrush(string key, Brush fallback)
    {
        try
        {
            var resources = Application.Current?.Resources;
            if (resources != null && resources.TryGetValue(key, out var value) && value is Brush brush)
                return brush;
        }
        catch
        {
            // 리소스 조회 실패 — 폴백으로
        }
        return fallback;
    }
}

// BoolToTodayBackgroundConverter 제거 (2026-08-19): RGB(240,240,240) 을 하드코딩해
//   다크 테마에서 눈부신 회색 상자를 그렸는데, 정작 어떤 바인딩에서도 쓰이지 않는
//   죽은 컨버터였다(선언만 XAML 에 남아 있었다). 오늘 날짜 강조가 필요해지면
//   XAML 에서 ThemeResource 로 붙인다.

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
