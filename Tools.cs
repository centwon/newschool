using System;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Data;

namespace NewSchool;

/// <summary>
/// WinUI 3 + Native AOT 호환 유틸리티 클래스
/// </summary>
public partial class Tool
{
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
}

#region Value Converters (WinUI 3용 - string language 사용)

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

// DateTimeToDateTimeOffsetConverter는 CommonConverters.cs에서 정의

#endregion
