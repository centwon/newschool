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
public sealed partial class CourseSpecPage : Page, IDisposable, IUnsavedWork
{
    // 학생부 기록 화면(StudentSpecPage)과 같은 이유 — 고친 세특을 두고 떠나면 묻지 않고 버렸다.
    public bool HasUnsavedWork => SpecListViewer.ModifiedSpecs.Count > 0;

    public string UnsavedWorkMessage =>
        $"저장하지 않은 교과 세특 {SpecListViewer.ModifiedSpecs.Count}건이 사라집니다.";

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
        // 수업을 바꾸면 목록을 다시 읽는다 — 그 전에 고친 채 저장하지 않은 세특을 묻는다(예전에는
        // 말없이 버렸다). 수업 선택은 이미 바뀐 뒤라 [취소] 로 멈추지 않고 저장할지 버릴지만 고른다.
        // 저장 대상은 앞 수업의 행 그 자체라 제 수업에 들어간다.
        var modified = SpecListViewer.ModifiedSpecs;
        if (modified.Count > 0 &&
            await MessageBox.ShowConfirmAsync(
                $"고친 뒤 저장하지 않은 세특이 {modified.Count}건 있습니다.\n저장하고 수업을 바꿀까요?",
                "저장하지 않은 변경", "저장하고 바꾸기", "버리기"))
        {
            try
            {
                // 학적 확인에서 취소하면 저장하지 않는다 — 수업은 이미 바뀌어 아래에서 목록을
                // 다시 읽으므로 고친 것은 버려진다. 말없이 사라지지 않게 알린다.
                if (!await SaveSpecsAsync(modified))
                    await MessageBox.ShowAsync(
                        $"고친 세특 {modified.Count}건을 저장하지 않았습니다. 고친 내용은 버렸습니다.", "저장하지 않음");
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync(
                    $"저장 중 오류가 발생했습니다: {ex.Message}\n고친 내용은 저장되지 않았습니다.", "오류");
            }
        }

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

    /// <summary>
    /// 세특을 저장한다 — [저장] 과 수업을 바꿀 때의 "저장하고 바꾸기" 가 같이 쓴다.
    /// 예외는 부르는 쪽이 처리한다(한 트랜잭션이라 실패하면 한 건도 들어가지 않는다).
    /// </summary>
    /// <returns>저장했으면 true, 학적 확인에서 취소했으면 false.</returns>
    private async Task<bool> SaveSpecsAsync(List<StudentSpecialViewModel> specs)
    {
        // 신규(No==0) + 내용 없음은 빈 행을 만들지 않도록 저장 대상에서 제외
        var toSave = specs
            .Where(s => s.Special.No > 0 || !string.IsNullOrWhiteSpace(s.Special.Content))
            .ToList();

        // 학생부를 저장하는 네 길이 같은 학적 확인을 받는다 — 예전에는 학생부 화면만 했고,
        // 같은 표를 저장하는 이 화면은 빠져 있었다.
        if (!await EnrollmentGuard.ConfirmSpecsAfterLeavingAsync(toSave.Select(s => s.Special)))
            return false;

        await _specialService.SaveManyAsync(toSave.Select(s => s.Special));
        foreach (var spec in toSave)
            spec.MarkAsSaved();
        return true;
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

            if (confirmed && await SaveSpecsAsync(selectedSpecs))
                await MessageBox.ShowAsync("저장되었습니다", "완료");
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
        // 체크한 행만 지운다 — 학생부 기록 화면과 같은 이유(SpecListViewer.CheckedSpecs 주석).
        var selectedSpecs = SpecListViewer.CheckedSpecs.ToList();

        if (!selectedSpecs.Any())
        {
            await MessageBox.ShowAsync("삭제할 항목이 없습니다", "알림");
            return;
        }

        // 지운 뒤 목록을 다시 읽는다 — 체크하지 않은 채 고쳐 둔 행은 거기서 사라지므로 먼저 묻는다.
        if (!await SpecListViewer.ConfirmDiscardModifiedAsync(except: selectedSpecs)) return;

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
                    // 실제로 지워진 것만 센다 — 예전에는 결과를 버리고 무조건 세었다.
                    if (await _specialService.DeleteAsync(spec.Special.No))
                        deletedCount++;
                }

                await LoadSpecsAsync();

                if (deletedCount == savedSpecs.Count)
                    await MessageBox.ShowAsync($"{deletedCount + unsavedSpecs.Count}개 항목이 삭제되었습니다", "완료");
                else
                    await MessageBox.ShowAsync(
                        $"{savedSpecs.Count}개 중 {deletedCount}개만 삭제됐습니다.", "일부 삭제 실패");
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
            SpecListViewer.ContentFontSize = e.NewValue;
            TxtFontSize.Text = $"{e.NewValue:F0}";
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await CoursePickerCtl.LoadAsync(CoursePickerCtl.SelectedYear, CoursePickerCtl.SelectedSemester);
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
            // 교과 세특은 학기별 — 교과목의 학기를 그대로 저장(CourseNo 가 지워져도 학기가 남는다)
            Semester = Helpers.NeisHelper.IsSemesterScoped(SpecType) ? course.Semester : 0,
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
