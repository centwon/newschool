using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Controls;

/// <summary>한 칸이 그 날 실제로 무엇인가 — 기초 위에 그 날 변경을 얹은 결과.</summary>
public readonly record struct DaySlot(string Subject, string Room, LessonChangeKind Kind, int CourseNo)
{
    public bool IsBlank => string.IsNullOrEmpty(Subject);

    /// <summary>맞바꾸기에서 옮겨 갈 내용 (휴강은 "없음"으로 친다)</summary>
    public bool Movable => !IsBlank && Kind != LessonChangeKind.Cancelled;
}

/// <summary>
/// 교사 한 사람의 시간표를 <b>날짜로 푼 장부</b> — 기초(<see cref="Lesson"/>) 위에 그 날
/// 변경(<see cref="LessonChange"/>)과 학사일정을 얹는다. 칸 풀기·변경 저장·일지 표시가 여기 있다.
///
/// <para>예전에는 이 전부가 <see cref="WeeklyTimetableView"/> 안에 있었다. 오늘 화면의
/// 내 수업 목록도 같은 칸 메뉴(<see cref="LessonSlotMenu"/>)를 쓰게 되면서 떼어 냈다(2026-09-28) —
/// 두 화면이 칸을 따로 풀면 휴업일·학년 행사 판정이 어긋난다. 실제로 오늘 화면은
/// 학교 전체 휴업만 보고, 수업 홈은 "3학년만 수련회" 같은 학년 행사까지 봤다.</para>
///
/// ⚠ 여기서 손대는 것은 <see cref="LessonChange"/> 뿐이다 — 기초 시간표는 그대로다.
/// </summary>
public sealed class LessonSlotBook
{
    public List<Course> Courses { get; } = [];
    public List<Lesson> Lessons { get; } = [];

    /// <summary>(날짜, 교시) → 변경 (읽은 구간 안의 것만)</summary>
    public Dictionary<(DateTime Date, int Period), LessonChange> Changes { get; } = [];

    /// <summary>(날짜, 교시) → 그 칸에 써 둔 수업 일지 글 번호 (<c>withJournals</c> 로 읽었을 때만)</summary>
    public Dictionary<(DateTime Date, int Period), int> Journals { get; } = [];

    private List<SchoolSchedule> _schedules = [];

    /// <summary>학교의 학년 수 (0 = 모름 → 학사일정 판정이 종전 기준으로 돈다)</summary>
    private int _gradeCount;

    /// <summary>수업 → 진도 계획. 칸 메뉴를 열 때 읽고, 다시 읽기·진도 표시 때 버린다.</summary>
    private readonly Dictionary<int, CourseProgressPlan> _plans = [];

    public int Year { get; private set; }
    public int Semester { get; private set; }
    public string TeacherId { get; private set; } = string.Empty;

    /// <summary>읽은 구간 (그 사이 날짜만 칸을 풀 수 있다)</summary>
    public DateTime First { get; private set; }
    public DateTime Last { get; private set; }

    public bool HasScope => !string.IsNullOrEmpty(TeacherId) && Year != 0 && Semester != 0;

    #region 로드

    /// <summary>
    /// 학년도·학기와 수업 목록을 받는다. 학년도·학기가 바뀌었거나 아직 안 읽었으면 학사일정도 읽는다.
    /// </summary>
    /// <returns>학년도·학기가 바뀌었는가</returns>
    public async Task<bool> SetScopeAsync(int year, int semester, IReadOnlyList<Course> courses)
    {
        bool scopeChanged = Year != year || Semester != semester;

        Year = year;
        Semester = semester;
        TeacherId = Settings.User.Value;

        Courses.Clear();
        Courses.AddRange(courses);

        if (scopeChanged || _schedules.Count == 0)
            await LoadSchedulesAsync();

        return scopeChanged;
    }

    private async Task LoadSchedulesAsync()
    {
        _schedules = [];

        var schoolCode = Settings.SchoolCode.Value;
        if (string.IsNullOrEmpty(schoolCode) || Year == 0) return;

        try
        {
            using var repo = new SchoolScheduleRepository(SchoolDatabase.DbPath);
            _schedules = await repo.GetBySchoolYearAsync(schoolCode, Year);

            // 학년 수를 알아야 "1·2학년만 수련회" 같은 날을 그 학년의 휴강 사유로 잡는다.
            _gradeCount = await SchoolProfile.GetGradeCountAsync();
        }
        catch (Exception ex)
        {
            // 학사일정이 없으면 휴업일 표시만 빠질 뿐 표는 그대로 쓸 수 있다.
            NewSchool.Logging.Log.Warning("LessonSlotBook", $"학사일정을 읽지 못해 휴업일 표시가 빠진다: {ex.Message}");
        }
    }

    /// <summary>
    /// <paramref name="first"/>~<paramref name="last"/> 구간의 기초·변경(·일지 표시)을 다시 읽는다.
    /// 실패하면 예외를 그대로 던진다 — 알리는 방식은 화면마다 다르다.
    /// </summary>
    public async Task LoadRangeAsync(DateTime first, DateTime last, bool withJournals)
    {
        First = first.Date;
        Last = last.Date;

        Lessons.Clear();
        Changes.Clear();
        Journals.Clear();
        _plans.Clear();

        if (!HasScope) return;

        using (var repo = new LessonRepository(SchoolDatabase.DbPath))
        {
            var lessons = await repo.GetTeacherScheduleAsync(TeacherId, Year, Semester);
            var known = Courses.Select(c => c.No).ToHashSet();
            Lessons.AddRange(lessons.Where(l => known.Contains(l.Course)));
        }

        using (var repo = new LessonChangeRepository(SchoolDatabase.DbPath))
        {
            foreach (var change in await repo.GetRangeAsync(TeacherId, First, Last))
                Changes[(change.Date.Date, change.Period)] = change;
        }

        if (withJournals)
            await LoadJournalMarksAsync();
    }

    /// <summary>읽은 구간의 칸마다 수업 일지를 써 두었는지 — 공책 표시용.</summary>
    public async Task LoadJournalMarksAsync()
    {
        Journals.Clear();

        for (var date = First; date <= Last; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            foreach (var (period, post) in await Dialogs.LessonJournalComposer.FindByDateAsync(date))
                Journals[(date.Date, period)] = post.No;
        }
    }

    #endregion

    #region 칸 풀기

    public Course? FindCourse(int courseNo)
        => courseNo <= 0 ? null : Courses.FirstOrDefault(c => c.No == courseNo);

    public DaySlot Resolve(DateTime date, int period)
    {
        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        var lesson = Lessons.FirstOrDefault(l => l.DayOfWeek == day && l.Period == period);
        var baseCourse = lesson != null ? FindCourse(lesson.Course) : null;

        // 그 학년이 수업하지 않는 날(학교 휴업일, "3학년만 수련회" 같은 그 학년 행사)에는
        // 평소 수업이 없다. 시수 계산·진도 예정일과 같은 판정(IsTeachingDayFor)이라야
        // 시간표에 보이는 수업과 세는 수업이 어긋나지 않는다. 그 날 따로 넣은 변경(보강 등)은
        // 아래에서 그대로 얹힌다.
        if (lesson != null
            && !SchoolCalendar.IsTeachingDayFor(date, _schedules, baseCourse?.Grade ?? lesson.Grade, _gradeCount))
        {
            lesson = null;
            baseCourse = null;
        }

        if (!Changes.TryGetValue((date.Date, period), out var change))
        {
            return new DaySlot(
                baseCourse?.Subject ?? string.Empty,
                lesson?.Room ?? string.Empty,
                LessonChangeKind.None,
                lesson?.Course ?? 0);
        }

        if (change.IsCancellation)
        {
            // 휴강은 무엇이 빠졌는지 보이도록 원래 수업을 그대로 들고 있는다.
            return new DaySlot(
                baseCourse?.Subject ?? string.Empty,
                lesson?.Room ?? string.Empty,
                LessonChangeKind.Cancelled,
                lesson?.Course ?? 0);
        }

        var kind = change.IsSubstitute
            ? LessonChangeKind.Substitute
            : lesson != null ? LessonChangeKind.Replaced : LessonChangeKind.Added;

        return new DaySlot(change.Subject, change.Room ?? string.Empty, kind, change.CourseNo ?? 0);
    }

    /// <summary>그 칸에 걸린 변경의 사유 메모 (없으면 빈 문자열)</summary>
    public string MemoOf(DateTime date, int period)
        => Changes.TryGetValue((date.Date, period), out var change) ? change.Memo : string.Empty;

    /// <summary>그 날 학교가 통째로 쉬는 사유 (없으면 null)</summary>
    public string? OffDayReason(DateTime date)
    {
        foreach (var schedule in _schedules)
        {
            if (schedule == null || schedule.IsDeleted) continue;
            if (schedule.AA_YMD.Date != date.Date) continue;

            if (SchoolCalendar.IsNonTeachingDay(schedule))
                return string.IsNullOrWhiteSpace(schedule.EVENT_NM) ? schedule.SBTR_DD_SC_NM : schedule.EVENT_NM;
        }

        return null;
    }

    /// <summary>
    /// 그 날 <b>일부 학년만</b> 걸리는 행사 (없으면 null).
    ///
    /// <para>예전에는 "위에서 고른 수업의 학년" 으로 판정했다. 주별 시간표 탭에서 수업 선택을 걷어내면서
    /// <b>내가 가르치는 학년들</b> 기준으로 바꿨다 — 어느 수업을 골랐는지와 무관하게 내 시간표에
    /// 걸리는 행사면 알려야 한다.</para>
    /// </summary>
    public string? GradeEventNote(DateTime date)
    {
        var grades = Courses.Select(c => c.Grade).Where(g => g > 0).Distinct().OrderBy(g => g).ToList();
        if (grades.Count == 0) return null;

        foreach (var schedule in _schedules)
        {
            if (schedule == null || schedule.IsDeleted) continue;
            if (schedule.AA_YMD.Date != date.Date) continue;

            foreach (int grade in grades)
            {
                if (SchoolCalendar.IsGradeOnlyEvent(schedule, grade, _gradeCount))
                    return $"{grade}학년 {schedule.EVENT_NM}";
            }
        }

        return null;
    }

    #endregion

    #region 진도 계획

    /// <summary>그 수업의 진도 계획 — 메뉴를 열 때 읽어 두고, 기록이 바뀌면 버린다.</summary>
    public async Task<CourseProgressPlan?> GetPlanAsync(Course course)
    {
        if (_plans.TryGetValue(course.No, out var cached)) return cached;

        try
        {
            var plan = await CourseProgressPlan.LoadAsync(course, DateTime.Today);
            _plans[course.No] = plan;
            return plan;
        }
        catch (Exception ex)
        {
            // 진도를 못 읽어도 일지 쓰기·수업 변경은 된다 — 진도 항목만 비운다.
            NewSchool.Logging.Log.Warning("LessonSlotBook", $"진도 계획을 읽지 못해 진도 항목을 비운다: {ex.Message}");
            return null;
        }
    }

    public void ForgetPlans() => _plans.Clear();

    #endregion

    #region 변경 저장

    /// <summary>
    /// 그 날 한 칸의 최종 내용을 정해 저장한다.
    ///
    /// 결과가 평소와 같아지면 <b>변경 행을 지운다</b> — 남겨 두면 나중에 기초 시간표를 고쳤을 때
    /// 옛 내용이 그 날에만 고정으로 버틴다.
    /// </summary>
    /// <returns>저장했으면 true (못 했으면 장부는 그대로다)</returns>
    public async Task<bool> SetSlotAsync(
        DateTime date, int period, Course? course, string subjectText, string room, string? memo = null)
    {
        var plan = PlanSlot(date, period, course, subjectText, room, memo);

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        if (!await ApplyPlanAsync(repo, plan)) return false;

        RememberPlan(plan);
        return true;
    }

    /// <summary>
    /// 한 칸에 무엇을 쓸지 정한 결과. DB 를 건드리기 <b>전에</b> 계산해 둔다 —
    /// 맞바꾸기는 두 칸의 계획을 먼저 세운 뒤 한 트랜잭션으로 함께 적용한다.
    /// </summary>
    /// <param name="Change">쓸 내용. null 이면 "평소대로" 라서 기존 변경 행을 지운다.</param>
    /// <param name="DeleteNo">지울 변경 행 번호(0 이면 지울 것이 없다).</param>
    private readonly record struct SlotPlan(
        DateTime Date, int Period, LessonChange? Change, int DeleteNo);

    /// <summary>그 칸의 최종 내용을 정한다(DB 는 건드리지 않는다).</summary>
    private SlotPlan PlanSlot(
        DateTime date, int period, Course? course, string subjectText, string room, string? memo)
    {
        int day = SchoolCalendar.ToLessonDayOfWeek(date);
        var lesson = Lessons.FirstOrDefault(l => l.DayOfWeek == day && l.Period == period);

        bool cancelling = course == null && string.IsNullOrWhiteSpace(subjectText);
        bool sameAsUsual = cancelling
            ? lesson == null
            : course != null && lesson != null && lesson.Course == course.No && lesson.Room == room;

        Changes.TryGetValue((date.Date, period), out var existing);

        // 결과가 평소와 같아지면 변경 행을 지운다 — 남겨 두면 나중에 기초 시간표를 고쳤을 때
        // 옛 내용이 그 날에만 고정으로 버틴다.
        if (sameAsUsual)
            return new SlotPlan(date.Date, period, null, existing?.No ?? 0);

        var change = new LessonChange
        {
            TeacherID = TeacherId,
            Year = Year,
            Semester = Semester,
            Date = date.Date,
            Period = period,
            CourseNo = course?.No,
            SubjectText = course == null ? subjectText : string.Empty,
            Room = cancelling ? string.Empty : room,
            Memo = memo ?? existing?.Memo ?? string.Empty,
            CourseSubject = course?.Subject ?? string.Empty
        };

        return new SlotPlan(date.Date, period, change, 0);
    }

    /// <summary>계획 한 건을 DB 에 적용한다. 지울 것도 쓸 것도 없으면 성공으로 본다.</summary>
    private static async Task<bool> ApplyPlanAsync(LessonChangeRepository repo, SlotPlan plan)
    {
        if (plan.Change != null) return await repo.UpsertAsync(plan.Change);
        if (plan.DeleteNo > 0) return await repo.DeleteAsync(plan.DeleteNo);
        return true;
    }

    /// <summary>DB 반영이 끝난 계획을 장부의 변경에도 반영한다.</summary>
    private void RememberPlan(SlotPlan plan)
    {
        if (plan.Change != null) Changes[(plan.Date, plan.Period)] = plan.Change;
        else Changes.Remove((plan.Date, plan.Period));
    }

    /// <summary>그 칸의 변경을 지워 평소대로 되돌린다.</summary>
    /// <returns>되돌렸거나 되돌릴 것이 없었으면 true</returns>
    public async Task<bool> RevertSlotAsync(DateTime date, int period)
    {
        if (!Changes.TryGetValue((date.Date, period), out var change)) return true;

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        // ⚠ 결과를 봐야 한다. 예전에는 삭제 결과를 버리고 화면의 변경 목록에서만 지웠다 —
        // 지우지 못했는데도 칸은 평소 수업으로 돌아가 보이고, 다시 열면 변경이 되살아났다.
        // 바로 위 SetSlotAsync 는 UpsertAsync 결과를 확인한다. 같은 기준을 맞춘다.
        if (!await repo.DeleteAsync(change.No)) return false;

        Changes.Remove((date.Date, period));
        return true;
    }

    /// <summary>
    /// 두 칸을 맞바꾼다 — 그 날들의 변경 두 줄로 표현된다.
    ///
    /// <para>⚠ 두 줄은 <b>한 트랜잭션</b>으로 함께 들어가야 한다. 예전에는 <c>SetSlotAsync</c> 를
    /// 두 번 불러 각자 연결·각자 저장이었고, 두 번째가 실패하면 첫 칸만 바뀐 채로 남아
    /// <b>같은 수업이 두 칸에</b> 보였다(원래 자리는 그대로, 옮긴 자리에도 하나).</para>
    ///
    /// <para>계획(<see cref="SlotPlan"/>)은 둘 다 DB 를 건드리기 전에 세운다 — 먼저 쓴 내용이
    /// 두 번째 계획의 "평소와 같은가" 판정을 흔들지 않도록.</para>
    /// </summary>
    /// <returns>맞바꿨으면 true (못 했으면 둘 다 그대로다)</returns>
    public async Task<bool> SwapAsync((DateTime Date, int Period) a, (DateTime Date, int Period) b)
    {
        var slotA = Resolve(a.Date, a.Period);
        var slotB = Resolve(b.Date, b.Period);

        var planB = PlanFor(b, slotA);
        var planA = PlanFor(a, slotB);

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        repo.BeginTransaction();
        try
        {
            if (!await ApplyPlanAsync(repo, planB) || !await ApplyPlanAsync(repo, planA))
            {
                repo.Rollback();
                return false;
            }

            repo.Commit();
        }
        catch
        {
            repo.Rollback();
            throw;
        }

        RememberPlan(planB);
        RememberPlan(planA);
        return true;

        SlotPlan PlanFor((DateTime Date, int Period) target, DaySlot content)
        {
            if (!content.Movable)
                return PlanSlot(target.Date, target.Period, null, string.Empty, string.Empty, null);

            var course = FindCourse(content.CourseNo);
            return PlanSlot(
                target.Date, target.Period,
                course,
                course == null ? content.Subject : string.Empty,
                content.Room,
                null);
        }
    }

    #endregion
}
