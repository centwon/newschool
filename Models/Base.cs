// ========================================
// 급식(NEIS)과 교시 계산에 쓰는 기초 형식들.
//
// 머리에 SchoolDB.cs 라고 적혀 있었는데 그런 파일은 없다. 여기 있던 학급·수강 배정 모델
// 넷이 재설계로 대체돼 빠지면서(아래 주석) 파일의 내용도 이름도 달라졌다.
// ========================================

using System;

namespace NewSchool.Models;


#region 기초 데이터 형식

/// <summary>
/// 식단정보
/// </summary>
public sealed class SchoolMeal
{
    public string ATPT_OFCDC_SC_CODE { get; set; } = string.Empty; //        1	    ATPT_OFCDC_SC_CODE 시도교육청코드
    public string ATPT_OFCDC_SC_NM { get; set; } = string.Empty;        //2	    ATPT_OFCDC_SC_NM 시도교육청명

    public string SD_SCHUL_CODE { get; set; } = string.Empty;        //3	    SD_SCHUL_CODE 표준학교코드

    public string SCHUL_NM { get; set; } = string.Empty;        //4	    SCHUL_NM 학교명

    public string MMEAL_SC_NM { get; set; } = string.Empty;        //6	    MMEAL_SC_NM 식사명

    public DateTime MLSV_YMD { get; set; } = DateTime.Today;       //7	    MLSV_YMD 급식일자

    public string DDISH_NM { get; set; } = string.Empty;         //9	    DDISH_NM 요리명

    // 한 줄 표시용 DisplayText 는 바인딩도 호출도 없어 지웠다(39차) —
    // 급식 카드는 요리명을 여러 줄로 그대로 보여 준다.

    /// <summary>
    /// 식사 유형별 아이콘
    /// </summary>
    public string MealIcon => MMEAL_SC_NM switch
    {
        "조식" => "🌅",
        "중식" => "☀️",
        "석식" => "🌙",
        _ => "🍽️"
    };

    /// <summary>
    /// 메뉴 텍스트 (줄바꿈 → 쉼표 구분)
    /// </summary>
    public string MenuText => DDISH_NM
        .Replace("\r\n", ", ")
        .Replace("\n", ", ")
        .Replace("\r", ", ");

    //5	    MMEAL_SC_CODE 식사코드
    //8	    MLSV_FGR 급식인원수
    //10	    ORPLC_INFO 원산지정보
    //11	    CAL_INFO 칼로리정보
    //12	    NTR_INFO 영양정보
    //13	    MLSV_FROM_YMD 급식시작일자
    //14	    MLSV_TO_YMD 급식종료일자
}

public class Period
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeSpan Time { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// 요일별 교시 수 (월~금). 학교마다 시정이 달라 설정으로 관리한다.
/// 직렬화 형식: "6,7,6,7,7" (월,화,수,목,금)
/// </summary>
public readonly record struct PeriodCounts(int Mon, int Tue, int Wed, int Thu, int Fri)
{
    /// <summary>기존 하드코딩과 동일한 기본값 (월·수 6교시, 화·목·금 7교시)</summary>
    public static PeriodCounts Default => new(6, 7, 6, 7, 7);

    /// <summary><paramref name="dayOfWeek"/>는 .NET 규칙(0=일 … 6=토). 주말은 0.</summary>
    public int ForDay(int dayOfWeek) => dayOfWeek switch
    {
        1 => Mon, 2 => Tue, 3 => Wed, 4 => Thu, 5 => Fri,
        _ => 0,
    };

    /// <summary>"6,7,6,7,7" 형식 파싱. 형식이 어긋나거나 범위(1~12) 밖이면 기본값.</summary>
    public static PeriodCounts Parse(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized)) return Default;

        var parts = serialized.Split(',');
        if (parts.Length != 5) return Default;

        Span<int> counts = stackalloc int[5];
        for (int i = 0; i < 5; i++)
        {
            if (!int.TryParse(parts[i].Trim(), out counts[i]) || counts[i] < 1 || counts[i] > 12)
                return Default;
        }
        return new PeriodCounts(counts[0], counts[1], counts[2], counts[3], counts[4]);
    }

    public string Serialize() => $"{Mon},{Tue},{Wed},{Thu},{Fri}";
}

/// <summary>
/// 교시 계산에 필요한 시간 설정 묶음. <see cref="Functions.GetPeriodAt"/> 를 Settings·시계에서
/// 분리해 순수 함수로 테스트할 수 있게 한다.
/// </summary>
public readonly record struct PeriodTimes(
    TimeSpan DayStarting,   // 등교(일과 시작) 시각
    TimeSpan AssemblyTime,  // 조례 길이
    TimeSpan BreakTime,     // 쉬는 시간 길이
    TimeSpan OnePeriod,     // 1교시 길이
    TimeSpan LunchTime)     // 점심 시간 길이
{
    /// <summary>요일별 교시 수. 명시하지 않으면 기본 시정(월·수 6, 그 외 7).</summary>
    public PeriodCounts Periods { get; init; } = PeriodCounts.Default;

    public static PeriodTimes FromSettings() => new(
        Settings.DayStarting.Value,
        Settings.AssemblyTime.Value,
        Settings.BreakTime.Value,
        Settings.OnePeriod.Value,
        Settings.LunchTime.Value)
    {
        Periods = PeriodCounts.Parse(Settings.PeriodsPerDay.Value),
    };
}

#endregion

// 이 파일에서 지운 것들 — 넷 다 정의부 말고는 읽는 곳도 쓰는 곳도 한 군데 없었다.
//
// "Core Models" 영역의 ClassAssignment·Subject·CourseAssignment 는 재설계로 대체된
// 이전 세대다. 학급 배정은 Enrollment, 수강 배정은 CourseEnrollment 가 맡는다
// (docs/enrollment-redesign.md). Subject 는 테이블 쪽이 먼저 없어졌고
// (DatabaseInitializer 의 "Subject 테이블은 만들지 않는다") 모델만 남아 있었다 —
// 과목명은 Course.Subject 에 문자열로 들어간다.
//
// Classroom(Grade, Class) 은 학년·반 한 쌍을 담던 그릇인데, 그 한 쌍을 함께 넘길 일이
// 생기면 Enrollment 나 ClassTimetable 처럼 맥락을 가진 형이 이미 있다. 아무것도 뜻하지
// 않는 좌표쌍이 따로 있으면 그쪽으로 새기 쉬워 되살리지 않는다.
