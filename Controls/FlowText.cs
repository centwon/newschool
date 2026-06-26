using System.IO;
using System.Text;
using WinUIRichEditor.Formatters;

namespace NewSchool.Controls;

/// <summary>
/// 평문 ↔ .flow 변환 헬퍼. 리치 에디터(RichTextEditor) 없이 Post.Content(.flow BLOB)를 다뤄야 하는
/// 평문 입력 화면(예: MaterialEditDialog) 용. 모델/포매터만 사용해 헤드리스 동작.
/// </summary>
public static class FlowText
{
    /// <summary>평문을 .flow 패키지 바이트로 변환 (단일 문단, 줄바꿈은 &lt;br&gt;).</summary>
    public static byte[] FromPlainText(string? text)
    {
        var doc = HtmlDocumentFormatter.ParseHtml(PlainToHtml(text));
        using var ms = new MemoryStream();
        DocumentPackage.Save(doc, ms);
        return ms.ToArray();
    }

    private static string PlainToHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder("<p>");
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '\n': sb.Append("<br>"); break;
                case '\r': break;
                default: sb.Append(c); break;
            }
        }
        sb.Append("</p>");
        return sb.ToString();
    }
}
