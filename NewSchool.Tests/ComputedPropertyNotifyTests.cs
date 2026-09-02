using System;
using System.Collections.Generic;
using System.ComponentModel;
using NewSchool.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>규칙: 계산 속성은 자기 입력이 바뀔 때 알림을 낸다</b>
/// (근거는 <see cref="NotifyPropertyChangedBase"/> 머리 주석).
///
/// <para><c>x:Bind</c> 는 <c>Mode</c> 를 빼면 <b>OneTime</b> 이라, 알림을 안 내도 지금 화면은
/// 멀쩡해 보인다. 그래서 이 규칙은 <b>깨져도 아무 증상이 없다</b> — 나중에 누가
/// <c>Mode=OneWay</c> 를 붙이는 순간 조용히 안 돌 뿐이다. 주석만으로는 지켜지지 않으므로
/// 여기서 못박는다.</para>
///
/// <para>실제로 <c>ClassTimetableEditDialog</c> 는 계산 속성 <c>DayName</c> 에 이미
/// <c>Mode=OneWay</c> 를 걸어 두었다. 지금은 <c>DayOfWeek</c> 가 생성 뒤 안 바뀌어 드러나지
/// 않을 뿐이다.</para>
/// </summary>
public class ComputedPropertyNotifyTests
{
    /// <summary><paramref name="mutate"/> 를 실행하는 동안 올라온 PropertyChanged 이름들.</summary>
    private static List<string> Capture(INotifyPropertyChanged model, Action mutate)
    {
        var seen = new List<string>();
        void Handler(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null) seen.Add(e.PropertyName);
        }

        model.PropertyChanged += Handler;
        try { mutate(); }
        finally { model.PropertyChanged -= Handler; }
        return seen;
    }

    private static void AssertNotifies(
        INotifyPropertyChanged model, Action mutate, params string[] expected)
    {
        var seen = Capture(model, mutate);
        foreach (var name in expected)
            Assert.True(seen.Contains(name),
                $"'{name}' 알림이 없다 — 그 계산 속성을 Mode=OneWay 로 걸면 조용히 안 돈다. " +
                $"올라온 것: {string.Join(", ", seen)}");
    }

    [Fact]
    public void Course_의_계산_속성이_따라_돈다()
    {
        var course = new Course { Grade = 1, Subject = "국어", Type = CourseTypes.Class, Rooms = "1-1" };

        AssertNotifies(course, () => course.Subject = "수학", nameof(Course.DisplayName));
        AssertNotifies(course, () => course.Grade = 2, nameof(Course.DisplayName));
        AssertNotifies(course, () => course.Type = CourseTypes.Selective,
            nameof(Course.EffectiveType), nameof(Course.IsClassType), nameof(Course.TypeDisplay));
        AssertNotifies(course, () => course.Rooms = "2-1,2-2",
            nameof(Course.RoomList), nameof(Course.RoomListDisplay));
    }

    [Fact]
    public void ClassTimetable_의_계산_속성이_따라_돈다()
    {
        var slot = new ClassTimetable { DayOfWeek = 1, Grade = 1, Class = 1 };

        AssertNotifies(slot, () => slot.DayOfWeek = 3, nameof(ClassTimetable.DayName));
        AssertNotifies(slot, () => slot.Grade = 2, nameof(ClassTimetable.ClassInfo));
        AssertNotifies(slot, () => slot.Class = 4, nameof(ClassTimetable.ClassInfo));
    }

    [Fact]
    public void Lesson_의_계산_속성이_따라_돈다()
    {
        var lesson = new Lesson { DayOfWeek = 1, Period = 1 };

        AssertNotifies(lesson, () => lesson.DayOfWeek = 5,
            nameof(Lesson.DayName), nameof(Lesson.ScheduleDisplay));
        AssertNotifies(lesson, () => lesson.Period = 6, nameof(Lesson.ScheduleDisplay));
    }

    [Fact]
    public void ClassDiary_의_계산_속성이_따라_돈다()
    {
        var diary = new ClassDiary { Date = new DateTime(2026, 3, 2) };

        AssertNotifies(diary, () => diary.Date = new DateTime(2026, 3, 3),
            nameof(ClassDiary.DayOfWeek), nameof(ClassDiary.DayOfWeekKorean), nameof(ClassDiary.DateDisplay));

        // 출결 세 칸은 같은 계산 속성 셋을 먹인다 — 무엇이 바뀌어도 같이 알려야 한다.
        AssertNotifies(diary, () => diary.Absent = "김하늘",
            nameof(ClassDiary.HasAttendanceIssues), nameof(ClassDiary.AttendanceSummary));
        AssertNotifies(diary, () => diary.Late = "박지민",
            nameof(ClassDiary.HasAttendanceIssues), nameof(ClassDiary.AttendanceSummary));
        AssertNotifies(diary, () => diary.LeaveEarly = "이서준",
            nameof(ClassDiary.HasAttendanceIssues), nameof(ClassDiary.AttendanceSummary));

        AssertNotifies(diary, () => diary.Memo = "메모", nameof(ClassDiary.HasMemo));
        AssertNotifies(diary, () => diary.Notice = "알림", nameof(ClassDiary.HasNotice));
        AssertNotifies(diary, () => diary.Life = "생활", nameof(ClassDiary.HasLifeRecord));
    }

    [Fact]
    public void CourseSection_의_계산_속성이_따라_돈다()
    {
        var section = new CourseSection { UnitNo = 1, ChapterNo = 1, SectionNo = 1, StartPage = 1 };

        AssertNotifies(section, () => section.UnitNo = 2, nameof(CourseSection.FullPath));
        AssertNotifies(section, () => section.ChapterNo = 2, nameof(CourseSection.FullPath));
        AssertNotifies(section, () => section.SectionNo = 2, nameof(CourseSection.FullPath));
        AssertNotifies(section, () => section.StartPage = 8,
            nameof(CourseSection.PageRangeDisplay), nameof(CourseSection.ShortInfo));
        AssertNotifies(section, () => section.EndPage = 12,
            nameof(CourseSection.PageRangeDisplay), nameof(CourseSection.ShortInfo));
        AssertNotifies(section, () => section.EstimatedHours = 3,
            nameof(CourseSection.HoursDisplay), nameof(CourseSection.ShortInfo));
    }

    [Fact]
    public void LessonChange_의_계산_속성이_따라_돈다()
    {
        var change = new LessonChange { Date = new DateTime(2026, 3, 2), Period = 1 };

        AssertNotifies(change, () => change.Date = new DateTime(2026, 3, 3), nameof(LessonChange.DateDisplay));
        AssertNotifies(change, () => change.Period = 4, nameof(LessonChange.PeriodDisplay));
        AssertNotifies(change, () => change.CourseNo = 7,
            nameof(LessonChange.HasCourse), nameof(LessonChange.IsCancellation),
            nameof(LessonChange.IsSubstitute), nameof(LessonChange.Subject),
            nameof(LessonChange.ContentDisplay));
        AssertNotifies(change, () => change.CourseSubject = "수학",
            nameof(LessonChange.Subject), nameof(LessonChange.ContentDisplay));
        AssertNotifies(change, () => change.Room = "1-3", nameof(LessonChange.ContentDisplay));
    }

    [Fact]
    public void StudentLog_의_계산_속성이_따라_돈다()
    {
        var log = new StudentLog();

        // Log·ActivityName·Topic·Description 은 Summary 와 DraftSummary 를 함께 먹인다
        AssertNotifies(log, () => log.Log = "기록",
            nameof(StudentLog.Summary), nameof(StudentLog.DraftSummary));
        AssertNotifies(log, () => log.ActivityName = "토론",
            nameof(StudentLog.Summary), nameof(StudentLog.DraftSummary));
        AssertNotifies(log, () => log.Topic = "환경",
            nameof(StudentLog.Summary), nameof(StudentLog.DraftSummary));
        AssertNotifies(log, () => log.Description = "설명",
            nameof(StudentLog.Summary), nameof(StudentLog.DraftSummary));

        // ⚠ 나머지 넷도 마찬가지다. 예전 이 자리에는 "나머지 넷은 Summary 에만 들어간다"고
        // 적혀 있었지만 사실이 아니었다 — DraftSummary 의 문장은 역할·기른 능력·장점·성취를
        // 전부 쓴다. 모델 주석도 같은 오해를 적어 두었고, 테스트가 그것을 못박고 있었다.
        AssertNotifies(log, () => log.Role = "사회자",
            nameof(StudentLog.Summary), nameof(StudentLog.DraftSummary));
        AssertNotifies(log, () => log.SkillDeveloped = "발표력",
            nameof(StudentLog.Summary), nameof(StudentLog.DraftSummary));
        AssertNotifies(log, () => log.StrengthShown = "배려",
            nameof(StudentLog.Summary), nameof(StudentLog.DraftSummary));
        AssertNotifies(log, () => log.ResultOrOutcome = "우수",
            nameof(StudentLog.Summary), nameof(StudentLog.DraftSummary));
    }

    /// <summary>
    /// ⚠ <b>같은 값을 다시 넣으면 아무 알림도 없어야 한다.</b>
    /// <c>SetProperty</c> 가 거짓을 돌려줄 때 <c>Notify</c> 를 부르면 값이 그대로인데도 화면이
    /// 다시 그려지고, 목록이 길면 그대로 버벅임이 된다.
    /// </summary>
    [Fact]
    public void 같은_값을_다시_넣으면_알리지_않는다()
    {
        var course = new Course { Grade = 1, Subject = "국어", Type = CourseTypes.Class, Rooms = "1-1" };

        Assert.Empty(Capture(course, () => course.Subject = "국어"));
        Assert.Empty(Capture(course, () => course.Grade = 1));
        Assert.Empty(Capture(course, () => course.Type = CourseTypes.Class));
        Assert.Empty(Capture(course, () => course.Rooms = "1-1"));

        var diary = new ClassDiary { Absent = "김하늘" };
        Assert.Empty(Capture(diary, () => diary.Absent = "김하늘"));
    }
}
