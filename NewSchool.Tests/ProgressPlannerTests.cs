using System;
using System.Collections.Generic;
using System.Linq;
using NewSchool.Models;
using NewSchool.Services;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 진도 예정일 계산(<see cref="ProgressPlanner"/>) — 학급별 실제 수업 칸과 "마지막 표시 기준" 전망.
/// </summary>
public class ProgressPlannerTests
{
    private static readonly PeriodTimes Std = new(
        DayStarting: new TimeSpan(8, 30, 0),
        AssemblyTime: TimeSpan.FromMinutes(10),
        BreakTime: TimeSpan.FromMinutes(10),
        OnePeriod: TimeSpan.FromMinutes(50),
        LunchTime: TimeSpan.FromMinutes(60));

    // 2026-03-02 는 월요일
    private static readonly DateTime Mon1 = new(2026, 3, 2);

    private static Course Course() =>
        new() { No = 1, Grade = 3, Subject = "역사", Unit = 3, Rooms = "3-1,3-2" };

    /// <summary>월=1 … 금=5</summary>
    private static Lesson Lesson(int dayOfWeek, int period, string room) =>
        new() { Course = 1, DayOfWeek = dayOfWeek, Period = period, Room = room };

    private static List<WeeklyHoursWeek> Weeks(Course course, IReadOnlyCollection<Lesson> lessons,
        IReadOnlyCollection<SchoolSchedule> schedules, int weekCount = 2)
        => WeeklyHoursCalculator.Calculate(course, lessons, schedules, Mon1, Mon1.AddDays(7 * weekCount - 3));

    private static Dictionary<string, List<LessonSlot>> Slots(
        IReadOnlyCollection<Lesson> lessons,
        IReadOnlyCollection<LessonChange>? changes = null,
        IReadOnlyCollection<SchoolSchedule>? schedules = null,
        Dictionary<(string, DateTime), CourseWeeklyHours>? adjustments = null,
        int weekCount = 2)
    {
        var course = Course();
        schedules ??= [];
        return ProgressPlanner.BuildSlots(course, lessons, changes ?? [], schedules,
            Weeks(course, lessons, schedules, weekCount), adjustments ?? []);
    }

    // 3-1: 월 1교시·수 3교시 / 3-2: 화 2교시
    private static readonly Lesson[] Regular =
    [
        Lesson(1, 1, "3-1"),
        Lesson(3, 3, "3-1"),
        Lesson(2, 2, "3-2"),
    ];

    #region 수업 칸

    [Fact]
    public void 정기_배치가_학급별_날짜_교시로_펼쳐진다()
    {
        var slots = Slots(Regular);

        Assert.Equal(
            [(Mon1, 1), (Mon1.AddDays(2), 3), (Mon1.AddDays(7), 1), (Mon1.AddDays(9), 3)],
            slots["3-1"].Select(s => (s.Date, s.Period)));
        Assert.Equal(2, slots["3-2"].Count);
    }

    [Fact]
    public void 휴업일에는_칸이_없다()
    {
        var holiday = new SchoolSchedule
        {
            AA_YMD = Mon1.AddDays(2), SBTR_DD_SC_NM = "휴업일", EVENT_NM = "재량휴업일"
        };

        var slots = Slots(Regular, schedules: [holiday]);

        Assert.DoesNotContain(slots["3-1"], s => s.Date == Mon1.AddDays(2));
        Assert.Equal(3, slots["3-1"].Count);
    }

    [Fact]
    public void 시간표_변경을_얹는다_휴강_보강_대강()
    {
        var changes = new List<LessonChange>
        {
            // 첫 월요일 1교시 휴강
            new() { Date = Mon1, Period = 1 },
            // 첫 금요일 5교시 3-1 보강
            new() { Date = Mon1.AddDays(4), Period = 5, CourseNo = 1, Room = "3-1" },
            // 첫 화요일 2교시 — 남의 수업 대강이라 내 3-2 수업은 없다
            new() { Date = Mon1.AddDays(1), Period = 2, SubjectText = "국어", Room = "2-4" },
            // 다른 수업의 보강은 이 수업과 상관없다
            new() { Date = Mon1.AddDays(3), Period = 4, CourseNo = 2, Room = "1-1" },
        };

        var slots = Slots(Regular, changes);

        Assert.DoesNotContain(slots["3-1"], s => s.Date == Mon1 && s.Period == 1);
        Assert.Contains(slots["3-1"], s => s.Date == Mon1.AddDays(4) && s.Period == 5);
        Assert.DoesNotContain(slots["3-2"], s => s.Date == Mon1.AddDays(1));
        Assert.DoesNotContain(slots["3-1"], s => s.Date == Mon1.AddDays(3));
    }

    [Fact]
    public void 시수표에서_고친_주는_그_수로_맞춘다()
    {
        var adjustments = new Dictionary<(string, DateTime), CourseWeeklyHours>
        {
            // 3-1 둘째 주 2 → 1: 그 주 뒤쪽(수요일) 칸을 뺀다
            [("3-1", Mon1.AddDays(7))] = new() { Room = "3-1", WeekStart = Mon1.AddDays(7), PlannedHours = 1 },
            // 3-2 첫 주 1 → 3: 교시를 모르는 칸 둘을 그 주 마지막 수업일에 붙인다
            [("3-2", Mon1)] = new() { Room = "3-2", WeekStart = Mon1, PlannedHours = 3 },
        };

        var slots = Slots(Regular, adjustments: adjustments);

        Assert.DoesNotContain(slots["3-1"], s => s.Date == Mon1.AddDays(9));
        Assert.Contains(slots["3-1"], s => s.Date == Mon1.AddDays(7));

        var firstWeek32 = slots["3-2"].Where(s => s.Date < Mon1.AddDays(7)).ToList();
        Assert.Equal(3, firstWeek32.Count);
        Assert.Equal(2, firstWeek32.Count(s => s.IsExtra));
        Assert.All(firstWeek32.Where(s => s.IsExtra), s => Assert.Equal(Mon1.AddDays(4), s.Date));
    }

    #endregion

    #region 예정

    private static List<CourseSection> Sections(params int[] hours)
        => hours.Select((h, i) => new CourseSection { No = i + 1, SectionName = $"단원{i + 1}", EstimatedHours = h }).ToList();

    /// <summary>매일 1·2교시가 있는 학급 — 날짜 계산을 눈으로 따라가기 쉽게</summary>
    private static List<LessonSlot> Daily(int days)
    {
        var list = new List<LessonSlot>();
        for (int d = 0; d < days; d++)
        {
            var date = Mon1.AddDays(d);
            list.Add(new LessonSlot(date, 1, "3-1"));
            list.Add(new LessonSlot(date, 2, "3-1"));
        }
        return list;
    }

    private static LessonProgress Done(int sectionNo, DateTime? date, int period = 0) => new()
    {
        CourseSectionId = sectionNo,
        Room = "3-1",
        IsCompleted = true,
        CompletedDate = date == null ? null : ProgressPlanner.Stamp(date.Value, period, Std),
    };

    private static RoomForecast Run(List<CourseSection> sections, IEnumerable<LessonProgress> progress,
        List<LessonSlot> slots, DateTime today)
        => ProgressPlanner.Forecast("3-1", sections, progress.ToDictionary(p => p.CourseSectionId),
            slots, today, stamp => ProgressPlanner.PeriodOfStamp(stamp, Std));

    [Fact]
    public void 기록이_없으면_첫_수업부터_차례로_예정을_잡는다()
    {
        // 칸: 월1 월2 화1 화2 수1 수2 …
        var result = Run(Sections(3, 2), [], Daily(5), today: Mon1);

        Assert.Equal(Mon1.AddDays(1), result.Sections[1].Date);   // 3칸째 = 화1
        Assert.Equal(Mon1.AddDays(2), result.Sections[2].Date);   // 5칸째 = 수1
        Assert.Equal(10, result.RemainingHours);
        Assert.Equal(5, result.RemainingUnits);
        Assert.Equal(5, result.Balance);
    }

    [Fact]
    public void 마지막_표시_앞의_단원은_날짜_없이_끝난_것으로_본다()
    {
        // 1번은 표시를 놓쳤고 2번만 화요일 2교시에 완료로 표시했다
        var result = Run(Sections(3, 2, 2), [Done(2, Mon1.AddDays(1), period: 2)], Daily(5), today: Mon1.AddDays(3));

        var first = result.Sections[1];
        Assert.Equal(SectionForecastKind.Done, first.Kind);
        Assert.Null(first.Date);
        Assert.False(first.IsRecorded);

        var second = result.Sections[2];
        Assert.True(second.IsBaseline);
        Assert.Equal(2, second.Period);

        // 3번은 화요일 2교시 다음 칸(수1)부터 2칸 → 수2
        Assert.Equal(Mon1.AddDays(2), result.Sections[3].Date);
        Assert.True(result.Sections[3].IsOverdue);   // 오늘(목) 이전
        Assert.Equal(6, result.RemainingHours);      // 수·목·금 × 2
        Assert.Equal(2, result.RemainingUnits);
    }

    [Fact]
    public void 같은_날_완료_교시_뒤의_칸부터_센다()
    {
        // 월요일 1교시에 끝냈으면 월요일 2교시가 다음 단원의 첫 시간이다
        var result = Run(Sections(1, 1), [Done(1, Mon1, period: 1)], Daily(2), today: Mon1);

        Assert.Equal(Mon1, result.Sections[2].Date);
        Assert.Equal(3, result.RemainingHours);
    }

    [Fact]
    public void 교시를_모르면_그_날_칸은_모두_쓴_것으로_본다()
    {
        var result = Run(Sections(1, 1), [Done(1, Mon1)], Daily(2), today: Mon1);

        Assert.Equal(Mon1.AddDays(1), result.Sections[2].Date);
        Assert.Equal(2, result.RemainingHours);
    }

    [Fact]
    public void 남은_시수가_모자라면_닿지_못하는_단원은_예정이_없다()
    {
        var result = Run(Sections(2, 3, 4), [], Daily(2), today: Mon1);

        Assert.Equal(SectionForecastKind.Planned, result.Sections[1].Kind);
        Assert.Equal(SectionForecastKind.Unscheduled, result.Sections[2].Kind);
        Assert.Equal(SectionForecastKind.Unscheduled, result.Sections[3].Kind);
        Assert.Equal(-5, result.Balance);             // 4시간 − 9차시
    }

    [Fact]
    public void 날짜_없는_완료가_기준이면_그_앞의_가장_늦은_날짜부터_센다()
    {
        // 1번은 월요일에 완료, 2번은 날짜 없이 완료(예전 기록)
        var result = Run(Sections(1, 1, 1), [Done(1, Mon1), Done(2, null)], Daily(3), today: Mon1);

        Assert.True(result.Sections[2].IsBaseline);
        Assert.Equal(Mon1.AddDays(1), result.Sections[3].Date);
    }

    [Fact]
    public void 뒤의_표시를_취소하면_앞_단원도_원래대로다()
    {
        // 기록은 1번만 남았다 — 2번 칸은 다시 예정이다(앞 단원 완료를 저장하지 않으므로)
        var result = Run(Sections(1, 1), [Done(1, Mon1, period: 1)], Daily(2), today: Mon1);

        Assert.Equal(SectionForecastKind.Planned, result.Sections[2].Kind);
    }

    #endregion

    #region 완료 시각 ↔ 교시

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void 완료_시각에_교시를_담았다가_되읽는다(int period)
    {
        var tue = Mon1.AddDays(1);
        var stamp = ProgressPlanner.Stamp(tue, period, Std);

        Assert.Equal(tue, stamp.Date);
        Assert.Equal(period, ProgressPlanner.PeriodOfStamp(stamp, Std));
    }

    [Fact]
    public void 교시를_모르면_날짜만_남는다()
    {
        var stamp = ProgressPlanner.Stamp(Mon1, 0, Std);

        Assert.Equal(Mon1, stamp);
        Assert.Equal(0, ProgressPlanner.PeriodOfStamp(stamp, Std));
    }

    #endregion
}
