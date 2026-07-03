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
/// CourseSpecPage - 교과 관련 학생부(세특) 특기사항 관리 페이지
/// 과목·강의실 단위로 수강생 전체의 교과세특을 한 화면에서 조회/작성한다.
/// </summary>
public sealed partial class CourseSpecPage : Page, IDisposable
{
    private const string SpecType = "교과활동";

    private bool _disposed;
    private Course? _selectedCourse;
    private int _selectedYear;
    private IReadOnlyList<Enrollment> _currentStudents = Array.Empty<Enrollment>();

    private readonly StudentSpecialService _specialService = new();

    public CourseSpecPage()
    {
        this.InitializeComponent();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _specialService?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Event Handlers - Filters

    /// <summary>
    /// 과목/강의실 선택 확정 — CoursePicker 가 수강생 목록까지 함께 전달
    /// </summary>
    private async void CoursePickerCtl_CourseChanged(object? sender, CourseChangedEventArgs e)
    {
        _selectedCourse = e.Course;
        _selectedYear = e.Year;
        _currentStudents = e.Students
            .OrderBy(s => s.Grade)
            .ThenBy(s => s.Class)
            .ThenBy(s => s.Number)
            .ToList();

        await LoadSpecsAsync();
    }

    #endregion

    #region Event Handlers - Buttons

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
                // 신규(No==0) + 내용 없음은 빈 행을 만들지 않도록 저장 대상에서 제외
                var toSave = selectedSpecs
                    .Where(s => s.Special.No > 0 || !string.IsNullOrWhiteSpace(s.Special.Content))
                    .ToList();

                await _specialService.SaveManyAsync(toSave.Select(s => s.Special));
                foreach (var spec in toSave)
                {
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

                await LoadSpecsAsync();
                await MessageBox.ShowAsync($"{deletedCount + unsavedSpecs.Count}개 항목이 삭제되었습니다", "완료");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다: {ex.Message}", "오류");
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

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await CoursePickerCtl.LoadAsync(Settings.WorkYear.Value, Settings.WorkSemester.Value);
    }

    #endregion

    #region Data Loading

    private async Task LoadSpecsAsync()
    {
        try
        {
            if (_selectedCourse == null || _currentStudents.Count == 0)
            {
                SpecListViewer.LoadSpecs(new List<StudentSpecial>());
                TxtStudentCount.Text = "0명";
                return;
            }

            var course = _selectedCourse;
            int year = _selectedYear;

            var studentInfoLookup = _currentStudents.ToDictionary(
                s => s.StudentID,
                s => (Grade: s.Grade, ClassNum: s.Class, Number: s.Number, Name: s.Name)
            );

            var existingSpecs = await _specialService.GetByCourseAsync(course.No, year);

            var allSpecs = new List<StudentSpecial>();
            foreach (var student in _currentStudents)
            {
                var spec = existingSpecs.FirstOrDefault(s => s.StudentID == student.StudentID);
                allSpecs.Add(spec ?? CreateEmptySpec(student.StudentID, course, year));
            }

            SpecListViewer.Category = LogCategory.교과활동;
            SpecListViewer.LoadSpecs(allSpecs, studentInfoLookup);
            SpecListViewer.StudentInfoMode = StudentInfoMode.GradeClassNumName;

            TxtStudentCount.Text = $"{_currentStudents.Count}명";
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"데이터 로드 중 오류: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Helper Methods

    private static StudentSpecial CreateEmptySpec(string studentId, Course course, int year)
    {
        return new StudentSpecial
        {
            No = 0,
            StudentID = studentId,
            Year = year,
            Type = SpecType,
            Title = course.Subject,
            Content = string.Empty,
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            TeacherID = Settings.User.Value,
            CourseNo = course.No,
            SubjectName = course.Subject,
            IsFinalized = false,
            Tag = string.Empty
        };
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    #endregion
}
