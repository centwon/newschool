using System;
using Microsoft.UI.Xaml.Data;

namespace NewSchool;

/// <summary>
/// WinUI 3 + Native AOT 호환 유틸리티 클래스
/// </summary>
// class Tool 은 통째로 지웠다(39차). 담고 있던 FormatSize·CountNeisByte 둘 다 호출부가 없었다.
// NEIS 바이트 세기는 NeisHelper.CountSpecBytes 가 맡는다(영역별 규칙까지 본다).

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
