using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NewSchool.Controls;

/// <summary>
/// 단원(소단원) 관리 — 특정 교과의 단원 목록 CRUD · CSV 가져오기/내보내기.
///
/// 원래는 연간 수업 계획 페이지의 첫 탭이었고, 그것을 걷어내면서 독립 페이지로 옮겼다가
/// 지금은 수업 관리의 탭 하나로 들어왔다(수업일지가 단원을 참조하므로 단원을 만들 UI 는 필요하다).
/// 그래서 페이지가 아니라 컨트롤이다 — 대상 교과는 <see cref="LoadAsync"/> 로 받는다.
/// </summary>
public sealed partial class CourseSectionView : UserControl
{
    private Course? _selectedCourse;
    private readonly ObservableCollection<CourseSection> _courseSections = [];
    private List<CourseSection>? _pendingImportSections;

    public CourseSectionView()
    {
        this.InitializeComponent();
        SectionListView.ItemsSource = _courseSections;
        UpdateSectionUI();
    }

    /// <summary>
    /// 대상 교과를 바꾼다. null 이면 목록을 비우고 안내를 띄운다.
    /// </summary>
    public async Task LoadAsync(Course? course)
    {
        _selectedCourse = course;
        SectionErrorInfoBar.IsOpen = false;

        if (course == null)
        {
            _courseSections.Clear();
            UpdateSectionUI();
            return;
        }

        await LoadCourseSectionsAsync(course.No);
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
            Debug.WriteLine($"[CourseSectionView] 단원 로드 실패: {ex.Message}");
            _courseSections.Clear();
            UpdateSectionUI();
            ShowSectionError($"단원을 불러오지 못했습니다: {ex.Message}");
        }
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
            Debug.WriteLine($"[CourseSectionView] CSV 가져오기 실패: {ex.Message}");
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
            Debug.WriteLine($"[CourseSectionView] CSV 가져오기 완료: {sections.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionView] CSV 가져오기 저장 실패: {ex.Message}");
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

            Debug.WriteLine($"[CourseSectionView] CSV 내보내기 완료: {file.Path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionView] CSV 내보내기 실패: {ex.Message}");
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

            Debug.WriteLine($"[CourseSectionView] 템플릿 다운로드 완료: {file.Path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionView] 템플릿 다운로드 실패: {ex.Message}");
            ShowSectionError($"템플릿 다운로드 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private static List<CourseSection> ParseCsv(string content)
    {
        var sections = new List<CourseSection>();

        // RFC 4180 기준 파싱 — GenerateCsv 가 만드는 따옴표 이스케이프("")와
        // 따옴표 안 줄바꿈(여러 줄 메모)을 그대로 되읽을 수 있어야 왕복이 깨지지 않는다.
        var records = Services.CsvExportService.ParseRecords(content);

        for (int i = 1; i < records.Count; i++)  // 0번은 헤더
        {
            var fields = records[i];
            if (fields.Length < 6) continue;
            if (fields.All(string.IsNullOrWhiteSpace)) continue;

            try
            {
                var hoursField = fields.Length > 8 ? fields[8].Trim() : "";
                var hoursParsed = int.TryParse(hoursField, out var hours);

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
                    EstimatedHours = hoursParsed && hours > 0 ? hours : 1
                };

                // 10번째 열 이후(유형·학습목표·수업계획·자료·메모·고정날짜)는 1.0 에서 제거됐다.
                // 옛 CSV 를 가져와도 그 열들은 그냥 무시된다.

                if (section.UnitNo > 0 && !string.IsNullOrWhiteSpace(section.SectionName))
                {
                    sections.Add(section);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CourseSectionView] CSV 라인 파싱 실패 (라인 {i + 1}): {ex.Message}");
            }
        }

        return sections;
    }

    private string GenerateCsv()
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("대단원번호,대단원명,중단원번호,중단원명,소단원번호,소단원명,시작페이지,끝페이지,예상차시");

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
                section.EstimatedHours
            ));
        }

        return sb.ToString();
    }

    private static string GenerateCsvTemplate()
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("대단원번호,대단원명,중단원번호,중단원명,소단원번호,소단원명,시작페이지,끝페이지,예상차시");
        sb.AppendLine("1,수와 연산,1,자연수의 혼합 계산,1,덧셈과 뺄셈의 혼합 계산,8,11,2");
        sb.AppendLine("1,수와 연산,1,자연수의 혼합 계산,2,곱셈과 나눗셈의 혼합 계산,12,15,2");
        sb.AppendLine("2,문자와 식,1,문자의 사용,1,문자를 사용한 식,16,20,3");
        return sb.ToString();
    }

    private static string EscapeCsv(string field)
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
            await LoadCourseSectionsAsync(_selectedCourse.No);
        }
    }

    private async void OnSectionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CourseSection section || _selectedCourse == null) return;

        var dialog = new Dialogs.CourseSectionDialog(_selectedCourse, section)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await LoadCourseSectionsAsync(_selectedCourse.No);
        }
    }

    private async void OnDeleteSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not CourseSection section) return;

        if (!await MessageBox.ShowConfirmAsync(
                $"\"{section.SectionName}\" 단원을 삭제하시겠습니까?",
                "삭제 확인", "삭제", "취소"))
            return;

        try
        {
            // DB에서 개별 삭제 (연관 데이터 보존)
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            await repo.DeleteAsync(section.No);

            _courseSections.Remove(section);
            UpdateSectionUI();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionView] 소단원 삭제 실패: {ex.Message}");
            ShowSectionError($"삭제 중 오류가 발생했습니다.\n{ex.Message}");
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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionView] 전체 삭제 실패: {ex.Message}");
            ShowSectionError($"전체 삭제 중 오류가 발생했습니다.\n{ex.Message}");
            ClearAllFlyout.Hide();
        }
    }

    #endregion

    #region Section Helpers

    private void UpdateSectionUI()
    {
        bool hasCourse = _selectedCourse != null;
        bool hasSections = _courseSections.Count > 0;

        BtnAddSection.IsEnabled = hasCourse;
        BtnImportCsv.IsEnabled = hasCourse;
        BtnClearAll.IsEnabled = hasCourse;

        SectionEmptyState.Visibility = hasSections ? Visibility.Collapsed : Visibility.Visible;
        SectionListView.Visibility = hasSections ? Visibility.Visible : Visibility.Collapsed;
        SectionListHeader.Visibility = hasSections ? Visibility.Visible : Visibility.Collapsed;

        if (!hasCourse)
        {
            TxtSectionEmpty.Text = "수업을 먼저 선택하세요";
            TxtSectionEmptyHint.Text = "위쪽 필터의 [수업] 에서 단원을 관리할 수업을 고르세요";
        }
        else
        {
            TxtSectionEmpty.Text = "등록된 단원이 없습니다";
            TxtSectionEmptyHint.Text = "CSV 파일을 가져오거나 소단원을 추가하세요";
        }

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
            // SortOrder 값 갱신 (ObservableCollection은 이미 드래그 순서로 정렬됨)
            for (int i = 0; i < _courseSections.Count; i++)
            {
                _courseSections[i].SortOrder = i + 1;
            }

            // 트랜잭션 일괄 업데이트
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            await repo.BulkUpdateSortOrderAsync(_courseSections.ToList());

            // DB에서 재로드하여 순서와 연번을 확실하게 반영
            await LoadCourseSectionsAsync(_selectedCourse.No);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionView] 단원 순서 변경 실패: {ex.Message}");
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

        // Grid 의 첫 번째 자식 = 연번 TextBlock
        if (args.ItemContainer?.ContentTemplateRoot is Grid grid
            && grid.Children.Count > 0
            && grid.Children[0] is TextBlock indexText)
        {
            indexText.Text = (args.ItemIndex + 1).ToString();
        }
    }

    #endregion
}
