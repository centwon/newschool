using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

/// <summary>
/// StudentSpecPage - 학생부 특기사항 관리 페이지
/// </summary>
public sealed partial class StudentSpecPage : Page
{
    #region Fields

    private LogCategory _selectedCategory = LogCategory.전체;

    private readonly StudentSpecialService _specialService = new();
    private readonly EnrollmentService _enrollservice = new();

    #endregion

    #region Constructor

    public StudentSpecPage()
    {
        this.InitializeComponent();
        InitializeFilters();
    }

    #endregion

    #region Initialization

    private void InitializeFilters()
    {
        var categories = new List<LogCategory>
        {
            LogCategory.전체,
            LogCategory.자율활동,
            LogCategory.진로활동,
            LogCategory.동아리활동,
            LogCategory.봉사활동,
            LogCategory.교과활동,
            LogCategory.개인별세특,
            LogCategory.종합의견
        };
        CBoxCategory.ItemsSource = categories;
        CBoxCategory.SelectedIndex = 0;
    }

    #endregion

    #region Event Handlers - Filters

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxCategory.SelectedItem is LogCategory category)
        {
            _selectedCategory = category;
            SpecListViewer.Category = category;
            UpdateUIByCategory();
        }
    }

    #endregion

    #region Event Handlers - Buttons

    private async void OnQueryClick(object sender, RoutedEventArgs e)
    {
        await LoadSpecsAsync();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var selectedSpecs = SpecListViewer.SelectedSpecs.ToList();

        if (!selectedSpecs.Any())
        {
            await MessageBox.ShowAsync("저장할 항목이 없습니다", "알림");
            return;
        }

        try
        {
            var confirmed = await MessageBox.ShowConfirmAsync(
                $"{selectedSpecs.Count}개 항목을 저장하시겠습니까?",
                "저장 확인", "저장", "취소");

            if (confirmed)
            {
                foreach (var spec in selectedSpecs)
                {
                    if (spec.Special.No > 0)
                    {
                        await _specialService.UpdateAsync(spec.Special);
                    }
                    else
                    {
                        spec.Special.No = await _specialService.CreateAsync(spec.Special);
                    }
                    spec.MarkAsSaved();
                }

                await MessageBox.ShowAsync("저장되었습니다", "완료");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 삭제 — DB에서 실제 삭제
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var selectedSpecs = SpecListViewer.SelectedSpecs.ToList();

        if (!selectedSpecs.Any())
        {
            await MessageBox.ShowAsync("삭제할 항목이 없습니다", "알림");
            return;
        }

        // DB에 저장된 항목만 카운트
        var savedSpecs = selectedSpecs.Where(s => s.Special.No > 0).ToList();
        var unsavedSpecs = selectedSpecs.Where(s => s.Special.No == 0).ToList();

        string msg = savedSpecs.Count > 0
            ? $"{savedSpecs.Count}개 항목을 DB에서 삭제하시겠습니까?\n(복구할 수 없습니다)"
            : $"{unsavedSpecs.Count}개 미저장 항목을 목록에서 제거하시겠습니까?";

        try
        {
            var confirmed = await MessageBox.ShowConfirmAsync(msg, "삭제 확인", "삭제", "취소");

            if (confirmed)
            {
                int deletedCount = 0;
                foreach (var spec in savedSpecs)
                {
                    await _specialService.DeleteAsync(spec.Special.No);
                    deletedCount++;
                }

                // 새로고침
                await LoadSpecsAsync();
                await MessageBox.ShowAsync($"{deletedCount + unsavedSpecs.Count}개 항목이 삭제되었습니다", "완료");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 일괄 입력 — BatchDialog 열기
    /// </summary>
    private void OnBatchInputClick(object sender, RoutedEventArgs e)
    {
        int year = FilterPicker.SelectedYear;
        int grade = FilterPicker.SelectedGrade;
        int classNo = FilterPicker.SelectedClass;

        if (year == 0 || grade == 0 || classNo == 0)
        {
            _ = MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요", "알림");
            return;
        }

        string? defaultType = _selectedCategory != LogCategory.전체
            ? _selectedCategory.ToString()
            : null;

        var dialog = new Dialogs.StudentSpecBatchDialog(year, 0, grade, classNo, defaultType);
        dialog.Activate();
    }

    /// <summary>
    /// 일괄 출력 — 필터 다이얼로그 → PDF/엑셀
    /// </summary>
    private async void OnBatchExportClick(object sender, RoutedEventArgs e)
    {
        int year = FilterPicker.SelectedYear;
        int grade = FilterPicker.SelectedGrade;
        int classNo = FilterPicker.SelectedClass;

        if (year == 0 || grade == 0 || classNo == 0)
        {
            await MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요", "알림");
            return;
        }

        var filterDialog = new Dialogs.SpecExportFilterDialog { XamlRoot = this.XamlRoot };
        var result = await filterDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var filterType = filterDialog.SelectedType;
        var statusFilter = filterDialog.StatusFilter;
        bool excludeEmpty = filterDialog.ExcludeEmpty;
        bool isPdf = filterDialog.IsPdf;

        try
        {
            string schoolCode = Settings.SchoolCode.Value;

            using var enrollmentService = new EnrollmentService();
            var enrollments = await enrollmentService.GetClassRosterAsync(schoolCode, year, grade, classNo);

            if (enrollments.Count == 0)
            {
                await MessageBox.ShowAsync("해당 학급에 학생이 없습니다.", "알림");
                return;
            }

            using var specService = new StudentSpecialService();
            var studentSpecsList = new List<(int Number, string Name, List<StudentSpecial> Specs)>();
            int totalSpecs = 0;

            foreach (var enrollment in enrollments.OrderBy(e2 => e2.Number))
            {
                List<StudentSpecial> specs;

                if (!string.IsNullOrEmpty(filterType))
                {
                    specs = await specService.GetByTypeAsync(enrollment.StudentID, year, filterType);
                }
                else
                {
                    specs = await specService.GetByStudentAsync(enrollment.StudentID, year);
                }

                // 상태 필터
                if (statusFilter == "draft")
                    specs = specs.Where(s => !s.IsFinalized).ToList();
                else if (statusFilter == "finalized")
                    specs = specs.Where(s => s.IsFinalized).ToList();

                // 빈 항목 제외
                if (excludeEmpty)
                    specs = specs.Where(s => !string.IsNullOrWhiteSpace(s.Content)).ToList();

                if (specs.Count == 0) continue;

                studentSpecsList.Add((enrollment.Number, enrollment.Name, specs));
                totalSpecs += specs.Count;
            }

            if (studentSpecsList.Count == 0)
            {
                await MessageBox.ShowAsync("조건에 맞는 기록이 없습니다.", "알림");
                return;
            }

            string filePath;
            if (isPdf)
            {
                var printService = new StudentSpecPrintService();
                filePath = printService.GenerateClassSpecPdf(year, grade, classNo, studentSpecsList);
            }
            else
            {
                var exportService = new StudentSpecExportService();
                filePath = exportService.ExportClassSpecsToExcel(year, grade, classNo, studentSpecsList);
            }

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

            await MessageBox.ShowAsync(
                $"{studentSpecsList.Count}명, 총 {totalSpecs}건의 특기사항을 출력했습니다.\n저장 위치: {filePath}",
                "일괄 출력 완료");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"일괄 출력 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    private void OnFontSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            FlyoutBase.ShowAttachedFlyout(button);
        }
    }

    private void OnFontSizeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SpecListViewer != null)
        {
            SpecListViewer.FontSize = e.NewValue;
            TxtFontSize.Text = $"{e.NewValue:F0}";
        }
    }

    #endregion

    #region Data Loading

    private async Task LoadSpecsAsync()
    {
        try
        {
            int selectedYear = FilterPicker.SelectedYear;
            int selectedGrade = FilterPicker.SelectedGrade;
            int selectedClass = FilterPicker.SelectedClass;

            if (selectedYear == 0 || selectedGrade == 0 || selectedClass == 0)
            {
                await MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요", "알림");
                return;
            }

            var students = await _enrollservice.GetClassRosterAsync(Settings.SchoolCode, selectedYear, selectedGrade, selectedClass);

            if (!students.Any())
            {
                await MessageBox.ShowAsync("학생이 없습니다", "알림");
                SpecListViewer.LoadSpecs(new List<StudentSpecial>());
                return;
            }

            var studentInfoLookup = students.ToDictionary(
                s => s.StudentID,
                s => (Grade: s.Grade, ClassNum: s.Class, Number: s.Number, Name: s.Name)
            );

            var allSpecs = new List<StudentSpecial>();

            foreach (var student in students)
            {
                List<StudentSpecial> specs;

                if (_selectedCategory == LogCategory.전체)
                {
                    specs = await _specialService.GetByStudentAsync(student.StudentID, selectedYear);
                }
                else
                {
                    specs = await _specialService.GetByTypeAsync(
                        student.StudentID,
                        selectedYear,
                        _selectedCategory.ToString()
                    );
                }

                if (specs.Any())
                {
                    allSpecs.AddRange(specs);
                }
                else
                {
                    if (IsAutoCreateCategory(_selectedCategory))
                    {
                        allSpecs.Add(CreateEmptySpec(student.StudentID));
                    }
                }
            }

            SpecListViewer.LoadSpecs(allSpecs, studentInfoLookup);
            SpecListViewer.StudentInfoMode = StudentInfoMode.NumName;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"데이터 로드 중 오류: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Helper Methods

    private void UpdateUIByCategory()
    {
        // SchoolFilterPicker로 변경되어 개별 콤보박스 숨김 불필요
    }

    private bool IsAutoCreateCategory(LogCategory category)
    {
        return category switch
        {
            LogCategory.자율활동 => true,
            LogCategory.진로활동 => true,
            LogCategory.종합의견 => true,
            LogCategory.개인별세특 => true,
            _ => false
        };
    }

    private StudentSpecial CreateEmptySpec(string studentId)
    {
        return new StudentSpecial
        {
            No = 0,
            StudentID = studentId,
            Year = FilterPicker.SelectedYear,
            Type = _selectedCategory.ToString(),
            Title = string.Empty,
            Content = string.Empty,
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            TeacherID = Settings.User.Value,
            CourseNo = 0,
            SubjectName = string.Empty,
            IsFinalized = false,
            Tag = string.Empty
        };
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _specialService?.Dispose();
        _enrollservice?.Dispose();
    }

    #endregion
}
