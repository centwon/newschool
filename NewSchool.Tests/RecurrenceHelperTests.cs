using System;
using System.Linq;
using NewSchool.Scheduler;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 반복 일정 날짜 계산(RecurrenceHelper) 테스트 — 월말·윤년 드리프트 회귀 방지.
/// </summary>
public class RecurrenceHelperTests
{
    [Fact]
    public void None_은_시작일_하나만()
    {
        var dates = RecurrenceHelper.GenerateDates(
            new DateTime(2026, 3, 10), new DateTime(2027, 1, 1), RepeatKind.None);

        Assert.Single(dates);
        Assert.Equal(new DateTime(2026, 3, 10), dates[0]);
    }

    [Fact]
    public void Daily_는_매일_그리고_종료일_포함()
    {
        var dates = RecurrenceHelper.GenerateDates(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 5), RepeatKind.Daily);

        Assert.Equal(5, dates.Count);
        Assert.Equal(new DateTime(2026, 3, 1), dates.First());
        Assert.Equal(new DateTime(2026, 3, 5), dates.Last());
    }

    [Fact]
    public void Weekly_는_7일_간격()
    {
        var dates = RecurrenceHelper.GenerateDates(
            new DateTime(2026, 3, 2), new DateTime(2026, 3, 30), RepeatKind.Weekly);

        Assert.Equal(new[]
        {
            new DateTime(2026, 3, 2),
            new DateTime(2026, 3, 9),
            new DateTime(2026, 3, 16),
            new DateTime(2026, 3, 23),
            new DateTime(2026, 3, 30),
        }, dates);
    }

    [Fact]
    public void Monthly_31일시작_은_28일에_고착되지_않는다()
    {
        // 드리프트 버그의 핵심 케이스: 1/31 → 2/28 → (드리프트면 3/28) 이어야 정상은 3/31
        var dates = RecurrenceHelper.GenerateDates(
            new DateTime(2026, 1, 31), new DateTime(2026, 5, 1), RepeatKind.Monthly);

        Assert.Equal(new[]
        {
            new DateTime(2026, 1, 31),
            new DateTime(2026, 2, 28), // 2월은 28일로 클램프
            new DateTime(2026, 3, 31), // ★ 다시 31일로 복귀 (드리프트면 3/28 이 됨)
            new DateTime(2026, 4, 30), // 4월은 30일
        }, dates);
    }

    [Fact]
    public void Yearly_2월29일_은_다음_윤년에_29일로_복귀()
    {
        var dates = RecurrenceHelper.GenerateDates(
            new DateTime(2024, 2, 29), new DateTime(2028, 12, 31), RepeatKind.Yearly);

        Assert.Equal(new[]
        {
            new DateTime(2024, 2, 29),
            new DateTime(2025, 2, 28),
            new DateTime(2026, 2, 28),
            new DateTime(2027, 2, 28),
            new DateTime(2028, 2, 29), // ★ 다음 윤년에 29일 복귀 (드리프트면 2/28 고착)
        }, dates);
    }

    [Fact]
    public void maxCount_상한을_넘지_않는다()
    {
        var dates = RecurrenceHelper.GenerateDates(
            new DateTime(2026, 1, 1), new DateTime(2100, 1, 1), RepeatKind.Daily, maxCount: 10);

        Assert.Equal(10, dates.Count);
    }

    [Fact]
    public void 시작이_종료보다_늦으면_빈_목록()
    {
        var dates = RecurrenceHelper.GenerateDates(
            new DateTime(2026, 6, 1), new DateTime(2026, 5, 1), RepeatKind.Daily);

        Assert.Empty(dates);
    }
}
