using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NewSchool.Controls;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NewSchool.Dialogs;

/// <summary>
/// 학생부 특기사항 일괄 입력 다이얼로그
/// 
/// 학급 학생 전원의 특기사항을 한 화면에서 편집
/// - ListStudent 컨트롤로 학생 선택 (방향키 전환)
/// - 학생별 Content를 메모리 캐시
/// - 변경된 항목만 일괄 저장
/// </summary>
public sealed partial class StudentSpecBatchDialog : Window
{
    #region Fields

    private int _year;
    private int _semester;
    private int _grade;
    private int _classNum;
    private string _selectedType = string.Empty;

    private Enrollment? _currentStudent;
    private string _currentOriginalContent = string.Empty;

    /// <summary>StudentID → StudentSpecial 캐시 (메모리)</summary>
    private readonly Dictionary<string, StudentSpecial> _specCache = new();

    /// <summary>StudentID → 누가기록 DraftSummary 캐시</summary>
    private readonly Dictionary<string, List<string>> _logDraftCache = new();

    /// <summary>변경된 StudentID 집합</summary>
    private readonly HashSet<string> _modifiedIds = new();

    private bool _isRefPanelOpen;

    public ObservableCollection<Course> Courses { get; } = new();

    #endregion

    #region Constructor

    /// <summary>
    /// 학급 기반 일괄 입력
    /// </summary>
    /// <param name="year">학년도</param>
    /// <param name="semester">학기</param>
    /// <param name="grade">학년</param>
    /// <param name="classNum">반</param>
    /// <param name="defaultType">기본 영역 (null이면 선택)</param>
    public StudentSpecBatchDialog(int year, int semester, int grade, int classNum, string? defaultType = null)
    {
        this.InitializeComponent();

        _year = year;
        _semester = semester;
        _grade = grade;
        _classNum = classNum;

        TxtClassInfo.Text = $"{grade}학년 {classNum}반";
        Title = $"학생부 특기사항 일괄 입력 - {grade}학년 {classNum}반";

        InitializeTypeComboBox(defaultType);
        InitializeStudentList();

        _ = LoadStudentsAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[StudentSpecBatchDialog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
        _ = LoadCoursesAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[StudentSpecBatchDialog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    #endregion

    #region Initialization

    private void InitializeTypeComboBox(string? defaultType)
    {
        var types = new List<string>
        {
            "교과활동", "개인별세특", "자율활동", "동아리활동", "진로활동", "종합의견"
        };
        CBoxType.ItemsSource = types;

        if (!string.IsNullOrEmpty(defaultType) && types.Contains(defaultType))
        {
            CBoxType.SelectedItem = defaultType;
        }
        else
        {
            CBoxType.SelectedIndex = 0;
        }
    }

    private void InitializeStudentList()
    {
        StudentList.ViewMode = ListStudent.View.NumName;
        StudentList.ShowCheckBox = false;
        StudentList.StudentSelected += OnStudentSelected;
    }

    #endregion

    #region Data Loading

    private async Task LoadStudentsAsync()
    {
        try
        {
            using var enrollmentService = new EnrollmentService();
            var students = await enrollmentService.GetClassRosterAsync(
                Settings.SchoolCode.Value, _year, _grade, _classNum);

            var sorted = students.OrderBy(s => s.Number).ToList();
            StudentList.LoadStudents(sorted);

            TxtClassInfo.Text = $"{_grade}학년 {_classNum}반 ({sorted.Count}명)";

            // 첫 학생 자동 선택
            if (sorted.Count > 0)
            {
                StudentList.SelectStudent(sorted[0].StudentID);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentSpecBatchDialog] 학생 로드 실패: {ex.Message}");
        }
    }

    private async Task LoadCoursesAsync()
    {
        try
        {
            using var courseService = new CourseService();
            var courses = await courseService.GetMyCoursesAsync();

            Courses.Clear();
            foreach (var c in courses)
                Courses.Add(c);

            if (Courses.Count > 0)
                CBoxCourse.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentSpecBatchDialog] 과목 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 학생의 특기사항 로드 (캐시 우선)
    /// </summary>
    private async Task<StudentSpecial> LoadOrGetSpecAsync(string studentId)
    {
        // 캐시에 있으면 반환
        if (_specCache.TryGetValue(studentId, out var cached))
            return cached;

        // DB 조회
        using var service = new StudentSpecialService();
        var specials = await service.GetByStudentAsync(studentId, _year);

        StudentSpecial? spec = null;

        if (_selectedType == "교과활동" || _selectedType == "개인별세특")
        {
            // 과목으로 매칭
            string subjectName = (CBoxCourse.SelectedItem as Course)?.Subject ?? string.Empty;
            spec = specials.FirstOrDefault(s => s.Type == _selectedType && s.SubjectName == subjectName);
        }
        else
        {
            spec = specials.FirstOrDefault(s => s.Type == _selectedType);
        }

        // 없으면 새로 생성
        spec ??= CreateEmptySpec(studentId);

        _specCache[studentId] = spec;
        return spec;
    }

    private StudentSpecial CreateEmptySpec(string studentId)
    {
        var course = CBoxCourse.SelectedItem as Course;
        bool isCourse = _selectedType == "교과활동" || _selectedType == "개인별세특";

        return new StudentSpecial
        {
            No = 0,
            StudentID = studentId,
            Year = _year,
            Type = _selectedType,
            Title = isCourse ? (course?.Subject ?? string.Empty) : string.Empty,
            Content = string.Empty,
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            TeacherID = Settings.User.Value,
            CourseNo = isCourse ? (course?.No ?? 0) : 0,
            SubjectName = isCourse ? (course?.Subject ?? string.Empty) : string.Empty,
            IsFinalized = false,
            Tag = string.Empty
        };
    }

    #endregion

    #region Student Selection

    private async void OnStudentSelected(object? sender, Enrollment student)
    {
        // 이전 학생 Content 저장
        SaveCurrentToCache();

        _currentStudent = student;

        // 현재 학생 정보 표시
        TxtStudentName.Text = student.Name;
        TxtStudentNum.Text = $"{student.Number}번";

        // 특기사항 로드
        var spec = await LoadOrGetSpecAsync(student.StudentID);

        _currentOriginalContent = spec.Content ?? string.Empty;
        TxtContent.Text = spec.Content ?? string.Empty;
        TxtContent.IsEnabled = !spec.IsFinalized;

        // 수정 아이콘
        IconModified.Visibility = _modifiedIds.Contains(student.StudentID)
            ? Visibility.Visible : Visibility.Collapsed;

        UpdateByteInfo();
        UpdateProgress();

        // 누가기록 참조 로드
        if (_isRefPanelOpen)
        {
            await LoadLogDraftsAsync(student.StudentID);
        }
    }

    /// <summary>
    /// 현재 편집 중인 학생의 Content를 캐시에 반영
    /// </summary>
    private void SaveCurrentToCache()
    {
        if (_currentStudent == null) return;

        var studentId = _currentStudent.StudentID;
        if (!_specCache.TryGetValue(studentId, out var spec)) return;

        var newContent = TxtContent.Text ?? string.Empty;
        spec.Content = newContent;

        // 변경 감지
        // _currentOriginalContent는 DB에서 로드한 원본
        // 캐시에 이미 수정된 상태일 수도 있으므로, modifiedIds로 추적
        if (newContent != _currentOriginalContent)
        {
            _modifiedIds.Add(studentId);
        }
    }

    #endregion

    #region Event Handlers — Filter

    private void CBoxType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxType.SelectedItem is not string type) return;

        // 현재 편집 내용 캐시 저장
        SaveCurrentToCache();

        _selectedType = type;

        // 교과활동/개인별세특이면 과목 선택 표시
        bool showCourse = type == "교과활동" || type == "개인별세특";
        CBoxCourse.Visibility = showCourse ? Visibility.Visible : Visibility.Collapsed;

        // 캐시 초기화 (영역이 바뀌면 다시 로드해야 함)
        ClearCache();

        // 현재 학생 다시 로드
        if (_currentStudent != null)
        {
            _ = ReloadCurrentStudentAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"[StudentSpecBatchDialog] {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private void CBoxCourse_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxCourse.SelectedItem is not Course) return;

        // 캐시 초기화 (과목이 바뀌면 다시 로드)
        SaveCurrentToCache();
        ClearCache();

        if (_currentStudent != null)
        {
            _ = ReloadCurrentStudentAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"[StudentSpecBatchDialog] {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private void ClearCache()
    {
        _specCache.Clear();
        _modifiedIds.Clear();
        _logDraftCache.Clear();
        _currentOriginalContent = string.Empty;
    }

    private async Task ReloadCurrentStudentAsync()
    {
        if (_currentStudent == null) return;

        var spec = await LoadOrGetSpecAsync(_currentStudent.StudentID);
        _currentOriginalContent = spec.Content ?? string.Empty;
        TxtContent.Text = spec.Content ?? string.Empty;
        TxtContent.IsEnabled = !spec.IsFinalized;

        IconModified.Visibility = Visibility.Collapsed;
        UpdateByteInfo();
        UpdateProgress();
    }

    #endregion

    #region Event Handlers — Content

    private void TxtContent_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateByteInfo();

        // 수정 표시
        if (_currentStudent != null)
        {
            bool isChanged = TxtContent.Text != _currentOriginalContent;
            IconModified.Visibility = isChanged ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    #endregion

    #region Event Handlers — Buttons

    private async void BtnSaveAll_Click(object sender, RoutedEventArgs e)
    {
        // 현재 편집 중인 학생 캐시 저장
        SaveCurrentToCache();

        if (_modifiedIds.Count == 0)
        {
            TxtSaveStatus.Text = "변경된 항목이 없습니다.";
            return;
        }

        try
        {
            BtnSaveAll.IsEnabled = false;
            TxtSaveStatus.Text = "저장 중...";

            using var service = new StudentSpecialService();
            int savedCount = 0;
            int failCount = 0;

            foreach (var studentId in _modifiedIds.ToList())
            {
                if (!_specCache.TryGetValue(studentId, out var spec)) continue;

                try
                {
                    if (spec.No > 0)
                    {
                        await service.UpdateAsync(spec);
                    }
                    else
                    {
                        spec.No = await service.CreateAsync(spec);
                    }
                    savedCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    Debug.WriteLine($"[StudentSpecBatchDialog] 저장 실패 StudentID={studentId}: {ex.Message}");
                }
            }

            // 저장 후 변경 목록 초기화
            _modifiedIds.Clear();

            // 현재 학생의 원본도 갱신
            if (_currentStudent != null && _specCache.TryGetValue(_currentStudent.StudentID, out var current))
            {
                _currentOriginalContent = current.Content ?? string.Empty;
            }

            IconModified.Visibility = Visibility.Collapsed;
            UpdateProgress();

            var msg = failCount > 0
                ? $"{savedCount}건 저장, {failCount}건 실패"
                : $"{savedCount}건 저장 완료";
            TxtSaveStatus.Text = msg;
        }
        catch (Exception ex)
        {
            TxtSaveStatus.Text = $"저장 실패: {ex.Message}";
        }
        finally
        {
            BtnSaveAll.IsEnabled = true;
        }
    }

    private async void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentToCache();

        if (_modifiedIds.Count > 0)
        {
            var dialog = new ContentDialog
            {
                Title = "저장하지 않은 변경사항",
                Content = $"{_modifiedIds.Count}건의 미저장 변경사항이 있습니다.\n저장하고 닫을까요?",
                PrimaryButtonText = "저장 후 닫기",
                SecondaryButtonText = "저장 안 함",
                CloseButtonText = "취소",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                BtnSaveAll_Click(sender, e);
                // 저장 완료 후 닫기 (저장 실패 시에도 닫음)
            }
            else if (result == ContentDialogResult.None)
            {
                return; // 취소
            }
        }

        this.Close();
    }

    private async void BtnSpellCheck_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://nara-speller.co.kr/speller"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentSpecBatchDialog] 맞춤법 검사기 열기 실패: {ex.Message}");
        }
    }

    #endregion

    #region UI Helpers

    private void UpdateByteInfo()
    {
        string text = TxtContent.Text ?? string.Empty;
        int currentBytes = NeisHelper.CountByte(text);
        int maxBytes = NeisHelper.GetMaxBytes(_selectedType);
        int charCount = text.Length;

        TxtByteInfo.Text = $"{currentBytes} / {maxBytes} Byte ({charCount}자)";

        TxtByteInfo.Foreground = currentBytes > maxBytes
            ? new SolidColorBrush(Colors.Red)
            : (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    private void UpdateProgress()
    {
        // 입력된 학생 수 / 전체 학생 수
        int total = StudentList.Students.Count;
        int filled = 0;

        foreach (var student in StudentList.Students)
        {
            if (_specCache.TryGetValue(student.StudentID, out var spec))
            {
                if (!string.IsNullOrWhiteSpace(spec.Content))
                    filled++;
            }
        }

        int modified = _modifiedIds.Count;
        TxtProgress.Text = $"입력: {filled}/{total}명";
        if (modified > 0)
            TxtProgress.Text += $" | 미저장: {modified}건";
    }

    #endregion

    #region 누가기록 참조 & 초안 자동 생성

    /// <summary>
    /// 참조 패널 토글
    /// </summary>
    private async void BtnToggleRef_Click(object sender, RoutedEventArgs e)
    {
        _isRefPanelOpen = BtnToggleRef.IsChecked == true;

        if (_isRefPanelOpen)
        {
            RowRef.Height = new GridLength(200);

            if (_currentStudent != null)
            {
                await LoadLogDraftsAsync(_currentStudent.StudentID);
            }
        }
        else
        {
            RowRef.Height = new GridLength(0);
        }
    }

    /// <summary>
    /// 학생의 누가기록 DraftSummary 로드
    /// </summary>
    private async Task LoadLogDraftsAsync(string studentId)
    {
        try
        {
            if (!_logDraftCache.TryGetValue(studentId, out var drafts))
            {
                using var logService = new StudentLogService();
                var logs = await logService.GetStudentLogsAsync(studentId, _year);

                // 현재 선택된 영역에 맞는 로그만 필터링
                if (!string.IsNullOrEmpty(_selectedType) && _selectedType != "전체")
                {
                    if (Enum.TryParse<LogCategory>(_selectedType, out var cat))
                    {
                        logs = logs.Where(l => l.Category == cat).ToList();
                    }
                }

                drafts = logs
                    .Where(l => !string.IsNullOrWhiteSpace(l.DraftSummary))
                    .Select(l => l.DraftSummary!)
                    .ToList();

                _logDraftCache[studentId] = drafts;
            }

            if (drafts.Count > 0)
            {
                TxtLogReference.Text = string.Join("\n\n", drafts.Select((d, i) => $"[{i + 1}] {d}"));
                TxtLogReference.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            }
            else
            {
                TxtLogReference.Text = "해당 영역의 누가기록 초안이 없습니다.";
                TxtLogReference.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }
        }
        catch (Exception ex)
        {
            TxtLogReference.Text = $"누가기록 로드 실패: {ex.Message}";
            Debug.WriteLine($"[StudentSpecBatchDialog] 누가기록 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 초안 자동 생성 — 누가기록 DraftSummary를 병합하여 Content에 채움
    /// </summary>
    private async void BtnAutoGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStudent == null)
        {
            TxtSaveStatus.Text = "학생을 먼저 선택하세요.";
            return;
        }

        // 이미 내용이 있으면 확인
        if (!string.IsNullOrWhiteSpace(TxtContent.Text))
        {
            var dialog = new ContentDialog
            {
                Title = "초안 자동 생성",
                Content = "기존 내용이 있습니다. 기존 내용 뒤에 추가할까요?",
                PrimaryButtonText = "뒤에 추가",
                SecondaryButtonText = "덮어쓰기",
                CloseButtonText = "취소",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None) return;

            var draft = await GenerateDraftAsync(_currentStudent.StudentID);
            if (string.IsNullOrEmpty(draft))
            {
                TxtSaveStatus.Text = "생성할 초안이 없습니다.";
                return;
            }

            if (result == ContentDialogResult.Primary)
            {
                // 뒤에 추가
                TxtContent.Text = string.IsNullOrWhiteSpace(TxtContent.Text)
                    ? draft
                    : TxtContent.Text.TrimEnd() + " " + draft;
            }
            else
            {
                // 덮어쓰기
                TxtContent.Text = draft;
            }
        }
        else
        {
            var draft = await GenerateDraftAsync(_currentStudent.StudentID);
            if (string.IsNullOrEmpty(draft))
            {
                TxtSaveStatus.Text = "생성할 초안이 없습니다.";
                return;
            }
            TxtContent.Text = draft;
        }

        TxtSaveStatus.Text = "초안이 생성되었습니다. 내용을 확인 후 저장하세요.";
    }

    /// <summary>
    /// 누가기록 DraftSummary를 병합하여 하나의 문장으로 생성
    /// </summary>
    private async Task<string> GenerateDraftAsync(string studentId)
    {
        // 캐시가 없으면 로드
        if (!_logDraftCache.TryGetValue(studentId, out var drafts))
        {
            await LoadLogDraftsAsync(studentId);
            _logDraftCache.TryGetValue(studentId, out drafts);
        }

        if (drafts == null || drafts.Count == 0)
            return string.Empty;

        // 각 초안을 공백으로 병합
        return string.Join(" ", drafts);
    }

    #endregion
}

