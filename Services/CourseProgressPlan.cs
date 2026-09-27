using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 수업 하나의 진도 계획 — 단원, 학급별 실제 수업 칸, 진도 기록, 학급별 전망을 한 번에 읽는다.
///
/// <para>진도표(<c>ProgressMatrixView</c>)와 수업 홈 시간표의 칸 메뉴가 같은 것을 본다.
/// 둘이 따로 모으면 "진도표에서는 2-2 가 진행 중인데 홈에서는 2-3 이 진행 중" 같은 어긋남이
/// 생긴다. 계산 규칙은 <see cref="ProgressPlanner"/> 에 있고, 여기는 재료를 DB 에서 모은다
/// — 시수표(<c>CourseHoursView</c>)와 같은 재료다.</para>
/// </summary>
public sealed class CourseProgressPlan
{
    public Course Course { get; }
    public IReadOnlyList<CourseSection> Sections { get; }
    public IReadOnlyList<string> Rooms { get; }

    /// <summary>학급 → 실제 수업 칸(날짜·교시 순)</summary>
    public IReadOnlyDictionary<string, List<LessonSlot>> Slots { get; }

    /// <summary>(단원 No, 학급) → 진도 기록</summary>
    public IReadOnlyDictionary<(int SectionNo, string Room), LessonProgress> Progress { get; }

    /// <summary>학급 → 전망</summary>
    public IReadOnlyDictionary<string, RoomForecast> Forecasts { get; }

    /// <summary>교시 ↔ 완료 시각 변환에 쓴 시정</summary>
    public PeriodTimes Times { get; }

    private CourseProgressPlan(
        Course course,
        IReadOnlyList<CourseSection> sections,
        IReadOnlyList<string> rooms,
        IReadOnlyDictionary<string, List<LessonSlot>> slots,
        IReadOnlyDictionary<(int, string), LessonProgress> progress,
        IReadOnlyDictionary<string, RoomForecast> forecasts,
        PeriodTimes times)
    {
        Course = course;
        Sections = sections;
        Rooms = rooms;
        Slots = slots;
        Progress = progress;
        Forecasts = forecasts;
        Times = times;
    }

    /// <summary>
    /// 그 학급에서 지금 할 차례인 단원 — 마지막으로 완료한 단원의 다음. 다 끝났으면 null.
    /// </summary>
    public CourseSection? CurrentSection(string room)
    {
        if (!Forecasts.TryGetValue(room, out var forecast)) return Sections.FirstOrDefault();

        return Sections.FirstOrDefault(s =>
            !forecast.Sections.TryGetValue(s.No, out var cell) || cell.Kind != SectionForecastKind.Done);
    }

    /// <summary>
    /// 그 학급의 지난 수업 칸(오늘까지) — 최근 것부터. 완료 날짜를 고르는 목록이다.
    /// 교시를 모르는 칸(손으로 늘린 시수)은 뺀다.
    /// </summary>
    public IReadOnlyList<LessonSlot> PastSlots(string room, DateTime today, int max = 12)
        => Slots.TryGetValue(room, out var list)
            ? list.Where(s => !s.IsExtra && s.Date <= today.Date)
                  .OrderByDescending(s => s.Date).ThenByDescending(s => s.Period)
                  .Take(max)
                  .ToList()
            : [];

    /// <summary>완료 시각 → 교시</summary>
    public int PeriodOf(DateTime stamp) => ProgressPlanner.PeriodOfStamp(stamp, Times);

    /// <summary>
    /// 수업 하나의 진도 계획을 읽는다.
    /// </summary>
    public static async Task<CourseProgressPlan> LoadAsync(Course course, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(course);

        List<SchoolSchedule> schedules = [];
        var schoolCode = Settings.SchoolCode.Value;
        if (!string.IsNullOrEmpty(schoolCode) && course.Year > 0)
        {
            try
            {
                using var repo = new SchoolScheduleRepository(SchoolDatabase.DbPath);
                schedules = await repo.GetBySchoolYearAsync(schoolCode, course.Year);
            }
            catch (Exception ex)
            {
                // 학사일정이 없으면 휴업일을 빼지 못할 뿐 계산은 된다(시수표와 같다).
                NewSchool.Logging.Log.Warning("CourseProgressPlan", $"학사일정을 읽지 못해 휴업일을 빼지 않는다: {ex.Message}");
            }
        }

        var range = WeeklyHoursCalculator.ResolveSemesterRange(course.Year, course.Semester, schedules);

        List<CourseSection> sections;
        using (var repo = new CourseSectionRepository(SchoolDatabase.DbPath))
            sections = await repo.GetByCourseAsync(course.No);

        List<Lesson> lessons;
        using (var repo = new LessonRepository(SchoolDatabase.DbPath))
            lessons = await repo.GetByCourseAsync(course.No);

        Dictionary<(string Room, DateTime WeekStart), CourseWeeklyHours> adjustments;
        using (var repo = new CourseWeeklyHoursRepository(SchoolDatabase.DbPath))
            adjustments = await repo.GetByCourseAsync(course.No);

        List<LessonChange> changes;
        using (var repo = new LessonChangeRepository(SchoolDatabase.DbPath))
            changes = await repo.GetRangeAsync(Settings.User.Value, range.Start, range.End);

        var progress = new Dictionary<(int, string), LessonProgress>();
        using (var repo = new LessonProgressRepository(SchoolDatabase.DbPath))
        {
            foreach (var row in await repo.GetByCourseAsync(course.No))
                progress[(row.CourseSectionId, row.Room)] = row;
        }

        int gradeCount = await SchoolProfile.GetGradeCountAsync();

        var weeks = WeeklyHoursCalculator.Calculate(course, lessons, schedules, range.Start, range.End, gradeCount);
        var slots = ProgressPlanner.BuildSlots(course, lessons, changes, schedules, weeks, adjustments, gradeCount);
        var rooms = WeeklyHoursCalculator.ResolveRooms(course, lessons);
        var times = PeriodTimes.FromSettings();

        var forecasts = new Dictionary<string, RoomForecast>();
        foreach (var room in rooms)
        {
            var roomProgress = sections
                .Where(s => progress.ContainsKey((s.No, room)))
                .ToDictionary(s => s.No, s => progress[(s.No, room)]);

            forecasts[room] = ProgressPlanner.Forecast(
                room, sections, roomProgress, slots.GetValueOrDefault(room) ?? [], today,
                stamp => ProgressPlanner.PeriodOfStamp(stamp, times));
        }

        return new CourseProgressPlan(course, sections, rooms, slots, progress, forecasts, times);
    }

    /// <summary>
    /// 완료로 표시한다 — 교시가 있으면 완료 시각에 담는다(<see cref="ProgressPlanner.Stamp"/>).
    /// </summary>
    public static async Task<bool> MarkCompletedAsync(int sectionNo, string room, DateTime date, int period)
    {
        using var repo = new LessonProgressRepository(SchoolDatabase.DbPath);
        return await repo.MarkAsCompletedAsync(sectionNo, room,
            ProgressPlanner.Stamp(date, period, PeriodTimes.FromSettings()));
    }

    /// <summary>완료를 취소한다.</summary>
    public static async Task<bool> MarkIncompleteAsync(int sectionNo, string room)
    {
        using var repo = new LessonProgressRepository(SchoolDatabase.DbPath);
        return await repo.MarkAsIncompleteAsync(sectionNo, room);
    }
}
