using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using WinUIRichEditor.Documents;
using WinUIRichEditor.Formatters;
using Doc = Microsoft.UI.Xaml.Documents;

namespace NewSchool.Helpers;

/// <summary>
/// 서식 편집기 본문(.flow)을 편집기 없이 <see cref="RichTextBlock"/> 로 그린다 — 메모판 미리보기.
///
/// <para>편집기는 처음 하나가 Win2D 장치 때문에 약 24MB 라 메모판은 누를 때 만든다. 그 전에는 본문 글자만
/// 보여 줬는데, 서식이 빠져 가운데 맞춘 첫 줄이 왼쪽에 있다가 누르는 순간 가운데로 뛰었다(2026-10-18).
/// <c>DocumentPackage.Load</c> 는 모델만 읽고 Win2D 장치를 만들지 않으므로 서식을 살려 그린다.</para>
///
/// <para>줄 높이는 편집기 규칙을 따른다: 문단의 가장 큰 글자 × <c>LineSpacing</c>(없으면
/// <c>LineHeight</c> 고정값, 그것도 없으면 160%), 문단 아래 간격은 <c>MarginBottom</c>.</para>
/// </summary>
public static class FlowPreview
{
    private const double DefaultFontSizePt = 10;    // 편집기 Run.FontSize 기본값
    private const double DefaultLineSpacing = 1.6;  // 편집기 기본 줄 간격(HWP 160%)
    private const double ListIndentPerLevel = 20;

    /// <summary><paramref name="flow"/> 를 <paramref name="target"/> 에 채운다. 읽지 못하면 false — 부르는 쪽이 글자만 보여 준다.</summary>
    public static bool TryFill(RichTextBlock target, byte[]? flow)
    {
        target.Blocks.Clear();
        if (flow is not { Length: > 0 }) return false;

        FlowDocument doc;
        try
        {
            using var ms = new MemoryStream(flow);
            doc = DocumentPackage.Load(ms);
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("FlowPreview", "메모 본문을 미리보기로 읽지 못했다", ex);
            return false;
        }

        var listCounters = new Dictionary<int, int>();
        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    target.Blocks.Add(BuildParagraph(p, listCounters));
                    break;
                case ImageBlock:
                    target.Blocks.Add(Placeholder("[그림]", block));
                    listCounters.Clear();
                    break;
                case DividerBlock:
                    target.Blocks.Add(Placeholder("―――――", block));
                    listCounters.Clear();
                    break;
                default:
                    target.Blocks.Add(Placeholder("[표]", block));
                    listCounters.Clear();
                    break;
            }
        }
        return true;
    }

    private static Doc.Paragraph BuildParagraph(Paragraph p, Dictionary<int, int> listCounters)
    {
        var para = new Doc.Paragraph
        {
            TextAlignment = p.TextAlignment,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        double maxPt = p.Inlines.OfType<Run>().Select(r => r.FontSize > 0 ? r.FontSize : DefaultFontSizePt)
                                .DefaultIfEmpty(DefaultFontSizePt).Max();
        para.LineHeight = !double.IsNaN(p.LineSpacing) ? PtToDip(maxPt) * p.LineSpacing
                        : !double.IsNaN(p.LineHeight) ? p.LineHeight
                        : PtToDip(maxPt) * DefaultLineSpacing;

        double left = p.Indent;
        if (p.IsListItem)
        {
            left += p.ListLevel * ListIndentPerLevel;
            // 같은 수준의 번호는 이어지고, 목록이 끊기면 처음부터.
            foreach (var deeper in listCounters.Keys.Where(k => k > p.ListLevel).ToList()) listCounters.Remove(deeper);
            int num = listCounters.TryGetValue(p.ListLevel, out var n) ? n + 1 : 1;
            listCounters[p.ListLevel] = num;
            para.Inlines.Add(new Doc.Run { Text = p.ListType == ListKind.Ordered ? $"{num}. " : "• " });
        }
        else listCounters.Clear();

        para.Margin = new Thickness(left, double.IsNaN(p.MarginTop) ? 0 : p.MarginTop, p.MarginRight, p.MarginBottom);

        foreach (var inline in p.Inlines)
        {
            switch (inline)
            {
                case Run r when !string.IsNullOrEmpty(r.Text):
                    para.Inlines.Add(BuildRun(r));
                    break;
                case InlineImage:
                    para.Inlines.Add(new Doc.Run { Text = "[그림]", Foreground = Secondary() });
                    break;
                case InlineTable:
                    para.Inlines.Add(new Doc.Run { Text = "[표]", Foreground = Secondary() });
                    break;
            }
        }

        // 빈 문단도 편집기에서는 한 줄 높이를 차지한다 — 비워 두면 RichTextBlock 이 접어 버린다.
        if (para.Inlines.Count == 0) para.Inlines.Add(new Doc.Run { Text = "​" });
        return para;
    }

    private static Doc.Run BuildRun(Run r)
    {
        var run = new Doc.Run
        {
            Text = r.Text,
            FontSize = PtToDip(r.FontSize > 0 ? r.FontSize : DefaultFontSizePt),
            FontWeight = r.FontWeight,
            FontStyle = r.FontStyle,
        };
        if (!string.IsNullOrEmpty(r.FontFamily)) run.FontFamily = new FontFamily(r.FontFamily);
        if (r.Foreground is { } color) run.Foreground = new SolidColorBrush(color);

        var deco = TextDecorations.None;
        if (r.TextDecorations.HasFlag(TextDecorationFlags.Underline)) deco |= TextDecorations.Underline;
        if (r.TextDecorations.HasFlag(TextDecorationFlags.Strikethrough)) deco |= TextDecorations.Strikethrough;
        run.TextDecorations = deco;
        return run;
    }

    private static Doc.Paragraph Placeholder(string text, Block block)
    {
        var para = new Doc.Paragraph
        {
            Margin = new Thickness(block.Indent, 0, 0, block.MarginBottom),
        };
        para.Inlines.Add(new Doc.Run { Text = text, Foreground = Secondary() });
        return para;
    }

    private static Brush Secondary()
        => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private static double PtToDip(double pt) => pt * 96.0 / 72.0;
}
