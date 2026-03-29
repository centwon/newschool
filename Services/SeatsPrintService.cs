using NewSchool.Controls;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NewSchool.Services;

/// <summary>
/// 좌석배정표 PDF 생성 서비스
/// A4 1장 고정 출력, 학생 표시: 이름(번호)
/// </summary>
public class SeatsPrintService
{
    /// <summary>
    /// 좌석배정표 PDF 생성
    /// </summary>
    public string GenerateSeatsPdf(
        List<PhotoCard> cards,
        int grade,
        int classRoom,
        int jul,
        int jjak,
        string message,
        bool showPhoto = false)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var fileName = $"좌석배정표_{grade}학년{classRoom}반_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var printsDir = Path.Combine(Settings.UserDataPath, "Prints");
        if (!Directory.Exists(printsDir))
            Directory.CreateDirectory(printsDir);
        var filePath = Path.Combine(printsDir, fileName);

        // 좌석 그리드 크기 계산
        int totalCols = jul * jjak;
        int totalRows = cards.Count > 0
            ? (int)Math.Ceiling((double)cards.Count / totalCols)
            : 1;

        // A4 사용 가능 영역 (margin 30 × 2 = 60 제외)
        // A4: 595 × 842pt → 가용: 535 × 782pt
        // 헤더 ~40pt, 메시지 ~40pt, 교탁 ~50pt, 여백 ~30pt → 좌석 영역 ~620pt
        float availableHeight = showPhoto ? 620f : 640f;
        float availableWidth = 535f;

        // 셀 크기 동적 계산 (1장에 맞추기)
        float cellWidth = availableWidth / totalCols;
        float cellHeight = availableHeight / (totalRows + 0.5f); // 교탁 공간 포함

        // 폰트 크기 동적 계산
        float nameFontSize = Math.Min(Math.Max(cellWidth / 5f, 8f), 14f);
        float numFontSize = nameFontSize * 0.8f;

        if (showPhoto)
        {
            cellHeight = Math.Min(cellHeight, 120f);
            nameFontSize = Math.Min(nameFontSize, 10f);
        }
        else
        {
            cellHeight = Math.Min(cellHeight, 50f);
        }

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.PageColor(Colors.White);

                page.Header().Element(c => ComposeHeader(c, grade, classRoom));

                page.Content().Element(content =>
                    ComposeContent(content, cards, jul, jjak, totalRows, totalCols,
                        message, showPhoto, cellHeight, nameFontSize, numFontSize));

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    private void ComposeHeader(IContainer container, int grade, int classRoom)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter()
                .Text($"{grade}학년 {classRoom}반 좌석배정표")
                .FontSize(20).Bold().FontColor(Colors.Blue.Darken3);

            column.Item().PaddingTop(3)
                .LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            column.Item().PaddingTop(3).AlignCenter()
                .Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                .FontSize(9).FontColor(Colors.Grey.Darken1);
        });
    }

    private void ComposeContent(IContainer container, List<PhotoCard> cards,
        int jul, int jjak, int totalRows, int totalCols,
        string message, bool showPhoto,
        float cellHeight, float nameFontSize, float numFontSize)
    {
        container.Column(column =>
        {
            // 설명란 (간결하게)
            if (!string.IsNullOrWhiteSpace(message))
            {
                column.Item().PaddingBottom(8)
                    .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                    .Background(Colors.Grey.Lighten4)
                    .Padding(6)
                    .Text(message).FontSize(9);
            }

            // 좌석 배치 테이블
            column.Item().PaddingTop(8).Element(content =>
                ComposeSeatsTable(content, cards, jul, jjak, totalRows, totalCols,
                    showPhoto, cellHeight, nameFontSize, numFontSize));

            // 교탁
            column.Item().PaddingTop(12).AlignCenter()
                .Width(160).Height(36)
                .Border(1.5f).BorderColor(Colors.Blue.Medium)
                .Background(Colors.Blue.Lighten4)
                .AlignMiddle().AlignCenter()
                .Text("교 탁")
                .FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
        });
    }

    private void ComposeSeatsTable(IContainer container, List<PhotoCard> cards,
        int jul, int jjak, int totalRows, int totalCols,
        bool showPhoto, float cellHeight, float nameFontSize, float numFontSize)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (int i = 0; i < totalCols; i++)
                    columns.RelativeColumn();
            });

            // 뒤에서 앞으로 (교실 좌석 순서)
            for (int row = totalRows - 1; row >= 0; row--)
            {
                for (int col = totalCols - 1; col >= 0; col--)
                {
                    var card = cards.FirstOrDefault(c => c.Row == row && c.Col == col);

                    table.Cell()
                        .Border(0.5f).BorderColor(Colors.Grey.Medium)
                        .Height(cellHeight)
                        .Element(cell => ComposeSeatCell(cell, card, showPhoto, nameFontSize, numFontSize));
                }
            }
        });
    }

    private void ComposeSeatCell(IContainer container, PhotoCard? card,
        bool showPhoto, float nameFontSize, float numFontSize)
    {
        if (card == null || card.IsUnUsed)
        {
            // 미사용 좌석
            container.Background(Colors.Grey.Lighten3)
                .AlignMiddle().AlignCenter()
                .Text("×").FontSize(12).FontColor(Colors.Grey.Darken1);
            return;
        }

        if (card.StudentData == null)
        {
            // 빈 좌석
            container.Background(Colors.White);
            return;
        }

        var student = card.StudentData;
        var bgColor = card.IsFixed ? Colors.Yellow.Lighten3 : Colors.White;
        var fixedMark = card.IsFixed ? " 📌" : "";

        if (showPhoto)
        {
            // 사진 포함 모드: 사진 + 이름(번호) 1줄
            container.Background(bgColor).Padding(2)
                .Column(col =>
                {
                    // 사진
                    if (!string.IsNullOrEmpty(student.PhotoPath) && File.Exists(student.PhotoPath))
                    {
                        col.Item().AlignCenter()
                            .Width(50).Height(65)
                            .Image(student.PhotoPath).FitArea();
                    }
                    else
                    {
                        col.Item().AlignCenter()
                            .Width(50).Height(65)
                            .Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                            .Background(Colors.Grey.Lighten3)
                            .AlignMiddle().AlignCenter()
                            .Text("👤").FontSize(20);
                    }

                    // 이름(번호)
                    col.Item().AlignCenter().PaddingTop(2)
                        .Text($"{student.Name}({student.Number}){fixedMark}")
                        .FontSize(numFontSize).Bold().FontColor(Colors.Black);
                });
        }
        else
        {
            // 텍스트만 모드: 이름(번호) 한 줄로 컴팩트 표시
            container.Background(bgColor)
                .AlignMiddle().AlignCenter()
                .Text($"{student.Name}({student.Number}){fixedMark}")
                .FontSize(nameFontSize).Bold().FontColor(Colors.Black);
        }
    }
}
