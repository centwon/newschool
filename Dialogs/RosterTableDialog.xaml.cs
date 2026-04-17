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

    private bool _isInitialized = false;

    public RosterTableDialog()
    {
        this.InitializeComponent();
        GradeBox.Value = Settings.HomeGrade.Value;
        _isInitialized = true;
        _ = LoadClassListAsync((int)GradeBox.Value).ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[RosterTableDialog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
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

    private async void GradeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized || double.IsNaN(args.NewValue)) return;
        await LoadClassListAsync((int)args.NewValue);
    }

    private async Task LoadClassListAsync(int grade)
    {
        try
        {
            using var service = new EnrollmentService();
            var enrollments = await service.GetEnrollmentsAsync(
                Settings.SchoolCode, Settings.WorkYear.Value);

            var classes = enrollments
                .Where(e => e.Grade == grade)
                .Select(e => e.Class)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            var items = new List<string> { "전체" };
            items.AddRange(classes.Select(c => $"{c}반"));
            ClassComboBox.ItemsSource = items;

            // 담임반 기본 선택
            var homeRoom = Settings.HomeRoom.Value;
            var homeIndex = classes.IndexOf(homeRoom);
            ClassComboBox.SelectedIndex = homeIndex >= 0 ? homeIndex + 1 : (items.Count > 1 ? 1 : 0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RosterTableDialog] 반 목록 로드 실패: {ex.Message}");
            ClassComboBox.ItemsSource = new List<string> { "전체" };
            ClassComboBox.SelectedIndex = 0;
        }
    }

    private void ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClassLayoutPanel == null) return;
        var selected = ClassComboBox.SelectedItem as string;
        ClassLayoutPanel.Visibility = selected == "전체"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void CourseComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoomComboBox == null) return;
        if (CourseComboBox.SelectedItem is not Course course)
        {
            RoomComboBox.Visibility = Visibility.Collapsed;
            CourseLayoutPanel.Visibility = Visibility.Collapsed;
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
                var items = new List<string> { "전체" };
                items.AddRange(rooms);
                RoomComboBox.ItemsSource = items;
                RoomComboBox.SelectedIndex = 0;
                RoomComboBox.Visibility = Visibility.Visible;
            }
            else
            {
                RoomComboBox.ItemsSource = null;
                RoomComboBox.Visibility = Visibility.Collapsed;
                CourseLayoutPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RosterTableDialog] 강의실 로드 실패: {ex.Message}");
            RoomComboBox.Visibility = Visibility.Collapsed;
            CourseLayoutPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void RoomComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CourseLayoutPanel == null) return;
        var selected = RoomComboBox.SelectedItem as string;
        CourseLayoutPanel.Visibility = selected == "전체"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
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

    #region 테이블 생성

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

        try
        {
            // 학급 전체 반 → 그룹 테이블
            if (scope == "Class" && ClassComboBox.SelectedItem as string == "전체")
                return await GenerateClassGroupedTableAsync(columns);

            // 수업 전체(강의실별 그룹) → 그룹 테이블
            if (scope == "Course" && RoomComboBox.SelectedItem as string == "전체"
                && CourseLayoutPanel.Visibility == Visibility.Visible)
                return await GenerateCourseGroupedTableAsync(columns);

            // 단일 학생 목록 기반 (학급/수업/동아리)
            List<(int Number, string Name)> students;
            string scopeLabel;

            switch (scope)
            {
                case "Class":
                    var grade = (int)GradeBox.Value;
                    var classStr = ClassComboBox.SelectedItem as string;
                    if (string.IsNullOrEmpty(classStr))
                    {
                        ShowError("반을 선택하세요.");
                        return string.Empty;
                    }
                    var classNum = int.Parse(classStr.Replace("반", ""));
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

            if (students.Count == 0)
            {
                ShowError("학생이 없습니다.");
                return string.Empty;
            }

            return BuildSingleTable(TableTitleBox.Text.Trim(), scopeLabel, columns, students);
        }
        catch (Exception ex)
        {
            ShowError($"학생 목록을 가져올 수 없습니다: {ex.Message}");
            return string.Empty;
        }
    }

    private async Task<string> GenerateClassGroupedTableAsync(List<string> columns)
    {
        var grade = (int)GradeBox.Value;
        var classMap = await LoadGradeStudentsAsync(grade);

        if (classMap.Count == 0)
        {
            ShowError($"{grade}학년에 학생이 없습니다.");
            return string.Empty;
        }

        var title = TableTitleBox.Text.Trim();
        var scopeLabel = $"{Settings.WorkYear.Value}학년도 {grade}학년 전체";

        var layoutItem = ClassLayoutRadio.SelectedItem as RadioButton;
        var layout = layoutItem?.Tag?.ToString() ?? "Vertical";

        var groupMap = classMap.ToDictionary(
            kv => $"{grade}학년 {kv.Key}반",
            kv => kv.Value);

        return layout == "Horizontal"
            ? BuildGroupedHorizontalTable(title, scopeLabel, columns, groupMap)
            : BuildGroupedVerticalTable(title, scopeLabel, columns, groupMap);
    }

    private async Task<string> GenerateCourseGroupedTableAsync(List<string> columns)
    {
        if (CourseComboBox.SelectedItem is not Course course)
        {
            ShowError("수업을 선택하세요.");
            return string.Empty;
        }

        var roomMap = await LoadCourseStudentsByRoomAsync(course.No);

        if (roomMap.Count == 0)
        {
            ShowError("수강생이 없습니다.");
            return string.Empty;
        }

        var title = TableTitleBox.Text.Trim();
        var scopeLabel = $"{Settings.WorkYear.Value}학년도 {course.DisplayName} 전체";

        var layoutItem = CourseLayoutRadio.SelectedItem as RadioButton;
        var layout = layoutItem?.Tag?.ToString() ?? "Vertical";

        return layout == "Horizontal"
            ? BuildGroupedHorizontalTable(title, scopeLabel, columns, roomMap)
            : BuildGroupedVerticalTable(title, scopeLabel, columns, roomMap);
    }

    #endregion

    #region 학생 데이터 로드

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

    /// <summary>
    /// 학년 내 모든 반 학생을 반별로 그룹화하여 반환
    /// </summary>
    private async Task<SortedDictionary<int, List<(int Number, string Name)>>> LoadGradeStudentsAsync(int grade)
    {
        using var service = new EnrollmentService();
        var enrollments = await service.GetEnrollmentsAsync(
            Settings.SchoolCode, Settings.WorkYear.Value);

        var gradeStudents = enrollments
            .Where(e => e.Grade == grade)
            .OrderBy(e => e.Class).ThenBy(e => e.Number)
            .ToList();

        var result = new SortedDictionary<int, List<(int Number, string Name)>>();
        foreach (var e in gradeStudents)
        {
            if (!result.ContainsKey(e.Class))
                result[e.Class] = new();
            result[e.Class].Add((e.Number, e.Name));
        }
        return result;
    }

    /// <summary>
    /// 수업의 학생을 강의실별로 그룹화하여 반환
    /// </summary>
    private async Task<Dictionary<string, List<(int Number, string Name)>>> LoadCourseStudentsByRoomAsync(int courseNo)
    {
        using var courseService = new CourseService();
        var enrollments = await courseService.GetCourseEnrollmentsAsync(
            Settings.SchoolCode, Settings.WorkYear.Value, Settings.WorkSemester.Value, courseNo);

        var studentIds = enrollments.Select(e => e.StudentID).ToList();
        using var enrollService = new EnrollmentService();
        var allEnrollments = await enrollService.GetEnrollmentsAsync(
            Settings.SchoolCode, Settings.WorkYear.Value);

        var studentMap = allEnrollments
            .Where(e => studentIds.Contains(e.StudentID))
            .ToDictionary(e => e.StudentID, e => (e.Number, e.Name));

        var result = new Dictionary<string, List<(int Number, string Name)>>();
        foreach (var ce in enrollments.OrderBy(e => e.Room))
        {
            var room = string.IsNullOrWhiteSpace(ce.Room) ? "미지정" : ce.Room;
            if (!result.ContainsKey(room))
                result[room] = new();
            if (studentMap.TryGetValue(ce.StudentID, out var student))
                result[room].Add(student);
        }

        // 각 강의실 내 번호순 정렬
        foreach (var list in result.Values)
            list.Sort((a, b) => a.Number.CompareTo(b.Number));

        return result;
    }

    private async Task<List<(int Number, string Name)>> LoadCourseStudentsAsync(int courseNo)
    {
        using var courseService = new CourseService();
        var enrollments = await courseService.GetCourseEnrollmentsAsync(
            Settings.SchoolCode, Settings.WorkYear.Value, Settings.WorkSemester.Value, courseNo);

        // 강의실 필터링 ("전체" 선택 시 필터링 안 함)
        var selectedRoom = RoomComboBox.Visibility == Visibility.Visible
            ? RoomComboBox.SelectedItem as string
            : null;
        if (!string.IsNullOrEmpty(selectedRoom) && selectedRoom != "전체")
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

    #endregion

    #region HTML 테이블 빌드

    /// <summary>
    /// 단일 학생 목록 테이블 (학급/수업/동아리)
    /// </summary>
    private static string BuildSingleTable(
        string title, string scopeLabel,
        List<string> columns, List<(int Number, string Name)> students)
    {
        int totalCols = 2 + columns.Count;

        var sb = new StringBuilder();
        sb.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse; width:100%; text-align:center;\">");

        if (!string.IsNullOrEmpty(title))
            sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:16px; padding:8px; background:#e8f0fe;\">{Esc(title)}</th></tr>");

        sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:13px; padding:6px; background:#f8f9fa; text-align:right;\">{Esc(scopeLabel)}</th></tr>");

        AppendHeaderRow(sb, columns);

        foreach (var (number, name) in students)
            AppendStudentRow(sb, number, name, columns.Count);

        sb.Append("</table>");
        return sb.ToString();
    }

    /// <summary>
    /// 그룹별 세로 레이아웃 (그룹별 구분)
    /// </summary>
    private static string BuildGroupedVerticalTable(
        string title, string scopeLabel, List<string> columns,
        Dictionary<string, List<(int Number, string Name)>> groupMap)
    {
        int totalCols = 2 + columns.Count;

        var sb = new StringBuilder();
        sb.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse; width:100%; text-align:center;\">");

        if (!string.IsNullOrEmpty(title))
            sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:16px; padding:8px; background:#e8f0fe;\">{Esc(title)}</th></tr>");

        sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:13px; padding:6px; background:#f8f9fa; text-align:right;\">{Esc(scopeLabel)}</th></tr>");

        foreach (var (groupName, students) in groupMap)
        {
            sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:14px; padding:6px; background:#fff3cd;\">{Esc(groupName)}</th></tr>");
            AppendHeaderRow(sb, columns);
            foreach (var (number, name) in students)
                AppendStudentRow(sb, number, name, columns.Count);
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    /// <summary>
    /// 그룹별 가로 레이아웃 (그룹별 칼럼)
    /// </summary>
    private static string BuildGroupedHorizontalTable(
        string title, string scopeLabel, List<string> columns,
        Dictionary<string, List<(int Number, string Name)>> groupMap)
    {
        var groups = groupMap.Keys.ToList();
        int colsPerGroup = 2 + columns.Count;
        int totalCols = colsPerGroup * groups.Count;
        int maxStudents = groupMap.Values.Max(s => s.Count);

        var sb = new StringBuilder();
        sb.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse; width:100%; text-align:center;\">");

        if (!string.IsNullOrEmpty(title))
            sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:16px; padding:8px; background:#e8f0fe;\">{Esc(title)}</th></tr>");

        sb.Append($"<tr><th colspan=\"{totalCols}\" style=\"font-size:13px; padding:6px; background:#f8f9fa; text-align:right;\">{Esc(scopeLabel)}</th></tr>");

        // 그룹 이름 헤더 (병합)
        sb.Append("<tr>");
        foreach (var groupName in groups)
            sb.Append($"<th colspan=\"{colsPerGroup}\" style=\"font-size:14px; padding:6px; background:#fff3cd;\">{Esc(groupName)}</th>");
        sb.Append("</tr>");

        // 칼럼 헤더 (그룹별 반복)
        sb.Append("<tr style=\"background:#d0e0f0;\">");
        foreach (var _ in groups)
        {
            sb.Append("<th style=\"width:40px;\">번호</th>");
            sb.Append("<th style=\"width:60px;\">이름</th>");
            foreach (var col in columns)
                sb.Append($"<th>{Esc(col)}</th>");
        }
        sb.Append("</tr>");

        // 학생 행 (가장 많은 그룹 기준)
        for (int row = 0; row < maxStudents; row++)
        {
            sb.Append("<tr>");
            foreach (var groupName in groups)
            {
                var students = groupMap[groupName];
                if (row < students.Count)
                {
                    var (number, name) = students[row];
                    sb.Append($"<td>{number}</td>");
                    sb.Append($"<td>{Esc(name)}</td>");
                    for (int c = 0; c < columns.Count; c++)
                        sb.Append("<td></td>");
                }
                else
                {
                    for (int c = 0; c < colsPerGroup; c++)
                        sb.Append("<td></td>");
                }
            }
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    /// <summary>헤더 행 (번호, 이름, 사용자 칼럼)</summary>
    private static void AppendHeaderRow(StringBuilder sb, List<string> columns)
    {
        sb.Append("<tr style=\"background:#d0e0f0;\">");
        sb.Append("<th style=\"width:50px;\">번호</th>");
        sb.Append("<th style=\"width:80px;\">이름</th>");
        foreach (var col in columns)
            sb.Append($"<th>{Esc(col)}</th>");
        sb.Append("</tr>");
    }

    /// <summary>학생 행</summary>
    private static void AppendStudentRow(StringBuilder sb, int number, string name, int colCount)
    {
        sb.Append("<tr>");
        sb.Append($"<td>{number}</td>");
        sb.Append($"<td>{Esc(name)}</td>");
        for (int i = 0; i < colCount; i++)
            sb.Append("<td></td>");
        sb.Append("</tr>");
    }

    #endregion

    private static string Esc(string text)
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
