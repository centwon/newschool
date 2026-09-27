using System;
using System.Collections.Generic;
using System.Linq;
using NewSchool.Helpers;
using NewSchool.Models;

namespace NewSchool.Services;

/// <summary>한 학급의 실제 수업 한 칸 — 날짜와 교시.</summary>
/// <param name="Period">교시. <see cref="ProgressPlanner.ExtraPeriod"/> 면 손으로 늘린 시수라 교시를 모른다.</param>
public readonly record struct LessonSlot(DateTime Date, int Period, string Room)
{
    /// <summary>시수표에서 손으로 늘린 시간 — 어느 교시인지 모른다.</summary>
    public bool IsExtra => Period == ProgressPlanner.ExtraPeriod;

    /// <summary>"9/24 (목) 3교시"</summary>
    public string Display => IsExtra
        ? $"{Date:M/d} ({"일월화수목금토"[(int)Date.DayOfWeek]}) 추가 시수"
        : $"{Date:M/d} ({"일월화수목금토"[(int)Date.DayOfWeek]}) {Period}교시";
}

/// <summary>진도표 한 칸의 상태.</summary>
public enum SectionForecastKind
{
    /// <summary>끝났다 — 날짜가 기록됐거나, 뒤의 단원이 완료로 표시돼 끝난 것으로 본다.</summary>
    Done,
    /// <summary>아직이다 — 예정일이 있다.</summary>
    Planned,
    /// <summary>남은 시수로는 학기 안에 닿지 못한다.</summary>
    Unscheduled,
}

/// <summary>진도표 한 칸(단원 × 학급).</summary>
public sealed class SectionForecast
{
    public SectionForecastKind Kind { get; init; }

    /// <summary>완료한 날(기록된 경우) 또는 예정일. 날짜 없이 끝난 것으로 본 칸이면 null.</summary>
    public DateTime? Date { get; init; }

    /// <summary>완료를 기록한 교시(모르면 0).</summary>
    public int Period { get; init; }

    /// <summary>이 학급에서 마지막으로 완료 표시한 단원이다.</summary>
    public bool IsBaseline { get; init; }

    /// <summary>예정일이 이미 지났다 — 표시를 놓쳤거나 늦어지는 중이다.</summary>
    public bool IsOverdue { get; init; }

    /// <summary>완료 기록이 실제로 있는 칸이다(없으면 앞 단원이라 끝난 것으로 본 칸).</summary>
    public bool IsRecorded { get; init; }
}

/// <summary>한 학급의 진도 전망.</summary>
public sealed class RoomForecast
{
    public string Room { get; init; } = string.Empty;

    /// <summary>단원 No → 칸</summary>
    public Dictionary<int, SectionForecast> Sections { get; init; } = [];

    /// <summary>마지막으로 완료한 단원 뒤로 남은 수업 시간</summary>
    public int RemainingHours { get; init; }

    /// <summary>마지막으로 완료한 단원 뒤로 남은 단원들의 예상 차시 합</summary>
    public int RemainingUnits { get; init; }

    /// <summary>남은 시간 − 남은 차시. 음수면 모자란다.</summary>
    public int Balance => RemainingHours - RemainingUnits;
}

/// <summary>
/// 진도 예정일 계산 — 학급마다 <b>실제 수업 칸</b>을 늘어놓고, 마지막으로 완료 표시한 단원 다음부터
/// 단원별 예상 차시만큼 칸을 소모해 예정일을 낸다.
///
/// <para><b>마지막 표시 기준.</b> 진도는 단원 순서대로 나가므로, 학급마다 완료로 표시한 단원 가운데
/// <b>가장 뒤의 것</b>까지는 모두 끝난 것으로 본다(표시를 놓친 앞 단원도). 그 앞 단원의 완료는
/// 저장하지 않는다 — 뒤의 표시를 취소하면 앞 단원도 원래대로 돌아가야 한다.</para>
///
/// <para><b>수업 칸</b>은 시수표와 같은 주차(방학을 뺀) 안에서, 그 수업 학년의 수업일에 정기 배치
/// (<see cref="Lesson"/>)를 놓고 그 날의 시간표 변경(<see cref="LessonChange"/>: 휴강·교체·보강·대강)을
/// 얹어 만든다. 시수표에서 손으로 고친 주는 그 수로 맞춘다 — 줄였으면 그 주 뒤쪽 칸을 빼고,
/// 늘렸으면 교시를 모르는 칸(<see cref="ExtraPeriod"/>)을 그 주 마지막 수업일에 붙인다.</para>
///
/// <para>DB 를 모르는 순수 함수다. 예정일은 저장하지 않고 볼 때마다 계산한다 — 시간표나 학사일정이
/// 바뀌면 저장해 둔 예정일은 그 순간 거짓이 된다(시수표의 자동값과 같은 원칙).</para>
/// </summary>
public static class ProgressPlanner
{
    /// <summary>손으로 늘린 시수의 교시 자리 — 그 날 실제 교시들보다 뒤로 정렬되게 큰 값.</summary>
    public const int ExtraPeriod = 99;

    #region 수업 칸

    /// <summary>
    /// 학급별 실제 수업 칸(날짜·교시 순). 학급 열은 <see cref="WeeklyHoursCalculator.ResolveRooms"/> 와 같다.
    /// </summary>
    /// <param name="lessons">이 수업의 정기 배치(다른 수업의 것은 넣지 않는다)</param>
    /// <param name="weeks">시수표의 주차(<see cref="WeeklyHoursCalculator.Calculate"/>) — 방학이 잘려 있다</param>
    /// <param name="changes">그 교사의 시간표 변경(학기 범위). 다른 수업의 변경도 섞여 있어도 된다.</param>
    /// <param name="adjustments">시수표에서 손으로 고친 칸 — (학급, 주 시작일) → 시수</param>
    public static Dictionary<string, List<LessonSlot>> BuildSlots(
        Course course,
        IReadOnlyCollection<Lesson> lessons,
        IReadOnlyCollection<LessonChange> changes,
        IReadOnlyCollection<SchoolSchedule> schedules,
        IReadOnlyList<WeeklyHoursWeek> weeks,
        IReadOnlyDictionary<(string Room, DateTime WeekStart), CourseWeeklyHours> adjustments,
        int gradeCount = 0)
    {
        ArgumentNullException.ThrowIfNull(course);

        var rooms = WeeklyHoursCalculator.ResolveRooms(course, lessons);
        var result = rooms.ToDictionary(r => r, _ => new List<LessonSlot>());

        var changesByDate = (changes ?? [])
            .Where(c => c != null)
            .GroupBy(c => c.Date.Date)
            .ToDictionary(g => g.Key, g => g.GroupBy(c => c.Period).ToDictionary(p => p.Key, p => p.Last()));

        foreach (var week in weeks)
        {
            var weekSlots = rooms.ToDictionary(r => r, _ => new List<LessonSlot>());
            DateTime? lastTeachingDay = null;

            for (var date = week.StartDate.Date; date <= week.EndDate.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (!SchoolCalendar.IsTeachingDayFor(date, schedules ?? [], course.Grade, gradeCount)) continue;

                lastTeachingDay = date;
                int dayOfWeek = SchoolCalendar.ToLessonDayOfWeek(date);

                // 교시 → 이 수업의 학급
                var byPeriod = new Dictionary<int, string>();
                foreach (var lesson in lessons)
                {
                    if (lesson.DayOfWeek != dayOfWeek) continue;
                    byPeriod[lesson.Period] = RoomOf(lesson.Room);
                }

                if (changesByDate.TryGetValue(date, out var dayChanges))
                {
                    foreach (var (period, change) in dayChanges)
                    {
                        // 휴강·대강·다른 수업으로 교체 → 이 교시에 이 수업은 없다
                        byPeriod.Remove(period);

                        // 교체·보강·강의실 바꾸기로 이 수업이 들어온다
                        if (!change.IsCancellation && !change.IsSubstitute && change.CourseNo == course.No)
                            byPeriod[period] = RoomOf(change.Room);
                    }
                }

                foreach (var (period, room) in byPeriod.OrderBy(p => p.Key))
                {
                    if (weekSlots.TryGetValue(room, out var list))
                        list.Add(new LessonSlot(date, period, room));
                }
            }

            foreach (var room in rooms)
            {
                var list = weekSlots[room];

                if (adjustments != null
                    && adjustments.TryGetValue((room, week.StartDate), out var adjustment))
                {
                    int want = Math.Max(0, adjustment.PlannedHours);
                    if (list.Count > want)
                    {
                        list.RemoveRange(want, list.Count - want);
                    }
                    else if (list.Count < want && lastTeachingDay != null)
                    {
                        while (list.Count < want)
                            list.Add(new LessonSlot(lastTeachingDay.Value, ExtraPeriod, room));
                    }
                }

                result[room].AddRange(list);
            }
        }

        return result;
    }

    private static string RoomOf(string? room)
        => string.IsNullOrWhiteSpace(room) ? WeeklyHoursCalculator.UnassignedRoom : room;

    #endregion

    #region 예정

    /// <summary>
    /// 한 학급의 진도 전망.
    /// </summary>
    /// <param name="sections">단원들(진도 순서대로)</param>
    /// <param name="progress">이 학급의 진도 기록 — 단원 No → 기록</param>
    /// <param name="slots">이 학급의 실제 수업 칸(<see cref="BuildSlots"/>)</param>
    /// <param name="today">예정일이 지났는지 가를 날</param>
    /// <param name="periodOf">완료 시각 → 교시(모르면 0). <see cref="PeriodOfStamp"/> 를 쓴다.</param>
    public static RoomForecast Forecast(
        string room,
        IReadOnlyList<CourseSection> sections,
        IReadOnlyDictionary<int, LessonProgress> progress,
        IReadOnlyList<LessonSlot> slots,
        DateTime today,
        Func<DateTime, int> periodOf)
    {
        var cells = new Dictionary<int, SectionForecast>();

        // ① 마지막으로 완료 표시한 단원
        int baseline = -1;
        for (int i = 0; i < sections.Count; i++)
        {
            if (progress.TryGetValue(sections[i].No, out var p) && p.IsCompleted)
                baseline = i;
        }

        // ② 그 앞은 모두 끝난 것으로 본다. 이어서 셀 자리는 날짜가 기록된 완료 가운데 가장 늦은 것.
        DateTime? fromDate = null;
        int fromPeriod = 0;

        for (int i = 0; i <= baseline; i++)
        {
            var section = sections[i];
            progress.TryGetValue(section.No, out var p);
            bool recorded = p?.IsCompleted == true;

            DateTime? date = recorded ? p!.CompletedDate?.Date : null;
            int period = recorded && p!.CompletedDate.HasValue ? periodOf(p.CompletedDate.Value) : 0;

            cells[section.No] = new SectionForecast
            {
                Kind = SectionForecastKind.Done,
                Date = date,
                Period = period,
                IsBaseline = i == baseline,
                IsRecorded = recorded,
            };

            if (date != null && (fromDate == null || (date.Value, period).CompareTo((fromDate.Value, fromPeriod)) > 0))
            {
                fromDate = date;
                fromPeriod = period;
            }
        }

        // ③ 이어서 셀 칸 — 기준 시각 뒤. 교시를 모르면 그 날 칸은 모두 쓴 것으로 본다.
        var remaining = slots
            .Where(s => fromDate == null
                        || s.Date > fromDate.Value
                        || (s.Date == fromDate.Value && fromPeriod > 0 && s.Period > fromPeriod))
            .ToList();

        // ④ 남은 단원에 칸을 차례로 나눠 준다
        int cursor = 0;
        int remainingUnits = 0;
        bool exhausted = false;
        DateTime? lastPlanned = fromDate;

        for (int i = baseline + 1; i < sections.Count; i++)
        {
            var section = sections[i];
            int need = Math.Max(0, section.EstimatedHours);
            remainingUnits += need;

            if (!exhausted && cursor + need <= remaining.Count)
            {
                DateTime? date = need > 0
                    ? remaining[cursor + need - 1].Date
                    : lastPlanned ?? (remaining.Count > cursor ? remaining[cursor].Date : null);
                cursor += need;
                lastPlanned = date;

                cells[section.No] = date == null
                    ? new SectionForecast { Kind = SectionForecastKind.Unscheduled }
                    : new SectionForecast
                    {
                        Kind = SectionForecastKind.Planned,
                        Date = date,
                        IsOverdue = date.Value < today.Date,
                    };
            }
            else
            {
                exhausted = true;
                cells[section.No] = new SectionForecast { Kind = SectionForecastKind.Unscheduled };
            }
        }

        return new RoomForecast
        {
            Room = room,
            Sections = cells,
            RemainingHours = remaining.Count,
            RemainingUnits = remainingUnits,
        };
    }

    #endregion

    #region 완료 시각 ↔ 교시

    /// <summary>
    /// 완료를 교시와 함께 기록할 시각 — 그 날 그 교시가 시작하고 1분 뒤.
    ///
    /// <para>진도 기록에는 교시 칸이 없다. <c>CompletedDate</c> 가 시각까지 담으므로 교시 시작 시각을
    /// 넣어 두고, 읽을 때 <see cref="PeriodOfStamp"/> 로 되돌린다. 1분을 더하는 것은 경계에서
    /// 앞 교시로 읽히지 않게 하려는 것이다. 교시를 모르면 날짜만 넣는다.</para>
    /// </summary>
    public static DateTime Stamp(DateTime date, int period, PeriodTimes times)
    {
        if (period <= 0 || period == ExtraPeriod) return date.Date;
        return date.Date + Functions.PeriodStartTime(period, times) + TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// <see cref="Stamp"/> 의 반대 — 기록된 시각이 몇 교시였나(모르면 0).
    /// ⚠ 기록한 뒤에 시정(교시 길이 등)을 바꾸면 어긋날 수 있다. 날짜는 그대로 맞다.
    /// </summary>
    public static int PeriodOfStamp(DateTime stamp, PeriodTimes times)
    {
        if (stamp.TimeOfDay == TimeSpan.Zero) return 0;
        return Functions.GetPeriodAt(stamp.TimeOfDay, (int)stamp.DayOfWeek, times).Index;
    }

    #endregion
}
