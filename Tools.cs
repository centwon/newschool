using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace NewSchool;

/// <summary>
/// WinUI 3 + Native AOT 호환 유틸리티 클래스
/// </summary>
public partial class Tool
{
    /// <summary>
    /// 비동기 대기 (WinUI 3용 - DispatcherFrame 대체)
    /// </summary>
    public static async Task WaitAsync(double milliseconds)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(milliseconds));
    }

    /// <summary>
    /// 파일 크기 포맷
    /// </summary>
    public static string FormatSize(long bytes)
    {
        string[] suffixes = { "Bytes", "KB", "MB", "GB", "TB", "PB" };

        int counter = 0;
        decimal number = bytes;
        while (number >= 1024 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }

        return $"({number:n1}{suffixes[counter]})";
    }

    /// <summary>
    /// NEIS 바이트 카운트 (영문 1byte, 한글 3byte)
    /// </summary>
    [GeneratedRegex("[a-zA-Z]")]
    private static partial Regex Abc();

    [GeneratedRegex("[가-\ud7af\u3130-\u318f]")]
    private static partial Regex Ganada();

    public static int CountNeisByte(string str)
    {
        int byteCount = 0;
        foreach (char ch in str)
        {
            if (Abc().IsMatch(ch.ToString()))
            {
                byteCount += 1;
            }
            else if (Ganada().IsMatch(ch.ToString()))
            {
                byteCount += 3;
            }
            else
            {
                byteCount += 1;
            }
        }
        return byteCount;
    }

    /// <summary>
    /// 인터넷 연결 확인
    /// </summary>
    public static bool IsConnected()
    {
        try
        {
            return Dns.GetHostEntry("www.google.com") != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 숫자 여부 확인
    /// </summary>
    public static bool IsNumeric(string text)
    {
        return NumRegex().IsMatch(text);
    }

    [GeneratedRegex("[^0-9]+")]
    private static partial Regex NumRegex();

    /// <summary>
    /// 비주얼 트리에서 부모 찾기
    /// </summary>
    public static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);

        if (parentObject == null) return null;

        if (parentObject is T parent)
        {
            return parent;
        }
        else
        {
            return FindVisualParent<T>(parentObject);
        }
    }

    /// <summary>
    /// 비주얼 트리에서 자식 찾기
    /// </summary>
    public static T? FindVisualChild<T>(DependencyObject? depObj, string childName) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T t)
                {
                    if (string.IsNullOrWhiteSpace(childName))
                    {
                        return t;
                    }
                    else if ((child as FrameworkElement)?.Name == childName)
                    {
                        return t;
                    }
                }
                T? childItem = FindVisualChild<T>(child, childName);
                if (childItem != null) return childItem;
            }
        }
        return null;
    }

    /// <summary>
    /// 월의 주차 계산
    /// </summary>
    public static int GetWeekOfMonth(
        DateTime date,
        CalendarWeekRule rule = CalendarWeekRule.FirstDay,
        DayOfWeek firstday = DayOfWeek.Sunday)
    {
        DateTime firstdayofmonth = new(date.Year, date.Month, 1);
        Calendar calenderCalc = CultureInfo.CurrentCulture.Calendar;

        return calenderCalc.GetWeekOfYear(date, rule, firstday) -
               calenderCalc.GetWeekOfYear(firstdayofmonth, rule, firstday) + 1;
    }
}

#region Value Converters (WinUI 3용 - string language 사용)

// 1️⃣ 2024 → 2024학년도
public partial class YearToAcademicYearConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int year)
            return $"{year}학년도";
        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is string s && s.EndsWith("학년도"))
        {
            if (int.TryParse(s.Replace("학년도", ""), out int result))
                return result;
        }
        return value;
    }
}

// 2️⃣ 1 → 1학년
public partial class GradeConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int grade)
            return $"{grade}학년";
        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is string s && s.EndsWith("학년"))
        {
            if (int.TryParse(s.Replace("학년", ""), out int result))
                return result;
        }
        return value;
    }
}

// 3️⃣ 1 → 1반
public partial class ClassConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int number)
            return $"{number}반";
        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is string s && s.EndsWith("반"))
        {
            if (int.TryParse(s.Replace("반", ""), out int result))
                return result;
        }
        return value;
    }
}




/// <summary>
/// HTML을 평문으로 변환 (HtmlAgilityPack 제거 버전)
/// </summary>
public partial class HtmlToPlainTextConverter : IValueConverter
{
    // GeneratedRegex를 클래스 레벨에서 static 필드로 선언
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex SimpleHtmlRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTagRegex();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string html)
        {
            // ⚠️ HtmlAgilityPack 대신 간단한 정규식 사용
            // Native AOT 호환성을 위해 리플렉션 없는 방식 사용
            var text = BrTagRegex().Replace(html, Environment.NewLine);
            text = SimpleHtmlRegex().Replace(text, "");
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 날짜를 문자열로 변환
/// </summary>
public partial class DateStringConverter : IValueConverter
{
    public object Convert(object sourceValue, Type targetType, object parameter, string language)
    {
        if (sourceValue is null) { return string.Empty; }

        DateTime dt;
        if (sourceValue is DateTime time1)
        {
            dt = time1;
        }
        else if (sourceValue is DateTimeOffset offset)
        {
            dt = offset.ToLocalTime().DateTime;
        }
        else
        {
            return string.Empty;
        }

        int day = (dt.Date - DateTime.Today).Days;
        string date;
        string weekday = dt.ToString("ddd");

        switch (day)
        {
            case -2:
                date = "그제";
                break;
            case -1:
                date = "어제";
                break;
            case 0:
                date = "오늘";
                break;
            case 1:
                date = "내일";
                break;
            case 2:
                date = "모레";
                break;
            default:
                date = dt.ToString("M. d");
                break;
        }

        string time = dt.Hour + dt.Minute == 0 ? string.Empty : dt.ToString("HH:mm");
        return $"{date}({weekday}) {time}";
    }

    public object? ConvertBack(object sourceValue, Type targetType, object parameter, string language)
    {
        string date = (string)sourceValue;
        if (date.Contains("오늘"))
        {
            date = date.Replace("오늘 ", "");

            // 변수를 미리 선언
            if (!int.TryParse(date.AsSpan(0, 2), null, out int hour))
            {
                return null;
            }

            if (!int.TryParse(date.AsSpan(3, 2), null, out int minute))
            {
                return null;
            }

            return DateTime.Today.AddHours(hour).AddMinutes(minute);
        }
        else
        {
            // 변수를 미리 선언
            if (!int.TryParse(date.AsSpan(0, 2), null, out int month))
            {
                return null;
            }

            if (!int.TryParse(date.AsSpan(3, 2), null, out int day))
            {
                return null;
            }

            return new DateTime(DateTime.Today.Year, month, day);
        }
    }
}

/// <summary>
/// 파일 크기 포맷 변환
/// </summary>
public partial class SizeFormatConverter : IValueConverter
{
    public object Convert(object sourceValue, Type targetType, object parameter, string language)
    {
        if (sourceValue is null) { return "알 수 없음"; }

        var bytes = (int)sourceValue;
        string[] suffixes = { "Bytes", "KB", "MB", "GB", "TB", "PB" };

        var counter = 0;
        long number = bytes;
        while (number >= 1024 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }

        return $"({number:n1}{suffixes[counter]})";
    }

    public object? ConvertBack(object sourceValue, Type targetType, object parameter, string language)
    {
        if (sourceValue is string sizeString)
        {
            string[] suffixes = { "Bytes", "KB", "MB", "GB", "TB", "PB" };
            foreach (var suffix in suffixes)
            {
                if (sizeString.Contains(suffix))
                {
                    var numberPart = sizeString.Replace(suffix, "").Trim('(', ')', ' ');
                    if (long.TryParse(numberPart, out var number))
                    {
                        int index = Array.IndexOf(suffixes, suffix);
                        return (long)(number * Math.Pow(1024, index));
                    }
                }
            }
        }
        return null;
    }
}

/// <summary>
/// UTC를 Local 시간으로 변환
/// </summary>
public partial class UtcToLocal : IValueConverter
{
    public object Convert(object sourceValue, Type targetType, object parameter, string language)
    {
        if (sourceValue is not null and DateTime)
        {
            DateTime dt = (DateTime)sourceValue;
            return dt.ToLocalTime();
        }
        else
        {
            return DateTime.MinValue;
        }
    }

    public object? ConvertBack(object sourceValue, Type targetType, object parameter, string language)
    {
        if (sourceValue is not null and DateTime)
        {
            DateTime dt = (DateTime)sourceValue;
            return dt.ToUniversalTime();
        }
        else
        {
            return DateTime.MinValue.ToUniversalTime();
        }
    }
}

/// <summary>
/// 날짜를 짧은 문자열로 변환
/// </summary>
public partial class DateToShortString : IValueConverter
{
    public object Convert(object sourceValue, Type targetType, object parameter, string language)
    {
        if (sourceValue is not null and DateTime)
        {
            DateTime dt = (DateTime)sourceValue;
            return dt.ToString("M.d. ddd HH:mm");
        }
        else
        {
            return string.Empty;
        }
    }

    public object ConvertBack(object sourceValue, Type targetType, object parameter, string language)
    {
        return sourceValue is string @string ? DateTime.Parse(@string) : new DateTime();
    }
}

/// <summary>
/// NEIS 바이트 카운트 표시
/// </summary>
public partial class NeisByteConverter : IValueConverter
{
    public object? Convert(object sourceValue, Type targetType, object parameter, string language)
    {
        if (sourceValue is not null and string)
        {
            return $"{Tool.CountNeisByte((string)sourceValue)}Byte\r\n{((string)sourceValue).Length}자";
        }
        else
        {
            return null;
        }
    }

    public object ConvertBack(object sourceValue, Type targetType, object parameter, string language)
    {
        return sourceValue;
    }
}

/// <summary>
/// DateTime을 DateTimeOffset으로 변환
/// </summary>
public partial class DateTimeToDateTimeOffsetConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dateTime)
        {
            return (DateTimeOffset)dateTime;
        }
        return null;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.DateTime;
        }
        return null;
    }
}

/// <summary>
/// bool 반전 변환
/// </summary>
public sealed partial class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool b ? !b : false;
    }
}

#endregion
