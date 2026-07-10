using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>엑셀 학생 일괄 입력 파서 테스트 (TEST_PLAN 3단계).</summary>
public class ImportParsingTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("15", 15)]
    [InlineData("1학년", 1)]
    [InlineData("3반", 3)]
    [InlineData("12번", 12)]
    [InlineData(" 7 ", 7)]
    [InlineData("2학년3반", 23)]   // 숫자만 모음 (현 계약)
    public void TryParseNumber_숫자추출_성공(string input, int expected)
    {
        Assert.True(ImportParsing.TryParseNumberFromText(input, out int result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("이름")]
    [InlineData("없음")]
    public void TryParseNumber_숫자없으면_false(string? input)
    {
        Assert.False(ImportParsing.TryParseNumberFromText(input, out int result));
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("여", "여")]
    [InlineData("여자", "여")]
    [InlineData("F", "여")]
    [InlineData("f", "여")]
    [InlineData("Female", "여")]
    [InlineData(" 여 ", "여")]
    public void NormalizeSex_여성_변형(string input, string expected)
        => Assert.Equal(expected, ImportParsing.NormalizeSex(input));

    [Theory]
    [InlineData("남")]
    [InlineData("남자")]
    [InlineData("M")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("미상")]
    public void NormalizeSex_그외는_남_기본값(string? input)
        => Assert.Equal("남", ImportParsing.NormalizeSex(input));
}
