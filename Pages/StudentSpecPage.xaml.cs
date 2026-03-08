using System;
using System.Collections.Generic;
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

    /// <summary>
    /// 필터 초기화
    /// </summary>
    private void InitializeFilters()
    {
        // 카테고리 초기화
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
        
        // SchoolFilterPicker가 자동으로 초기화함
    }

    #endregion

    #region Event Handlers - Filters

    /// <summary>
    /// 카테고리 변경
    /// </summary>
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

    /// <summary>
    /// 조회 버튼
    /// </summary>
    private async void OnQueryClick(object sender, RoutedEventArgs e)
    {
        await LoadSpecsAsync();
    }

    /// <summary>
    /// 저장 버튼
    /// </summary>
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
    /// 삭제 버튼
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var selectedSpecs = SpecListViewer.SelectedSpecs.ToList();
        
        if (!selectedSpecs.Any())
        {
            await MessageBox.ShowAsync("삭제할 항목이 없습니다", "알림");
            return;
        }

        try
        {
            var confirmed = await MessageBox.ShowConfirmAsync(
                $"{selectedSpecs.Count}개 항목을 삭제하시겠습니까?\n(내용이 초기화됩니다)",
                "삭제 확인", "삭제", "취소");

            if (confirmed)
            {
                foreach (var spec in selectedSpecs)
                {
                    if (spec.Special.No > 0)
                    {
                        // 내용 초기화
                        spec.Special.Content = string.Empty;
                        await _specialService.UpdateAsync(spec.Special);
                        spec.IsSelected = false;
                    }
                }

                // 새로고침
                await LoadSpecsAsync();
                await MessageBox.ShowAsync("삭제되었습니다", "완료");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 인쇄 버튼
    /// </summary>
    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var selectedSpecs = SpecListViewer.SelectedSpecs.ToList();
        
        if (!selectedSpecs.Any())
        {
            await MessageBox.ShowAsync("인쇄할 항목이 없습니다", "알림");
            return;
        }

        // TODO: 인쇄 기능 구현
        await MessageBox.ShowAsync("인쇄 기능은 개발 중입니다", "알림");
    }

    /// <summary>
    /// 폰트 크기 버튼
    /// </summary>
    private void OnFontSizeClick(object sender, RoutedEventArgs e)
    {
        // AttachedFlyout 열기
        if (sender is Button button)
        {
            FlyoutBase.ShowAttachedFlyout(button);
        }
    }

    /// <summary>
    /// 폰트 크기 변경
    /// </summary>
    private void OnFontSizeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (SpecListViewer != null)
        {
            SpecListViewer.FontSize = e.NewValue;
            TxtFontSize.Text = $"{e.NewValue:F0}";
        }
    }

    #endregion

    #region Data Loading

    // LoadClassesAsync 제거 - SchoolFilterPicker가 자동으로 처리

    /// <summary>
    /// 학생부 기록 로드
    /// </summary>
    private async Task LoadSpecsAsync()
    {
        try
        {
            // FilterPicker에서 값 가져오기
            int selectedYear = FilterPicker.SelectedYear;
            int selectedGrade = FilterPicker.SelectedGrade;
            int selectedClass = FilterPicker.SelectedClass;
            
            // 필터 검증
            if (selectedYear == 0 || selectedGrade == 0 || selectedClass == 0)
            {
                await MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요", "알림");
                return;
            }

            // 학생 목록 가져오기
            var students = await _enrollservice.GetClassRosterAsync(Settings.SchoolCode, selectedYear, selectedGrade, selectedClass);

            if (!students.Any())
            {
                await MessageBox.ShowAsync("학생이 없습니다", "알림");
                SpecListViewer.LoadSpecs(new List<StudentSpecial>());
                return;
            }

            // 학생 정보 Dictionary 생성 (StudentID -> 학생 정보 매핑)
            var studentInfoLookup = students.ToDictionary(
                s => s.StudentID,
                s => (Grade: s.Grade, ClassNum: s.Class, Number: s.Number, Name: s.Name)
            );

            // 각 학생의 특기사항 가져오기
            var allSpecs = new List<StudentSpecial>();

            foreach (var student in students)
            {
                List<StudentSpecial> specs;

                if (_selectedCategory == LogCategory.전체)
                {
                    // 전체 조회
                    specs = await _specialService.GetByStudentAsync(student.StudentID, selectedYear);
                }
                else
                {
                    // 카테고리별 조회
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
                    // 데이터가 없으면 빈 항목 추가 (자율/진로/종합의견 등)
                    if (IsAutoCreateCategory(_selectedCategory))
                    {
                        allSpecs.Add(CreateEmptySpec(student.StudentID));
                    }
                }
            }

            // SpecListViewer에 로드 (학생 정보 포함)
            SpecListViewer.LoadSpecs(allSpecs, studentInfoLookup);

            // 학생 정보 표시 모드 설정
            SpecListViewer.StudentInfoMode = StudentInfoMode.NumName;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"데이터 로드 중 오류: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 카테고리에 따른 UI 조정
    /// </summary>
    private void UpdateUIByCategory()
    {
        // SchoolFilterPicker로 변경되어 개별 콤보박스 숨김 불필요
        // 교과/동아리 카테고리는 조회 시 별도 로직으로 처리
    }

    /// <summary>
    /// 자동 생성 카테고리 여부
    /// </summary>
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

    /// <summary>
    /// 빈 StudentSpecial 생성
    /// </summary>
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

    /// <summary>
    /// 메시지 표시
    /// </summary>

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _specialService?.Dispose();
        _enrollservice?.Dispose();
    }

    #endregion
}
