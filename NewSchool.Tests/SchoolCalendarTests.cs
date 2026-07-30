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
    [InlineData("여름방학")]
    [InlineData("재량휴업일")]
    [InlineData("공휴일(어린이날)")]
    public void 휴업_공휴_방학이_들어간_행사는_수업일이_아니다(string name)
    {
        Assert.True(SchoolCalendar.IsNonTeachingDay(Event(name)));
    }

    [Theory]
    [InlineData("휴업일")]
    [InlineData("공휴일")]
    public void NEIS_구분값이_휴업일_공휴일이면_행사명과_무관하게_수업일이_아니다(string category)
    {
        Assert.True(SchoolCalendar.IsNonTeachingDay(Event("체육대회", category)));
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
