using System;
using System.Collections.Generic;
using System.Linq;
using NewSchool.Scheduler;
using NewSchool.Scheduler.Repositories;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// KEventRepository.SortForDisplay 회귀 테스트.
///
/// 배경: KEvent.Start 열에는 두 가지 형식이 섞여 저장된다 —
/// 종일은 로컬 날짜("yyyy-MM-dd"), 시간 이벤트는 UTC("yyyy-MM-ddTHH:mm:ss.fffZ").
/// SQL 의 <c>ORDER BY Start</c> 는 이 둘을 한 축에서 바이트 비교하므로
/// KST(UTC+9) 에서 "8/15 오전 이벤트 → 8/15 종일 → 8/15 오후 이벤트" 처럼
/// 종일 항목이 시간 이벤트 사이에 끼어드는 순서가 나왔다.
/// 정렬은 Map 이 로컬 DateTime 으로 되돌린 뒤 메모리에서 해야 한다.
/// </summary>
public class KEventSortForDisplayTests
{
    private static KEvent Timed(string title, DateTime start)
        => new() { Title = title, Start = start, End = start, IsAllday = false };

    private static KEvent AllDay(string title, DateTime date)
        => new() { Title = title, Start = date.Date, End = date.Date, IsAllday = true };

    [Fact]
    public void 같은_날이면_종일이_시간이벤트보다_먼저()
    {
        var d = new DateTime(2026, 8, 15);

        // SQL 이 내놓던 잘못된 순서(오전 → 종일 → 오후)를 그대로 입력한다.
        var list = new List<KEvent>
        {
            Timed("오전 회의", d.AddHours(8)),
            AllDay("체육대회", d),
            Timed("저녁 모임", d.AddHours(20)),
        };

        var sorted = KEventRepository.SortForDisplay(list);

        Assert.Equal(
            new[] { "체육대회", "오전 회의", "저녁 모임" },
            sorted.Select(e => e.Title));
    }

    [Fact]
    public void 날짜가_다르면_날짜순이_우선()
    {
        var list = new List<KEvent>
        {
            AllDay("16일 종일", new DateTime(2026, 8, 16)),
            Timed("15일 저녁", new DateTime(2026, 8, 15, 23, 0, 0)),
            Timed("14일 낮", new DateTime(2026, 8, 14, 12, 0, 0)),
        };

        var sorted = KEventRepository.SortForDisplay(list);

        Assert.Equal(
            new[] { "14일 낮", "15일 저녁", "16일 종일" },
            sorted.Select(e => e.Title));
    }

    [Fact]
    public void 같은_날_시간이벤트끼리는_시각순()
    {
        var d = new DateTime(2026, 8, 15);
        var list = new List<KEvent>
        {
            Timed("셋째", d.AddHours(17)),
            Timed("첫째", d.AddHours(9)),
            Timed("둘째", d.AddHours(13).AddMinutes(30)),
        };

        var sorted = KEventRepository.SortForDisplay(list);

        Assert.Equal(new[] { "첫째", "둘째", "셋째" }, sorted.Select(e => e.Title));
    }

    [Fact]
    public void 시각까지_같으면_제목순_안정정렬()
    {
        var at = new DateTime(2026, 8, 15, 10, 0, 0);
        var list = new List<KEvent>
        {
            Timed("나 회의", at),
            Timed("가 회의", at),
        };

        var sorted = KEventRepository.SortForDisplay(list);

        Assert.Equal(new[] { "가 회의", "나 회의" }, sorted.Select(e => e.Title));
    }

    [Fact]
    public void 같은_날_종일이_여러개면_제목순()
    {
        var d = new DateTime(2026, 8, 15);
        var list = new List<KEvent>
        {
            AllDay("나 행사", d),
            AllDay("가 행사", d),
        };

        var sorted = KEventRepository.SortForDisplay(list);

        Assert.Equal(new[] { "가 행사", "나 행사" }, sorted.Select(e => e.Title));
    }

    [Fact]
    public void 빈_목록도_안전하다()
        => Assert.Empty(KEventRepository.SortForDisplay(new List<KEvent>()));
}
