using NewSchool.Controls;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 알림장 헤더 삽입 규칙 회귀 테스트.
///
/// 헤더("알림장" + 날짜)는 <b>처음 작성할 때만</b> 넣고, 그 뒤로는 본문의 일부다.
/// 예전에는 편집창을 열 때마다 헤더를 붙였다가 저장할 때 정규식으로 떼어냈는데,
/// 그 정규식이 어긋나면 헤더가 눌러앉아 편집할 때마다 중복되거나 본문 첫 줄이 잘렸다.
/// 이 규칙이 무너지면 같은 문제가 되살아난다.
/// </summary>
public class NoticeHeaderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<p><br></p>")]
    [InlineData("<p>&nbsp;</p>")]
    public void 내용이_비어_있으면_헤더를_넣는다(string html)
    {
        Assert.True(ClassDiaryBox.ShouldInsertNoticeHeader(html));
    }

    [Fact]
    public void null_도_빈_것으로_본다()
    {
        Assert.True(ClassDiaryBox.ShouldInsertNoticeHeader(null));
    }

    [Fact]
    public void 이미_쓴_알림장에는_헤더를_다시_넣지_않는다()
    {
        // 헤더가 이미 들어 있는 경우 — 두 번 붙으면 안 된다
        string withHeader =
            "<div data-notice-header='true'>" +
            "<p style='text-align:center;margin:0;'><span style='font-size:16px;'><strong>알림장</strong></span></p>" +
            "<p style='text-align:right;margin:0;'><span style='font-size:14px;'><strong>2026년 7월 31일(금)</strong></span></p>" +
            "</div><p>1. 준비물을 챙겨 오세요.</p>";

        Assert.False(ClassDiaryBox.ShouldInsertNoticeHeader(withHeader));
    }

    [Fact]
    public void 헤더를_지운_알림장에도_다시_넣지_않는다()
    {
        // 사용자가 일부러 헤더를 지웠을 수 있다 — 다시 붙이면 의도를 거스른다
        Assert.False(ClassDiaryBox.ShouldInsertNoticeHeader("<p>1. 준비물을 챙겨 오세요.</p>"));
    }

    [Fact]
    public void 태그가_많아도_글자가_있으면_헤더를_넣지_않는다()
    {
        Assert.False(ClassDiaryBox.ShouldInsertNoticeHeader("<div><p><span><strong>ㄱ</strong></span></p></div>"));
    }
}
