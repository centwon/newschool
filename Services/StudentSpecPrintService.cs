using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewSchool.Models;
using NewSchool.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NewSchool.Services;

/// <summary>
/// 학생부 특기사항 PDF 생성 서비스
/// </summary>
public class StudentSpecPrintService
{
    private static string GetOutputDir()
    {
        var dir = Path.Combine(Settings.UserDataPath, "Prints");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 개인 학생부 PDF 생성
    /// </summary>
    public string GenerateStudentSpecPdf(
        int year, int grade, int classNo, int number, string name,
        List<StudentSpecial> specs)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var fileName = $"학생부_{grade}학년{classNo}반_{number}번_{name}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Path.Combine(GetOutputDir(), fileName);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);

                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text($"{year}학년도 학생부 특기사항")
                            .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                        row.AutoItem().AlignRight()
                            .Text($"{grade}학년 {classNo}반 {number}번 {name}")
                            .FontSize(14).SemiBold().FontColor(Colors.Grey.Darken2);
                    });
                    column.Item().PaddingTop(6).LineHorizontal(2).LineColor(Colors.Blue.Medium);
                    column.Item().PaddingBottom(8);
                });

                page.Content().Element(content => ComposeSpecTable(content, specs));

                page.Footer().Row(row =>
                {
                    row.RelativeItem().AlignLeft()
                        .Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.AutoItem().AlignRight().Text(t =>
                    {
                        t.CurrentPageNumber().FontSize(8);
                        t.Span(" / ").FontSize(8);
                        t.TotalPages().FontSize(8);
                    });
                });
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    /// <summary>
    /// 학급 전체 학생부를 하나의 PDF로 생성 (표 형식)
    /// </summary>
    public string GenerateClassSpecPdf(
        int year, int grade, int classNo,
        List<(int Number, string Name, List<StudentSpecial> Specs)> studentSpecs)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var fileName = $"학생부_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Path.Combine(GetOutputDir(), fileName);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.PageColor(Colors.White);

                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text($"{year}학년도 학생부 특기사항")
                            .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);
                        row.AutoItem().AlignRight().Text($"{grade}학년 {classNo}반")
                            .FontSize(14).SemiBold().FontColor(Colors.Grey.Darken2);
                    });
                    column.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Colors.Blue.Medium);
                    column.Item().PaddingBottom(6);
                });

                page.Content().Element(content => ComposeClassTable(content, studentSpecs));

                page.Footer().Row(row =>
                {
                    row.RelativeItem().AlignLeft()
                        .Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.AutoItem().AlignRight().Text(t =>
                    {
                        t.CurrentPageNumber().FontSize(8);
                        t.Span(" / ").FontSize(8);
                        t.TotalPages().FontSize(8);
                    });
                });
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    /// <summary>개인 학생부 표</summary>
    private void ComposeSpecTable(IContainer container, List<StudentSpecial> specs)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(90);   // 영역
                columns.ConstantColumn(80);   // 과목
                columns.RelativeColumn(1);    // 특기사항
                columns.ConstantColumn(80);   // Byte
            });

            table.Header(header =>
            {
                var style = TextStyle.Default.FontSize(10).Bold().FontColor(Colors.White);
                void H(IContainer c, string text) =>
                    c.Background(Colors.Blue.Darken2).Padding(5)
                     .AlignCenter().AlignMiddle().Text(text).Style(style);

                H(header.Cell(), "영역");
                H(header.Cell(), "과목");
                H(header.Cell(), "특기사항");
                H(header.Cell(), "Byte");
            });

            foreach (var spec in specs)
            {
                var bg = Colors.White;

                void Cell(IContainer c, string text, bool wrap = false) =>
                    c.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                     .Background(bg).Padding(5)
                     .Text(text ?? string.Empty).FontSize(9)
                     .LineHeight(wrap ? 1.4f : 1f);

                Cell(table.Cell(), spec.Type);
                Cell(table.Cell(), spec.SubjectName ?? string.Empty);
                Cell(table.Cell(), spec.Content ?? string.Empty, true);

                var byteCount = Helpers.NeisHelper.CountByte(spec.Content ?? string.Empty);
                var maxBytes = Helpers.NeisHelper.GetMaxBytes(spec.Type);
                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                    .Background(bg).Padding(5).AlignCenter()
                    .Text($"{byteCount}/{maxBytes}").FontSize(8)
                    .FontColor(byteCount > maxBytes ? Colors.Red.Medium : Colors.Grey.Darken1);
            }
        });
    }

    /// <summary>학급 전체 표</summary>
    private void ComposeClassTable(
        IContainer container,
        List<(int Number, string Name, List<StudentSpecial> Specs)> studentSpecs)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(28);   // 번호
                columns.ConstantColumn(48);   // 이름
                columns.ConstantColumn(70);   // 영역
                columns.ConstantColumn(60);   // 과목
                columns.RelativeColumn(1);    // 특기사항
                columns.ConstantColumn(60);   // Byte
            });

            table.Header(header =>
            {
                var style = TextStyle.Default.FontSize(9).Bold().FontColor(Colors.White);
                void H(IContainer c, string text) =>
                    c.Background(Colors.Blue.Darken2)
                     .BorderBottom(1).BorderColor(Colors.White)
                     .Padding(4).AlignCenter().AlignMiddle()
                     .Text(text).Style(style);

                H(header.Cell(), "번호");
                H(header.Cell(), "이름");
                H(header.Cell(), "영역");
                H(header.Cell(), "과목");
                H(header.Cell(), "특기사항");
                H(header.Cell(), "Byte");
            });

            uint rowIdx = 0;
            foreach (var (number, name, specs) in studentSpecs)
            {
                uint specCount = (uint)specs.Count;
                if (specCount == 0) continue;

                bool isEven = rowIdx % 2 == 0;
                var bg = isEven ? Colors.White : Colors.Grey.Lighten5;

                // 번호/이름 행 병합
                table.Cell().RowSpan(specCount)
                    .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                    .Background(bg).Padding(3).AlignCenter().AlignMiddle()
                    .Text(number.ToString()).FontSize(8).SemiBold();

                table.Cell().RowSpan(specCount)
                    .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                    .Background(bg).Padding(3).AlignCenter().AlignMiddle()
                    .Text(name).FontSize(8).SemiBold();

                foreach (var spec in specs)
                {
                    void D(IContainer c, string text) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                         .Background(bg).Padding(3)
                         .Text(text ?? string.Empty).FontSize(8);

                    D(table.Cell(), spec.Type);
                    D(table.Cell(), spec.SubjectName ?? string.Empty);

                    // 특기사항 (줄바꿈 허용)
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Background(bg).Padding(3)
                        .Text(spec.Content ?? string.Empty).FontSize(8).LineHeight(1.3f);

                    // Byte
                    var byteCount = Helpers.NeisHelper.CountByte(spec.Content ?? string.Empty);
                    var maxBytes = Helpers.NeisHelper.GetMaxBytes(spec.Type);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Background(bg).Padding(3).AlignCenter()
                        .Text($"{byteCount}/{maxBytes}").FontSize(7)
                        .FontColor(byteCount > maxBytes ? Colors.Red.Medium : Colors.Grey.Darken1);
                }

                rowIdx++;
            }
        });
    }
}
