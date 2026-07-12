using System;
using NewSchool.Scheduler;
using Windows.UI.Text;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// KEvent 할일/일정 변환·표시 로직 테스트 (TEST_PLAN 4단계).
/// 색상 매핑 · 종일/시간 라벨 · 상태 판정 · 할일 완료 취소선을 검증한다.
/// </summary>
public class KEventTests
{
    [Theory]
    [InlineData("1", "#7986CB")]
    [InlineData("5", "#F6BF26")]
    [InlineData("7", "#039BE5")]
    [InlineData("11", "#D50000")]
    public void ColorId_HEX_변환(string colorId, string expected)
        => Assert.Equal(expected, KEvent.ColorIdToHex(colorId));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("12")]
    [InlineData("abc")]
    public void 알수없는_ColorId는_빈문자열(string colorId)
        => Assert.Equal(string.Empty, KEvent.ColorIdToHex(colorId));

    [Fact]
    public void DisplayColor_ColorId_우선()
    {
        var e = new KEvent { ColorId = "11", CalendarColor = "#123456" };
        Assert.Equal("#D50000", e.DisplayColor);
    }

    [Fact]
    public void DisplayColor_ColorId_없으면_캘린더색_폴백()
    {
        var e = new KEvent { ColorId = "", CalendarColor = "#123456" };
        Assert.Equal("#123456", e.DisplayColor);
    }

    [Fact]
    public void DisplayColor_둘다_없으면_빈문자열()
    {
        var e = new KEvent { ColorId = "", CalendarColor = "" };
        Assert.Equal(string.Empty, e.DisplayColor);
    }

    [Fact]
    public void TimeLabel_종일이면_빈문자열()
    {
        var e = new KEvent { IsAllday = true, Start = new DateTime(2026, 7, 12, 9, 30, 0) };
        Assert.Equal(string.Empty, e.TimeLabel);
    }

    [Fact]
    public void TimeLabel_시간있으면_HHmm()
    {
        var e = new KEvent { IsAllday = false, Start = new DateTime(2026, 7, 12, 9, 5, 0) };
        Assert.Equal("09:05", e.TimeLabel);
    }

    [Theory]
    [InlineData("cancelled", true, false)]
    [InlineData("tentative", false, true)]
    [InlineData("confirmed", false, false)]
    public void 상태_판정(string status, bool cancelled, bool tentative)
    {
        var e = new KEvent { Status = status };
        Assert.Equal(cancelled, e.IsCancelled);
        Assert.Equal(tentative, e.IsTentative);
    }

    [Theory]
    [InlineData("task", true)]
    [InlineData("event", false)]
    public void 할일_판정(string itemType, bool expected)
        => Assert.Equal(expected, new KEvent { ItemType = itemType }.IsTaskItem);

    [Fact]
    public void 기본_ItemType은_event()
        => Assert.False(new KEvent().IsTaskItem);

    [Fact]
    public void 완료된_할일은_취소선()
        => Assert.Equal(TextDecorations.Strikethrough, new KEvent { IsDone = true }.TextDecorations);

    [Fact]
    public void 미완료_할일은_장식없음()
        => Assert.Equal(TextDecorations.None, new KEvent { IsDone = false }.TextDecorations);

    [Fact]
    public void ToString_제목과_시간범위_포함()
    {
        var e = new KEvent
        {
            Title = "회의",
            Start = new DateTime(2026, 7, 12, 9, 0, 0),
            End = new DateTime(2026, 7, 12, 10, 0, 0)
        };
        Assert.Equal("회의 (2026-07-12 09:00~10:00)", e.ToString());
    }
}
