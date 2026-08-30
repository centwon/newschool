using System;
using NewSchool.Helpers;
using NewSchool.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// SchoolCalendar 회귀 테스트 —
/// 휴일 판정과 요일 규약은 자동배치·밀기/당기기가 <b>같은 기준</b>을 써야 한다.
/// 예전에는 두 곳에 복붙돼 있어 한쪽만 바뀌면 조용히 어긋났다.
/// </summary>
public class SchoolCalendarTests
{
    private static SchoolSchedule Event(string name, string category = "") =>
        new() { EVENT_NM = name, SBTR_DD_SC_NM = category };

    [Theory]
    [InlineData("휴업일")]
    [InlineData("공휴일")]
    [InlineData("토요휴업일")]   // 교육청에 따라 앞뒤가 붙어 온다 — 같은지가 아니라 포함으로 본다
    public void 수업공제일자명이_휴업_공휴면_행사명과_무관하게_수업일이_아니다(string category)
    {
        Assert.True(SchoolCalendar.IsNonTeachingDay(Event("체육대회", category)));
    }

    [Theory]
    [InlineData("여름방학")]
    [InlineData("재량휴업일")]
    [InlineData("공휴일(어린이날)")]
    public void 행사명만_그럴듯해도_수업공제일자명이_해당없음이면_수업일이다(string name)
    {
        // 근거는 행사명이 아니라 수업공제일자명 하나다. 실제 자료에서 방학·휴업 기간은
        // 그 칸이 "휴업일" 로 채워져 오므로, 이름으로 짐작할 일이 없다.
        Assert.False(SchoolCalendar.IsNonTeachingDay(Event(name, "해당없음")));
    }

    [Fact]
    public void 방학식은_수업일이다()
    {
        // "여름방학식" 은 이름에 "방학" 이 들어가지만 수업공제일자명이 "해당없음" 인 수업일이다.
        // 예전에는 이름 규칙 때문에 휴업일로 잡혀 시수에서 하루가 조용히 빠졌다.
        Assert.False(SchoolCalendar.IsNonTeachingDay(Event("여름방학식", "해당없음")));
    }

    [Fact]
    public void 일반_행사는_수업일이다()
    {
        Assert.False(SchoolCalendar.IsNonTeachingDay(Event("중간고사")));
    }

    [Fact]
    public void 행사명이_비어도_예외가_나지_않는다()
    {
        // 예전에는 여기서 NullReference 가 나면 바깥 catch 가 삼켜 휴일 목록 전체가 비었다
        var schedule = new SchoolSchedule { EVENT_NM = null! };
        Assert.False(SchoolCalendar.IsNonTeachingDay(schedule));
    }

    [Fact]
    public void 한_학년만_표시된_행사는_그_학년만_빠진다()
    {
        var trip = new SchoolSchedule { EVENT_NM = "현장체험학습", ONE_GRADE_EVENT_YN = true };

        Assert.True(SchoolCalendar.IsGradeOnlyEvent(trip, 1));
        Assert.False(SchoolCalendar.IsGradeOnlyEvent(trip, 2));
    }

    [Fact]
    public void 여러_학년이_표시된_행사는_학년_전용이_아니다()
    {
        // 전교 행사에 가깝다 — 학년 전용으로 보면 남의 학년 시수까지 깎인다
        var assembly = new SchoolSchedule
        {
            EVENT_NM = "학교 설명회",
            ONE_GRADE_EVENT_YN = true,
            TW_GRADE_EVENT_YN = true
        };

        Assert.False(SchoolCalendar.IsGradeOnlyEvent(assembly, 1));
    }

    [Theory]
    [InlineData("초등학교", 6)]
    [InlineData("중학교", 3)]
    [InlineData("고등학교", 3)]
    [InlineData("특수학교", 0)]   // 유·초·중·고 과정이 섞여 단정할 수 없다
    [InlineData("각종학교", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void 학교급에서_학년수를_얻는다(string? schoolType, int expected)
    {
        Assert.Equal(expected, SchoolCalendar.GradeCountFor(schoolType));
    }

    [Fact]
    public void 학년수를_알면_두_학년_수련회도_그_학년들만_빠진다()
    {
        // 3학년제 중학교에서 1·2학년만 수련회를 간다. 3학년은 정상 수업.
        //   예전에는 "표시된 학년이 하나뿐" 일 때만 걸러서 1·2학년 모두 정상 수업일로 셌고,
        //   그만큼 시수가 부풀려졌다.
        var trip = new SchoolSchedule
        {
            EVENT_NM = "수련회",
            ONE_GRADE_EVENT_YN = true,
            TW_GRADE_EVENT_YN = true
        };

        Assert.True(SchoolCalendar.IsGradeOnlyEvent(trip, 1, gradeCount: 3));
        Assert.True(SchoolCalendar.IsGradeOnlyEvent(trip, 2, gradeCount: 3));
        Assert.False(SchoolCalendar.IsGradeOnlyEvent(trip, 3, gradeCount: 3));
    }

    [Fact]
    public void 학년수를_알면_전교_행사는_학년_전용이_아니다()
    {
        // 3학년제에서 세 학년이 모두 표시 = 전교 행사. 휴업 판정에 맡긴다.
        //   감사 문서가 제안한 marked < flags.Length 는 flags 가 항상 6칸이라 여기서 오판했다.
        var festival = new SchoolSchedule
        {
            EVENT_NM = "체육대회",
            ONE_GRADE_EVENT_YN = true,
            TW_GRADE_EVENT_YN = true,
            THREE_GRADE_EVENT_YN = true
        };

        Assert.False(SchoolCalendar.IsGradeOnlyEvent(festival, 1, gradeCount: 3));
        Assert.False(SchoolCalendar.IsGradeOnlyEvent(festival, 2, gradeCount: 3));
        Assert.False(SchoolCalendar.IsGradeOnlyEvent(festival, 3, gradeCount: 3));
    }

    [Fact]
    public void 초등학교는_여섯_학년_기준으로_판정한다()
    {
        var trip = new SchoolSchedule
        {
            EVENT_NM = "현장체험학습",
            ONE_GRADE_EVENT_YN = true,
            TW_GRADE_EVENT_YN = true,
            THREE_GRADE_EVENT_YN = true
        };
        // 6학년제에서 세 학년만 가는 날 → 그 학년들만 빠진다
        Assert.True(SchoolCalendar.IsGradeOnlyEvent(trip, 1, gradeCount: 6));
        Assert.False(SchoolCalendar.IsGradeOnlyEvent(trip, 4, gradeCount: 6));

        var allGrades = new SchoolSchedule
        {
            EVENT_NM = "학예회",
            ONE_GRADE_EVENT_YN = true,
            TW_GRADE_EVENT_YN = true,
            THREE_GRADE_EVENT_YN = true,
            FR_GRADE_EVENT_YN = true,
            FIV_GRADE_EVENT_YN = true,
            SIX_GRADE_EVENT_YN = true
        };
        Assert.False(SchoolCalendar.IsGradeOnlyEvent(allGrades, 1, gradeCount: 6));
    }

    [Fact]
    public void 학년수를_모르면_종전_기준을_그대로_쓴다()
    {
        // gradeCount 0 = 모름. 학년 수 없이 "전부는 아님" 을 적용하면 3학년제 전교 행사를
        //   학년 전용으로 오판하므로, 표시 학년이 하나뿐일 때만 거르던 종전 규칙으로 물러난다.
        var two = new SchoolSchedule
        {
            EVENT_NM = "수련회",
            ONE_GRADE_EVENT_YN = true,
            TW_GRADE_EVENT_YN = true
        };
        Assert.False(SchoolCalendar.IsGradeOnlyEvent(two, 1));

        var one = new SchoolSchedule { EVENT_NM = "수련회", ONE_GRADE_EVENT_YN = true };
        Assert.True(SchoolCalendar.IsGradeOnlyEvent(one, 1));
    }

    [Fact]
    public void 두_학년_수련회는_그_학년의_수업일에서_빠진다()
    {
        var trip = new SchoolSchedule
        {
            EVENT_NM = "수련회",
            AA_YMD = new DateTime(2026, 5, 13),   // 수요일
            ONE_GRADE_EVENT_YN = true,
            TW_GRADE_EVENT_YN = true
        };
        var date = new DateTime(2026, 5, 13);

        Assert.False(SchoolCalendar.IsTeachingDayFor(date, [trip], 1, gradeCount: 3));
        Assert.False(SchoolCalendar.IsTeachingDayFor(date, [trip], 2, gradeCount: 3));
        Assert.True(SchoolCalendar.IsTeachingDayFor(date, [trip], 3, gradeCount: 3));

        // 학년 수를 모르면 종전대로 셋 다 수업일
        Assert.True(SchoolCalendar.IsTeachingDayFor(date, [trip], 1));
    }
    [Fact]
    public void 행사명이_없으면_학년_전용_판정을_하지_않는다()
    {
        var blank = new SchoolSchedule { EVENT_NM = "", ONE_GRADE_EVENT_YN = true };

        Assert.False(SchoolCalendar.IsGradeOnlyEvent(blank, 1));
    }

    [Fact]
    public void 주말은_수업일이_아니다()
    {
        Assert.False(SchoolCalendar.IsTeachingDayFor(new DateTime(2026, 3, 7), [], 1));
        Assert.True(SchoolCalendar.IsTeachingDayFor(new DateTime(2026, 3, 6), [], 1));
    }

    [Fact]
    public void 삭제된_학사일정은_수업일_판정에_끼어들지_않는다()
    {
        var deleted = new SchoolSchedule
        {
            AA_YMD = new DateTime(2026, 3, 4),
            SBTR_DD_SC_NM = "휴업일",
            IsDeleted = true
        };

        Assert.True(SchoolCalendar.IsTeachingDayFor(new DateTime(2026, 3, 4), [deleted], 1));
    }

    [Theory]
    [InlineData(2026, 3, 2, 1)]   // 월
    [InlineData(2026, 3, 6, 5)]   // 금
    [InlineData(2026, 3, 7, 6)]   // 토
    [InlineData(2026, 3, 8, 7)]   // 일 — .NET 은 0, 시간표 규약은 7
    public void 요일은_시간표_규약_월1_일7_로_변환된다(int y, int m, int d, int expected)
    {
        Assert.Equal(expected, SchoolCalendar.ToLessonDayOfWeek(new DateTime(y, m, d)));
    }
}
