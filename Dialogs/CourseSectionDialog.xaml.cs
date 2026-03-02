using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NewSchool.Dialogs;

/// <summary>
/// 소단원 편집 다이얼로그 (단순화 버전)
/// 단원 1개만 추가/편집
/// </summary>
public sealed partial class CourseSectionDialog : ContentDialog
{
    private readonly Course _course;
    private readonly CourseSection? _existingSection;
    private readonly bool _isEditMode;

    /// <summary>
    /// 생성자
    /// </summary>
    /// <param name="course">수업 정보</param>
    /// <param name="section">편집할 단원 (null이면 새 단원 추가)</param>
    public CourseSectionDialog(Course course, CourseSection? section = null)
    {
        InitializeComponent();

        _course = course;
        _existingSection = section;
        _isEditMode = section != null;

        LoadCourseInfo();
        SetupEventHandlers();

        if (_isEditMode && _existingSection != null)
        {
            Dialog.Title = "소단원 편집";
            LoadSectionData(_existingSection);
        }
        else
        {
            Dialog.Title = "소단원 추가";
            // 기본값 설정
            NumUnitNo.Value = 1;
            NumChapterNo.Value = 1;
            NumSectionNo.Value = 1;
            NumEstimatedHours.Value = 1;
        }
    }

    #region Initialization

    /// <summary>
    /// 과목 정보 표시
    /// </summary>
    private void LoadCourseInfo()
    {
        TxtCourseName.Text = _course.Subject;
        TxtCourseInfo.Text = $"{_course.Grade}학년 · {_course.Type}";
    }

    /// <summary>
    /// 이벤트 핸들러 설정
    /// </summary>
    private void SetupEventHandlers()
    {
        CmbSectionType.SelectionChanged += OnSectionTypeChanged;
    }

    /// <summary>
    /// 유형 선택 변경 시 고정 날짜 패널 표시/숨김
    /// </summary>
    private void OnSectionTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbSectionType == null || PinnedDatePanel == null) return;

        if (CmbSectionType.SelectedItem is ComboBoxItem item && item.Tag is string type)
        {
            PinnedDatePanel.Visibility = (type == "Exam" || type == "Assessment")
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    #endregion

    #region Data Loading/Saving

    /// <summary>
    /// 섹션 데이터를 폼에 로드 (편집 모드)
    /// </summary>
    private void LoadSectionData(CourseSection section)
    {
        NumUnitNo.Value = section.UnitNo;
        TxtUnitName.Text = section.UnitName;
        NumChapterNo.Value = section.ChapterNo;
        TxtChapterName.Text = section.ChapterName;
        NumSectionNo.Value = section.SectionNo;
        TxtSectionName.Text = section.SectionName;
        NumStartPage.Value = section.StartPage;
        NumEndPage.Value = section.EndPage;
        NumEstimatedHours.Value = section.EstimatedHours;

        // 유형 선택
        CmbSectionType.SelectedIndex = section.SectionType switch
        {
            "Exam" => 1,
            "Assessment" => 2,
            "Event" => 3,
            _ => 0
        };

        // 고정 날짜
        if (section.PinnedDate.HasValue)
        {
            DpPinnedDate.Date = new DateTimeOffset(section.PinnedDate.Value);
        }

        TxtLearningObjective.Text = section.LearningObjective;
        TxtLessonPlan.Text = section.LessonPlan;
        TxtMaterialPath.Text = section.MaterialPath;
        TxtMaterialUrl.Text = section.MaterialUrl;
        TxtMemo.Text = section.Memo;
    }

    /// <summary>
    /// 폼 데이터를 섹션 객체로 변환
    /// </summary>
    private CourseSection CreateSectionFromForm()
    {
        var section = _isEditMode && _existingSection != null
            ? _existingSection
            : new CourseSection { Course = _course.No };

        section.UnitNo = (int)NumUnitNo.Value;
        section.UnitName = TxtUnitName.Text?.Trim() ?? string.Empty;
        section.ChapterNo = (int)NumChapterNo.Value;
        section.ChapterName = TxtChapterName.Text?.Trim() ?? string.Empty;
        section.SectionNo = (int)NumSectionNo.Value;
        section.SectionName = TxtSectionName.Text?.Trim() ?? string.Empty;
        section.StartPage = (int)NumStartPage.Value;
        section.EndPage = (int)NumEndPage.Value;
        section.EstimatedHours = (int)NumEstimatedHours.Value;

        // 유형
        var sectionType = "Normal";
        if (CmbSectionType.SelectedItem is ComboBoxItem item && item.Tag is string type)
        {
            sectionType = type;
        }
        section.SectionType = sectionType;

        // 평가 유형이면 고정 설정
        if (sectionType == "Exam" || sectionType == "Assessment")
        {
            section.IsPinned = true;
            section.PinnedDate = DpPinnedDate.Date?.DateTime;
        }
        else
        {
            section.IsPinned = false;
            section.PinnedDate = null;
        }

        section.LearningObjective = TxtLearningObjective.Text?.Trim() ?? string.Empty;
        section.LessonPlan = TxtLessonPlan.Text?.Trim() ?? string.Empty;
        section.MaterialPath = TxtMaterialPath.Text?.Trim() ?? string.Empty;
        section.MaterialUrl = TxtMaterialUrl.Text?.Trim() ?? string.Empty;
        section.Memo = TxtMemo.Text?.Trim() ?? string.Empty;

        return section;
    }

    #endregion

    #region Validation

    /// <summary>
    /// 입력 유효성 검사
    /// </summary>
    private bool ValidateInput()
    {
        // 소단원명 필수
        if (string.IsNullOrWhiteSpace(TxtSectionName.Text))
        {
            ShowError("소단원명을 입력해주세요.");
            TxtSectionName.Focus(FocusState.Programmatic);
            return false;
        }

        // 페이지 범위 검증
        int startPage = (int)NumStartPage.Value;
        int endPage = (int)NumEndPage.Value;
        if (startPage > 0 && endPage > 0 && startPage > endPage)
        {
            ShowError("시작 페이지가 끝 페이지보다 클 수 없습니다.");
            NumStartPage.Focus(FocusState.Programmatic);
            return false;
        }

        // 평가 단원 날짜 검증
        if (CmbSectionType.SelectedItem is ComboBoxItem item && item.Tag is string type)
        {
            if ((type == "Exam" || type == "Assessment") && DpPinnedDate.Date == null)
            {
                ShowError("평가 단원은 고정 날짜를 설정해주세요.");
                DpPinnedDate.Focus(FocusState.Programmatic);
                return false;
            }
        }

        return true;
    }

    #endregion

    #region File Browsing

    /// <summary>
    /// 자료 파일 찾아보기
    /// </summary>
    private async void OnBrowseMaterialClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".ppt");
            picker.FileTypeFilter.Add(".pptx");
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".doc");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".hwp");
            picker.FileTypeFilter.Add(".hwpx");
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                TxtMaterialPath.Text = file.Path;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionDialog] 파일 선택 실패: {ex.Message}");
        }
    }

    #endregion

    #region Save

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            // 유효성 검사
            if (!ValidateInput())
            {
                args.Cancel = true;
                return;
            }

            // 섹션 객체 생성
            var section = CreateSectionFromForm();

            // DB 저장
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);

            if (_isEditMode)
            {
                // 편집 모드: 업데이트
                await repo.UpdateAsync(section);
                Debug.WriteLine($"[CourseSectionDialog] 단원 수정: {section.FullPath}");
            }
            else
            {
                // 추가 모드: SortOrder를 마지막+1로 설정하여 맨 뒤에 배치
                var existing = await repo.GetByCourseAsync(_course.No);
                section.SortOrder = existing.Count > 0
                    ? existing.Max(s => s.SortOrder) + 1
                    : 1;

                await repo.CreateAsync(section);
                Debug.WriteLine($"[CourseSectionDialog] 단원 추가: {section.FullPath}, SortOrder={section.SortOrder}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseSectionDialog] 저장 실패: {ex.Message}");
            ShowError($"저장 중 오류가 발생했습니다.\n{ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    #endregion

    #region UI Helpers

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    #endregion
}
