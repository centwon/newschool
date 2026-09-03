using System;
using System.Globalization;

namespace NewSchool;

/// <summary>
/// 프로젝트 전체에서 사용할 DateTime 헬퍼 클래스
/// ISO 8601/RFC 3339 형식으로 통일
/// </summary>
public static class DateTimeHelper
{
    // 학기 시작일 기준 주차를 세던 WeekNumber 는 호출부가 없어 지웠다(2026-09-04).
    // 연간 수업 계획의 주차 표시에 쓰려던 것인데, 그 화면은 계획 행이 들고 있는
    // 주차 값을 그대로 쓴다(날짜에서 되계산하지 않는다).

    /// <summary>
    /// 그 날짜가 속한 학기(1 또는 2). **1학기 = 3~8월, 2학기 = 9~다음해 2월**.
    ///
    /// <para>규칙을 화면마다 적지 말 것 — 실제로 어긋나 있었다. 초기 설정 창은 이 규칙을
    /// 그대로 썼는데 누가기록 입력 상자만 <c>Month &lt;= 6</c> 이라, <b>7·8월에 쓴 1학기
    /// 기록이 2학기로, 1·2월에 쓴 2학기 기록이 1학기로</b> 기본 선택됐다. 기록 조회는 학기로
    /// 거르므로 그렇게 저장된 기록은 제 학기 목록에서 사라진다.</para>
    ///
    /// <para>정본은 <see cref="Services.WeeklyHoursCalculator.DefaultSemesterRange"/> 다
    /// (1학기 3월 시작·2학기 9월 시작을 테스트가 고정해 두었다). 여기서는 그 관례를
    /// "날짜 → 학기" 방향으로만 쓴다. 실제 학기 구간은 학사일정에서 유추하는 쪽이 정확하므로,
    /// 시수 계산처럼 구간이 필요한 곳은 <c>ResolveSemesterRange</c> 를 쓴다.</para>
    /// </summary>
    public static int SemesterOf(DateTime date) => date.Month is >= 3 and <= 8 ? 1 : 2;

    /// <summary>
    /// 그 날짜가 속한 <b>학년도</b>. 학년도는 3월에 시작하므로 1·2월은 지난해 학년도다.
    ///
    /// <para>NEIS 학사일정을 내려받는 곳들이 저마다 <c>DateTime.Today.Year</c> 를 그대로
    /// 학년도로 넘겼는데, 1·2월에는 그것이 아직 시작하지도 않은 학년도라 조회가 비었다.</para>
    /// </summary>
    public static int SchoolYearOf(DateTime date) => date.Month >= 3 ? date.Year : date.Year - 1;

    /// <summary>
    /// 그 날짜가 속한 주의 월요일. 주 단위 시간표가 어느 주를 펼칠지 정하는 기준이다.
    /// (수업 홈의 내 시간표와 주간 시간표 화면이 각자 같은 코드를 들고 있었다 — 한 벌로 모은다.)
    /// </summary>
    public static DateTime MondayOf(DateTime date)
    {
        var monday = date.Date;
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(-1);
        return monday;
    }

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
    /// DateTimeOffset을 표준 문자열로 변환
    /// </summary>
    public static string ToStandardString(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.UtcDateTime.ToString(STANDARD_FORMAT, CultureInfo.InvariantCulture);
    }

    // 변환 넷(ToDateString·ToNeisDateString·NowToStandardString·UtcNowToStandardString)은
    // 호출부가 없어 지웠다(39차). 화면·리포지토리는 필요한 자리에서 직접 서식을 지정한다.
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
            "yyyy-MM-dd HH:mm:ss"              // 기본 형식
        };

        // 형식별로 파싱 시도
        // 성공 시 로그는 남기지 않음 — 리포지토리 대량 로드에서 행마다 호출되어
        // Debug.WriteLine 이 심각한 병목이 됐었음(실패만 로그)
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateTimeString, format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var result))
            {
                // UTC → Local 시간으로 변환
                return result.ToLocalTime();
            }
        }

        // 날짜만 있는 형식은 UTC 가정 없이 그대로 파싱
        // (AssumeUniversal 로 처리하면 로컬 변환 시 시각이 붙고, 음수 오프셋 시간대에선 날짜가 밀림)
        string[] dateOnlyFormats = { DATE_ONLY_FORMAT, NEIS_DATE_FORMAT };
        foreach (var format in dateOnlyFormats)
        {
            if (DateTime.TryParseExact(dateTimeString, format,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            {
                return dateOnly;
            }
        }

        // 폴백: 일반 파싱 시도
        if (DateTime.TryParse(dateTimeString,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var fallbackResult))
        {
            return fallbackResult.ToLocalTime();
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

        // 폴백 — 이 파일의 다른 파싱과 같이 InvariantCulture 로 통일한다.
        // 여기만 현재 문화권을 쓰고 있어서, 로캘에 따라 "01/02/2026" 의 월·일 해석이
        // 달라질 수 있었다(DB 에 들어간 값은 문화권과 무관한데도).
        if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var fallback))
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

    // 같은 날 비교(IsSameDay)와 하루의 시작(ToStartOfDay)은 호출부가 없어 지웠다(39차) —
    // 코드에서는 `.Date` 를 직접 비교한다.

    /// <summary>
    /// 오늘인지 확인
    /// </summary>
    public static bool IsToday(DateTime dateTime)
    {
        return dateTime.Date == DateTime.Today;
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
