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

        return true;
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

            // 반영 여부를 확인한다. 예전에는 결과를 버려서, 갱신된 행이 없어도
            // (예: 다른 화면에서 이미 지운 단원) 창이 닫히며 저장된 것처럼 보였다.
            // 형제인 CourseEditDialog 는 원래부터 확인하고 있었다.
            if (_isEditMode)
            {
                // 편집 모드: 업데이트
                if (!await repo.UpdateAsync(section))
                {
                    ShowError("단원 수정에 실패했습니다. 이미 지워진 단원일 수 있습니다.");
                    args.Cancel = true;
                    return;
                }
                Debug.WriteLine($"[CourseSectionDialog] 단원 수정: {section.FullPath}");
            }
            else
            {
                // 추가 모드: SortOrder를 마지막+1로 설정하여 맨 뒤에 배치
                var existing = await repo.GetByCourseAsync(_course.No);
                section.SortOrder = existing.Count > 0
                    ? existing.Max(s => s.SortOrder) + 1
                    : 1;

                if (await repo.CreateAsync(section) <= 0)
                {
                    ShowError("단원 추가에 실패했습니다.");
                    args.Cancel = true;
                    return;
                }
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
