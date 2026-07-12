using System;
using NewSchool;
using NewSchool.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 현재 교시 판정(순수 함수 Functions.GetPeriodAt) 경계 테스트 (TEST_PLAN 3단계).
/// 표준 설정: 등교 08:30, 조례 10분, 쉬는시간 10분, 1교시 50분, 점심 60분.
/// → 1교시 08:50, 4교시 11:50~12:40, 점심 12:40~13:40, 5교시 13:40~.
/// </summary>
public class GetPeriodAtTests
{
    private static readonly PeriodTimes Std = new(
        DayStarting: new TimeSpan(8, 30, 0),
        AssemblyTime: TimeSpan.FromMinutes(10),
        BreakTime: TimeSpan.FromMinutes(10),
        OnePeriod: TimeSpan.FromMinutes(50),
        LunchTime: TimeSpan.FromMinutes(60));

    private const int Tue = 2;  // 평일(7교시)
    private const int Mon = 1;  // 6교시
    private const int Sun = 0;
    private const int Sat = 6;

    private static Period At(int h, int m, int dow = Tue)
        => Functions.GetPeriodAt(new TimeSpan(h, m, 0), dow, Std);

    [Theory]
    [InlineData(Sun)]
    [InlineData(Sat)]
    public void 주말은_항상_방과후(int dow)
        => Assert.Equal("방과후", At(10, 0, dow).Name);

    [Fact]
    public void 등교전은_방과후() => Assert.Equal("방과후", At(8, 0).Name);

    [Fact]
    public void 조례시간() => Assert.Equal("조례", At(8, 35).Name);

    [Fact]
    public void 조례_1교시_사이_휴식() => Assert.Equal("휴식: 조례~1교시", At(8, 45).Name);

    [Theory]
    [InlineData(9, 0, 1)]    // 1교시 08:50~09:40
    [InlineData(10, 0, 2)]   // 2교시 09:50~10:40
    [InlineData(12, 0, 4)]   // 4교시 11:50~12:40
    [InlineData(14, 0, 5)]   // 5교시 13:40~14:30
    public void 교시_판정(int h, int m, int expectedIndex)
    {
        var p = At(h, m);
        Assert.Equal(expectedIndex, p.Index);
        Assert.Equal($"{expectedIndex}교시", p.Name);
    }

    [Fact]
    public void 교시_사이_휴식() => Assert.Equal("휴식: 1교시~2교시", At(9, 45).Name);

    [Fact]
    public void 점심시간_4교시후() => Assert.Equal("점심 시간", At(13, 0).Name);

    [Fact]
    public void 교시경계_정각은_다음_휴식으로()
    {
        // 1교시 end 09:40 정각은 교시(< end)에 안 걸리고 휴식으로 넘어감
        Assert.Equal("휴식: 1교시~2교시", At(9, 40).Name);
    }

    [Fact]
    public void 방과후_수업종료_20분후()
    {
        // 7교시 학교 종료 16:30 → +20분(16:50) 이후는 방과후
        Assert.Equal("방과후", At(17, 0).Name);
        // 종료~+10분: 청소, +10~+20분: 종례
        Assert.Equal("청소", At(16, 35).Name);
        Assert.Equal("종례", At(16, 45).Name);
    }

    [Fact]
    public void 월요일은_6교시라_종료가_더_이름()
    {
        // 월요일 6교시: 종료 = 08:50 + 50*6 + 10*4 + 60 = 08:50 + 400 = 15:30
        // 7교시는 존재하지 않음 → 15:30 이후는 청소/종례/방과후
        Assert.Equal("방과후", At(16, 0, Mon).Name);
    }

    // ── 요일별 교시 수 설정화 ──────────────────────────────────

    [Fact]
    public void 설정된_교시수를_따름_월요일_7교시()
    {
        // 월요일도 7교시인 시정: 기본값(월 6교시)이면 방과후일 시각에 7교시로 판정돼야 함
        var t = Std with { Periods = new PeriodCounts(7, 7, 7, 7, 7) };
        var p = Functions.GetPeriodAt(new TimeSpan(16, 0, 0), Mon, t);
        Assert.Equal(7, p.Index);
    }

    [Fact]
    public void 설정된_교시수를_따름_화요일_5교시()
    {
        // 화요일 5교시 시정: 5교시 종료(14:30) 후 청소·종례 지나면 방과후
        var t = Std with { Periods = new PeriodCounts(6, 5, 6, 5, 5) };
        Assert.Equal(5, Functions.GetPeriodAt(new TimeSpan(14, 0, 0), Tue, t).Index);
        Assert.Equal("방과후", Functions.GetPeriodAt(new TimeSpan(15, 0, 0), Tue, t).Name);
    }
}

/// <summary>요일별 교시 수 직렬화/파싱 (PeriodCounts)</summary>
public class PeriodCountsTests
{
    [Fact]
    public void 기본값은_기존_하드코딩과_동일() // 월·수 6교시, 화·목·금 7교시
        => Assert.Equal(new PeriodCounts(6, 7, 6, 7, 7), PeriodCounts.Default);

    [Fact]
    public void 직렬화_왕복()
    {
        var p = new PeriodCounts(4, 5, 6, 7, 8);
        Assert.Equal("4,5,6,7,8", p.Serialize());
        Assert.Equal(p, PeriodCounts.Parse(p.Serialize()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("6,7,6,7")]        // 개수 부족
    [InlineData("6,7,6,7,7,7")]    // 개수 초과
    [InlineData("6,7,x,7,7")]      // 숫자 아님
    [InlineData("0,7,6,7,7")]      // 범위 밖 (1 미만)
    [InlineData("6,7,13,7,7")]     // 범위 밖 (12 초과)
    public void 잘못된_입력은_기본값(string? input)
        => Assert.Equal(PeriodCounts.Default, PeriodCounts.Parse(input));

    [Fact]
    public void 공백_허용()
        => Assert.Equal(new PeriodCounts(6, 7, 6, 7, 7), PeriodCounts.Parse(" 6, 7 ,6,7,7 "));

    [Theory]
    [InlineData(0, 0)]  // 일
    [InlineData(1, 6)]  // 월
    [InlineData(3, 4)]  // 수
    [InlineData(5, 7)]  // 금
    [InlineData(6, 0)]  // 토
    public void ForDay_요일별_조회(int dow, int expected)
        => Assert.Equal(expected, new PeriodCounts(6, 5, 4, 5, 7).ForDay(dow));
}
