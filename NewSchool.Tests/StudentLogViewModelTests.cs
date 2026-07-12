using NewSchool.Models;
using NewSchool.ViewModels;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// StudentLogViewModel 변환 로직 테스트 (TEST_PLAN 4단계).
/// DB 의존을 피하기 위해 카테고리 → 라벨/색상 매핑을 순수 정적 함수로 추출하여 검증한다.
/// </summary>
public class StudentLogViewModelTests
{
    [Theory]
    [InlineData(LogCategory.교과활동, "교과")]
    [InlineData(LogCategory.동아리활동, "동아리")]
    [InlineData(LogCategory.봉사활동, "봉사")]
    [InlineData(LogCategory.진로활동, "진로")]
    [InlineData(LogCategory.자율활동, "자율")]
    [InlineData(LogCategory.개인별세특, "세특")]
    [InlineData(LogCategory.종합의견, "행특")]
    [InlineData(LogCategory.상담기록, "상담")]
    [InlineData(LogCategory.기타, "기타")]
    public void 카테고리_라벨_매핑(LogCategory category, string expected)
        => Assert.Equal(expected, StudentLogViewModel.ToCategoryLabel(category));

    [Fact]
    public void 전체_카테고리는_전체_라벨()
        => Assert.Equal("전체", StudentLogViewModel.ToCategoryLabel(LogCategory.전체));

    [Theory]
    [InlineData(LogCategory.교과활동, "#FF6B9BD1")]
    [InlineData(LogCategory.동아리활동, "#FF9B59B6")]
    [InlineData(LogCategory.봉사활동, "#FF27AE60")]
    [InlineData(LogCategory.진로활동, "#FFFF9800")]
    [InlineData(LogCategory.자율활동, "#FF3498DB")]
    [InlineData(LogCategory.개인별세특, "#FFE74C3C")]
    [InlineData(LogCategory.종합의견, "#FF95A5A6")]
    [InlineData(LogCategory.상담기록, "#FFF39C12")]
    [InlineData(LogCategory.기타, "#FF7F8C8D")]
    public void 카테고리_색상_매핑(LogCategory category, string expected)
        => Assert.Equal(expected, StudentLogViewModel.ToCategoryColor(category));

    [Fact]
    public void 전체_카테고리는_밝은회색()
        => Assert.Equal("#FFBDC3C7", StudentLogViewModel.ToCategoryColor(LogCategory.전체));

    [Fact]
    public void 모든_카테고리는_색상이_ARGB_8자리_HEX()
    {
        foreach (LogCategory c in System.Enum.GetValues<LogCategory>())
        {
            var color = StudentLogViewModel.ToCategoryColor(c);
            Assert.StartsWith("#FF", color);
            Assert.Equal(9, color.Length);
        }
    }
}
