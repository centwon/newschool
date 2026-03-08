using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Dialogs;

public sealed partial class RosterTableDialog : ContentDialog
{
    /// <summary>생성된 HTML 테이블 (삽입 후 사용)</summary>
    public string GeneratedHtml { get; private set; } = string.Empty;

    /// <summary>생성된 표 제목 (게시글 제목 자동 채움용)</summary>
    public string TableTitle { get; private set; } = string.Empty;

    private List<Course> _courses = new();
    private List<Club> _clubs = new();

    public RosterTableDialog()
    {
        this.InitializeComponent();
        GradeBox.Value = Settings.HomeGrade.Value;
        ClassBox.Value = Settings.HomeRoom.Value;
    }

    /// <summary>
    /// 초기 스코프를 설정 (학급/수업/동아리)
    /// </summary>
    public void SetScope(string scopeType)
    {
        foreach (ComboBoxItem item in ScopeComboBox.Items)
        {
            if (item.Tag?.ToString() == scopeType)
            {
                ScopeComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private async void ScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScopeComboBox.SelectedItem is not ComboBoxItem selected) return;
        // InitializeComponent 중에 호출될 수 있으므로 null 체크
        if (ClassPanel == null || CoursePanel == null || ClubPanel == null) return;

        var scope = selected.Tag?.ToString() ?? "Class";

        ClassPanel.Visibility = scope == "Class" ? Visibility.Visible : Visibility.Collapsed;
        CoursePanel.Visibility = scope == "Course" ? Visibility.Visible : Visibility.Collapsed;
        ClubPanel.Visibility = scope == "Club" ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            if (scope == "Course" && _courses.Count == 0)
            {
                using var service = new CourseService();
                var courses = await service.GetMyCoursesAsync();
                _courses = courses.ToList();
                CourseComboBox.ItemsSource = _courses;
                CourseComboBox.DisplayMemberPath = "DisplayName";
            }
            else if (scope == "Club" && _clubs.Count == 0)
            {
                using var service = new ClubService();
                _clubs = await service.GetAllClubsAsync(
                    Settings.SchoolCode, Settings.WorkYear.Value);
                ClubComboBox.ItemsSource = _clubs;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RosterTableDialog] 데이터 로드 실패: {ex.Message}");
        }
    }

    private async void CourseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoomComboBox == null) return;
        if (CourseComboBox.SelectedItem is not Course course)
        {
            RoomComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            using var courseService = new CourseService();
            var enrollments = await courseService.GetCourseEnrollmentsAsync(
                Settings.SchoolCode, Settings.WorkYear.Value, Settings.WorkSemester.Value, course.No);

            var rooms = enrollments
                .Select(ce => ce.Room)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct()
                .OrderBy(r => r)
                .ToList();

            if (rooms.Count > 1)
            {
                RoomComboBox.ItemsSource = rooms;
                RoomComboBox.SelectedIndex = 0;
                RoomComboBox.Visibility = Visibility.Visible;
            }
            else
            {
                RoomComboBox.ItemsSource = null;
                RoomComboBox.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RosterTableDialog] 강의실 로드 실패: {ex.Message}");
            RoomComboBox.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 유효성 검사 - deferral로 비동기 처리
        var deferral = args.GetDeferral();
        try
        {
            if (string.IsNullOrWhiteSpace(ColumnsBox.Text))
            {
                ShowError("컬럼을 입력하세요.");
                args.Cancel = true;
                return;
            }

            var html = await GenerateTableAsync();
            if (string.IsNullOrEmpty(html))
            {
                args.Cancel = true;
                return;
            }

            GeneratedHtml = html;
            TableTitle = TableTitleBox.Text.Trim();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<string> GenerateTableAsync()
    {
        var scope = (ScopeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Class";
        var columns = ColumnsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        if (columns.Count == 0)
        {
            ShowError("컬럼을 하나 이상 입력하세요.");
            return string.Empty;
        }

        // 학생 목록 가져오기 (번호, 이름)
        List<(int Number, string Name)> students;
        string scopeLabel;

        try
        {
            switch (scope)
            {
                case "Class":
                    var grade = (int)GradeBox.Value;
                    var classNum = (int)ClassBox.Value;
                    students = await LoadClassStudentsAsync(grade, classNum);
                    scopeLabel = $"{Settings.WorkYear.Value}학년도 {grade}학년 {classNum}반";
                    break;

                case "Course":
                    if (CourseComboBox.SelectedItem is not Course course)
                    {
                        ShowError("수업을 선택하세요.");
                        return string.Empty;
                    }
                    students = await LoadCourseStudentsAsync(course.No);
                    scopeLabel = $"{Settings.WorkYear.Value}학년도 {course.DisplayName}";
                    break;

                case "Club":
                    if (ClubComboBox.SelectedItem is not Club club)
                    {
                        ShowError("동아리를 선택하세요.");
                        return string.Empty;
                    }
                    students = await LoadClubStudentsAsync(club.No);
                    scopeLabel = $"{Settings.WorkYear.Value}학년도 {club.ClubName}";
                    break;

                default:
                    return string.Empty;
            }
        }
        catch (Exception ex)
        {
            ShowError($"학생 목록을 가져올 수 없습니다: {ex.Message}");
            return string.Empty;
        }

        if (students.Count == 0)
        {
            ShowError("학생이 없습니다.");
            return string.Empty;
        }

        // HTML 테이블 생성
        return BuildHtmlTable(TableTitleBox.Text.Trim(), scopeLabel, columns, students);
    }

    private async Task<List<(int Number, string Name)>> LoadClassStudentsAsync(int grade, int classNum)
    {
        using var service = new EnrollmentService();
        var enrollments = await service.GetClassRosterAsync(
            Settings.SchoolCode, Settings.WorkYear.Value, grade, classNum);
        return enrollments
            .OrderBy(e => e.Number)
            .Select(e => (e.Number, e.Name))
            .ToList();
    }

    private async Task<List<(int Number, string Name)>> LoadCourseStudentsAsync(int courseNo)
    {
        using var courseService = new CourseService();
        var enrollments = await courseService.GetCourseEnrollmentsAsync(
            Settings.SchoolCode, Settings.WorkYear.Value, Settings.WorkSemester.Value, courseNo);

        // 강의실 필터링
        var selectedRoom = RoomComboBox.Visibility == Visibility.Visible
            ? RoomComboBox.SelectedItem as string
            : null;
        if (!string.IsNullOrEmpty(selectedRoom))
        {
            enrollments = enrollments.Where(e => e.Room == selectedRoom).ToList();
        }

        // CourseEnrollment에는 Name이 없으므로 Enrollment에서 조회
        var studentIds = enrollments.Select(e => e.StudentID).ToList();
        using var enrollService = new EnrollmentService();
        var allEnrollments = await enrollService.GetEnrollmentsAsync(
            Settings.SchoolCode, Settings.WorkYear.Value);

        return allEnrollments
            .Where(e => studentIds.Contains(e.StudentID))
            .OrderBy(e => e.Grade).ThenBy(e => e.Class).ThenBy(e => e.Number)
            .Select(e => (e.Number, e.Name))
            .ToList();
    }

    private async Task<List<(int Number, string Name)>> LoadClubStudentsAsync(int clubNo)
    {
        using var clubRepo = new Repositories.ClubEnrollmentRepository(SchoolDatabase.DbPath);
        var enrollments = await clubRepo.GetByClubAsync(clubNo);

        var studentIds = enrollments.Select(e => e.StudentID).ToList();
        using var enrollService = new EnrollmentService();
        var allEnrollments = await enrollService.GetEnrollmentsAsync(
            Settings.SchoolCode, Settings.WorkYear.Value);

        return allEnrollments
            .Where(e => studentIds.Contains(e.StudentID))
            .OrderBy(e => e.Grade).ThenBy(e => e.Class).ThenBy(e => e.Number)
            .Select(e => (e.Number, e.Name))
            .ToList();
    }

    private static string BuildHtmlTable(
        string title, string scopeLabel,
        List<string> columns, List<(int Number, string Name)> students)
    {
        int totalCols = 2 + columns.Count; // 번호 + 이름 + 사용자 컬럼

        var sb = new StringBuilder();
        sb.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse; width:100%; text-align:center;\">");

        // 표 제목 행
        if (!string.IsNullOrEmpty(title))
        {
            sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:16px; padding:8px; background:#e8f0fe;\">{Escape(title)}</th></tr>");
        }

        // 범위 정보 행
        sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:13px; padding:6px; background:#f8f9fa; text-align:right;\">{Escape(scopeLabel)}</th></tr>");

        // 헤더 행
        sb.Append("<tr style=\"background:#d0e0f0;\">");
        sb.Append("<th style=\"width:50px;\">번호</th>");
        sb.Append("<th style=\"width:80px;\">이름</th>");
        foreach (var col in columns)
        {
            sb.Append($"<th>{Escape(col)}</th>");
        }
        sb.Append("</tr>");

        // 학생 행
        foreach (var (number, name) in students)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{number}</td>");
            sb.Append($"<td>{Escape(name)}</td>");
            for (int i = 0; i < columns.Count; i++)
            {
                sb.Append("<td></td>");
            }
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    private static string Escape(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private void ShowError(string message)
    {
        InfoMessage.Message = message;
        InfoMessage.Severity = InfoBarSeverity.Error;
        InfoMessage.IsOpen = true;
    }
}
