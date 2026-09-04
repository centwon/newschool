using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Pages;
using NewSchool.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NewSchool.Services;

/// <summary>
/// 학생카드 PDF 생성 서비스 — 화면 StudentCard 레이아웃과 동일한 폼 테이블 형식
/// </summary>
public class StudentCardPrintService
{
    // 공통 스타일 상수
    private const float LabelFontSize = 10f;
    private const float ValueFontSize = 10f;
    private const float SectionTitleFontSize = 12f;
    private const float LabelWidth = 65f;
    private static readonly string LabelColor = Colors.Grey.Darken2;
    private static readonly string BorderColor = Colors.Grey.Lighten1;

    /// <summary>
    /// 개인 학생카드 PDF 를 만든다.
    ///
    /// <para>다른 출력물과 같이 <b>묻지 않고</b> 정해진 자리에 저장한다
    /// (<see cref="Helpers.ExportPaths"/>). 예전에는 이것만 <c>FileSavePicker</c> 로
    /// 위치를 물었다 — 같은 앱에서 어떤 출력은 묻고 어떤 출력은 안 묻는 상태였고,
    /// 물어보는 쪽이 오히려 소수였다. 알릴 것은 <b>저장이 실패했을 때</b>지
    /// 매번 어디에 둘지가 아니다.</para>
    /// </summary>
    /// <returns>만들어진 파일 경로. 실패하면 예외를 올린다.</returns>
    public async Task<string> GenerateStudentCardPdfAsync(
        StudentCardViewModel viewModel,
        bool includeDetailInfo = true,
        List<StudentLogViewModel>? studentLogs = null)
    {
        // 한글 폰트는 따로 설정하지 않는다 — QuestPDF 가 시스템 폰트에서 글리프를 찾아 준다.
        // (53차 실측: 폰트 경로를 더하든 안 더하든 만들어진 PDF 에 MalgunGothic 이 임베드됐다.
        //  여기에만 있던 ConfigureKoreanFont 는 형제 서비스 네 곳에 없었는데도 한글이 잘 나왔다.)

        var grade = viewModel.Enrollment?.Grade ?? 0;
        var classNo = viewModel.Enrollment?.Class ?? 0;
        var number = viewModel.Enrollment?.Number ?? 0;
        var year = viewModel.Enrollment?.Year ?? Settings.WorkYear.Value;

        // 이름은 사용자 입력이라 파일명에 못 쓰는 문자가 섞일 수 있다
        var fileName = $"학생정보_{grade}학년{classNo}반_{number}번_{Helpers.FileNameHelper.Sanitize(viewModel.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

        await Task.Run(() =>
        {
            GeneratePdfDocument(filePath, viewModel, year, grade, classNo, number, includeDetailInfo, studentLogs);
        });

        return filePath;
    }

    private void GeneratePdfDocument(string filePath, StudentCardViewModel vm, int year, int grade, int classNo, int number, bool includeDetailInfo, List<StudentLogViewModel>? studentLogs)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.PageColor(Colors.White);

                page.Header().Element(c => ComposeHeader(c, year, grade, classNo, number));
                page.Content().Element(content => ComposeContent(content, vm, includeDetailInfo, studentLogs));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(filePath);
    }

    // ── 헤더 ────────────────────────────────
    private void ComposeHeader(IContainer container, int year, int grade, int classNo, int number)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("학생 정보 카드")
                .FontSize(20).Bold().FontColor(Colors.Blue.Darken3);
            col.Item().PaddingTop(4).AlignCenter()
                .Text($"{year}학년도 {grade}학년 {classNo}반 {number}번")
                .FontSize(12).FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Colors.Blue.Medium);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(4).AlignCenter()
                .Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                .FontSize(9).FontColor(Colors.Grey.Darken1);
        });
    }

    // ── 본문 ────────────────────────────────
    private void ComposeContent(IContainer container, StudentCardViewModel vm, bool includeDetailInfo, List<StudentLogViewModel>? studentLogs)
    {
        container.Column(column =>
        {
            column.Item().PaddingTop(15).Element(c => ComposeBasicInfo(c, vm));

            if (includeDetailInfo)
            {
                column.Item().PaddingTop(12).Element(c => ComposeGuardianInfo(c, vm));
                column.Item().PaddingTop(12).Element(c => ComposeFamilyInfo(c, vm));
                column.Item().PaddingTop(12).Element(c => ComposeCareerInfo(c, vm));
                column.Item().PaddingTop(12).Element(c => ComposeHealthInfo(c, vm));
                column.Item().PaddingTop(12).Element(c => ComposeDetailMemo(c, vm));
            }

            if (studentLogs != null && studentLogs.Count > 0)
            {
                column.Item().PaddingTop(12).Element(c => ComposeStudentLogs(c, studentLogs));
            }
        });
    }

    // ── 기본 정보 (사진 + 폼 테이블) ─────────
    private void ComposeBasicInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(col =>
        {
            SectionTitle(col, "기본 정보", Colors.Blue.Darken2);

            col.Item().Border(0.5f).BorderColor(BorderColor).Row(row =>
            {
                // 왼쪽: 폼 테이블
                row.RelativeItem().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(LabelWidth);
                        c.RelativeColumn(2);
                        c.ConstantColumn(50);
                        c.RelativeColumn(1);
                        c.ConstantColumn(40);
                        c.RelativeColumn(0.5f);
                    });

                    // Row 1: 이름 | 성별 | 나이
                    LabelCell(table, "이름");
                    ValueCell(table, vm.Student?.Name);
                    LabelCell(table, "성별");
                    ValueCell(table, vm.Student?.Sex);
                    LabelCell(table, "나이");
                    ValueCell(table, vm.Age > 0 ? $"{vm.Age}세" : "");

                    // Row 2: 생년월일
                    LabelCell(table, "생년월일");
                    ValueCellSpan(table, vm.Student?.BirthDate?.ToString("yyyy년 M월 d일") ?? "", 5);

                    // Row 3: 전화번호 | 이메일
                    LabelCell(table, "전화번호");
                    ValueCellSpan(table, vm.Student?.Phone, 2);
                    LabelCell(table, "이메일");
                    ValueCellSpan(table, vm.Student?.Email, 2);

                    // Row 4: 주소
                    LabelCell(table, "주소");
                    ValueCellSpan(table, vm.Student?.Address, 5);

                    // Row 5: 메모
                    LabelCell(table, "메모");
                    ValueCellSpan(table, vm.Student?.Memo, 5);
                });

                // 오른쪽: 사진
                row.ConstantItem(100).Border(0.5f).BorderColor(BorderColor)
                    .Padding(4).AlignCenter().AlignMiddle()
                    .Element(c => ComposePhoto(c, vm));
            });
        });
    }

    /// <summary>사진 삽입 (파일이 존재하면 표시)</summary>
    private void ComposePhoto(IContainer container, StudentCardViewModel vm)
    {
        string? photoPath = vm.Student?.Photo;
        if (string.IsNullOrEmpty(photoPath))
        {
            container.AlignCenter().AlignMiddle()
                .Text("사진 없음").FontSize(9).FontColor(Colors.Grey.Medium);
            return;
        }

        // 사진 파일의 절대 경로 확인 (저장 기준인 UserDataPath 기준 — PhotoService 와 동일하게 해석)
        string fullPath = PhotoService.ResolveFullPath(photoPath) ?? "";

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            container.AlignCenter().AlignMiddle()
                .Text("사진 없음").FontSize(9).FontColor(Colors.Grey.Medium);
            return;
        }

        try
        {
            container.Image(fullPath).FitArea();
        }
        catch
        {
            container.AlignCenter().AlignMiddle()
                .Text("사진 오류").FontSize(9).FontColor(Colors.Red.Medium);
        }
    }

    // ── 보호자 정보 ─────────────────────────
    private void ComposeGuardianInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(col =>
        {
            SectionTitle(col, "보호자 정보", Colors.Green.Darken2);

            col.Item().Border(0.5f).BorderColor(BorderColor).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(LabelWidth);
                    c.RelativeColumn(1);
                    c.ConstantColumn(50);
                    c.RelativeColumn(1);
                    c.ConstantColumn(50);
                    c.RelativeColumn(1);
                });

                LabelCell(table, "보호자");
                ValueCell(table, vm.Detail?.GuardianName);
                LabelCell(table, "관계");
                ValueCell(table, vm.Detail?.GuardianRelation);
                LabelCell(table, "연락처");
                ValueCell(table, vm.Detail?.GuardianPhone);
            });
        });
    }

    // ── 가족 정보 ──────────────────────────
    private void ComposeFamilyInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(col =>
        {
            SectionTitle(col, "가족 정보", Colors.Green.Darken2);

            col.Item().Border(0.5f).BorderColor(BorderColor).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(LabelWidth);
                    c.RelativeColumn(1);
                    c.ConstantColumn(50);
                    c.RelativeColumn(1);
                    c.ConstantColumn(50);
                    c.RelativeColumn(1);
                });

                // 아버지 — 직업은 엑셀에만 있고 PDF 에는 없었다. 같은 학생카드인데
                // 형식에 따라 정보가 다를 이유가 없어 여기에도 넣는다.
                LabelCell(table, "아버지");
                ValueCell(table, vm.Detail?.FatherName);
                LabelCell(table, "연락처");
                ValueCell(table, vm.Detail?.FatherPhone);
                LabelCell(table, "직업");
                ValueCell(table, vm.Detail?.FatherJob);

                // 어머니
                LabelCell(table, "어머니");
                ValueCell(table, vm.Detail?.MotherName);
                LabelCell(table, "연락처");
                ValueCell(table, vm.Detail?.MotherPhone);
                LabelCell(table, "직업");
                ValueCell(table, vm.Detail?.MotherJob);

                // 가족구성
                LabelCell(table, "가족구성");
                ValueCellSpan(table, vm.Detail?.FamilyInfo, 5);
            });
        });
    }

    // ── 진로 및 관심사 ─────────────────────
    private void ComposeCareerInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(col =>
        {
            SectionTitle(col, "진로 및 관심사", Colors.Orange.Darken2);

            col.Item().Border(0.5f).BorderColor(BorderColor).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(LabelWidth);
                    c.RelativeColumn(1);
                    c.ConstantColumn(LabelWidth);
                    c.RelativeColumn(1);
                });

                LabelCell(table, "진로희망");
                ValueCell(table, vm.Detail?.CareerGoal);
                LabelCell(table, "특기");
                ValueCell(table, vm.Detail?.Talents);

                LabelCell(table, "관심/취미");
                ValueCellSpan(table, vm.Detail?.Interests, 3);
            });
        });
    }

    // ── 건강 정보 ──────────────────────────
    private void ComposeHealthInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(col =>
        {
            SectionTitle(col, "건강 정보", Colors.Red.Darken2);

            col.Item().Border(0.5f).BorderColor(BorderColor).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(LabelWidth);
                    c.RelativeColumn(1);
                    c.ConstantColumn(LabelWidth);
                    c.RelativeColumn(1);
                });

                LabelCell(table, "건강정보");
                ValueCellSpan(table, vm.Detail?.HealthInfo, 3);

                LabelCell(table, "알레르기");
                ValueCell(table, vm.Detail?.Allergies);
                LabelCell(table, "특수교육");
                ValueCell(table, vm.Detail?.SpecialNeeds);
            });
        });
    }

    // ── 상세 메모 ──────────────────────────
    private void ComposeDetailMemo(IContainer container, StudentCardViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Detail?.Memo)) return;

        container.Column(col =>
        {
            SectionTitle(col, "상세 메모", Colors.Grey.Darken2);

            col.Item().Border(0.5f).BorderColor(BorderColor)
                .Padding(8)
                .Text(vm.Detail?.Memo ?? "")
                .FontSize(ValueFontSize);
        });
    }

    // ── 학생 생활 기록 ─────────────────────
    private void ComposeStudentLogs(IContainer container, List<StudentLogViewModel> logs)
    {
        container.Column(column =>
        {
            SectionTitle(column, "누가기록", Colors.Purple.Darken2);

            column.Item().Border(0.5f).BorderColor(BorderColor).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(70);   // 날짜
                    columns.ConstantColumn(65);   // 카테고리
                    columns.ConstantColumn(65);   // 과목
                    columns.RelativeColumn();     // 내용
                });

                // 헤더
                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(BorderColor).Padding(5).AlignCenter().Text("날짜").FontSize(LabelFontSize).SemiBold();
                    header.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(BorderColor).Padding(5).AlignCenter().Text("카테고리").FontSize(LabelFontSize).SemiBold();
                    header.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(BorderColor).Padding(5).AlignCenter().Text("과목").FontSize(LabelFontSize).SemiBold();
                    header.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(BorderColor).Padding(5).AlignCenter().Text("내용").FontSize(LabelFontSize).SemiBold();
                });

                foreach (var log in logs)
                {
                    TableDataCell(table, log.Date.ToString("MM/dd"));
                    TableDataCell(table, log.Category.ToString());
                    // 동아리활동이면 동아리명 — 화면 목록·인쇄·엑셀과 같은 기준
                    TableDataCell(table, log.SubjectOrClubDisplay);
                    // 한 칸에 담는 내용 규칙도 한 벌로 — 예전에는 Log 만 찍어서
                    // 활동 상세만 적은 기록이 통째로 빈칸이었다.
                    TableDataCell(table, log.ContentDigest);
                }
            });
        });
    }

    // ── 셀 헬퍼 메서드 ──────────────────────
    private static void SectionTitle(ColumnDescriptor col, string title, string color)
    {
        col.Item().Background(Colors.Grey.Lighten4)
            .BorderBottom(1.5f).BorderColor(color)
            .Padding(6)
            .Text(title)
            .FontSize(SectionTitleFontSize)
            .Bold()
            .FontColor(color);
    }

    private static void LabelCell(TableDescriptor table, string label)
    {
        table.Cell()
            .Border(0.5f).BorderColor(BorderColor)
            .Background(Colors.Grey.Lighten4)
            .Padding(5)
            .AlignRight()
            .AlignMiddle()
            .Text(label)
            .FontSize(LabelFontSize)
            .SemiBold()
            .FontColor(LabelColor);
    }

    private static void ValueCell(TableDescriptor table, string? value)
    {
        table.Cell()
            .Border(0.5f).BorderColor(BorderColor)
            .Padding(5)
            .AlignMiddle()
            .Text(value ?? "")
            .FontSize(ValueFontSize);
    }

    private static void ValueCellSpan(TableDescriptor table, string? value, uint colSpan)
    {
        table.Cell().ColumnSpan(colSpan)
            .Border(0.5f).BorderColor(BorderColor)
            .Padding(5)
            .AlignMiddle()
            .Text(value ?? "")
            .FontSize(ValueFontSize);
    }

    private static void TableDataCell(TableDescriptor table, string text)
    {
        table.Cell()
            .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
            .Padding(4)
            .Text(text)
            .FontSize(9);
    }

    // ── 학급 전체 학생카드 PDF (통합 내보내기) ──
    /// <summary>
    /// 학급 전체 학생카드를 단일 PDF로 생성 (학생당 1 페이지 세트).
    /// </summary>
    public async Task<string?> GenerateClassCardsPdfFromDbAsync(int year, int grade, int classNo)
    {

        var students = await LoadClassStudentsAsync(year, grade, classNo);
        if (students.Count == 0) return null;

        var fileName = $"학생카드_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                foreach (var vm in students)
                {
                    var g = vm.Enrollment?.Grade ?? grade;
                    var c = vm.Enrollment?.Class ?? classNo;
                    var n = vm.Enrollment?.Number ?? 0;
                    var y = vm.Enrollment?.Year ?? year;

                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(30);
                        page.PageColor(Colors.White);
                        page.Header().Element(hc => ComposeHeader(hc, y, g, c, n));
                        page.Content().Element(cc => ComposeContent(cc, vm, includeDetailInfo: true, studentLogs: null));
                        page.Footer().Element(ComposeFooter);
                    });
                }
            }).GeneratePdf(filePath);
        });

        return filePath;
    }

    /// <summary>
    /// 학급 전체 학생카드 DB 로드 (Enrollment + Student + StudentDetail).
    /// </summary>
    /// <summary>
    /// 학급 전체 학생카드를 Excel 로 출력 — 학생 1명당 1행, 카드의 전체 항목을 컬럼으로.
    /// 데이터 없으면 null.
    /// </summary>
    public async Task<string?> GenerateClassCardsExcelFromDbAsync(int year, int grade, int classNo)
    {
        var students = await LoadClassStudentsAsync(year, grade, classNo);
        if (students.Count == 0) return null;

        var rows = students.Select(vm => new CardExportDto
        {
            학년 = vm.Enrollment?.Grade ?? grade,
            반 = vm.Enrollment?.Class ?? classNo,
            번호 = vm.Enrollment?.Number ?? 0,
            이름 = vm.Student?.Name ?? string.Empty,
            성별 = vm.Student?.Sex ?? string.Empty,
            생년월일 = vm.Student?.BirthDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            연락처 = vm.Student?.Phone ?? string.Empty,
            이메일 = vm.Student?.Email ?? string.Empty,
            주소 = vm.Student?.Address ?? string.Empty,
            // 기본정보의 메모는 PDF 에만 있고 엑셀에는 없었다.
            // ⚠ 아래 "상세메모"(StudentDetail.Memo)와 다른 칸이다 — 이름이 겹치면
            //   어느 쪽이 어느 칸인지 열 제목만 보고 알 수 없다.
            기본메모 = vm.Student?.Memo ?? string.Empty,
            부이름 = vm.Detail?.FatherName ?? string.Empty,
            부연락처 = vm.Detail?.FatherPhone ?? string.Empty,
            부직업 = vm.Detail?.FatherJob ?? string.Empty,
            모이름 = vm.Detail?.MotherName ?? string.Empty,
            모연락처 = vm.Detail?.MotherPhone ?? string.Empty,
            모직업 = vm.Detail?.MotherJob ?? string.Empty,
            보호자 = vm.Detail?.GuardianName ?? string.Empty,
            보호자연락처 = vm.Detail?.GuardianPhone ?? string.Empty,
            보호자관계 = vm.Detail?.GuardianRelation ?? string.Empty,
            가족사항 = vm.Detail?.FamilyInfo ?? string.Empty,
            교우관계 = vm.Detail?.Friends ?? string.Empty,
            흥미 = vm.Detail?.Interests ?? string.Empty,
            특기 = vm.Detail?.Talents ?? string.Empty,
            진로희망 = vm.Detail?.CareerGoal ?? string.Empty,
            건강정보 = vm.Detail?.HealthInfo ?? string.Empty,
            알레르기 = vm.Detail?.Allergies ?? string.Empty,
            특별지원 = vm.Detail?.SpecialNeeds ?? string.Empty,
            상세메모 = vm.Detail?.Memo ?? string.Empty,
        }).ToList();

        var fileName = $"학생카드_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

        await Task.Run(() => MiniExcelLibs.MiniExcel.SaveAs(filePath, rows));
        return filePath;
    }

    private record CardExportDto
    {
        public int 학년 { get; init; }
        public int 반 { get; init; }
        public int 번호 { get; init; }
        public string 이름 { get; init; } = string.Empty;
        public string 성별 { get; init; } = string.Empty;
        public string 생년월일 { get; init; } = string.Empty;
        public string 연락처 { get; init; } = string.Empty;
        public string 이메일 { get; init; } = string.Empty;
        public string 주소 { get; init; } = string.Empty;
        public string 기본메모 { get; init; } = string.Empty;
        public string 부이름 { get; init; } = string.Empty;
        public string 부연락처 { get; init; } = string.Empty;
        public string 부직업 { get; init; } = string.Empty;
        public string 모이름 { get; init; } = string.Empty;
        public string 모연락처 { get; init; } = string.Empty;
        public string 모직업 { get; init; } = string.Empty;
        public string 보호자 { get; init; } = string.Empty;
        public string 보호자연락처 { get; init; } = string.Empty;
        public string 보호자관계 { get; init; } = string.Empty;
        public string 가족사항 { get; init; } = string.Empty;
        public string 교우관계 { get; init; } = string.Empty;
        public string 흥미 { get; init; } = string.Empty;
        public string 특기 { get; init; } = string.Empty;
        public string 진로희망 { get; init; } = string.Empty;
        public string 건강정보 { get; init; } = string.Empty;
        public string 알레르기 { get; init; } = string.Empty;
        public string 특별지원 { get; init; } = string.Empty;
        public string 상세메모 { get; init; } = string.Empty;
    }

    /// <summary>
    /// 학급 명렬표(학생정보 요약) PDF — 번호·이름·성별·생년월일·연락처·주소·보호자 연락처 표.
    /// 데이터 없으면 null.
    /// </summary>
    public async Task<string?> GenerateClassInfoPdfFromDbAsync(int year, int grade, int classNo)
    {
        var students = await LoadClassStudentsAsync(year, grade, classNo);
        if (students.Count == 0) return null;

        var fileName = $"학생정보_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(30);
                    page.PageColor(Colors.White);

                    page.Header().Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{year}학년도 {grade}학년 {classNo}반 학생정보")
                            .FontSize(16).Bold();
                        col.Item().AlignRight().Text($"출력일시: {DateTime.Now:yyyy-MM-dd HH:mm}")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingBottom(8);
                    });

                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(34);   // 번호
                            columns.ConstantColumn(64);   // 이름
                            columns.ConstantColumn(34);   // 성별
                            columns.ConstantColumn(76);   // 생년월일
                            columns.ConstantColumn(92);   // 연락처
                            columns.RelativeColumn(3);    // 주소
                            columns.ConstantColumn(64);   // 보호자
                            columns.ConstantColumn(40);   // 관계
                            columns.ConstantColumn(92);   // 보호자 연락처
                        });

                        static QuestPDF.Infrastructure.IContainer Head(QuestPDF.Infrastructure.IContainer c) =>
                            c.Border(0.5f).BorderColor(Colors.Grey.Medium)
                             .Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().AlignMiddle();
                        static QuestPDF.Infrastructure.IContainer Cell(QuestPDF.Infrastructure.IContainer c) =>
                            c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignMiddle();

                        table.Header(header =>
                        {
                            foreach (var h in new[] { "번호", "이름", "성별", "생년월일", "연락처", "주소", "보호자", "관계", "보호자 연락처" })
                                header.Cell().Element(Head).Text(h).FontSize(9).Bold();
                        });

                        foreach (var vm in students)
                        {
                            table.Cell().Element(Cell).AlignCenter().Text((vm.Enrollment?.Number ?? 0).ToString()).FontSize(9);
                            table.Cell().Element(Cell).Text(vm.Student?.Name ?? "").FontSize(9);
                            table.Cell().Element(Cell).AlignCenter().Text(vm.Student?.Sex ?? "").FontSize(9);
                            table.Cell().Element(Cell).AlignCenter().Text(vm.Student?.BirthDate?.ToString("yyyy-MM-dd") ?? "").FontSize(9);
                            table.Cell().Element(Cell).Text(vm.Student?.Phone ?? "").FontSize(9);
                            table.Cell().Element(Cell).Text(vm.Student?.Address ?? "").FontSize(9);
                            // 이름·관계·연락처를 한 번에 고른다 — 따로 고르면 서로 다른 사람이 한 줄에 실린다.
                            var guardian = vm.Detail?.ResolvePrimaryGuardian() ?? (string.Empty, string.Empty, string.Empty);
                            table.Cell().Element(Cell).Text(guardian.Name).FontSize(9);
                            table.Cell().Element(Cell).AlignCenter().Text(guardian.Relation).FontSize(9);
                            table.Cell().Element(Cell).Text(guardian.Phone).FontSize(9);
                        }
                    });

                    page.Footer().AlignRight().Text("NewSchool").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            }).GeneratePdf(filePath);
        });

        return filePath;
    }

    internal static async Task<List<StudentCardViewModel>> LoadClassStudentsAsync(int year, int grade, int classNo)
    {
        string schoolCode = Settings.SchoolCode.Value;
        using var enrollmentService = new EnrollmentService();
        var enrollments = await enrollmentService.GetClassRosterAsync(schoolCode, year, grade, classNo);
        if (enrollments.Count == 0) return new List<StudentCardViewModel>();

        var ids = enrollments.Select(e => e.StudentID).ToList();
        using var studentService = new StudentService(SchoolDatabase.DbPath);
        var students = (await studentService.GetStudentsByIdsAsync(ids))
            .ToDictionary(s => s.StudentID, s => s);

        using var detailService = new StudentDetailService(SchoolDatabase.DbPath);
        var details = (await detailService.GetByStudentIdsAsync(ids))
            .ToDictionary(d => d.StudentID, d => d);

        var list = new List<StudentCardViewModel>();
        foreach (var e in enrollments.OrderBy(x => x.Number))
        {
            students.TryGetValue(e.StudentID, out var st);
            details.TryGetValue(e.StudentID, out var d);
            var vm = new StudentCardViewModel();
            vm.LoadFromModels(e, st, d);
            list.Add(vm);
        }
        return list;
    }
}
