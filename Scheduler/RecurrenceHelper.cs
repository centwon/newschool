using System;
using System.Collections.Generic;

namespace NewSchool.Scheduler;

internal enum RepeatKind
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly
}

/// <summary>
/// 반복 일정의 발생 날짜 계산 (순수 로직 — UI·Settings 비의존, 테스트 가능).
/// </summary>
internal static class RecurrenceHelper
{
    /// <summary>
    /// <paramref name="anchorDate"/>(원본 시작일) 기준으로 반복 발생 날짜 목록을 만든다.
    ///
    /// 핵심: 매번 직전 값에 AddMonths/AddYears 를 누적하면(예: 1/31 → 2/28 → 3/28 …)
    /// 월말·윤년에서 28일 등으로 고착되는 드리프트가 생긴다. 그래서 항상 <paramref name="anchorDate"/>
    /// 에 offset 을 더해 계산한다(1/31 → 2/28 → 3/31 → 4/30 …, 2/29 → 다음 윤년엔 2/29 복귀).
    /// </summary>
    /// <param name="anchorDate">원본 시작일(날짜만; 시각 제외)</param>
    /// <param name="endDate">이 날짜까지 포함</param>
    /// <param name="kind">반복 종류</param>
    /// <param name="maxCount">안전 상한(런어웨이 방지)</param>
    public static List<DateTime> GenerateDates(
        DateTime anchorDate, DateTime endDate, RepeatKind kind, int maxCount = 365)
    {
        var dates = new List<DateTime>();
        anchorDate = anchorDate.Date;

        if (kind == RepeatKind.None)
        {
            dates.Add(anchorDate);
            return dates;
        }

        var current = anchorDate;
        int count = 0;
        while (current <= endDate.Date && count < maxCount)
        {
            dates.Add(current);
            count++;

            // 항상 앵커에서 offset 을 더해 드리프트 방지
            current = kind switch
            {
                RepeatKind.Daily => anchorDate.AddDays(count),
                RepeatKind.Weekly => anchorDate.AddDays(7 * count),
                RepeatKind.Monthly => anchorDate.AddMonths(count),
                RepeatKind.Yearly => anchorDate.AddYears(count),
                _ => endDate.Date.AddDays(1) // 루프 종료
            };
        }

        return dates;
    }
}
