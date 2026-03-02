using System;
using System.Globalization;

namespace NewSchool;

/// <summary>
/// 프로젝트 전체에서 사용할 DateTime 헬퍼 클래스
/// ISO 8601/RFC 3339 형식으로 통일
/// </summary>
public static class DateTimeHelper
{
    #region 상수 정의
    /// <summary>
    /// DB 저장 및 API 통신용 표준 형식 (ISO 8601/RFC 3339)
    /// </summary>
    public const string STANDARD_FORMAT = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>
    /// 날짜만 저장할 때 사용
    /// </summary>
    public const string DATE_ONLY_FORMAT = "yyyy-MM-dd";

    /// <summary>
    /// NEIS API용 날짜 형식
    /// </summary>
    public const string NEIS_DATE_FORMAT = "yyyyMMdd";

    /// <summary>
    /// 사용자 표시용 날짜 시간 형식
    /// </summary>
    public const string DISPLAY_DATETIME_FORMAT = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// 사용자 표시용 날짜 형식
    /// </summary>
    public const string DISPLAY_DATE_FORMAT = "yyyy-MM-dd";

    /// <summary>
    /// 사용자 표시용 시간 형식
    /// </summary>
    public const string DISPLAY_TIME_FORMAT = "HH:mm:ss";
    #endregion

    #region DateTime → String 변환 (저장용)

    /// <summary>
    /// DateTime을 표준 문자열로 변환 (DB 저장, API 통신용)
    /// UTC로 변환 후 RFC 3339 형식으로 저장
    /// </summary>
    public static string ToStandardString(DateTime dateTime)
    {
        try
        {
            // Local 또는 Unspecified → UTC로 변환
            var utcTime = dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Local).ToUniversalTime(),
                _ => dateTime.ToUniversalTime()
            };

            return utcTime.ToString(STANDARD_FORMAT, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DateTimeHelper] ToStandardString 오류: {ex.Message}");
            return DateTime.MinValue.ToString(STANDARD_FORMAT, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// DateTime을 날짜 문자열로 변환
    /// </summary>
    public static string ToDateString(DateTime dateTime)
    {
        return dateTime.ToString(DATE_ONLY_FORMAT, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// DateTime을 NEIS API용 형식으로 변환
    /// </summary>
    public static string ToNeisDateString(DateTime dateTime)
    {
        return dateTime.ToString(NEIS_DATE_FORMAT, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// DateTimeOffset을 표준 문자열로 변환
    /// </summary>
    public static string ToStandardString(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.UtcDateTime.ToString(STANDARD_FORMAT, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 현재 시간을 표준 문자열로 변환
    /// </summary>
    public static string NowToStandardString()
    {
        return ToStandardString(DateTime.Now);
    }

    /// <summary>
    /// UTC 현재 시간을 표준 문자열로 변환
    /// </summary>
    public static string UtcNowToStandardString()
    {
        return DateTime.UtcNow.ToString(STANDARD_FORMAT, CultureInfo.InvariantCulture);
    }
    #endregion

    #region String → DateTime 변환 (로드용)

    /// <summary>
    /// 문자열을 DateTime으로 변환 (DB 로드, API 응답 파싱용)
    /// 다양한 형식 자동 인식 및 Local 시간으로 변환
    /// </summary>
    public static DateTime FromString(string dateTimeString)
    {
        if (string.IsNullOrWhiteSpace(dateTimeString))
        {
            System.Diagnostics.Debug.WriteLine($"[DateTimeHelper] FromString: 빈 문자열");
            return DateTime.MinValue;
        }

        // 지원하는 날짜 형식들 (우선순위 순)
        string[] formats = new[]
        {
            STANDARD_FORMAT,                    // RFC 3339 (표준)
            "yyyy-MM-ddTHH:mm:ssZ",            // RFC 3339 (초 단위)
            "yyyy-MM-ddTHH:mm:ss.ffffffZ",     // RFC 3339 (마이크로초)
            "yyyy-MM-ddTHH:mm:ss.fffffffZ",    // RFC 3339 (나노초)
            "yyyy-MM-dd HH:mm:ss.FFFFFFF",     // 레거시 DB 형식
            "yyyy-MM-dd HH:mm:ss.FFFFFF",
            "yyyy-MM-dd HH:mm:ss.FFFFF",
            "yyyy-MM-dd HH:mm:ss.FFFF",
            "yyyy-MM-dd HH:mm:ss.FFF",
            "yyyy-MM-dd HH:mm:ss.FF",
            "yyyy-MM-dd HH:mm:ss.F",
            "yyyy-MM-dd HH:mm:ss",             // 기본 형식
            DATE_ONLY_FORMAT,                  // 날짜만
            NEIS_DATE_FORMAT                   // NEIS API 형식
        };

        // 형식별로 파싱 시도
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateTimeString, format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var result))
            {
                // UTC → Local 시간으로 변환
                var localTime = result.ToLocalTime();
                System.Diagnostics.Debug.WriteLine($"[DateTimeHelper] 변환 성공: '{dateTimeString}' → {localTime:yyyy-MM-dd HH:mm:ss} (형식: {format})");
                return localTime;
            }
        }

        // 폴백: 일반 파싱 시도
        if (DateTime.TryParse(dateTimeString,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var fallbackResult))
        {
            var localTime = fallbackResult.ToLocalTime();
            System.Diagnostics.Debug.WriteLine($"[DateTimeHelper] 폴백 파싱: '{dateTimeString}' → {localTime:yyyy-MM-dd HH:mm:ss}");
            return localTime;
        }

        System.Diagnostics.Debug.WriteLine($"[DateTimeHelper] 변환 실패: '{dateTimeString}'");
        return DateTime.MinValue;
    }

    /// <summary>
    /// 날짜 문자열을 DateTime으로 변환
    /// </summary>
    public static DateTime FromDateString(string dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
            return DateTime.MinValue;

        // 날짜 형식들
        string[] dateFormats = new[]
        {
            DATE_ONLY_FORMAT,     // yyyy-MM-dd
            NEIS_DATE_FORMAT,     // yyyyMMdd
            "yyyy/MM/dd",
            "yyyy.MM.dd"
        };

        foreach (var format in dateFormats)
        {
            if (DateTime.TryParseExact(dateString, format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result))
            {
                return result;
            }
        }

        // 폴백
        if (DateTime.TryParse(dateString, out var fallback))
            return fallback;

        return DateTime.MinValue;
    }
    #endregion

    #region 유틸리티 메서드

    /// <summary>
    /// DateTime이 유효한지 확인
    /// </summary>
    public static bool IsValid(DateTime dateTime)
    {
        return dateTime != DateTime.MinValue && dateTime != DateTime.MaxValue;
    }

    /// <summary>
    /// 두 날짜가 같은 날인지 확인
    /// </summary>
    public static bool IsSameDay(DateTime date1, DateTime date2)
    {
        return date1.Date == date2.Date;
    }

    /// <summary>
    /// 오늘인지 확인
    /// </summary>
    public static bool IsToday(DateTime dateTime)
    {
        return dateTime.Date == DateTime.Today;
    }

    /// <summary>
    /// 날짜를 하루의 시작 시간으로 설정 (00:00:00)
    /// </summary>
    public static DateTime ToStartOfDay(DateTime dateTime)
    {
        return dateTime.Date;
    }

    /// <summary>
    /// 날짜를 하루의 끝 시간으로 설정 (23:59:59.999)
    /// </summary>
    public static DateTime ToEndOfDay(DateTime dateTime)
    {
        return dateTime.Date.AddDays(1).AddMilliseconds(-1);
    }

    /// <summary>
    /// 사용자 표시용 문자열 변환
    /// </summary>
    public static string ToDisplayString(DateTime dateTime, bool includeTime = true)
    {
        if (!IsValid(dateTime))
            return string.Empty;

        if (includeTime)
        {
            return dateTime.ToString(DISPLAY_DATETIME_FORMAT, CultureInfo.CurrentCulture);
        }
        else
        {
            return dateTime.ToString(DISPLAY_DATE_FORMAT, CultureInfo.CurrentCulture);
        }
    }

    /// <summary>
    /// 상대 시간 표시 (예: "5분 전", "어제", "3일 전")
    /// </summary>
    public static string ToRelativeTimeString(DateTime dateTime)
    {
        var now = DateTime.Now;
        var span = now - dateTime;

        if (span.TotalMinutes < 1)
            return "방금 전";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}분 전";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}시간 전";
        if (span.TotalDays < 2)
            return "어제";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays}일 전";
        if (span.TotalDays < 30)
            return $"{(int)(span.TotalDays / 7)}주 전";
        if (span.TotalDays < 365)
            return $"{(int)(span.TotalDays / 30)}개월 전";

        return $"{(int)(span.TotalDays / 365)}년 전";
    }
    #endregion
}

/// <summary>
/// DateTime 확장 메서드
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>
    /// DateTime을 표준 문자열로 변환
    /// </summary>
    public static string ToStandardString(this DateTime dateTime)
    {
        return DateTimeHelper.ToStandardString(dateTime);
    }

    /// <summary>
    /// DateTimeOffset을 표준 문자열로 변환
    /// </summary>
    public static string ToStandardString(this DateTimeOffset dateTimeOffset)
    {
        return DateTimeHelper.ToStandardString(dateTimeOffset);
    }

    /// <summary>
    /// 사용자 표시용 문자열 변환
    /// </summary>
    public static string ToDisplayString(this DateTime dateTime, bool includeTime = true)
    {
        return DateTimeHelper.ToDisplayString(dateTime, includeTime);
    }

    /// <summary>
    /// 상대 시간 표시
    /// </summary>
    public static string ToRelativeTimeString(this DateTime dateTime)
    {
        return DateTimeHelper.ToRelativeTimeString(dateTime);
    }
}
