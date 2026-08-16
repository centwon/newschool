using System;
using System.Collections.Generic;
using System.Linq;
using NewSchool.Models;
using NewSchool.Services;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 주차별 시수 계산 회귀 테스트.
///
/// 예전 계산기는 리포지토리 3개를 생성자로 받아서 화면 없이 규칙만 확인할 수 없었다.
/// 지금은 읽어온 데이터만 받는 순수 함수라, 아래 규칙들을 여기서 못박아 둔다.
/// </summary>
public class WeeklyHoursCalculatorTests
{
    private static Course Course(int grade = 1, int unit = 4) =>
        new() { No = 1, Grade = grade, Subject = "수학", Unit = unit };

    /// <summary>월=1 … 금=5</summary>
    private static Lesson Lesson(int dayOfWeek, int period, string room = "1-1") =>
        new() { Course = 1, DayOfWeek = dayOfWeek, Period = period, Room = room, IsRecurring = true };

    private static SchoolSchedule Holiday(DateTime date, string category = "휴업일") =>
        new() { AA_YMD = date, SBTR_DD_SC_NM = category, EVENT_NM = "재량휴업일" };

    [Fact]
    public void 학기_첫주는_시작일이_속한_주의_남은_평일만_센다()
    {
        // 2026-03-04 는 수요일. 그 주의 월·화는 학기 전이므로 세면 안 된다.
        var weeks = WeeklyHoursCalculator.GetSemesterWeeks(
            new DateTime(2026, 3, 4), new DateTime(2026, 3, 20));

        Assert.Equal(new DateTime(2026, 3, 4), weeks[0].StartDate);
        Assert.Equal(1, weeks[0].Number);
    }

    [Fact]
    public void 마지막_주는_종료일에서_잘린다()
    {
        var weeks = WeeklyHoursCalculator.GetSemesterWeeks(
            new DateTime(2026, 3, 2), new DateTime(2026, 3, 18));

        Assert.Equal(new DateTime(2026, 3, 18), weeks[^1].EndDate);
    }

    [Fact]
    public void 평일이_없는_주는_건너뛴다()
    {
        // 토·일만 있는 구간
        var weeks = WeeklyHoursCalculator.GetSemesterWeeks(
            new DateTime(2026, 3, 7), new DateTime(2026, 3, 8));

        Assert.Empty(weeks);
    }

    [Fact]
    public void 배치한_요일_수만큼_주당_시수가_잡힌다()
    {
        // 월·수·금 3시간 배치 → 온전한 한 주는 3시간
        var lessons = new[] { Lesson(1, 1), Lesson(3, 2), Lesson(5, 3) };

        var result = WeeklyHoursCalculator.Calculate(
            Course(), lessons, [], new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        Assert.Single(result);
        Assert.Equal(3, result[0].AutoTotal);
    }

    [Fact]
    public void 휴업일은_그날_시수만큼_빠진다()
    {
        var lessons = new[] { Lesson(1, 1), Lesson(3, 2), Lesson(5, 3) };
        var schedules = new[] { Holiday(new DateTime(2026, 3, 4)) };  // 수요일

        var result = WeeklyHoursCalculator.Calculate(
            Course(), lessons, schedules, new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        Assert.Equal(2, result[0].AutoTotal);
    }

    [Fact]
    public void 그_학년만_빠지는_행사는_시수에서_빠진다()
    {
        var lessons = new[] { Lesson(1, 1), Lesson(3, 2), Lesson(5, 3) };

        // 1학년만 대상인 현장체험학습(수요일)
        var trip = new SchoolSchedule
        {
            AA_YMD = new DateTime(2026, 3, 4),
            EVENT_NM = "현장체험학습",
            ONE_GRADE_EVENT_YN = true
        };

        var grade1 = WeeklyHoursCalculator.Calculate(
            Course(grade: 1), lessons, [trip], new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));
        var grade2 = WeeklyHoursCalculator.Calculate(
            Course(grade: 2), lessons, [trip], new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        Assert.Equal(2, grade1[0].AutoTotal);
        // 2학년은 평소대로 수업한다 — 예전에는 학년 구분 없이 전부 빼서 남의 학년 행사로도 시수가 줄었다
        Assert.Equal(3, grade2[0].AutoTotal);
    }

    [Fact]
    public void 전학년_대상_행사는_시수를_줄이지_않는다()
    {
        var lessons = new[] { Lesson(3, 2) };

        var assembly = new SchoolSchedule
        {
            AA_YMD = new DateTime(2026, 3, 4),
            EVENT_NM = "학교 설명회",
            ONE_GRADE_EVENT_YN = true,
            TW_GRADE_EVENT_YN = true,
            THREE_GRADE_EVENT_YN = true
        };

        var result = WeeklyHoursCalculator.Calculate(
            Course(), lessons, [assembly], new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        Assert.Equal(1, result[0].AutoTotal);
    }

    [Fact]
    public void 삭제된_학사일정은_무시한다()
    {
        var lessons = new[] { Lesson(3, 2) };
        var deleted = Holiday(new DateTime(2026, 3, 4));
        deleted.IsDeleted = true;

        var result = WeeklyHoursCalculator.Calculate(
            Course(), lessons, [deleted], new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        Assert.Equal(1, result[0].AutoTotal);
    }

    [Theory]
    [InlineData(1, 3, 8)]
    [InlineData(2, 9, 2)]
    public void 관례값은_1학기_3월_2학기_9월에_시작한다(int semester, int startMonth, int endMonth)
    {
        var (start, end) = WeeklyHoursCalculator.DefaultSemesterRange(2026, semester);

        Assert.Equal(startMonth, start.Month);
        Assert.Equal(endMonth, end.Month);
        Assert.True(end > start);
    }

    /// <summary>3/1 ~ 7/20 수업, 7/21~8/17 여름방학, 8/18 부터 다시 수업.</summary>
    private static List<SchoolSchedule> WithSummerVacation()
    {
        var schedules = new List<SchoolSchedule>();
        for (var d = new DateTime(2026, 7, 21); d <= new DateTime(2026, 8, 17); d = d.AddDays(1))
            schedules.Add(new SchoolSchedule { AA_YMD = d, SBTR_DD_SC_NM = "휴업일", EVENT_NM = "여름방학" });
        return schedules;
    }

    [Fact]
    public void 학사일정이_없으면_기간은_관례값이고_그렇다고_알린다()
    {
        var range = WeeklyHoursCalculator.ResolveSemesterRange(2026, 1, []);

        Assert.False(range.FromSchedule);   // 화면이 "믿지 말라" 고 표시할 근거
        Assert.Equal(new DateTime(2026, 3, 1), range.Start);
        Assert.Equal(new DateTime(2026, 8, 31), range.End);
    }

    [Fact]
    public void 학기는_방학을_품는다_1학기는_3월1일부터_여름방학_마지막날까지()
    {
        var range = WeeklyHoursCalculator.ResolveSemesterRange(2026, 1, WithSummerVacation());

        Assert.True(range.FromSchedule);
        Assert.Equal(new DateTime(2026, 3, 1), range.Start);
        Assert.Equal(new DateTime(2026, 8, 17), range.End);   // 2학기 개학 전날
    }

    [Fact]
    public void 학기는_방학을_품는다_2학기는_여름방학_다음날부터_학년도_끝까지()
    {
        var range = WeeklyHoursCalculator.ResolveSemesterRange(2026, 2, WithSummerVacation());

        Assert.True(range.FromSchedule);
        Assert.Equal(new DateTime(2026, 8, 18), range.Start);   // 8월 하순 수업이 2학기로 들어간다
        Assert.Equal(new DateTime(2027, 2, 28), range.End);     // 겨울방학 마지막날 = 학년도 끝
    }

    [Fact]
    public void 짧은_연휴는_방학으로_보지_않는다()
    {
        // 재량휴업이 연휴에 붙어도 열흘을 넘기기 어렵다 — 여기서 학기가 잘리면 안 된다.
        var schedules = new List<SchoolSchedule>();
        for (var d = new DateTime(2026, 5, 1); d <= new DateTime(2026, 5, 8); d = d.AddDays(1))
            schedules.Add(new SchoolSchedule { AA_YMD = d, SBTR_DD_SC_NM = "휴업일", EVENT_NM = "재량휴업" });

        schedules.AddRange(WithSummerVacation());

        var range = WeeklyHoursCalculator.ResolveSemesterRange(2026, 1, schedules);

        Assert.Equal(new DateTime(2026, 8, 17), range.End);
    }

    [Fact]
    public void 기간_유추는_학년_행사를_보지_않는다()
    {
        // 학기의 경계를 정하는 일이라 학년을 타면 안 된다 —
        // 1학년만 빠지는 행사로 경계가 밀리면 다른 학년의 계산까지 어긋난다.
        var schedules = WithSummerVacation();
        schedules.Add(new SchoolSchedule
        {
            AA_YMD = new DateTime(2026, 3, 2),
            EVENT_NM = "현장체험학습",
            ONE_GRADE_EVENT_YN = true
        });

        var range = WeeklyHoursCalculator.ResolveSemesterRange(2026, 1, schedules);

        Assert.Equal(new DateTime(2026, 3, 1), range.Start);
        Assert.Equal(new DateTime(2026, 8, 17), range.End);
    }

    [Fact]
    public void 학급별로_열이_갈리고_각_학급의_요일만_센다()
    {
        // 1-1 은 월·목, 1-2 는 화 하나
        var lessons = new[]
        {
            Lesson(1, 1, "1-1"),
            Lesson(4, 1, "1-1"),
            Lesson(2, 3, "1-2")
        };

        var result = WeeklyHoursCalculator.Calculate(
            Course(), lessons, [], new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        Assert.Equal(2, result[0].AutoFor("1-1"));
        Assert.Equal(1, result[0].AutoFor("1-2"));
        Assert.Equal(3, result[0].AutoTotal);
    }

    [Fact]
    public void 학급_열은_수업에_등록된_강의실_순서를_따른다()
    {
        var course = new Course { No = 1, Grade = 1, Subject = "수학", Rooms = "1-1,1-2,1-3" };

        // 배치는 뒤죽박죽이고, 등록되지 않은 강의실도 하나 섞여 있다
        var lessons = new[]
        {
            Lesson(1, 1, "1-3"),
            Lesson(2, 1, "1-1"),
            Lesson(3, 1, "과학실")
        };

        var rooms = WeeklyHoursCalculator.ResolveRooms(course, lessons);

        // 등록 순서(1-1, 1-3)가 먼저, 등록에 없는 강의실은 뒤에
        Assert.Equal(["1-1", "1-3", "과학실"], rooms);
    }

    [Fact]
    public void 강의실이_비어_있으면_미지정_열로_묶인다()
    {
        var lessons = new[] { Lesson(1, 1, ""), Lesson(3, 1, "") };

        var result = WeeklyHoursCalculator.Calculate(
            Course(), lessons, [], new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        Assert.Equal(2, result[0].AutoFor(WeeklyHoursCalculator.UnassignedRoom));
    }

    [Fact]
    public void 그_주의_학사일정이_비고에_모인다()
    {
        var lessons = new[] { Lesson(1, 1) };
        var events = new[]
        {
            new SchoolSchedule { AA_YMD = new DateTime(2026, 3, 2), EVENT_NM = "개학식" },
            new SchoolSchedule { AA_YMD = new DateTime(2026, 3, 4), EVENT_NM = "진로의 날" }
        };

        var result = WeeklyHoursCalculator.Calculate(
            Course(), lessons, events, new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        Assert.Equal("3-2 개학식, 3-4 진로의 날", result[0].EventsDisplay);
    }

    [Fact]
    public void 수업_가능일수를_함께_센다()
    {
        var lessons = new[] { Lesson(1, 1) };
        var schedules = new[] { Holiday(new DateTime(2026, 3, 4)) };

        var result = WeeklyHoursCalculator.Calculate(
            Course(), lessons, schedules, new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        // 월~금 5일 중 수요일 휴업 → 4일
        Assert.Equal(4, result[0].TeachingDays);
    }
}
