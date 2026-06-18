using NewSchool.Controls;
using NewSchool.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NewSchool.Services;

/// <summary>출력 방향 — Auto: 좌석 가로 칸 > 세로 칸이면 가로, 아니면 세로.</summary>
public enum PrintOrientation { Auto, Portrait, Landscape }

/// <summary>
/// 좌석배정표 PDF/HTML 생성 서비스
/// A4 1장 고정, 좌석은 용지 하단 기준 배치
/// </summary>
public class SeatsPrintService
{
    // A4: 595 × 842pt, margin 30 each → 가용: 535 × 782pt (세로), 782 × 535pt (가로)
    private const float PortraitWidth = 535f;
    private const float PortraitHeight = 782f;
    private const float LandscapeWidth = 782f;
    private const float LandscapeHeight = 535f;

    // 고정 영역 높이
    private const float HeaderHeight = 32f;   // 제목 + 구분선
    private const float DeskHeight = 40f;     // 교탁 박스
    private const float DeskGap = 10f;        // 좌석↔교탁 간격
    private const float FooterHeight = 22f;   // 출력일시
    private const float AisleWidth = 10f;     // 줄 사이 통로 너비

    /// <summary>
    /// 렌더링에 필요한 최소 셀 정보 (PhotoCard 비의존).
    /// </summary>
    public class SeatCellData
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public bool IsHidden { get; set; }
        public bool IsUnUsed { get; set; }
        public bool IsFixed { get; set; }
        public StudentCardData? StudentData { get; set; }
    }

    #region PDF 생성 (PhotoCard 기반 - 기존 API)

    public string GenerateSeatsPdf(
        List<PhotoCard> cards,
        int grade,
        int classRoom,
        int jul,
        int jjak,
        string message,
        bool showPhoto = false,
        PrintOrientation orientation = PrintOrientation.Auto,
        bool includeRoster = false)
    {
        var cells = cards.Select(c => new SeatCellData
        {
            Row = c.Row,
            Col = c.Col,
            IsHidden = c.IsHidden,
            IsUnUsed = c.IsUnUsed,
            IsFixed = c.IsFixed,
            StudentData = c.StudentData
        }).ToList();

        return GenerateSeatsPdfCore(cells, grade, classRoom, jul, jjak, message, showPhoto, orientation, includeRoster);
    }

    #endregion

    #region PDF / HTML 생성 (DB 로드)

    /// <summary>
    /// DB에 저장된 학급 좌석 배치를 PDF로 출력. 저장된 배치가 없으면 null.
    /// </summary>
    public async Task<string?> GenerateSeatsPdfFromDbAsync(int year, int grade, int classNo,
        PrintOrientation orientation = PrintOrientation.Auto, bool includeRoster = false)
    {
        var loaded = await LoadCellsAsync(year, grade, classNo);
        if (loaded == null) return null;
        var (cells, jul, jjak, message, showPhoto) = loaded.Value;
        return GenerateSeatsPdfCore(cells, grade, classNo, jul, jjak, message, showPhoto, orientation, includeRoster);
    }

    /// <summary>
    /// DB에 저장된 학급 좌석 배치의 HTML 문자열을 생성 (파일 미저장).
    /// 저장된 배치가 없으면 null.
    /// </summary>
    public async Task<string?> BuildSeatsHtmlFromDbAsync(int year, int grade, int classNo,
        PrintOrientation orientation = PrintOrientation.Auto, bool includeRoster = false)
    {
        var loaded = await LoadCellsAsync(year, grade, classNo);
        if (loaded == null) return null;
        var (cells, jul, jjak, message, showPhoto) = loaded.Value;
        return BuildSeatsHtml(cells, grade, classNo, jul, jjak, message, showPhoto, orientation, includeRoster);
    }

    /// <summary>
    /// DB에 저장된 학급 좌석 배치를 HTML 파일로 출력. 저장된 배치가 없으면 null.
    /// </summary>
    public async Task<string?> GenerateSeatsHtmlFromDbAsync(int year, int grade, int classNo,
        PrintOrientation orientation = PrintOrientation.Auto, bool includeRoster = false)
    {
        var loaded = await LoadCellsAsync(year, grade, classNo);
        if (loaded == null) return null;
        var (cells, jul, jjak, message, showPhoto) = loaded.Value;

        var html = BuildSeatsHtml(cells, grade, classNo, jul, jjak, message, showPhoto, orientation, includeRoster);

        var dir = Path.Combine(Settings.UserDataPath, "Prints");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var fileName = $"좌석배정표_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Path.Combine(dir, fileName);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// DB + 명단에서 좌석 셀 목록을 구성한다.
    /// </summary>
    private async Task<(List<SeatCellData> Cells, int Jul, int Jjak, string Message, bool ShowPhoto)?>
        LoadCellsAsync(int year, int grade, int classNo)
    {
        string schoolCode = Settings.SchoolCode.Value;

        SeatArrangement? arrangement;
        using (var seatService = new SeatService())
        {
            arrangement = await seatService.LoadAsync(schoolCode, year, grade, classNo);
        }
        if (arrangement == null || arrangement.Assignments.Count == 0) return null;

        // 학생 정보 조회 (Enrollment + Student 조인)
        Dictionary<string, StudentCardData> studentMap = new();
        using (var enrollmentService = new EnrollmentService())
        using (var studentService = new StudentService(SchoolDatabase.DbPath))
        {
            var enrollments = await enrollmentService.GetClassRosterAsync(schoolCode, year, grade, classNo);
            var ids = enrollments.Select(e => e.StudentID).ToList();
            var students = await studentService.GetStudentsByIdsAsync(ids);
            var studentById = students.ToDictionary(s => s.StudentID, s => s);

            foreach (var e in enrollments)
            {
                if (studentById.TryGetValue(e.StudentID, out var st))
                    studentMap[e.StudentID] = StudentCardData.FromEnrollment(e, st);
            }
        }

        var cells = new List<SeatCellData>();
        foreach (var a in arrangement.Assignments)
        {
            StudentCardData? data = null;
            if (!string.IsNullOrEmpty(a.StudentID) && studentMap.TryGetValue(a.StudentID, out var sd))
                data = sd;

            cells.Add(new SeatCellData
            {
                Row = a.Row,
                Col = a.Col,
                IsHidden = a.IsHidden,
                IsUnUsed = a.IsUnUsed,
                IsFixed = a.IsFixed,
                StudentData = data
            });
        }

        return (cells, arrangement.Jul, arrangement.Jjak, arrangement.Message, arrangement.ShowPhoto);
    }

    #endregion

    #region PDF 렌더링 코어

    private string GenerateSeatsPdfCore(
        List<SeatCellData> cards,
        int grade,
        int classRoom,
        int jul,
        int jjak,
        string message,
        bool showPhoto,
        PrintOrientation orientation = PrintOrientation.Auto,
        bool includeRoster = false)
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

        // 출력 방향 (Auto: 좌석 가로 칸수 > 세로 칸수이면 가로)
        bool isLandscape = orientation switch
        {
            PrintOrientation.Portrait => false,
            PrintOrientation.Landscape => true,
            _ => totalCols > totalRows
        };
        float pageWidth = isLandscape ? LandscapeWidth : PortraitWidth;
        float pageHeight = isLandscape ? LandscapeHeight : PortraitHeight;

        // 메시지 높이
        float messageHeight = string.IsNullOrWhiteSpace(message) ? 0f : 32f;

        // 학급 명렬표 (번호·이름) — 좌석에 배정된 학생 중복 제거, 번호순
        var roster = includeRoster
            ? cards.Where(c => c.StudentData != null)
                   .Select(c => c.StudentData!)
                   .GroupBy(s => s.StudentID)
                   .Select(g => g.First())
                   .OrderBy(s => s.Number)
                   .ToList()
            : new List<StudentCardData>();
        bool hasRoster = roster.Count > 0;

        // 명렬표 좌측 사이드바 (번호+이름 좁은 2열)
        const float RosterSidebarWidth = 82f;
        const float RosterGap = 8f;
        float sidebarReserve = hasRoster ? RosterSidebarWidth + RosterGap : 0f;

        // 사이드바 세로 공간: 페이지 컨텐츠 영역 전체
        float contentHeight = pageHeight - HeaderHeight - FooterHeight;
        // 헤더 1행 + 학생 N행이 모두 들어가도록 행 높이 계산
        float rosterRowH = hasRoster ? contentHeight / (roster.Count + 1) : 0f;
        float rosterFontSize = Math.Max(5f, Math.Min(rosterRowH * 0.55f, 11f));
        float rosterHeaderFontSize = Math.Max(6f, Math.Min(rosterRowH * 0.6f, 10f));

        // 줄 사이 통로 반영한 셀 너비 (명렬표 공간 제외)
        int aisleCount = jul > 1 ? jul - 1 : 0;
        float totalAisleWidth = aisleCount * AisleWidth;
        float cellWidth = (pageWidth - sidebarReserve - totalAisleWidth) / totalCols;

        // ── 셀 높이: 사진/텍스트 기준 적정 크기 → 나머지는 상단 여백 ──
        float seatAreaMax = pageHeight - HeaderHeight - messageHeight - DeskGap - DeskHeight - FooterHeight;
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
        float topPadding = pageHeight - HeaderHeight - messageHeight
                           - seatsTotal - DeskGap - DeskHeight - FooterHeight;
        topPadding = Math.Max(topPadding, 0f);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(isLandscape ? PageSizes.A4.Landscape() : PageSizes.A4);
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

                // ── 본문 — 왼쪽 명렬표 사이드바 + 오른쪽 메인 영역 ──
                page.Content().Row(bodyRow =>
                {
                    // 왼쪽: 학급 명렬표 (번호·이름)
                    if (hasRoster)
                    {
                        bodyRow.ConstantItem(RosterSidebarWidth).Table(rt =>
                        {
                            rt.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(26f); // 번호
                                c.RelativeColumn();    // 이름
                            });

                            // 헤더
                            rt.Cell().Height(rosterRowH)
                                .Border(0.4f).BorderColor(Colors.Grey.Darken1)
                                .Background(Colors.Grey.Lighten3)
                                .AlignMiddle().AlignCenter()
                                .Text("번호").FontSize(rosterHeaderFontSize).Bold();
                            rt.Cell().Height(rosterRowH)
                                .Border(0.4f).BorderColor(Colors.Grey.Darken1)
                                .Background(Colors.Grey.Lighten3)
                                .AlignMiddle().AlignCenter()
                                .Text("이름").FontSize(rosterHeaderFontSize).Bold();

                            foreach (var s in roster)
                            {
                                rt.Cell().Height(rosterRowH)
                                    .Border(0.3f).BorderColor(Colors.Grey.Lighten1)
                                    .AlignMiddle().AlignCenter()
                                    .Text(s.Number.ToString()).FontSize(rosterFontSize);
                                rt.Cell().Height(rosterRowH)
                                    .Border(0.3f).BorderColor(Colors.Grey.Lighten1)
                                    .PaddingHorizontal(3).AlignMiddle()
                                    .Text(s.Name).FontSize(rosterFontSize);
                            }
                        });
                        bodyRow.ConstantItem(RosterGap);
                    }

                    // 오른쪽: 기존 본문 컬럼
                    bodyRow.RelativeItem().Column(column =>
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

                        // 교탁 (학년·반 표시)
                        column.Item().Height(DeskGap);
                        column.Item().Height(DeskHeight).AlignCenter()
                            .Width(150).Height(DeskHeight)
                            .Border(1.5f).BorderColor(Colors.Blue.Medium)
                            .Background(Colors.Blue.Lighten4)
                            .AlignMiddle().AlignCenter()
                            .Text($"{grade}학년 {classRoom}반")
                            .FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                    });
                });
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    private void RenderSeatCell(IContainer container, SeatCellData? card,
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

    #endregion

    #region HTML 렌더링

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    /// <summary>
    /// 좌석배정표 HTML 생성 — 브라우저 열람/Ctrl+P로 PDF 저장 가능.
    /// </summary>
    public string BuildSeatsHtml(
        List<SeatCellData> cells,
        int grade,
        int classRoom,
        int jul,
        int jjak,
        string message,
        bool showPhoto,
        PrintOrientation orientation = PrintOrientation.Auto,
        bool includeRoster = false)
    {
        int totalCols = jul * jjak;
        int totalRows = cells.Count > 0
            ? (int)Math.Ceiling((double)cells.Count / totalCols)
            : 1;
        bool isLandscape = orientation switch
        {
            PrintOrientation.Portrait => false,
            PrintOrientation.Landscape => true,
            _ => totalCols > totalRows
        };
        string pageSize = isLandscape ? "A4 landscape" : "A4";

        var roster = includeRoster
            ? cells.Where(c => c.StudentData != null)
                   .Select(c => c.StudentData!)
                   .GroupBy(s => s.StudentID)
                   .Select(g => g.First())
                   .OrderBy(s => s.Number)
                   .ToList()
            : new List<StudentCardData>();
        bool hasRoster = roster.Count > 0;

        // 명렬표 행 높이·글자 크기 (인쇄 시 페이지 높이 추정)
        float rosterAvailH = isLandscape ? 460f : 700f;
        float rosterRowPt = hasRoster ? rosterAvailH / (roster.Count + 1) : 0f;
        float rosterFontPt = Math.Max(5f, Math.Min(rosterRowPt * 0.55f, 11f));

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"ko\"><head><meta charset=\"UTF-8\">");
        sb.Append($"<title>{E($"{grade}학년 {classRoom}반 좌석배정표")}</title>");
        sb.Append(@"<style>
  body { font-family: 'Malgun Gothic','Noto Sans KR',sans-serif; margin:0; padding:24px; color:#222; }
  h1 { font-size:20pt; margin:0 0 10px 0; color:#1a3d7a; text-align:center; }
  .meta { color:#666; font-size:10pt; text-align:right; margin-bottom:12px; }
  .msg { border:1px solid #ccc; background:#fafafa; padding:8px 10px; margin:0 0 12px 0; font-size:11pt; }
  table.seats { border-collapse:collapse; margin:24px auto 10px auto; }
  table.seats td.seat {
    border:1px solid #888; padding:6px 4px; text-align:center; vertical-align:middle;
    min-width:72px; height:48px; font-size:11pt; background:#fff;
  }
  table.seats td.seat.photo { height:auto; padding:4px; }
  table.seats td.aisle { min-width:12px; border:0; background:transparent; }
  .layout { display:flex; gap:10px; align-items:stretch; }
  .sidebar { flex:0 0 auto; }
  .main { flex:1; display:flex; flex-direction:column; align-items:center; }
  .main table.seats { margin-top:auto; }
  table.roster { border-collapse:collapse; width:92px; }
  table.roster th { border:1px solid #999; background:#eee; padding:2px 2px; font-weight:bold; text-align:center; }
  table.roster td { border:1px solid #ccc; padding:1px 3px; text-align:center; vertical-align:middle; }
  table.roster td.name { text-align:left; }
  table.seats td.empty { background:#fff; color:#bbb; }
  table.seats td.unused { background:#f0f0f0; color:#999; }
  .photo-wrap img { display:block; width:64px; height:86px; object-fit:cover; border:1px solid #ccc; margin:0 auto 2px auto; }
  .photo-ph { display:flex; align-items:center; justify-content:center;
              width:64px; height:86px; background:#eee; border:1px solid #ccc; margin:0 auto 2px auto; font-size:20pt; }
  .name-box { border:1px solid #999; padding:2px 4px; font-weight:600; display:inline-block; font-size:10pt; }
  .desk { margin:18px auto 0 auto; padding:10px 30px; border:2px solid #1a3d7a;
          background:#e8eef7; color:#1a3d7a; font-weight:700; font-size:14pt;
          display:block; width:160px; text-align:center; border-radius:6px; }
  .footer { margin-top:24px; font-size:9pt; color:#888; text-align:right; }
  @media print { @page { size:" + pageSize + @"; margin:15mm; } body { padding:0; } }
</style></head><body>");

        sb.Append($"<h1>{E($"{grade}학년 {classRoom}반 좌석배정표")}</h1>");
        sb.Append($"<div class=\"meta\">출력일시: {DateTime.Now:yyyy년 M월 d일 HH:mm}</div>");

        if (!string.IsNullOrWhiteSpace(message))
            sb.Append($"<div class=\"msg\">{E(message)}</div>");

        sb.Append("<div class=\"layout\">");

        // 왼쪽 사이드바 — 학급 명렬표
        if (hasRoster)
        {
            var fs = rosterFontPt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append($"<div class=\"sidebar\"><table class=\"roster\" style=\"font-size:{fs}pt;\">");
            sb.Append("<thead><tr><th>번호</th><th>이름</th></tr></thead><tbody>");
            foreach (var s in roster)
                sb.Append($"<tr><td>{s.Number}</td><td class=\"name\">{E(s.Name)}</td></tr>");
            sb.Append("</tbody></table></div>");
        }

        // 오른쪽 메인 — 좌석 + 교탁
        sb.Append("<div class=\"main\">");
        sb.Append("<table class=\"seats\">");
        for (int row = totalRows - 1; row >= 0; row--)
        {
            sb.Append("<tr>");
            for (int g = jul - 1; g >= 0; g--)
            {
                for (int j = jjak - 1; j >= 0; j--)
                {
                    int col = g * jjak + j;
                    var card = cells.FirstOrDefault(c => c.Row == row && c.Col == col);
                    AppendSeatCellHtml(sb, card, showPhoto);
                }
                if (g > 0) sb.Append("<td class=\"aisle\"></td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        sb.Append($"<div class=\"desk\">{grade}학년 {classRoom}반</div>");
        sb.Append("</div>"); // .main
        sb.Append("</div>"); // .layout
        sb.Append("<div class=\"footer\">NewSchool</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendSeatCellHtml(StringBuilder sb, SeatCellData? card, bool showPhoto)
    {
        var cls = showPhoto ? "seat photo" : "seat";

        if (card != null && card.IsHidden)
        {
            sb.Append($"<td class=\"{cls} empty\"></td>");
            return;
        }
        if (card == null || card.IsUnUsed)
        {
            sb.Append($"<td class=\"{cls} unused\">×</td>");
            return;
        }
        if (card.StudentData == null)
        {
            sb.Append($"<td class=\"{cls} empty\"></td>");
            return;
        }

        var s = card.StudentData;
        var pin = card.IsFixed ? " 📌" : "";
        var label = $"{E(s.Name)}({s.Number}){pin}";

        if (showPhoto)
        {
            sb.Append($"<td class=\"{cls}\"><div class=\"photo-wrap\">");
            if (!string.IsNullOrEmpty(s.PhotoPath) && File.Exists(s.PhotoPath))
            {
                // 파일 URI로 직접 참조 (로컬 뷰어 전용)
                var uri = new Uri(s.PhotoPath).AbsoluteUri;
                sb.Append($"<img src=\"{E(uri)}\" alt=\"\">");
            }
            else
            {
                sb.Append("<div class=\"photo-ph\">👤</div>");
            }
            sb.Append($"<span class=\"name-box\">{label}</span>");
            sb.Append("</div></td>");
        }
        else
        {
            sb.Append($"<td class=\"{cls}\"><span class=\"name-box\">{label}</span></td>");
        }
    }

    #endregion
}
