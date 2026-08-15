using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NewSchool.Pages;

/// <summary>
/// 단원(소단원) 관리 — 특정 교과의 단원 목록 CRUD · CSV 가져오기/내보내기.
///
/// 예전에는 연간 수업 계획 페이지의 첫 번째 탭이었다. 연간 계획·진도 관리를 걷어내면서
/// 단원을 만들 UI 가 통째로 사라지는 문제가 있어(수업일지가 단원을 참조한다),
/// 이 부분만 떼어내 수업 관리에서 교과별로 들어오는 독립 페이지로 만들었다.
/// </summary>
public sealed partial class CourseSectionPage : Page
{
    private Course? _selectedCourse;
    private readonly ObservableCollection<CourseSection> _courseSections = [];
    private List<CourseSection>? _pendingImportSections;

    public CourseSectionPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _selectedCourse = e.Parameter as Course;
        if (_selectedCourse == null)
        {
            await MessageBox.ShowAsync("교과 정보를 받지 못했습니다.", "오류");
            GoBack();
            return;
        }

        TxtCourseInfo.Text = $"{_selectedCourse.Grade}학년 · {_selectedCourse.Subject}";
        SectionListView.ItemsSource = _courseSections;
        await LoadCourseSectionsAsync(_selectedCourse.No);
    }

    private async Task LoadCourseSectionsAsync(int courseNo)
    {
        try
        {
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            var sections = await repo.GetByCourseAsync(courseNo);

            _courseSections.Clear();
            foreach (var section in sections)
                _courseSections.Add(section);

            UpdateSectionUI();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionPage] 단원 로드 실패: {ex.Message}");
            _courseSections.Clear();
            UpdateSectionUI();
            ShowSectionError($"단원을 불러오지 못했습니다: {ex.Message}");
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (Frame.CanGoBack) Frame.GoBack();
        else Frame.Navigate(typeof(CourseManagementPage));
    }

    #region CSV Import/Export

    private async void OnImportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null)
        {
            ShowSectionError("먼저 수업을 선택해주세요.");
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".csv");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var content = await FileIO.ReadTextAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            var sections = ParseCsv(content);

            if (sections.Count == 0)
            {
                ShowSectionError("CSV 파일에서 유효한 단원 데이터를 찾을 수 없습니다.\n형식을 확인해주세요.");
                return;
            }

            if (_courseSections.Count > 0)
            {
                _pendingImportSections = sections;
                TxtImportConfirm.Text = $"기존 {_courseSections.Count}개의 단원이 삭제되고 새로 {sections.Count}개의 단원이 추가됩니다.\n계속하시겠습니까?";
                ImportConfirmFlyout.ShowAt(sender as FrameworkElement ?? BtnImportCsv);
                return;
            }

            await ApplyImportSectionsAsync(sections);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionPage] CSV 가져오기 실패: {ex.Message}");
            ShowSectionError($"CSV 파일 가져오기 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private void OnImportCancelClick(object sender, RoutedEventArgs e)
    {
        _pendingImportSections = null;
        ImportConfirmFlyout.Hide();
    }

    private async void OnImportConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_pendingImportSections != null)
        {
            await ApplyImportSectionsAsync(_pendingImportSections);
            _pendingImportSections = null;
        }
        ImportConfirmFlyout.Hide();
    }

    private async Task ApplyImportSectionsAsync(List<CourseSection> sections)
    {
        if (_selectedCourse == null) return;

        try
        {
            // CSV 가져오기는 전체 대체 동작이므로 BulkCreateAsync 사용
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            await repo.BulkCreateAsync(_selectedCourse.No, sections);

            _courseSections.Clear();
            foreach (var section in sections)
            {
                _courseSections.Add(section);
            }

            UpdateSectionUI();
            Debug.WriteLine($"[CourseSectionPage] CSV 가져오기 완료: {sections.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionPage] CSV 가져오기 저장 실패: {ex.Message}");
            ShowSectionError($"CSV 가져오기 저장 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private async void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_courseSections.Count == 0)
        {
            ShowSectionError("내보낼 단원이 없습니다.");
            return;
        }

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            // 과목명은 사용자가 직접 입력한 값이라 "국어/문학" 처럼 경로 문자가 섞일 수 있다
            var subjectPart = Helpers.FileNameHelper.Sanitize(_selectedCourse?.Subject);
            if (subjectPart.Length == 0) subjectPart = "단원";
            picker.SuggestedFileName = $"{subjectPart}_단원구조";
            picker.FileTypeChoices.Add("CSV 파일", new List<string> { ".csv" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var csv = GenerateCsv();
            await FileIO.WriteTextAsync(file, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

            Debug.WriteLine($"[CourseSectionPage] CSV 내보내기 완료: {file.Path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionPage] CSV 내보내기 실패: {ex.Message}");
            ShowSectionError($"CSV 파일 내보내기 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private async void OnDownloadTemplateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = "단원구조_템플릿";
            picker.FileTypeChoices.Add("CSV 파일", new List<string> { ".csv" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var template = GenerateCsvTemplate();
            await FileIO.WriteTextAsync(file, template, Windows.Storage.Streams.UnicodeEncoding.Utf8);

            Debug.WriteLine($"[CourseSectionPage] 템플릿 다운로드 완료: {file.Path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionPage] 템플릿 다운로드 실패: {ex.Message}");
            ShowSectionError($"템플릿 다운로드 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private List<CourseSection> ParseCsv(string content)
    {
        var sections = new List<CourseSection>();

        // RFC 4180 기준 파싱 — GenerateCsv 가 만드는 따옴표 이스케이프("")와
        // 따옴표 안 줄바꿈(여러 줄 메모)을 그대로 되읽을 수 있어야 왕복이 깨지지 않는다.
        // (기존: '\n' 단순 분리 + 이스케이프 미처리 → 줄바꿈 포함 필드에서 행이 깨짐)
        var records = ParseCsvRecords(content);

        for (int i = 1; i < records.Count; i++)  // 0번은 헤더
        {
            var fields = records[i];
            if (fields.Length < 6) continue;
            if (fields.All(string.IsNullOrWhiteSpace)) continue;

            try
            {
                // 예상차시 파싱 디버깅
                var hoursField = fields.Length > 8 ? fields[8].Trim() : "";
                var hoursParsed = int.TryParse(hoursField, out var hours);
                Debug.WriteLine($"[CSV] 라인 {i}: 예상차시 필드='{hoursField}', 파싱성공={hoursParsed}, 값={hours}");

                var section = new CourseSection
                {
                    UnitNo = int.TryParse(fields[0], out var unitNo) ? unitNo : 0,
                    UnitName = fields[1].Trim(),
                    ChapterNo = int.TryParse(fields[2], out var chapterNo) ? chapterNo : 0,
                    ChapterName = fields[3].Trim(),
                    SectionNo = int.TryParse(fields[4], out var sectionNo) ? sectionNo : 0,
                    SectionName = fields[5].Trim(),
                    StartPage = fields.Length > 6 && int.TryParse(fields[6], out var startPage) ? startPage : 0,
                    EndPage = fields.Length > 7 && int.TryParse(fields[7], out var endPage) ? endPage : 0,
                    EstimatedHours = hoursParsed && hours > 0 ? hours : 1,
                    SectionType = fields.Length > 9 && !string.IsNullOrWhiteSpace(fields[9]) ? fields[9].Trim() : "Normal",
                    LearningObjective = fields.Length > 10 ? fields[10].Trim() : string.Empty,
                    LessonPlan = fields.Length > 11 ? fields[11].Trim() : string.Empty,
                    MaterialPath = fields.Length > 12 ? fields[12].Trim() : string.Empty,
                    MaterialUrl = fields.Length > 13 ? fields[13].Trim() : string.Empty,
                    Memo = fields.Length > 14 ? fields[14].Trim() : string.Empty
                };

                if (fields.Length > 15 && !string.IsNullOrWhiteSpace(fields[15]))
                {
                    if (DateTime.TryParse(fields[15].Trim(), out var pinnedDate))
                    {
                        section.IsPinned = true;
                        section.PinnedDate = pinnedDate;
                    }
                }

                if (section.SectionType == "Exam" || section.SectionType == "Assessment")
                {
                    section.IsPinned = true;
                }

                if (section.UnitNo > 0 && !string.IsNullOrWhiteSpace(section.SectionName))
                {
                    sections.Add(section);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CourseSectionPage] CSV 라인 파싱 실패 (라인 {i + 1}): {ex.Message}");
            }
        }

        return sections;
    }

    /// <summary>
    /// CSV 전체를 레코드 단위로 파싱 (RFC 4180). 공용 파서(CsvExportService)로 위임 —
    /// 따옴표 필드 안의 쉼표·줄바꿈, "" 이스케이프를 처리해 GenerateCsv 출력과 왕복이 일치한다.
    /// </summary>
    private static List<string[]> ParseCsvRecords(string content)
        => Services.CsvExportService.ParseRecords(content);

    private string GenerateCsv()
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("대단원번호,대단원명,중단원번호,중단원명,소단원번호,소단원명,시작페이지,끝페이지,예상차시,유형,학습목표,수업계획,자료파일,자료링크,메모,고정날짜");

        foreach (var section in _courseSections)
        {
            sb.AppendLine(string.Join(",",
                section.UnitNo,
                EscapeCsv(section.UnitName),
                section.ChapterNo,
                EscapeCsv(section.ChapterName),
                section.SectionNo,
                EscapeCsv(section.SectionName),
                section.StartPage,
                section.EndPage,
                section.EstimatedHours,
                section.SectionType,
                EscapeCsv(section.LearningObjective),
                EscapeCsv(section.LessonPlan),
                EscapeCsv(section.MaterialPath),
                EscapeCsv(section.MaterialUrl),
                EscapeCsv(section.Memo),
                section.PinnedDate?.ToString("yyyy-MM-dd") ?? ""
            ));
        }

        return sb.ToString();
    }

    private string GenerateCsvTemplate()
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("대단원번호,대단원명,중단원번호,중단원명,소단원번호,소단원명,시작페이지,끝페이지,예상차시,유형,학습목표,수업계획,자료파일,자료링크,메모,고정날짜");
        sb.AppendLine("1,수와 연산,1,자연수의 혼합 계산,1,덧셈과 뺄셈의 혼합 계산,8,11,2,Normal,덧셈과 뺄셈의 혼합 계산 순서를 안다,개념 도입 → 연습,,,,,");
        sb.AppendLine("1,수와 연산,1,자연수의 혼합 계산,2,곱셈과 나눗셈의 혼합 계산,12,15,2,Normal,곱셈과 나눗셈의 혼합 계산을 할 수 있다,모둠 활동,,,,,");
        sb.AppendLine("0,1학기 중간고사,0,지필평가,1,1단원 평가,0,0,1,Exam,1단원 학습 내용 평가,시험,,,,,2026-04-15");
        sb.AppendLine("0,수행평가,0,포트폴리오,1,수학 탐구 보고서,0,0,2,Assessment,탐구 주제 선정 및 보고서 작성,발표,,,,,2026-05-20");
        return sb.ToString();
    }

    private string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    #endregion

    #region Section Dialog

    /// <summary>
    /// 소단원 추가 버튼 클릭 - CourseSectionDialog 표시
    /// </summary>
    private async void OnAddSectionClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null)
        {
            ShowSectionError("먼저 수업을 선택해주세요.");
            return;
        }

        // 새 단원 추가 (section = null)
        var dialog = new Dialogs.CourseSectionDialog(_selectedCourse, null)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // 다이얼로그에서 저장됨 - 데이터 다시 로드
            await LoadCourseSectionsAsync(_selectedCourse.No);
            Debug.WriteLine("[CourseSectionPage] 단원 추가 완료 - 데이터 새로고침");
        }
    }

    /// <summary>
    /// 리스트 아이템 클릭 - 해당 단원 편집
    /// </summary>
    private async void OnSectionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CourseSection section || _selectedCourse == null) return;

        // 해당 단원 편집
        var dialog = new Dialogs.CourseSectionDialog(_selectedCourse, section)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // 다이얼로그에서 저장됨 - 데이터 다시 로드
            await LoadCourseSectionsAsync(_selectedCourse.No);
            Debug.WriteLine("[CourseSectionPage] 단원 편집 완료 - 데이터 새로고침");
        }
    }

    private async void OnDeleteSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is CourseSection section)
        {
            // 삭제 확인
            if (await MessageBox.ShowConfirmAsync(
                $"\"{section.SectionName}\" 단원을 삭제하시겠습니까?",
                "삭제 확인", "삭제", "취소"))
            {
                try
                {
                    // DB에서 개별 삭제 (연관 데이터 보존)
                    using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
                    await repo.DeleteAsync(section.No);

                    _courseSections.Remove(section);
                    UpdateSectionUI();
                    Debug.WriteLine($"[CourseSectionPage] 소단원 삭제: {section.FullPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CourseSectionPage] 소단원 삭제 실패: {ex.Message}");
                    ShowSectionError($"삭제 중 오류가 발생했습니다.\n{ex.Message}");
                }
            }
        }
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        if (_courseSections.Count == 0)
        {
            ShowSectionError("삭제할 단원이 없습니다.");
            return;
        }

        TxtClearConfirm.Text = $"{_courseSections.Count}개의 단원을 모두 삭제하시겠습니까?";
    }

    private void OnClearAllCancelClick(object sender, RoutedEventArgs e)
    {
        ClearAllFlyout.Hide();
    }

    private async void OnClearAllConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null) return;

        try
        {
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            await repo.DeleteByCourseAsync(_selectedCourse.No);

            _courseSections.Clear();
            UpdateSectionUI();
            ClearAllFlyout.Hide();

            Debug.WriteLine("[CourseSectionPage] 전체 삭제 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionPage] 전체 삭제 실패: {ex.Message}");
            ShowSectionError($"전체 삭제 중 오류가 발생했습니다.\n{ex.Message}");
            ClearAllFlyout.Hide();
        }
    }

    #endregion

    #region Section Helpers

    private void UpdateSectionUI()
    {
        bool hasSections = _courseSections.Count > 0;

        SectionEmptyState.Visibility = hasSections ? Visibility.Collapsed : Visibility.Visible;
        SectionListView.Visibility = hasSections ? Visibility.Visible : Visibility.Collapsed;
        SectionListHeader.Visibility = hasSections ? Visibility.Visible : Visibility.Collapsed;

        if (hasSections)
        {
            int unitCount = _courseSections.Select(s => s.UnitNo).Distinct().Count();
            int chapterCount = _courseSections.Select(s => (s.UnitNo, s.ChapterNo)).Distinct().Count();
            int totalHours = _courseSections.Sum(s => s.EstimatedHours);

            TxtSectionStatistics.Text =
                $"대단원 {unitCount}개 · 중단원 {chapterCount}개 · 소단원 {_courseSections.Count}개 · 총 {totalHours}차시";
        }
        else
        {
            TxtSectionStatistics.Text = "";
        }
    }

    // SaveSectionsAsync 제거됨 (v2 리팩토링)
    // 기존: BulkCreateAsync로 전체 삭제+재생성 → 연관 데이터 소실 문제
    // 변경: 개별 작업별로 적절한 Repository 메서드 직접 호출
    //   - 삭제: repo.DeleteAsync(no)
    //   - 전체 삭제: repo.DeleteByCourseAsync(courseNo)
    //   - CSV 가져오기: repo.BulkCreateAsync(courseNo, sections)

    private void ShowSectionError(string message)
    {
        SectionErrorInfoBar.Message = message;
        SectionErrorInfoBar.IsOpen = true;
    }



    /// <summary>
    /// 단원 드래그 완료 - SortOrder 업데이트
    /// </summary>
        private async void OnSectionDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_selectedCourse == null) return;

            try
            {
                Debug.WriteLine($"[CourseSectionPage] 드래그 완료: {_courseSections.Count}개 단원 정렬");

                // SortOrder 값 갱신 (ObservableCollection은 이미 드래그 순서로 정렬됨)
                for (int i = 0; i < _courseSections.Count; i++)
                {
                    _courseSections[i].SortOrder = i + 1;
                    Debug.WriteLine($"  [{i}] No={_courseSections[i].No}, SortOrder={i + 1}, Name={_courseSections[i].SectionName}");
                }

                // 트랜잭션 일괄 업데이트
                using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
                await repo.BulkUpdateSortOrderAsync(_courseSections.ToList());

                // DB에서 재로드하여 순서와 연번을 확실하게 반영
                await LoadCourseSectionsAsync(_selectedCourse.No);

                Debug.WriteLine($"[CourseSectionPage] SortOrder 일괄 업데이트 + 재로드 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CourseSectionPage] 단원 순서 변경 실패: {ex.Message}");
                ShowSectionError($"순서 변경 중 오류가 발생했습니다.\n{ex.Message}");
            }
        }

    /// <summary>
    /// 컨테이너 내용 변경 시 - 연번 업데이트 (1번부터 시작)
    /// </summary>
    private void OnSectionContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
            return;

        // ItemIndex를 사용하여 1부터 시작하는 연번 표시
        int displayIndex = args.ItemIndex + 1;

        // 첫 번째 TextBlock(연번)을 찾아서 업데이트
        if (args.ItemContainer?.ContentTemplateRoot is Grid grid)
        {
            // Grid의 첫 번째 자식 = 연번 TextBlock
            if (grid.Children.Count > 0 && grid.Children[0] is TextBlock indexText)
            {
                indexText.Text = displayIndex.ToString();
            }
        }
    }

    #endregion
}
