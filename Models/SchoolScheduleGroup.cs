using System;
using System.Collections.Generic;
using System.Linq;

namespace NewSchool.Models;

/// <summary>
/// 학사일정 그룹 (연속된 날짜를 하나로 묶음)
/// </summary>
[WinRT.GeneratedBindableCustomProperty]
public sealed partial class SchoolScheduleGroup
{
    public string EventName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsVacation { get; set; }
    public bool IsToday { get; set; }

    /// <summary>
    /// 표시용 날짜 문자열
    /// </summary>
    public string DateText
    {
        get
        {
            if (StartDate == EndDate)
            {
                return $"{StartDate:M월 d일}: {EventName}";
            }
            else
            {
                return $"{StartDate:M월 d일}~{EndDate:M월 d일}: {EventName}";
            }
        }
    }
}

/// <summary>
/// 학사일정 그룹화 헬퍼
/// </summary>
public static class SchoolScheduleGroupHelper
{
    /// <summary>
    /// 학사일정을 그룹화하여 연속된 날짜를 하나로 묶음
    /// </summary>
    public static List<SchoolScheduleGroup> GroupSchedules(List<SchoolSchedule> schedules)
    {
        if (schedules == null || schedules.Count == 0)
            return new List<SchoolScheduleGroup>();

        var today = DateTime.Today;
        
        // 행사명별로 그룹화 후 날짜 범위 계산
        var grouped = schedules
            .OrderBy(x => x.EVENT_NM)
            .ThenBy(x => x.AA_YMD)
            .GroupBy(x => x.EVENT_NM)
            .SelectMany(g =>
            {
                var ranges = GetDateRanges(g.Select(x => x.AA_YMD).OrderBy(d => d).ToList());
                var isVacation = g.Any(x => x.SBTR_DD_SC_NM?.Contains("휴") == true);
                
                return ranges.Select(r => new SchoolScheduleGroup
                {
                    EventName = g.Key,
                    StartDate = r.StartDate,
                    EndDate = r.EndDate,
                    IsVacation = isVacation,
                    IsToday = today >= r.StartDate && today <= r.EndDate
                });
            })
            .OrderBy(r => r.StartDate)
            .ThenBy(r => r.EventName)
            .ToList();

        return grouped;
    }

    /// <summary>
    /// 연속된 날짜를 범위로 묶음
    /// </summary>
    private static List<DateRange> GetDateRanges(List<DateTime> dates)
    {
        if (dates.Count == 0)
            return new List<DateRange>();

        var ranges = new List<DateRange>();
        DateTime startDate = dates[0];
        DateTime endDate = dates[0];

        for (int i = 1; i < dates.Count; i++)
        {
            if ((dates[i] - endDate).TotalDays == 1)
            {
                // 연속된 날짜
                endDate = dates[i];
            }
            else
            {
                // 연속되지 않음 - 이전 범위 저장
                ranges.Add(new DateRange { StartDate = startDate, EndDate = endDate });
                startDate = dates[i];
                endDate = dates[i];
            }
        }

        // 마지막 범위 추가
        ranges.Add(new DateRange { StartDate = startDate, EndDate = endDate });

        return ranges;
    }

    private class DateRange
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
