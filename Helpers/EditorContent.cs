using System.Linq;
using WinUIRichEditor.Documents;

namespace NewSchool.Helpers;

/// <summary>
/// 서식 편집기의 본문이 비었는가 — 글(게시판 글·수업 일지)을 저장하기 전에 "내용을 입력하세요" 를 가른다.
///
/// <para><b>글자도 없고 그림·표·구분선도 없으면</b> 빈 것이다. 예전 판정은 둘이 어긋나 있었다.
/// 게시판 글은 HTML(<c>ToHtml</c>)이 빈 문자열인지 봤는데, 편집기는 한 번 쓰고 지우면 빈 문단
/// (<c>&lt;p&gt;&lt;/p&gt;</c>)을 돌려줘 빈 글이 그대로 저장됐다. 수업 일지는 글자만 봐서
/// 사진 한 장만 붙인 일지를 막았다.</para>
/// </summary>
public static class EditorContent
{
    public static bool IsBlank(FlowDocument? document)
        => document == null || document.Blocks.All(block =>
            block is Paragraph paragraph && paragraph.Inlines.All(inline =>
                inline is Run run && string.IsNullOrWhiteSpace(run.Text)));
}
