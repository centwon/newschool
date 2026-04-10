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
/// A4 1장 고정, 좌석은 용지 하단 기준 배치
/// </summary>
public class SeatsPrintService
{
    // A4: 595 × 842pt, margin 30 each → 가용: 535 × 782pt
    private const float PageWidth = 535f;
    private const float PageHeight = 782f;

    // 고정 영역 높이
    private const float HeaderHeight = 32f;   // 제목 + 구분선
    private const float DeskHeight = 40f;     // 교탁 박스
    private const float DeskGap = 10f;        // 좌석↔교탁 간격
    private const float FooterHeight = 22f;   // 출력일시
    private const float AisleWidth = 10f;     // 줄 사이 통로 너비

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

        // 그리드 크기
        int totalCols = jul * jjak;
        int totalRows = cards.Count > 0
            ? (int)Math.Ceiling((double)cards.Count / totalCols)
            : 1;

        // 메시지 높이
        float messageHeight = string.IsNullOrWhiteSpace(message) ? 0f : 32f;

        // 줄 사이 통로 반영한 셀 너비
        int aisleCount = jul > 1 ? jul - 1 : 0;
        float totalAisleWidth = aisleCount * AisleWidth;
        float cellWidth = (PageWidth - totalAisleWidth) / totalCols;

        // ── 셀 높이: 사진/텍스트 기준 적정 크기 → 나머지는 상단 여백 ──
        float seatAreaMax = PageHeight - HeaderHeight - messageHeight - DeskGap - DeskHeight - FooterHeight;
        float photoWidth = 0, photoHeight = 0;
        float cellHeight;
        float nameFontSize;

        if (showPhoto)
        {
            float cellPad = 4f;
            float nameH = 20f;     // 이름 높이 (패딩 포함)
            float rowGap = 12f;    // 앞뒤 학생 행간 간격
            float photoMaxW = cellWidth - cellPad;

            // 셀 너비 기준 3:4 비율 사진 + 이름 + 행간 = 적정 셀 높이
            float idealCellH = photoMaxW * 4f / 3f + nameH + cellPad + rowGap;

            // 페이지 초과 방지: 적정 높이 vs 최대 가용 중 작은 값
            cellHeight = Math.Min(idealCellH, seatAreaMax / totalRows);

            float photoAreaH = cellHeight - nameH - cellPad;
            float photoAreaW = cellWidth - cellPad;

            // 3:4 비율로 셀 내 최대 크기
            if (photoAreaW * 4f / 3f <= photoAreaH)
            {
                photoWidth = photoAreaW;
                photoHeight = photoAreaW * 4f / 3f;
            }
            else
            {
                photoHeight = photoAreaH;
                photoWidth = photoAreaH * 3f / 4f;
            }

            photoWidth = Math.Max(photoWidth, 15f);
            photoHeight = Math.Max(photoHeight, 20f);
            nameFontSize = Math.Min(Math.Max(cellWidth / 8f, 7f), 11f);
        }
        else
        {
            cellHeight = Math.Min(seatAreaMax / totalRows, 45f);
            nameFontSize = Math.Min(Math.Max(cellWidth / 5f, 8f), 14f);
        }

        // ── 상단 여백 = 나머지 공간 (좌석을 교탁 쪽으로 모음) ──
        float seatsTotal = cellHeight * totalRows;
        float topPadding = PageHeight - HeaderHeight - messageHeight
                           - seatsTotal - DeskGap - DeskHeight - FooterHeight;
        topPadding = Math.Max(topPadding, 0f);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.PageColor(Colors.White);

                // ── 헤더 ──
                page.Header().Height(HeaderHeight).Element(c =>
                {
                    c.Column(col =>
                    {
                        col.Item().AlignCenter()
                            .Text($"{grade}학년 {classRoom}반 좌석배정표")
                            .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                        col.Item().PaddingTop(2)
                            .LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                    });
                });

                // ── 푸터 ──
                page.Footer().Height(FooterHeight).Element(c =>
                {
                    c.Column(col =>
                    {
                        col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                        col.Item().PaddingTop(2).AlignCenter()
                            .Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });

                // ── 본문 ──
                page.Content().Column(column =>
                {
                    // 메시지
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        column.Item().Height(messageHeight)
                            .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Background(Colors.Grey.Lighten4)
                            .Padding(5)
                            .AlignMiddle()
                            .Text(message).FontSize(9);
                    }

                    // 상단 여백 → 좌석을 하단으로 밀어냄
                    if (topPadding > 0)
                        column.Item().Height(topPadding);

                    // 좌석 테이블 (줄 사이 통로 포함)
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            for (int g = 0; g < jul; g++)
                            {
                                for (int j = 0; j < jjak; j++)
                                    cols.RelativeColumn();
                                if (g < jul - 1)
                                    cols.ConstantColumn(AisleWidth); // 통로
                            }
                        });

                        // 뒤에서 앞으로 (교실 좌석 순서)
                        for (int row = totalRows - 1; row >= 0; row--)
                        {
                            // 줄: 오른쪽→왼쪽 (교실 뒤에서 본 시점)
                            for (int g = jul - 1; g >= 0; g--)
                            {
                                // 짝: 같은 줄 내 좌석
                                for (int j = jjak - 1; j >= 0; j--)
                                {
                                    int col = g * jjak + j;
                                    var card = cards.FirstOrDefault(c => c.Row == row && c.Col == col);
                                    table.Cell().Height(cellHeight)
                                        .Element(cell => RenderSeatCell(cell, card, showPhoto,
                                            photoWidth, photoHeight, nameFontSize));
                                }
                                // 통로 셀 (마지막 줄 제외)
                                if (g > 0)
                                    table.Cell().Height(cellHeight);
                            }
                        }
                    });

                    // 교탁
                    column.Item().Height(DeskGap);
                    column.Item().Height(DeskHeight).AlignCenter()
                        .Width(150).Height(DeskHeight)
                        .Border(1.5f).BorderColor(Colors.Blue.Medium)
                        .Background(Colors.Blue.Lighten4)
                        .AlignMiddle().AlignCenter()
                        .Text("교 탁")
                        .FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                });
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    private void RenderSeatCell(IContainer container, PhotoCard? card,
        bool showPhoto, float photoW, float photoH, float fontSize)
    {
        // 미표시 좌석
        if (card != null && card.IsHidden)
            return;

        // 미사용 좌석
        if (card == null || card.IsUnUsed)
        {
            container.AlignMiddle().AlignCenter()
                .Text("×").FontSize(12).FontColor(Colors.Grey.Darken1);
            return;
        }

        // 빈 좌석 (학생 미배정)
        if (card.StudentData == null)
            return;

        var student = card.StudentData;
        var pin = card.IsFixed ? " 📌" : "";
        var label = $"{student.Name}({student.Number}){pin}";

        if (showPhoto)
        {
            container.AlignCenter().AlignBottom()
                .Column(col =>
                {
                    // 사진
                    col.Item().AlignCenter()
                        .Width(photoW).Height(photoH)
                        .Element(photo =>
                        {
                            if (!string.IsNullOrEmpty(student.PhotoPath) && File.Exists(student.PhotoPath))
                            {
                                photo.Image(student.PhotoPath).FitArea();
                            }
                            else
                            {
                                photo.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                    .Background(Colors.Grey.Lighten3)
                                    .AlignMiddle().AlignCenter()
                                    .Text("👤").FontSize(Math.Max(photoH * 0.25f, 10f));
                            }
                        });

                    // 이름: 사진 너비에 맞춘 가는 테두리 박스
                    col.Item().AlignCenter()
                        .Width(photoW)
                        .Border(0.3f).BorderColor(Colors.Grey.Medium)
                        .PaddingVertical(3)
                        .AlignCenter()
                        .Text(label).FontSize(fontSize).Bold().FontColor(Colors.Black);
                });
        }
        else
        {
            // 텍스트 모드: 이름만 가는 테두리
            container.AlignMiddle().AlignCenter()
                .Border(0.3f).BorderColor(Colors.Grey.Medium)
                .PaddingVertical(2).PaddingHorizontal(4)
                .Text(label).FontSize(fontSize).Bold().FontColor(Colors.Black);
        }
    }
}
