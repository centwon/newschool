using System;
using System.Collections.Generic;
using System.Linq;
using NewSchool.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학사일정 그룹화(SchoolScheduleGroupHelper) 변환 로직 테스트 (TEST_PLAN 4단계).
/// 연속 날짜 묶음 · 방학(휴업) 표시 · 정렬 · DateText 포맷을 검증한다.
/// </summary>
public class SchoolScheduleGroupTests
{
    private static SchoolSchedule Sch(string name, DateTime date, string subtractName = "해당없음")
        => new() { EVENT_NM = name, AA_YMD = date, SBTR_DD_SC_NM = subtractName };

    private static readonly DateTime D1 = new(2026, 3, 2);
    private static readonly DateTime D2 = new(2026, 3, 3);
    private static readonly DateTime D3 = new(2026, 3, 4);

    [Fact]
    public void 빈_입력은_빈_리스트()
    {
        Assert.Empty(SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule>()));
        Assert.Empty(SchoolScheduleGroupHelper.GroupSchedules(null!));
    }

    [Fact]
    public void 연속된_같은_행사는_하나의_범위로_묶임()
    {
        var groups = SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule>
        {
            Sch("체험학습", D1), Sch("체험학습", D2), Sch("체험학습", D3)
        });

        var g = Assert.Single(groups);
        Assert.Equal("체험학습", g.EventName);
        Assert.Equal(D1, g.StartDate);
        Assert.Equal(D3, g.EndDate);
    }

    [Fact]
    public void 하루짜리_행사는_시작일과_종료일이_같음()
    {
        var g = Assert.Single(SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule> { Sch("입학식", D1) }));
        Assert.Equal(g.StartDate, g.EndDate);
    }

    [Fact]
    public void 같은_행사라도_날짜가_끊기면_별도_범위()
    {
        var gap = D1.AddDays(5);
        var groups = SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule>
        {
            Sch("행사", D1), Sch("행사", D2), Sch("행사", gap)
        });

        Assert.Equal(2, groups.Count);
        Assert.Equal(D1, groups[0].StartDate);
        Assert.Equal(D2, groups[0].EndDate);
        Assert.Equal(gap, groups[1].StartDate);
        Assert.Equal(gap, groups[1].EndDate);
    }

    [Fact]
    public void 입력_순서와_무관하게_정렬됨()
    {
        // 역순 입력 → 시작일 오름차순으로 정렬되어 나와야 함
        var groups = SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule>
        {
            Sch("나중행사", D3), Sch("먼저행사", D1)
        });

        Assert.Equal("먼저행사", groups[0].EventName);
        Assert.Equal("나중행사", groups[1].EventName);
    }

    [Fact]
    public void 휴업_포함시_방학_표시()
    {
        var groups = SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule>
        {
            Sch("여름방학", D1, "휴업일"), Sch("여름방학", D2, "휴업일")
        });

        Assert.True(Assert.Single(groups).IsVacation);
    }

    [Fact]
    public void 일반_일정은_방학_아님()
    {
        var groups = SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule> { Sch("개학식", D1) });
        Assert.False(Assert.Single(groups).IsVacation);
    }

    [Fact]
    public void 오늘이_범위에_포함되면_IsToday()
    {
        var today = DateTime.Today;
        var groups = SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule>
        {
            Sch("진행중", today.AddDays(-1)), Sch("진행중", today), Sch("진행중", today.AddDays(1))
        });

        Assert.True(Assert.Single(groups).IsToday);
    }

    [Fact]
    public void 오늘이_범위밖이면_IsToday_아님()
    {
        var groups = SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule>
        {
            Sch("과거행사", DateTime.Today.AddDays(-30))
        });

        Assert.False(Assert.Single(groups).IsToday);
    }

    [Fact]
    public void DateText_단일날짜_포맷()
    {
        var g = new SchoolScheduleGroup { EventName = "입학식", StartDate = D1, EndDate = D1 };
        Assert.Equal("3월 2일: 입학식", g.DateText);
    }

    [Fact]
    public void DateText_범위_포맷()
    {
        var g = new SchoolScheduleGroup { EventName = "체험학습", StartDate = D1, EndDate = D3 };
        Assert.Equal("3월 2일~3월 4일: 체험학습", g.DateText);
    }

    [Fact]
    public void 서로_다른_행사는_각각_묶임()
    {
        var groups = SchoolScheduleGroupHelper.GroupSchedules(new List<SchoolSchedule>
        {
            Sch("A", D1), Sch("B", D1)
        });

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(D1, g.StartDate));
    }
}
