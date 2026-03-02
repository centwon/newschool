using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using NewSchool.Models;
using NewSchool.Pages;
using NewSchool.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NewSchool.Services;

/// <summary>
/// 학생카드 PDF 생성 서비스
/// </summary>
public class StudentCardPrintService
{
    /// <summary>
    /// 학생카드 PDF 생성 (사용자가 저장 위치 선택)
    /// </summary>
    /// <param name="viewModel">학생 정보 ViewModel</param>
    /// <param name="window">WinUI3 Window 참조 (FileSavePicker 초기화에 필요)</param>
    /// <param name="includeDetailInfo">세부 정보 포함 여부</param>
    /// <param name="studentLogs">학생 생활 기록 (null이면 포함하지 않음)</param>
    /// <returns>저장된 파일 경로 (취소 시 null)</returns>
    public async Task<string?> GenerateStudentCardPdfAsync(
        StudentCardViewModel viewModel,
        Window window,
        bool includeDetailInfo = true,
        List<StudentLogViewModel>? studentLogs = null)
    {
        if (window == null)
            throw new ArgumentNullException(nameof(window));

        // QuestPDF 라이선스 설정
        QuestPDF.Settings.License = LicenseType.Community;

        // 한글 폰트 설정 (시스템 폰트 사용)
        ConfigureKoreanFont();

        // 리팩토링 후: Enrollment를 통해 접근
        var grade = viewModel.Enrollment?.Grade ?? 0;
        var classNo = viewModel.Enrollment?.Class ?? 0;
        var number = viewModel.Enrollment?.Number ?? 0;
        var year = viewModel.Enrollment?.Year ?? Settings.WorkYear.Value;

        // 제안할 파일명 생성
        var suggestedFileName = $"학생정보_{grade}학년{classNo}반_{number}번_{viewModel.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

        // FileSavePicker 설정
        var savePicker = new FileSavePicker();

        // WinUI3에서 필요한 초기화
        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(savePicker, hwnd);

        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("PDF 문서", new List<string>() { ".pdf" });
        savePicker.SuggestedFileName = suggestedFileName;

        // 사용자가 저장 위치 선택
        var file = await savePicker.PickSaveFileAsync();
        if (file == null)
            return null; // 사용자가 취소함

        var filePath = file.Path;

        // PDF 생성을 백그라운드 스레드에서 실행 (UI 블로킹 방지)
        await Task.Run(() =>
        {
            GeneratePdfDocument(filePath, viewModel, year, grade, classNo, number, includeDetailInfo, studentLogs);
        });

        return filePath;
    }

    /// <summary>
    /// PDF 문서 생성 (동기 메서드 - 백그라운드에서 호출)
    /// </summary>
    private void GeneratePdfDocument(string filePath, StudentCardViewModel viewModel, int year, int grade, int classNo, int number, bool includeDetailInfo, List<StudentLogViewModel>? studentLogs)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);

                // 헤더
                page.Header().Element(c => ComposeHeader(c, year, grade, classNo, number));

                // 본문
                page.Content().Element(content => ComposeContent(content, viewModel, includeDetailInfo, studentLogs));

                // 푸터
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(filePath);
    }

    /// <summary>헤더 구성</summary>
    private void ComposeHeader(IContainer container, int year, int grade, int classNo, int number)
    {
        container.Column(column =>
        {
            // 제목
            column.Item().AlignCenter().Text("학생 정보 카드")
                .FontSize(26)
                .Bold()
                .FontColor(Colors.Blue.Darken3);

            // 학급 정보
            column.Item().PaddingTop(10).AlignCenter().Text($"{year}학년도 {grade}학년 {classNo}반 {number}번")
                .FontSize(14)
                .FontColor(Colors.Grey.Darken2);

            column.Item().PaddingTop(10).LineHorizontal(2).LineColor(Colors.Blue.Medium);
        });
    }

    /// <summary>푸터 구성</summary>
    private void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            column.Item().PaddingTop(5).AlignCenter().Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                .FontSize(10)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    private void ComposeContent(IContainer container, StudentCardViewModel vm, bool includeDetailInfo, List<StudentLogViewModel>? studentLogs)
    {
        container.Column(column =>
        {
            // 기본 정보 섹션 (항상 포함)
            column.Item().PaddingTop(20).Element(c => ComposeBasicInfo(c, vm));

            // 세부 정보 포함 시
            if (includeDetailInfo)
            {
                // 보호자 정보 섹션
                column.Item().PaddingTop(20).Element(c => ComposeGuardianInfo(c, vm));

                // 학교생활 정보 섹션
                column.Item().PaddingTop(20).Element(c => ComposeSchoolLifeInfo(c, vm));

                // 특이사항 및 건강 정보 섹션
                column.Item().PaddingTop(20).Element(c => ComposeMemoInfo(c, vm));
            }

            // 학생 생활 기록 섹션
            if (studentLogs != null && studentLogs.Count > 0)
            {
                column.Item().PaddingTop(20).Element(c => ComposeStudentLogs(c, studentLogs));
            }
        });
    }

    /// <summary>기본 정보 섹션</summary>
    private void ComposeBasicInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(column =>
        {
            // 섹션 제목
            column.Item().Background(Colors.Blue.Lighten4)
                .Padding(8)
                .Text("기본 정보")
                .FontSize(16)
                .Bold()
                .FontColor(Colors.Blue.Darken2);

            // 정보 내용
            column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.ConstantItem(80).Text("이름").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Student?.Name ?? "").FontSize(12);
                    row.ConstantItem(80).Text("성별").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Student?.Sex ?? "").FontSize(12);
                });

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.ConstantItem(80).Text("생년월일").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Student?.BirthDate?.ToString("yyyy-MM-dd") ?? "").FontSize(12);
                    row.ConstantItem(80).Text("나이").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text($"{vm.Age}세").FontSize(12);
                });

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.ConstantItem(80).Text("전화번호").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Student?.Phone ?? "").FontSize(12);
                });

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.ConstantItem(80).Text("이메일").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Student?.Email ?? "").FontSize(12);
                });

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.ConstantItem(80).Text("주소").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Student?.Address ?? "").FontSize(12);
                });

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.ConstantItem(80).Text("메모").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Student?.Memo ?? "").FontSize(12);
                });
            });
        });
    }

    /// <summary>보호자 정보 섹션</summary>
    private void ComposeGuardianInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(column =>
        {
            // 섹션 제목
            column.Item().Background(Colors.Green.Lighten4)
                .Padding(8)
                .Text("보호자 정보")
                .FontSize(16)
                .Bold()
                .FontColor(Colors.Green.Darken2);

            // 정보 내용
            column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(col =>
            {
                // 아버지
                col.Item().Row(row =>
                {
                    row.ConstantItem(80).Text("아버지").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Detail?.FatherName ?? "").FontSize(12);
                    row.ConstantItem(80).Text("전화번호").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Detail?.FatherPhone ?? "").FontSize(12);
                });

                // 어머니
                col.Item().PaddingTop(8).Row(row =>
                {
                    row.ConstantItem(80).Text("어머니").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Detail?.MotherName ?? "").FontSize(12);
                    row.ConstantItem(80).Text("전화번호").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Detail?.MotherPhone ?? "").FontSize(12);
                });

                // 보호자
                col.Item().PaddingTop(8).Row(row =>
                {
                    row.ConstantItem(80).Text("보호자").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Detail?.GuardianName ?? "").FontSize(12);
                    row.ConstantItem(80).Text("관계").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Detail?.GuardianRelation ?? "").FontSize(12);
                });

                col.Item().PaddingTop(8).Row(row =>
                {
                    row.ConstantItem(80).Text("보호자 연락처").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Text(vm.Detail?.GuardianPhone ?? "").FontSize(12);
                });

                // 가족 구성
                col.Item().PaddingTop(8).Column(c =>
                {
                    c.Item().Text("가족 구성").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(4).Text(vm.Detail?.FamilyInfo ?? "").FontSize(11);
                });
            });
        });
    }

    /// <summary>학교생활 정보 섹션</summary>
    private void ComposeSchoolLifeInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(column =>
        {
            // 섹션 제목
            column.Item().Background(Colors.Orange.Lighten4)
                .Padding(8)
                .Text("학교생활 정보")
                .FontSize(16)
                .Bold()
                .FontColor(Colors.Orange.Darken2);

            // 정보 테이블
            column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(col =>
            {
                // 교우 관계
                col.Item().Column(c =>
                {
                    c.Item().Text("교우 관계").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(4).Text(vm.Detail?.Friends ?? "").FontSize(11);
                });

                // 흥미
                col.Item().PaddingTop(8).Column(c =>
                {
                    c.Item().Text("흥미").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(4).Text(vm.Detail?.Interests ?? "").FontSize(11);
                });

                // 특기
                col.Item().PaddingTop(8).Column(c =>
                {
                    c.Item().Text("특기").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(4).Text(vm.Detail?.Talents ?? "").FontSize(11);
                });

                // 진로 희망
                col.Item().PaddingTop(8).Column(c =>
                {
                    c.Item().Text("진로 희망").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(4).Text(vm.Detail?.CareerGoal ?? "").FontSize(11);
                });
            });
        });
    }

    /// <summary>특이사항 섹션</summary>
    private void ComposeMemoInfo(IContainer container, StudentCardViewModel vm)
    {
        container.Column(column =>
        {
            // 섹션 제목
            column.Item().Background(Colors.Red.Lighten4)
                .Padding(8)
                .Text("특이사항 및 건강 정보")
                .FontSize(16)
                .Bold()
                .FontColor(Colors.Red.Darken2);

            // 정보 테이블
            column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(col =>
            {
                // 건강 정보
                col.Item().Column(c =>
                {
                    c.Item().Text("건강 정보").FontSize(12).SemiBold().FontColor(Colors.Red.Darken1);
                    c.Item().PaddingTop(4).Text(vm.Detail?.HealthInfo ?? "").FontSize(11);
                });

                // 알레르기
                col.Item().PaddingTop(8).Column(c =>
                {
                    c.Item().Text("알레르기").FontSize(12).SemiBold().FontColor(Colors.Red.Darken1);
                    c.Item().PaddingTop(4).Text(vm.Detail?.Allergies ?? "").FontSize(11);
                });

                // 특수교육
                col.Item().PaddingTop(8).Column(c =>
                {
                    c.Item().Text("특수교육 대상").FontSize(12).SemiBold().FontColor(Colors.Red.Darken1);
                    c.Item().PaddingTop(4).Text(vm.Detail?.SpecialNeeds ?? "").FontSize(11);
                });

                // 상세 메모
                col.Item().PaddingTop(8).Column(c =>
                {
                    c.Item().Text("상세 메모").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                    c.Item().PaddingTop(4).Text(vm.Detail?.Memo ?? "").FontSize(11);
                });
            });
        });
    }

    /// <summary>학생 생활 기록 섹션</summary>
    private void ComposeStudentLogs(IContainer container, List<StudentLogViewModel> logs)
    {
        container.Column(column =>
        {
            // 섹션 제목
            column.Item().Background(Colors.Purple.Lighten4)
                .Padding(8)
                .Text("학생 생활 기록")
                .FontSize(16)
                .Bold()
                .FontColor(Colors.Purple.Darken2);

            // 로그 테이블
            column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(80);   // 날짜
                    columns.ConstantColumn(80);   // 카테고리
                    columns.ConstantColumn(80);   // 과목
                    columns.RelativeColumn();     // 내용
                });

                // 헤더
                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                        .Text("날짜").FontSize(11).SemiBold();
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                        .Text("카테고리").FontSize(11).SemiBold();
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                        .Text("과목").FontSize(11).SemiBold();
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                        .Text("내용").FontSize(11).SemiBold();
                });

                // 로그 데이터
                foreach (var log in logs)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                        .Text(log.Date.ToString("MM/dd")).FontSize(10);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                        .Text(log.Category.ToString()).FontSize(10);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                        .Text(log.SubjectName ?? "").FontSize(10);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                        .Text(log.Log ?? "").FontSize(10);
                }
            });
        });
    }

    /// <summary>
    /// 한글 폰트 설정
    /// </summary>
    private void ConfigureKoreanFont()
    {
        // Windows 시스템 폰트 경로
        var fontsPath = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        // FontDiscoveryPaths에 폰트 폴더 경로 추가 (파일 경로가 아닌 폴더 경로)
        if (!QuestPDF.Settings.FontDiscoveryPaths.Contains(fontsPath))
        {
            QuestPDF.Settings.FontDiscoveryPaths.Add(fontsPath);
        }

        // 사용 가능한 한글 폰트 확인 (디버깅용)
        var koreanFonts = new[]
        {
            Path.Combine(fontsPath, "malgun.ttf"),      // 맑은 고딕
            Path.Combine(fontsPath, "gulim.ttc"),       // 굴림
            Path.Combine(fontsPath, "batang.ttc"),      // 바탕
        };

        bool fontFound = false;
        foreach (var fontPath in koreanFonts)
        {
            if (File.Exists(fontPath))
            {
                fontFound = true;
                break;
            }
        }

        if (!fontFound)
        {
            System.Diagnostics.Debug.WriteLine("[StudentCardPrintService] 경고: 한글 폰트를 찾을 수 없습니다. 한글이 깨질 수 있습니다.");
        }
    }
}
