using System;
using System.Collections.Generic;
using System.Linq;
using NewSchool.Helpers;
using NewSchool.Models;

namespace NewSchool.Services;

/// <summary>
/// 주차별 수업 시수 계산기.
///
/// 입력은 이미 읽어온 데이터뿐이다(리포지토리를 들고 있지 않다) — 예전 계산기는 리포지토리 3개를
/// 생성자로 받아서, 화면 없이 계산 규칙만 검증하기가 어려웠다.
///
/// 근거는 <b>교사 본인의 시간표 배치(<see cref="Lesson"/>)</b>다. 학급 시간표(ClassTimetable)가
/// 아니라 Lesson 을 보는 이유는, 이 계산이 "내 수업이 이번 주에 몇 시간 있나"를 답하기 때문이다.
/// 결과는 <b>학급(강의실)별로</b> 쪼개 낸다 — 같은 과목이라도 학급마다 요일이 다르다.
/// </summary>
public static class WeeklyHoursCalculator
{
    /// <summary>강의실을 지정하지 않은 배치가 묶이는 자리</summary>
    public const string UnassignedRoom = "(미지정)";

    /// <summary>
    /// 표에 세울 학급(강의실) 열을 정한다.
    /// 수업에 등록된 강의실 순서를 먼저 따르고, 거기에 없는데 배치에만 있는 강의실을 뒤에 붙인다.
    /// </summary>
    public static List<string> ResolveRooms(Course course, IEnumerable<Lesson> lessons)
    {
        var placed = lessons
            .Select(l => string.IsNullOrWhiteSpace(l.Room) ? UnassignedRoom : l.Room)
            .Distinct()
            .ToList();

        var ordered = new List<string>();

        foreach (var room in course?.RoomList ?? [])
        {
            if (placed.Contains(room))
                ordered.Add(room);
        }

        foreach (var room in placed)
        {
            if (!ordered.Contains(room))
                ordered.Add(room);
        }

        return ordered;
    }

    /// <summary>
    /// 학기를 주 단위(월~일)로 자른다. 평일이 하나도 없는 주는 건너뛴다.
    /// </summary>
    public static List<WeekInfo> GetSemesterWeeks(DateTime start, DateTime end)
    {
        var weeks = new List<WeekInfo>();
        if (end < start) return weeks;

        // 첫 주는 학기 시작일이 속한 주의 월요일부터 센다.
        var weekStart = start.Date;
        while (weekStart.DayOfWeek != DayOfWeek.Monday)
            weekStart = weekStart.AddDays(-1);

        int number = 1;

        while (weekStart <= end.Date)
        {
            var weekEnd = weekStart.AddDays(6);

            // 학기 범위 밖은 잘라낸다 — 첫 주·마지막 주가 통째로 세어지면
            // 개학 전 며칠이 수업 가능일로 잡힌다.
            var effectiveStart = weekStart < start.Date ? start.Date : weekStart;
            var effectiveEnd = weekEnd > end.Date ? end.Date : weekEnd;

            if (HasWeekday(effectiveStart, effectiveEnd))
            {
                weeks.Add(new WeekInfo
                {
                    Number = number++,
                    StartDate = effectiveStart,
                    EndDate = effectiveEnd
                });
            }

            weekStart = weekStart.AddDays(7);
        }

        return weeks;
    }

    /// <summary>
    /// 수업 하나의 주차별·학급별 시수를 계산한다.
    /// </summary>
    /// <param name="course">대상 수업 (학년 정보로 학년 행사를 걸러낸다)</param>
    /// <param name="lessons">그 수업의 정기 배치 (<c>IsRecurring</c> 인 것만 넘길 것)</param>
    /// <param name="schedules">학년도 학사일정</param>
    /// <param name="semesterStart">학기 시작일</param>
    /// <param name="semesterEnd">학기 종료일</param>
    /// <param name="gradeCount">학교의 학년 수(<see cref="SchoolCalendar.GradeCountFor"/>). 0 이면 모름.</param>
    public static List<WeeklyHoursWeek> Calculate(
        Course course,
        IReadOnlyCollection<Lesson> lessons,
        IReadOnlyCollection<SchoolSchedule> schedules,
        DateTime semesterStart,
        DateTime semesterEnd,
        int gradeCount = 0)
    {
        ArgumentNullException.ThrowIfNull(course);

        var rooms = ResolveRooms(course, lessons);

        // (학급, 요일) → 그 요일에 배치된 시간 수
        var perRoomDay = new Dictionary<string, int[]>();
        foreach (var room in rooms)
            perRoomDay[room] = new int[8];

        foreach (var lesson in lessons)
        {
            if (lesson.DayOfWeek is < 1 or > 7) continue;

            var room = string.IsNullOrWhiteSpace(lesson.Room) ? UnassignedRoom : lesson.Room;
            if (perRoomDay.TryGetValue(room, out var days))
                days[lesson.DayOfWeek]++;
        }

        var result = new List<WeeklyHoursWeek>();

        foreach (var week in GetSemesterWeeks(semesterStart, semesterEnd))
        {
            var autoByRoom = rooms.ToDictionary(r => r, _ => 0);
            var events = new List<string>();
            int teachingDays = 0;

            for (var date = week.StartDate; date <= week.EndDate; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                CollectEvents(date, schedules, events);

                if (!SchoolCalendar.IsTeachingDayFor(date, schedules, course.Grade, gradeCount))
                    continue;

                teachingDays++;

                int dayOfWeek = SchoolCalendar.ToLessonDayOfWeek(date);
                foreach (var room in rooms)
                    autoByRoom[room] += perRoomDay[room][dayOfWeek];
            }

            result.Add(new WeeklyHoursWeek
            {
                Number = week.Number,
                StartDate = week.StartDate,
                EndDate = week.EndDate,
                TeachingDays = teachingDays,
                Events = events,
                AutoByRoom = autoByRoom
            });
        }

        return result;
    }

    /// <summary>
    /// 방학으로 볼 최소 공백. 수업일이 이만큼 끊기면 그 앞에서 학기가 끝난 것으로 본다.
    ///
    /// 재량휴업일이 연휴에 붙어도 열흘을 넘기기 어렵고, 방학은 못해도 3주다.
    /// 행사명("여름방학", "방학식")으로 찾지 않는 이유 — 이름은 학교마다 다르지만
    /// "3주 연속 휴업" 은 다르지 않다.
    /// </summary>
    private const int VacationGapDays = 14;

    /// <summary>
    /// 학사일정이 없을 때 쓰는 관례값 (1학기 3월~8월, 2학기 9월~다음해 2월).
    /// 방학이 섞여 있으므로 <see cref="ResolveSemesterRange"/> 가 유추에 실패했을 때만 쓴다.
    /// </summary>
    public static (DateTime Start, DateTime End) DefaultSemesterRange(int year, int semester)
    {
        if (semester == 2)
            return (new DateTime(year, 9, 1), LastDayOfSchoolYear(year));

        return (new DateTime(year, 3, 1), new DateTime(year, 8, 31));
    }

    /// <summary>학년도 마지막 날 = 다음해 2월 말일 (겨울방학의 끝)</summary>
    private static DateTime LastDayOfSchoolYear(int year)
        => new(year + 1, 2, DateTime.DaysInMonth(year + 1, 2));

    /// <summary>
    /// 학기 기간을 학사일정에서 유추한다. 학기는 <b>방학을 품는다</b>:
    /// <list type="bullet">
    ///   <item>1학기 = 3월 1일 ~ <b>여름방학 마지막날</b></item>
    ///   <item>2학기 = 여름방학 다음날 ~ <b>겨울방학 마지막날</b>(=학년도 끝, 2월 말)</item>
    /// </list>
    /// 그래서 유추할 것은 <b>여름방학의 끝</b> 하나뿐이다 — 그 지점이 두 학기의 경계다.
    /// 8월 중순에 개학하는 학교의 8월 하순 수업이 이렇게 해야 2학기로 제대로 들어간다.
    ///
    /// 방학은 행사명이 아니라 <b>수업일이 길게 끊기는 구간</b>으로 찾는다
    /// (<see cref="VacationGapDays"/>) — 이름은 학교마다 다르지만 공백은 다르지 않다.
    ///
    /// 유추에 실패하면 관례값을 돌려준다(<see cref="SemesterRange.FromSchedule"/> 가 false —
    /// 화면은 그 사실을 알려야 한다). 학년 행사는 보지 않는다: 학기의 경계를 정하는 일이라
    /// 학년을 타면 안 된다.
    /// </summary>
    public static SemesterRange ResolveSemesterRange(
        int year, int semester, IReadOnlyCollection<SchoolSchedule> schedules)
    {
        var secondSemesterStart = FindSecondSemesterStart(year, schedules);

        if (secondSemesterStart == null)
        {
            var (start, end) = DefaultSemesterRange(year, semester);
            return new SemesterRange(start, end, false);
        }

        return semester == 2
            ? new SemesterRange(secondSemesterStart.Value, LastDayOfSchoolYear(year), true)
            : new SemesterRange(new DateTime(year, 3, 1), secondSemesterStart.Value.AddDays(-1), true);
    }

    /// <summary>
    /// 2학기 첫 수업일 = 여름방학이 끝난 다음 첫 수업일. 못 찾으면 null.
    /// </summary>
    private static DateTime? FindSecondSemesterStart(
        int year, IReadOnlyCollection<SchoolSchedule> schedules)
    {
        if (schedules == null || schedules.Count == 0) return null;

        var teachingDays = new List<DateTime>();
        for (var date = new DateTime(year, 3, 1); date <= LastDayOfSchoolYear(year); date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (!IsTeachingDay(date, schedules)) continue;

            teachingDays.Add(date);
        }

        // 첫 번째 긴 공백이 여름방학이다. 겨울방학은 그 뒤라 여기서 걸리지 않는다.
        for (int i = 1; i < teachingDays.Count; i++)
        {
            if ((teachingDays[i] - teachingDays[i - 1]).TotalDays >= VacationGapDays)
                return teachingDays[i];
        }

        return null;
    }

    private static bool IsTeachingDay(DateTime date, IEnumerable<SchoolSchedule> schedules)
    {
        foreach (var schedule in schedules)
        {
            if (schedule == null || schedule.IsDeleted) continue;
            if (schedule.AA_YMD.Date != date.Date) continue;
            if (SchoolCalendar.IsNonTeachingDay(schedule)) return false;
        }

        return true;
    }

    private static void CollectEvents(DateTime date, IEnumerable<SchoolSchedule> schedules, List<string> into)
    {
        foreach (var schedule in schedules)
        {
            if (schedule == null || schedule.IsDeleted) continue;
            if (schedule.AA_YMD.Date != date.Date) continue;
            if (string.IsNullOrWhiteSpace(schedule.EVENT_NM)) continue;

            var text = $"{date:M-d} {schedule.EVENT_NM}";
            if (!into.Contains(text))
                into.Add(text);
        }
    }

    private static bool HasWeekday(DateTime start, DateTime end)
    {
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                return true;
        }

        return false;
    }
}

/// <summary>
/// 유추한 학기 기간.
/// </summary>
/// <param name="Start">학기 첫 수업일</param>
/// <param name="End">방학 직전 마지막 수업일</param>
/// <param name="FromSchedule">
/// 학사일정에서 유추했는가. false 면 관례값(3월~8월 / 8월~2월)이라 방학이 섞여 있을 수 있다 —
/// 화면은 그 사실을 알려야 한다.
/// </param>
public readonly record struct SemesterRange(DateTime Start, DateTime End, bool FromSchedule)
{
    public string Display => $"{Start:yyyy-MM-dd} ~ {End:yyyy-MM-dd}";
}

/// <summary>
/// 주차 정보 (기간만)
/// </summary>
public sealed class WeekInfo
{
    public int Number { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }

    public string DateRange => $"{StartDate:MM-dd}~{EndDate:MM-dd}";

    public override string ToString() => $"{Number}주차 ({DateRange})";
}

/// <summary>
/// 계산이 끝난 한 주 — 학급별 자동 시수와 그 주의 학사일정을 함께 담는다.
/// </summary>
public sealed class WeeklyHoursWeek
{
    public int Number { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }

    /// <summary>그 주의 수업 가능 평일 수</summary>
    public int TeachingDays { get; init; }

    /// <summary>그 주의 학사일정 행사 (비고 열)</summary>
    public IReadOnlyList<string> Events { get; init; } = [];

    /// <summary>학급(강의실)별 자동 계산 시수</summary>
    public Dictionary<string, int> AutoByRoom { get; init; } = [];

    public string WeekDisplay => $"{Number}주";
    public string PeriodDisplay => $"{StartDate:MM-dd}~{EndDate:MM-dd}";
    public string EventsDisplay => string.Join(", ", Events);

    public int AutoTotal => AutoByRoom.Values.Sum();

    public int AutoFor(string room) => AutoByRoom.GetValueOrDefault(room);
}
