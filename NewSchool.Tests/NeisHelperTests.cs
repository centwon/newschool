using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

public class NeisHelperTests
{
    #region CountByte

    [Fact]
    public void CountByte_Null_Returns0()
    {
        Assert.Equal(0, NeisHelper.CountByte(null!));
    }

    [Fact]
    public void CountByte_Empty_Returns0()
    {
        Assert.Equal(0, NeisHelper.CountByte(string.Empty));
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("abc", 3)]
    [InlineData("Hello", 5)]
    [InlineData("0123456789", 10)]
    [InlineData(" ", 1)]
    [InlineData("!@#$%^&*()", 10)]
    public void CountByte_Ascii_OneBytePerChar(string text, int expected)
    {
        Assert.Equal(expected, NeisHelper.CountByte(text));
    }

    [Theory]
    [InlineData("가", 3)]
    [InlineData("한글", 6)]
    [InlineData("가나다라마", 15)]
    [InlineData("힣", 3)]    // 한글 음절 영역 끝
    [InlineData("각", 3)]
    public void CountByte_Hangul_ThreeBytesPerChar(string text, int expected)
    {
        Assert.Equal(expected, NeisHelper.CountByte(text));
    }

    [Theory]
    [InlineData("한글a", 7)]   // 3+3+1
    [InlineData("AB가", 5)]    // 1+1+3
    [InlineData("1번 학생", 1 + 3 + 1 + 3 + 3)] // '1','번',' ','학','생' = 1+3+1+3+3 = 11
    public void CountByte_MixedHangulAscii_SumsCorrectly(string text, int expected)
    {
        Assert.Equal(expected, NeisHelper.CountByte(text));
    }

    [Theory]
    [InlineData("漢字", 6)]      // 한자도 3바이트
    [InlineData("あ", 3)]        // 히라가나도 3바이트 (기타 유니코드 룰)
    [InlineData("😀", 6)]       // 서로게이트 페어 → char 2개 × 3바이트
    public void CountByte_NonAsciiUnicode_ThreeBytesPerChar(string text, int expected)
    {
        Assert.Equal(expected, NeisHelper.CountByte(text));
    }

    [Fact]
    public void CountByte_HangulJamoOutsideMainBlock_TreatedAsOtherUnicode()
    {
        // ㄱ (U+3131) 은 한글 음절 영역(가-힣) 밖이지만 ASCII 도 아님 → 3바이트
        Assert.Equal(3, NeisHelper.CountByte("ㄱ"));
    }

    [Fact]
    public void CountByte_LongStringAtLimit_CountsExactly()
    {
        // 1500바이트 = 한글 500자
        var text = new string('가', 500);
        Assert.Equal(1500, NeisHelper.CountByte(text));
    }

    #endregion

    #region GetMaxBytes

    [Theory]
    [InlineData("교과활동", 1500)]
    [InlineData("개인별세특", 1500)]
    [InlineData("자율활동", 1500)]
    [InlineData("동아리활동", 1500)]
    [InlineData("종합의견", 1500)]
    public void GetMaxBytes_StandardAreas_Returns1500(string area, int expected)
    {
        Assert.Equal(expected, NeisHelper.GetMaxBytes(area));
    }

    [Fact]
    public void GetMaxBytes_진로활동_Returns2100()
    {
        Assert.Equal(2100, NeisHelper.GetMaxBytes("진로활동"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("미지정영역")]
    [InlineData("Lorem")]
    public void GetMaxBytes_Unknown_ReturnsDefault1500(string area)
    {
        Assert.Equal(1500, NeisHelper.GetMaxBytes(area));
    }

    #endregion
}
