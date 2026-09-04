using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 인쇄 옵션의 <b>개수 입력</b>이 실제 출력 개수로 바뀌는 규칙 — 53차(인쇄 축).
///
/// <para>실측으로 잡은 결함이다. 학생카드 인쇄에서 [학생 생활 기록]을 켜고 [전체 기록]을
/// 고른 뒤 <b>최대 출력 개수 칸을 비우고</b> 인쇄했더니, 누가기록이 <b>한 건도</b> 실리지
/// 않은 PDF 가 나왔다 — 그리고 아무 안내도 없었다. <c>NumberBox</c> 는 칸을 비우면
/// <c>Value</c> 가 <see cref="double.NaN"/> 인데(47차에 적어 둔 함정),
/// <c>(int)(MaxLogCount?.Value ?? 50)</c> 는 <c>??</c> 로 NaN 을 거르지 못한다.</para>
/// </summary>
public class PrintCountTests
{
    private const int Fallback = 50;

    /// <summary>이 시험의 핵심 — 빈 칸이 0 이 되면 안 된다.</summary>
    [Fact]
    public void 칸이_비어_있으면_기본값을_쓴다()
    {
        Assert.Equal(Fallback, PrintCount.Resolve(double.NaN, Fallback));
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void 무한대도_기본값으로_돌린다(double value)
    {
        Assert.Equal(Fallback, PrintCount.Resolve(value, Fallback));
    }

    [Fact]
    public void 적어_넣은_수는_그대로_쓴다()
    {
        Assert.Equal(7, PrintCount.Resolve(7, Fallback));
        Assert.Equal(1, PrintCount.Resolve(1, Fallback));
        Assert.Equal(500, PrintCount.Resolve(500, Fallback));
    }

    /// <summary>화면의 <c>Minimum</c>·<c>Maximum</c> 밖으로 나가면 그 경계로 붙인다.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(9999, 500)]
    public void 범위를_벗어나면_경계로_붙인다(double value, int expected)
    {
        Assert.Equal(expected, PrintCount.Resolve(value, Fallback));
    }

    /// <summary>"최대 N개" 라고 해 놓고 N+1 개를 내면 말과 어긋난다 — 올림이 아니라 내림이다.</summary>
    [Fact]
    public void 소수점은_내린다()
    {
        Assert.Equal(2, PrintCount.Resolve(2.9, Fallback));
    }

    /// <summary>기본값 자체가 범위 밖이어도 경계 안으로 들어와야 한다.</summary>
    [Fact]
    public void 기본값도_범위_안으로_들인다()
    {
        Assert.Equal(1, PrintCount.Resolve(double.NaN, fallback: 0));
        Assert.Equal(500, PrintCount.Resolve(double.NaN, fallback: 9999));
    }
}
