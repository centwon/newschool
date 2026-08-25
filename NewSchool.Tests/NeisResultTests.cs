using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// NEIS 결과 코드 판정 회귀 테스트 —
/// 급식·학사일정·학교검색 셋이 <b>같은 기준</b>을 써야 한다.
/// 예전에는 각자 규칙을 들고 있었고, 급식은 <c>StartsWith("INFO")</c> 로 판정해
/// 인증키 오류(INFO-300)를 정상으로 통과시켰다 — 결과가 0건이 되어 "오늘 급식 없음" 이 됐다.
/// </summary>
public class NeisResultTests
{
    [Theory]
    [InlineData("INFO-000")]   // 정상
    [InlineData("INFO-200")]   // 해당 데이터 없음 — 오류가 아니라 결과 0건
    [InlineData("")]           // 코드 없는 응답
    [InlineData(null)]
    public void 요청이_성립한_코드는_오류가_아니다(string? code)
    {
        Assert.True(NeisResult.IsSuccess(code));
        Assert.False(NeisResult.IsError(code));
    }

    [Theory]
    [InlineData("INFO-300")]   // 인증키가 유효하지 않습니다 — INFO 로 시작하지만 오류다
    [InlineData("ERROR-300")]
    [InlineData("ERROR-337")]  // 일일 트래픽 초과
    [InlineData("ERROR-500")]
    public void 요청이_실패한_코드는_오류다(string code)
    {
        Assert.True(NeisResult.IsError(code));
        Assert.False(NeisResult.IsSuccess(code));
        Assert.False(NeisResult.IsNoData(code));
    }

    [Fact]
    public void 데이터_없음은_오류와_구분된다()
    {
        Assert.True(NeisResult.IsNoData("INFO-200"));
        Assert.False(NeisResult.IsNoData("INFO-000"));
        Assert.False(NeisResult.IsNoData("INFO-300"));
    }

    [Fact]
    public void 사유는_메시지를_쓰고_비면_코드라도_보여준다()
    {
        Assert.Equal("인증키가 유효하지 않습니다.",
            NeisResult.Describe("INFO-300", "인증키가 유효하지 않습니다."));
        Assert.Equal("INFO-300", NeisResult.Describe("INFO-300", ""));
        Assert.Equal("INFO-300", NeisResult.Describe("INFO-300", null));
    }
}
