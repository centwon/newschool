using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NewSchool.Controls;
using NewSchool.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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

        var fileName = $"좌석배정표_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Helpers.ExportPaths.Resolve(fileName);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// DB에 저장된 학급 좌석 배치를 Excel 로 출력 — 시트1 "좌석배치"(교탁에서 바라본 그리드),
    /// 시트2 "명단"(번호·이름). 저장된 배치가 없으면 null.
    /// </summary>
    public async Task<string?> GenerateSeatsExcelFromDbAsync(int year, int grade, int classNo)
    {
        var loaded = await LoadCellsAsync(year, grade, classNo);
        if (loaded == null) return null;
        var (cells, jul, jjak, _, _) = loaded.Value;

        // 호출부가 DB 값을 그대로 넘길 수 있어 여기서도 보정한다(0 이면 나눗셈이 깨진다)
        // 보정값을 변수에 되돌려 담는다 — 예전에는 totalCols 계산에만 쓰고 정작 아래 렌더 루프는
        // 원본을 그대로 써서, jul=0 인 배치가 "행은 있는데 칸이 하나도 없는" 표로 나왔다.
        jul = SafeJul(jul);
        jjak = SafeJjak(jjak);

        int totalCols = jul * jjak;
        int totalRows = cells.Count > 0
            ? (int)Math.Ceiling((double)cells.Count / totalCols)
            : 1;

        // HTML/PDF 와 같은 시선(교탁에서 바라본 배치): 행은 뒤→앞, 열은 오른쪽→왼쪽 순
        var grid = new List<Dictionary<string, object>>();
        for (int row = totalRows - 1; row >= 0; row--)
        {
            var line = new Dictionary<string, object>();
            int colIdx = 1;
            for (int g = jul - 1; g >= 0; g--)
            {
                for (int j = jjak - 1; j >= 0; j--)
                {
                    int col = g * jjak + j;
                    var card = cells.FirstOrDefault(c => c.Row == row && c.Col == col);
                    line[colIdx.ToString()] = SeatCellText(card);
                    colIdx++;
                }
            }
            grid.Add(line);
        }

        var roster = BuildRoster(cells)
            .Select(s => new RosterExportDto { 번호 = s.Number, 이름 = s.Name })
            .ToList();

        var fileName = $"좌석배정표_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

        var sheets = new Dictionary<string, object>
        {
            ["좌석배치"] = grid,
            ["명단"] = roster
        };
        await Task.Run(() => MiniExcelLibs.MiniExcel.SaveAs(filePath, sheets));
        return filePath;
    }

    private record RosterExportDto
    {
        public int 번호 { get; init; }
        public string 이름 { get; init; } = string.Empty;
    }

    /// <summary>
    /// 명렬표에 실을 학생 — 번호순, 중복 없이. <b>세 형식(Excel·PDF·HTML)이 함께 쓴다.</b>
    ///
    /// <para>⚠ 예전에는 규칙이 갈라져 있었다. PDF·HTML 은 자리에 앉은 학생을 모두 실었는데
    /// Excel 만 <c>!IsHidden &amp;&amp; !IsUnUsed</c> 를 더 걸렀다 — 같은 반을 xlsx 로 뽑으면
    /// <b>사람이 명단에서 사라졌다</b>. 안 보이게 해 둔 자리에 앉은 학생은 배치 그림에는
    /// 안 나오므로(<see cref="SeatKind.Hidden"/>) 명단마저 빠지면 그 종이 어디에도 없게 된다.
    /// 그래서 <b>자리에 앉은 학생은 모두 싣는</b> 쪽으로 맞춘다(53차).</para>
    /// </summary>
    private static List<StudentCardData> BuildRoster(List<SeatCellData> cells) =>
        cells.Where(c => c.StudentData != null)
             .Select(c => c.StudentData!)
             .GroupBy(s => s.StudentID)
             .Select(g => g.First())
             .OrderBy(s => s.Number)
             .ToList();

    /// <summary>좌석 한 칸이 무엇인가. 세 형식(Excel·PDF·HTML)이 이 판정을 함께 쓴다.</summary>
    private enum SeatKind
    {
        /// <summary>보이지 않게 해 둔 자리 — 아무것도 그리지 않는다.</summary>
        Hidden,
        /// <summary>쓰지 않는 자리 — × 를 그린다.</summary>
        Unused,
        /// <summary>쓰지만 아직 아무도 앉지 않은 자리.</summary>
        Empty,
        /// <summary>학생이 앉은 자리.</summary>
        Student,
    }

    /// <summary>
    /// 좌석 한 칸의 종류를 가린다.
    ///
    /// <para>⚠ 예전에는 이 네 갈래가 <b>세 벌로 따로</b> 적혀 있었다(Excel·PDF·HTML).
    /// 순서까지 우연히 일치해 지금은 같게 보였지만, 한쪽만 고치면 조용히 갈라지는 자리였다.
    /// 좌석은 데이터 읽기(<c>LoadCellsAsync</c>)와 줄·짝 보정(<c>SafeJul</c>·<c>SafeJjak</c>)을
    /// 이미 한 곳에 모아 두었으므로 판정만 남아 있었다.</para>
    /// </summary>
    private static SeatKind Classify(SeatCellData? card)
    {
        if (card != null && card.IsHidden) return SeatKind.Hidden;
        if (card == null || card.IsUnUsed) return SeatKind.Unused;
        if (card.StudentData == null) return SeatKind.Empty;
        return SeatKind.Student;
    }

    /// <summary>학생이 앉은 칸에 적을 이름표 — <c>이름(번호)</c>, 고정석이면 📌.</summary>
    private static string SeatLabel(SeatCellData card)
    {
        var s = card.StudentData!;
        return $"{s.Name}({s.Number}){(card.IsFixed ? " 📌" : "")}";
    }

    /// <summary>좌석 셀의 Excel 표시 텍스트 (HTML 셀과 동일 규칙).</summary>
    private static string SeatCellText(SeatCellData? card)
    {
        return Classify(card) switch
        {
            SeatKind.Unused => "×",
            SeatKind.Student => SeatLabel(card!),
            _ => string.Empty,          // 숨김·빈자리는 아무것도 적지 않는다
        };
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

        return (cells, SafeJul(arrangement.Jul), SafeJjak(arrangement.Jjak),
                arrangement.Message, arrangement.ShowPhoto);
    }

    /// <summary>
    /// 줄 수를 편집 화면과 같은 범위(2~8)로 보정한다.
    ///
    /// <para>세 출력 경로(PDF·HTML·Excel)가 모두 <c>jul * jjak</c> 으로 나누는데, 이 값들은
    /// DB 에서 그대로 온다. 26차에 확인했듯 구데이터나 초기화 실패로 0 이 들어 있을 수 있고,
    /// 그러면 0 으로 나뉘어 좌석표가 깨지거나 QuestPDF 안쪽에서 이유를 알 수 없는 오류로 터졌다.</para>
    ///
    /// <para>하한을 1 이 아니라 <b>2</b> 로 둔다. 1 이면 열이 하나뿐인 격자가 되어 30명이 30행이 되고,
    /// 셀 높이가 사진(20pt)+이름(20pt) 아래로 내려가 QuestPDF 레이아웃 예외로 터진다(17행부터).
    /// 편집 화면(<c>PageSeats</c>)도 저장값을 2~8 로 자르므로 이제 화면과 출력물이 같은 격자를 본다.</para>
    /// </summary>
    private static int SafeJul(int value) => Math.Clamp(value, 2, 8);

    /// <summary>짝 수를 편집 화면과 같이 1 또는 2 로 보정한다.</summary>
    private static int SafeJjak(int value) => value == 2 ? 2 : 1;

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
        var fileName = $"좌석배정표_{grade}학년{classRoom}반_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

        // 그리드 크기
        // 호출부가 DB 값을 그대로 넘길 수 있어 여기서도 보정한다(0 이면 나눗셈이 깨진다)
        // 보정값을 변수에 되돌려 담는다 — 예전에는 totalCols 계산에만 쓰고 정작 아래 렌더 루프는
        // 원본을 그대로 써서, jul=0 인 배치가 "행은 있는데 칸이 하나도 없는" 표로 나왔다.
        jul = SafeJul(jul);
        jjak = SafeJjak(jjak);

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
        var roster = includeRoster ? BuildRoster(cards) : new List<StudentCardData>();
        bool hasRoster = roster.Count > 0;

        // ── 학급 명렬표: 좌석 아래에 가로로 눕힌다 ──
        //
        // ⚠ 예전에는 왼쪽에 세로 사이드바로 세웠다. 20명까지는 멀쩡했지만 40명이면 한 쪽에
        //   들어가지 않아 <b>좌석배정표가 2장으로 나왔고</b>, 둘째 장은 좌석 그림 없이
        //   21~40번 목록만 놓였다(쪽 경계에 걸린 이름은 반토막). 글자 크기를 줄여도 끊기는
        //   자리가 그대로여서 — 사이드바가 쓸 수 있는 세로 공간이 QuestPDF 의 Row 분할
        //   규칙에 묶여 있다 — 계산으로는 맞출 수 없었다(축 "많을 때·길 때", 2026-09-05).
        //   그래서 <b>교탁 아래에 가로로</b> 눕혀 한 장에 담는다(사용자 결정).
        //   ⚠ <b>표의 실제 행 높이는 글자의 약 2.5배</b>다(실측). <c>.Height()</c> 는 최소값일
        //   뿐이라 이보다 낮게 잡으면 표가 계산보다 커져 또 쪽을 넘긴다 — 명렬표를 아래로
        //   옮기고도 한 번 더 넘겼다. 그래서 글자에서 행 높이를 <b>거꾸로</b> 구한다.
        const int RosterColumns = 8;          // 한 줄에 여덟 명
        const float RosterTitleHeight = 12f;
        const float RosterFontSize = 7f;
        const float RosterRowHeight = RosterFontSize * 3.0f;   // 2.5 로는 모자랐다(실측 후 상향)

        int rosterRows = hasRoster
            ? (int)Math.Ceiling((double)roster.Count / RosterColumns)
            : 0;
        float rosterBandHeight = hasRoster
            ? RosterTitleHeight + rosterRows * RosterRowHeight
            : 0f;

        // 줄 사이 통로 반영한 셀 너비 (명렬표가 옆이 아니라 아래라 폭은 온전히 좌석 몫)
        int aisleCount = jul > 1 ? jul - 1 : 0;
        float totalAisleWidth = aisleCount * AisleWidth;
        float cellWidth = (pageWidth - totalAisleWidth) / totalCols;

        // ── 셀 높이: 사진/텍스트 기준 적정 크기 → 나머지는 상단 여백 ──
        float seatAreaMax = pageHeight - HeaderHeight - messageHeight - DeskGap - DeskHeight - rosterBandHeight - FooterHeight;
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
                           - seatsTotal - DeskGap - DeskHeight - rosterBandHeight - FooterHeight;
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

                // ── 본문 — 좌석 그림, 그 아래 명렬표 ──
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

                        // 교탁 (학년·반 표시)
                        column.Item().Height(DeskGap);
                        column.Item().Height(DeskHeight).AlignCenter()
                            .Width(150).Height(DeskHeight)
                            .Border(1.5f).BorderColor(Colors.Blue.Medium)
                            .Background(Colors.Blue.Lighten4)
                            .AlignMiddle().AlignCenter()
                            .Text($"{grade}학년 {classRoom}반")
                            .FontSize(13).Bold().FontColor(Colors.Blue.Darken2);

                        // 교탁 아래 — 학급 명렬표를 가로로 눕힌다(한 줄에 여덟 명)
                        if (hasRoster)
                        {
                            column.Item().Height(RosterTitleHeight).AlignMiddle()
                                .Text("학급 명렬표")
                                .FontSize(7.5f).SemiBold().FontColor(Colors.Grey.Darken2);

                            column.Item().Table(rt =>
                            {
                                rt.ColumnsDefinition(c =>
                                {
                                    for (int i = 0; i < RosterColumns; i++)
                                        c.RelativeColumn();
                                });

                                foreach (var s in roster)
                                {
                                    rt.Cell().Height(RosterRowHeight)
                                        .Border(0.3f).BorderColor(Colors.Grey.Lighten1)
                                        .PaddingHorizontal(3).AlignMiddle()
                                        .Text($"{s.Number}  {s.Name}").FontSize(RosterFontSize);
                                }

                                // 마지막 줄의 빈 칸도 테두리를 맞춘다
                                int blanks = rosterRows * RosterColumns - roster.Count;
                                for (int i = 0; i < blanks; i++)
                                {
                                    rt.Cell().Height(RosterRowHeight)
                                        .Border(0.3f).BorderColor(Colors.Grey.Lighten1);
                                }
                            });
                        }
                    });
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    private void RenderSeatCell(IContainer container, SeatCellData? card,
        bool showPhoto, float photoW, float photoH, float fontSize)
    {
        var kind = Classify(card);

        // 미표시 좌석
        if (kind == SeatKind.Hidden)
            return;

        // 미사용 좌석
        if (kind == SeatKind.Unused)
        {
            container.AlignMiddle().AlignCenter()
                .Text("×").FontSize(12).FontColor(Colors.Grey.Darken1);
            return;
        }

        // 빈 좌석 (학생 미배정)
        if (kind == SeatKind.Empty)
            return;

        var student = card!.StudentData!;
        var label = SeatLabel(card);

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
                            var photoFull = PhotoService.ResolveFullPath(student.PhotoPath);
                            if (!string.IsNullOrEmpty(photoFull) && File.Exists(photoFull))
                            {
                                photo.Image(photoFull).FitArea();
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
        // 호출부가 DB 값을 그대로 넘길 수 있어 여기서도 보정한다(0 이면 나눗셈이 깨진다)
        // 보정값을 변수에 되돌려 담는다 — 예전에는 totalCols 계산에만 쓰고 정작 아래 렌더 루프는
        // 원본을 그대로 써서, jul=0 인 배치가 "행은 있는데 칸이 하나도 없는" 표로 나왔다.
        jul = SafeJul(jul);
        jjak = SafeJjak(jjak);

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

        var roster = includeRoster ? BuildRoster(cells) : new List<StudentCardData>();
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

        switch (Classify(card))
        {
            case SeatKind.Hidden:
            case SeatKind.Empty:
                sb.Append($"<td class=\"{cls} empty\"></td>");
                return;
            case SeatKind.Unused:
                sb.Append($"<td class=\"{cls} unused\">×</td>");
                return;
        }

        var s = card!.StudentData!;
        var label = E(SeatLabel(card));

        if (showPhoto)
        {
            sb.Append($"<td class=\"{cls}\"><div class=\"photo-wrap\">");
            var photoFull = PhotoService.ResolveFullPath(s.PhotoPath);
            if (!string.IsNullOrEmpty(photoFull) && File.Exists(photoFull))
            {
                // 파일 URI로 직접 참조 (로컬 뷰어 전용)
                var uri = new Uri(photoFull).AbsoluteUri;
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
