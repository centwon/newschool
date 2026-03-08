using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NewSchool.Board.Services;

/// <summary>
/// 게시글 PDF 생성 서비스
/// </summary>
public class PostPrintService
{
    /// <summary>
    /// 게시글 + 댓글을 PDF로 생성
    /// </summary>
    public string GeneratePostPdf(Post post, List<Comment> comments)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var fileName = $"게시글_{SanitizeFileName(post.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NewSchool",
            "Prints",
            fileName);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);

                page.Header().Element(c => ComposeHeader(c, post));
                page.Content().Element(c => ComposeContent(c, post, comments));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    private void ComposeHeader(IContainer container, Post post)
    {
        container.Column(col =>
        {
            // 제목
            col.Item().Text(post.Title)
                .FontSize(18)
                .Bold()
                .FontFamily("Malgun Gothic");

            col.Item().PaddingTop(6).Row(row =>
            {
                if (!string.IsNullOrEmpty(post.Category))
                {
                    row.AutoItem().Background(Colors.Blue.Lighten4)
                        .PaddingVertical(2).PaddingHorizontal(6)
                        .Text($"[{post.Category}]")
                        .FontSize(9)
                        .FontFamily("Malgun Gothic");
                    row.AutoItem().PaddingLeft(8);
                }

                if (!string.IsNullOrEmpty(post.Subject))
                {
                    row.AutoItem().Text($"주제: {post.Subject}")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Medium)
                        .FontFamily("Malgun Gothic");
                    row.AutoItem().PaddingLeft(8);
                }

                row.AutoItem().Text(post.DateTime.ToString("yyyy-MM-dd HH:mm"))
                    .FontSize(10)
                    .FontColor(Colors.Grey.Medium)
                    .FontFamily("Malgun Gothic");
            });

            col.Item().PaddingTop(8)
                .LineHorizontal(1)
                .LineColor(Colors.Grey.Lighten2);
        });
    }

    private void ComposeContent(IContainer container, Post post, List<Comment> comments)
    {
        container.Column(col =>
        {
            // 본문 (HTML 태그 제거)
            col.Item().PaddingTop(12).Text(StripHtml(post.Content))
                .FontSize(11)
                .FontFamily("Malgun Gothic")
                .LineHeight(1.6f);

            // 댓글 섹션
            if (comments.Count > 0)
            {
                col.Item().PaddingTop(20)
                    .LineHorizontal(1)
                    .LineColor(Colors.Grey.Lighten2);

                col.Item().PaddingTop(10).Text($"댓글 ({comments.Count})")
                    .FontSize(13)
                    .Bold()
                    .FontFamily("Malgun Gothic");

                foreach (var comment in comments)
                {
                    col.Item().PaddingTop(8).Element(c => ComposeComment(c, comment));
                }
            }
        });
    }

    private void ComposeComment(IContainer container, Comment comment)
    {
        container.Background(Colors.Grey.Lighten4)
            .BorderLeft(3)
            .BorderColor(Colors.Blue.Lighten3)
            .Padding(8)
            .Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.AutoItem().Text(comment.User)
                        .FontSize(10)
                        .Bold()
                        .FontFamily("Malgun Gothic");

                    row.AutoItem().PaddingLeft(8).Text(comment.DateTime.ToString("yyyy-MM-dd HH:mm"))
                        .FontSize(9)
                        .FontColor(Colors.Grey.Medium)
                        .FontFamily("Malgun Gothic");
                });

                col.Item().PaddingTop(4).Text(comment.Content)
                    .FontSize(10)
                    .FontFamily("Malgun Gothic")
                    .LineHeight(1.4f);

                if (comment.HasFile && !string.IsNullOrEmpty(comment.FileName))
                {
                    col.Item().PaddingTop(2).Text($"📎 {comment.FileName}")
                        .FontSize(9)
                        .FontColor(Colors.Blue.Medium)
                        .FontFamily("Malgun Gothic");
                }
            });
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        // <br>, <p>, <div> 등을 줄바꿈으로 치환
        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(p|div|li|tr|h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);
        // 나머지 HTML 태그 제거
        text = Regex.Replace(text, @"<[^>]+>", "");
        // HTML 엔티티 디코딩
        text = System.Net.WebUtility.HtmlDecode(text);
        // 연속 줄바꿈 정리
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        return sanitized.Length > 30 ? sanitized[..30] : sanitized;
    }
}
