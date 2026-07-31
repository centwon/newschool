using NewSchool.Controls;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 알림장 헤더 제거 회귀 테스트.
///
/// 전체 편집창은 "헤더 + 본문"으로 열고 저장할 때 헤더만 떼어낸다.
/// 이 정규식이 어긋나면 헤더가 본문에 눌러앉아 편집할 때마다 중복되거나,
/// 반대로 본문 첫 줄이 잘려나간다. 2026-07-31 에 표 → 두 줄 텍스트로 바꿨으므로
/// 예전에 저장된 알림장(표 형식)도 계속 떨어져야 한다.
/// </summary>
public class NoticeHeaderTests
{
    private const string Body = "<p>1. 준비물을 챙겨 오세요.</p>";

    [Fact]
    public void 현재_형식_헤더가_제거된다()
    {
        string header =
            "<div data-notice-header='true'>" +
            "<p style='text-align:center;margin:0;'><span style='font-size:16px;'><strong>알림장</strong></span></p>" +
            "<p style='text-align:right;margin:0;'><span style='font-size:14px;'><strong>2026년 7월 31일(금)</strong></span></p>" +
            "</div>";

        Assert.Equal(Body, ClassDiaryBox.RemoveNoticeHeaderHtml(header + Body));
    }

    [Fact]
    public void 구_형식_표_헤더도_제거된다()
    {
        // 2026-07-31 이전에 저장된 알림장
        string header =
            "<table style='border-collapse:collapse;width:100%;border:0;' data-notice-header='true'><tbody>" +
            "<tr><td colspan='2'><span style='font-size:18px;'>알 림 장</span></td></tr>" +
            "<tr><td><span>1학년 2반</span></td><td><span>2026년 7월 31일(금)</span></td></tr>" +
            "</tbody></table>";

        Assert.Equal(Body, ClassDiaryBox.RemoveNoticeHeaderHtml(header + Body));
    }

    [Fact]
    public void 편집기가_data속성을_지워도_제거된다()
    {
        // 감싼 div 와 data-* 가 사라진 경우 — 문단 두 개만 남는다
        string header =
            "<p style='text-align:center;'><strong>알림장</strong></p>" +
            "<p style='text-align:right;'><strong>2026년 7월 31일(금)</strong></p>";

        Assert.Equal(Body, ClassDiaryBox.RemoveNoticeHeaderHtml(header + Body));
    }

    [Fact]
    public void 헤더가_없으면_본문을_건드리지_않는다()
    {
        Assert.Equal(Body, ClassDiaryBox.RemoveNoticeHeaderHtml(Body));
    }

    [Fact]
    public void 본문에_알림장이라는_말이_있어도_맨_앞이_아니면_남는다()
    {
        string html = "<p>오늘 안내</p><p>알림장에 적어 두세요.</p>";
        Assert.Equal(html, ClassDiaryBox.RemoveNoticeHeaderHtml(html));
    }

    [Fact]
    public void 빈_입력은_빈_문자열()
    {
        Assert.Equal(string.Empty, ClassDiaryBox.RemoveNoticeHeaderHtml("   "));
    }
}
