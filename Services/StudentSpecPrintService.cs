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
    // 저장 자리를 정하던 GetOutputDir 은 Helpers.ExportPaths 로 올렸다(45차).

    // 개인 학생부 PDF(GenerateStudentSpecPdf)와 그것만 쓰던 ComposeSpecTable 은 호출부가 없어
    // 지웠다(39차). 학생부 PDF 는 학급 전체를 한 부로 뽑는 GenerateClassSpecPdf 하나로 나간다.

    /// <summary>
    /// 학급 전체 학생부를 하나의 PDF로 생성 (표 형식)
    /// </summary>
    public string GenerateClassSpecPdf(
        int year, int grade, int classNo,
        List<(int Number, string Name, List<StudentSpecial> Specs)> studentSpecs)
    {
        var fileName = $"학생부_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

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
                H(header.Cell(), "과목/분야");
                H(header.Cell(), "특기사항");
                H(header.Cell(), "Byte");
            });

            uint rowIdx = 0;
            foreach (var (number, name, specs) in studentSpecs)
            {
                if (specs.Count == 0) continue;

                bool isEven = rowIdx % 2 == 0;
                var bg = isEven ? Colors.White : Colors.Grey.Lighten5;

                bool firstRowOfStudent = true;

                foreach (var spec in specs)
                {
                    // ⚠ 예전에는 번호·이름을 RowSpan 으로 묶었다. 보기에는 깔끔했지만
                    //   학생의 줄이 쪽 경계에 걸리면 병합 칸이 앞쪽에 남아 <b>다음 쪽 첫 줄들이
                    //   이름 없이</b> 떴다(축 "많을 때·길 때", 2026-09-05 실측 — 40명이면 6쪽에서
                    //   두세 번 일어난다). 줄마다 찍고 이어지는 줄은 흐리게 해서, 쪽이 어디서
                    //   넘어가든 누구의 기록인지 알 수 있게 한다.
                    void Label(IContainer c, string text) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                         .Background(bg).Padding(3).AlignCenter().AlignMiddle()
                         .Text(text).FontSize(8)
                         .SemiBold()
                         .FontColor(firstRowOfStudent ? Colors.Black : Colors.Grey.Medium);

                    Label(table.Cell(), number.ToString());
                    Label(table.Cell(), name);
                    firstRowOfStudent = false;

                    void D(IContainer c, string text) =>
                        c.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                         .Background(bg).Padding(3)
                         .Text(text ?? string.Empty).FontSize(8);

                    D(table.Cell(), spec.Type);
                    // 진로활동은 희망분야, 교과활동은 "과목 (N학기)" — 화면 목록과 같은 기준
                    D(table.Cell(), Helpers.NeisHelper.BuildSubjectDisplay(
                        spec.Type, spec.SubjectName, spec.Title, spec.Semester));

                    // 특기사항 (줄바꿈 허용)
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Background(bg).Padding(3)
                        .Text(spec.Content ?? string.Empty).FontSize(8).LineHeight(1.3f);

                    // Byte
                    var byteCount = Helpers.NeisHelper.CountSpecBytes(spec.Type, spec.Title, spec.Content);
                    var maxBytes = Settings.GetSpecMaxBytes(spec.Type, spec.Year);   // 설정 오버라이드 반영(입력 화면과 동일 기준)
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Background(bg).Padding(3).AlignCenter()
                        .Text($"{byteCount}/{maxBytes}").FontSize(7)
                        .FontColor(Helpers.NeisHelper.IsOverLimit(byteCount, maxBytes) ? Colors.Red.Medium : Colors.Grey.Darken1);
                }

                rowIdx++;
            }
        });
    }
}
