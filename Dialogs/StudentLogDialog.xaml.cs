using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NewSchool.Dialogs;

/// <summary>
/// 학생 기록 입력/편집 다이얼로그
/// 
/// 4가지 모드:
/// 1. 단일 학생 편집 (기존 로그)
/// 2. 학급별 일괄 입력 (자율/봉사/진로/종합의견/상담/기타)
/// 3. 교과활동 과목별 입력
/// 4. 동아리활동 동아리별 입력
/// </summary>
public sealed partial class StudentLogDialog : Window
{
    #region Fields

    private LogCategory _category = LogCategory.전체;
    private int _year;
    private int _semester;
    private int _selectedGrade;
    private int _selectedClass;
    private int _selectedCourseNo;
    private int _selectedClubNo;

    private List<StudentLog> _logs = new();
    private bool _isSingleStudentMode;
    private bool _isInitializing = true;
    private string? _pendingStudentId; // 생성자에서 비동기 로드 대신 Loaded에서 처리

    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<Club> Clubs { get; } = new();

    #endregion

    #region Properties

    public List<StudentLog> SavedLogs => _logs;
    public bool IsSuccess { get; private set; }

    #endregion

    #region Constructors

    /// <summary>
    /// 1. 단일 학생 - 기존 로그 편집
    /// </summary>
    public StudentLogDialog(StudentLog log)
    {
        this.InitializeComponent();

        _isSingleStudentMode = true;
        _category = log.Category;
        _year = log.Year;
        _semester = log.Semester;

        InitializeCommon();
        HideAllFilters();

        ColStudentList.Width = new GridLength(0);
        ListStudents.Visibility = Visibility.Collapsed;

        _pendingStudentId = log.StudentID;
        TxtStudentInfo.Text = "학생 정보 로드 중...";

        LogBox.LoadLog(log);
        Title = $"{log.Category} 기록 편집";

        this.Activated += async (s, e) =>
        {
            if (_pendingStudentId != null)
            {
                var studentId = _pendingStudentId;
                _pendingStudentId = null;
                try
                {
                    using var svc = new StudentService(SchoolDatabase.DbPath);
                    var student = await svc.GetBasicInfoAsync(studentId);
                    TxtStudentInfo.Text = student != null ? $"학생: {student.Name}" : "학생 정보 없음";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[StudentLogDialog] 학생 정보 로드 실패: {ex.Message}");
                    TxtStudentInfo.Text = "학생 정보 없음";
                }
            }
            _isInitializing = false;
        };
    }

    /// <summary>
    /// 1-b. 단일 학생 - 새 기록 작성
    /// </summary>
    public StudentLogDialog(Enrollment student, int year, int semester)
    {
        this.InitializeComponent();

        _isSingleStudentMode = true;
        _year = year;
        _semester = semester;

        InitializeCommon();
        HideAllFilters();

        ColStudentList.Width = new GridLength(0);
        ListStudents.Visibility = Visibility.Collapsed;

        TxtStudentInfo.Text = $"학생: {student.Name} ({student.Grade}학년 {student.Class}반 {student.Number}번)";

        LogBox.CreateNew(
            studentId: student.StudentID,
            teacherId: Settings.User.Value,
            year: year,
            semester: semester);

        Title = $"학생 기록 작성 — {student.Name}";
        _isInitializing = false;
    }

    /// <summary>
    /// 2. 학급별 일괄 입력 (자율/봉사/진로/종합의견/상담/기타)
    /// </summary>
    public StudentLogDialog(string dbPath, LogCategory category, int year, int semester, int grade, int classNum)
    {
        this.InitializeComponent();

        _category = category;
        _year = year;
        _semester = semester;
        _selectedGrade = grade;
        _selectedClass = classNum;

        InitializeCommon();
        SetupBatchMode();

        // 다이얼로그 상단 필터: 모두 숨김 (학급 고정)
        HideAllFilters();
        TxtStudentInfo.Text = $"{year}학년도 {semester}학기  ▸  {grade}학년 {classNum}반";

        // LogBox 카테고리 설정 + 학년도/학기 잠금
        LogBox.SetCategory(category, locked: false);
        LogBox.LockYearSemester();
        LogBox.HideStudentInfo();

        _ = LoadClassStudentsAsync(year, semester, grade, classNum);

        Title = $"{category} 기록 일괄 입력 — {year}학년도 {grade}학년 {classNum}반";
        _isInitializing = false;
    }

    /// <summary>
    /// 3. 교과활동 - 과목별 입력
    /// </summary>
    public StudentLogDialog(string dbPath, LogCategory category, int year, int semester, int courseNo, string teacherId)
    {
        this.InitializeComponent();

        _category = LogCategory.교과활동;
        _year = year;
        _semester = semester;
        _selectedCourseNo = courseNo;

        InitializeCommon();
        SetupBatchMode();

        // 다이얼로그 상단: 과목 선택만 표시 (학생 목록 로드용)
        HideAllFilters();
        CBoxCourse.Visibility = Visibility.Visible;

        // LogBox: 교과활동 고정 + 학년도/학기 잠금
        LogBox.SetCategory(LogCategory.교과활동, locked: true);
        LogBox.LockYearSemester();
        LogBox.HideStudentInfo();

        TxtStudentInfo.Text = $"{year}학년도 {semester}학기";
        Title = $"교과활동 기록 일괄 입력 — {year}학년도";

        _ = InitCourseAsync(year, semester, teacherId);
    }

    /// <summary>
    /// 4. 동아리활동 - 동아리별 입력
    /// </summary>
    public StudentLogDialog(string dbPath, int year, int semester, int clubNo, string schoolCode)
    {
        this.InitializeComponent();

        _category = LogCategory.동아리활동;
        _year = year;
        _semester = semester;
        _selectedClubNo = clubNo;

        InitializeCommon();
        SetupBatchMode();

        // 다이얼로그 상단: 동아리 선택만 표시
        HideAllFilters();
        CBoxClub.Visibility = Visibility.Visible;

        // LogBox: 동아리활동 고정
        LogBox.SetCategory(LogCategory.동아리활동, locked: true);
        LogBox.LockYearSemester();
        LogBox.HideStudentInfo();

        TxtStudentInfo.Text = $"{year}학년도 {semester}학기";
        Title = $"동아리활동 기록 일괄 입력 — {year}학년도";

        _ = InitClubAsync(year, schoolCode);
    }

    #endregion

    #region Initialization

    private void InitializeCommon()
    {
        CBoxCategory.Items.Clear();
        foreach (LogCategory cat in Enum.GetValues<LogCategory>())
        {
            if (cat == LogCategory.전체) continue;
            CBoxCategory.Items.Add(cat);
        }

        LogBox.LogSaved += OnLogBoxSaved;
        LogBox.LogCancelled += OnLogBoxCancelled;
    }

    private void SetupBatchMode()
    {
        _isSingleStudentMode = false;
        ListStudents.ViewMode = ListStudent.View.NumName;
        ListStudents.ShowCheckBox = true;

        // ★ LogBox 초기화 — 템플릿 로그 생성 (이게 없으면 _currentLog이 null이라 저장 안됨)
        LogBox.CreateNew(
            studentId: "BATCH",  // 일괄 입력용 임시 ID (저장 시 각 학생 ID로 대체)
            teacherId: Settings.User.Value,
            year: _year,
            semester: _semester);
    }

    private void HideAllFilters()
    {
        CBoxCategory.Visibility = Visibility.Collapsed;
        CBoxCourse.Visibility = Visibility.Collapsed;
        CBoxClub.Visibility = Visibility.Collapsed;
        CBoxGrade.Visibility = Visibility.Collapsed;
        CBoxClass.Visibility = Visibility.Collapsed;
    }

    private void SelectCategory(LogCategory category)
    {
        for (int i = 0; i < CBoxCategory.Items.Count; i++)
        {
            if (CBoxCategory.Items[i] is LogCategory cat && cat == category)
            {
                CBoxCategory.SelectedIndex = i;
                break;
            }
        }
    }

    /// <summary>교과활동 비동기 초기화: 과목 로드 → 선택 → 수강생 로드 → _isInitializing 해제</summary>
    private async Task InitCourseAsync(int year, int semester, string teacherId)
    {
        await LoadCoursesAsync(year, semester, teacherId);
        _isInitializing = false;
    }

    /// <summary>동아리활동 비동기 초기화: 동아리 로드 → 선택 → 학생 로드 → _isInitializing 해제</summary>
    private async Task InitClubAsync(int year, string schoolCode)
    {
        await LoadClubsAsync(year, schoolCode);
        _isInitializing = false;
    }

    #endregion

    #region Data Loading

    private async Task LoadClassStudentsAsync(int year, int semester, int grade, int classNum)
    {
        try
        {
            using var svc = new EnrollmentService();
            var list = await svc.GetEnrollmentsAsync(Settings.SchoolCode.Value, year, 0, grade, classNum);
            ListStudents.LoadStudents(list.OrderBy(e => e.Number));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentLogDialog] 학생 로드 실패: {ex.Message}");
        }
    }

    private async Task LoadCoursesAsync(int year, int semester, string teacherId)
    {
        try
        {
            using var svc = new CourseService();
            var courses = await svc.GetByTeacherAsync(teacherId, year, semester);
            Courses.Clear();
            foreach (var c in courses) Courses.Add(c);

            CBoxCourse.ItemsSource = Courses;

            // 초기 선택 (이벤트 자동 발생 → 수강생 로드)
            var target = Courses.FirstOrDefault(c => c.No == _selectedCourseNo) ?? Courses.FirstOrDefault();
            if (target != null)
                CBoxCourse.SelectedItem = target;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentLogDialog] 과목 로드 실패: {ex.Message}");
        }
    }

    private async Task LoadClubsAsync(int year, string schoolCode)
    {
        try
        {
            using var svc = new ClubService();
            var clubs = await svc.GetAllClubsAsync(schoolCode, year);
            Clubs.Clear();
            foreach (var c in clubs) Clubs.Add(c);

            CBoxClub.ItemsSource = Clubs;

            var target = Clubs.FirstOrDefault(c => c.No == _selectedClubNo) ?? Clubs.FirstOrDefault();
            if (target != null)
                CBoxClub.SelectedItem = target;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentLogDialog] 동아리 로드 실패: {ex.Message}");
        }
    }

    private async Task LoadCourseStudentsAsync(int courseNo)
    {
        try
        {
            using var ceRepo = new CourseEnrollmentRepository(SchoolDatabase.DbPath);
            var ceList = await ceRepo.GetByCourseAsync(courseNo);

            if (ceList.Count == 0)
            {
                ListStudents.ClearStudents();
                return;
            }

            var studentIds = ceList.Select(ce => ce.StudentID).ToHashSet();

            // GetEnrollmentsAsync(schoolCode, year) → 해당 학년도 전체
            using var enrollSvc = new EnrollmentService();
            var allEnroll = await enrollSvc.GetEnrollmentsAsync(Settings.SchoolCode.Value, _year);
            var matched = allEnroll.Where(e => studentIds.Contains(e.StudentID)).ToList();

            ListStudents.LoadStudents(matched.OrderBy(e => e.Class).ThenBy(e => e.Number));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentLogDialog] 수강생 로드 실패: {ex.Message}");
            ListStudents.ClearStudents();
        }
    }

    private async Task LoadClubStudentsAsync(int clubNo)
    {
        try
        {
            using var ceRepo = new ClubEnrollmentRepository(SchoolDatabase.DbPath);
            var ceList = await ceRepo.GetByClubAsync(clubNo);

            if (ceList.Count == 0)
            {
                ListStudents.ClearStudents();
                return;
            }

            var studentIds = ceList.Select(ce => ce.StudentID).ToHashSet();

            using var enrollSvc = new EnrollmentService();
            var allEnroll = await enrollSvc.GetEnrollmentsAsync(Settings.SchoolCode.Value, _year);
            var matched = allEnroll.Where(e => studentIds.Contains(e.StudentID)).ToList();

            ListStudents.ViewMode = ListStudent.View.ClassNumName;
            ListStudents.LoadStudents(matched.OrderBy(e => e.Class).ThenBy(e => e.Number));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentLogDialog] 동아리 학생 로드 실패: {ex.Message}");
            ListStudents.ClearStudents();
        }
    }

    #endregion

    #region Event Handlers — Filters

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CBoxCategory.SelectedItem is not LogCategory cat) return;

        _category = cat;

        CBoxCourse.Visibility = (cat == LogCategory.교과활동 || cat == LogCategory.개인별세특)
            ? Visibility.Visible : Visibility.Collapsed;

        CBoxClub.Visibility = cat == LogCategory.동아리활동
            ? Visibility.Visible : Visibility.Collapsed;

        // 학급이 이미 고정(생성자에서 지정)되었으면 학년/반 필터 항상 숨김
        bool showGradeClass = _selectedGrade == 0 && _selectedClass == 0
            && cat != LogCategory.교과활동 && cat != LogCategory.개인별세특
            && cat != LogCategory.동아리활동;

        CBoxGrade.Visibility = showGradeClass ? Visibility.Visible : Visibility.Collapsed;
        CBoxClass.Visibility = showGradeClass ? Visibility.Visible : Visibility.Collapsed;

        // 학년/반 필터가 보이면 학년 목록 채우기
        if (showGradeClass && CBoxGrade.Items.Count == 0)
        {
            _ = FillGradeComboAsync();
        }
    }

    private async Task FillGradeComboAsync()
    {
        try
        {
            using var svc = new EnrollmentService();
            var grades = await svc.GetGradeListByYearAsync(Settings.SchoolCode.Value, _year);
            CBoxGrade.Items.Clear();
            foreach (var g in grades)
                CBoxGrade.Items.Add(g);

            if (CBoxGrade.Items.Count > 0)
                CBoxGrade.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentLogDialog] 학년 목록 실패: {ex.Message}");
        }
    }

    private async void OnCourseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxCourse.SelectedItem is not Course course) return;

        _selectedCourseNo = course.No;
        TxtStudentInfo.Text = $"{_year}학년도 {_semester}학기  ▸  {course.Subject}";

        // LogBox 과목명 동기화
        LogBox.SetSubjectName(course.Subject, locked: true);

        await LoadCourseStudentsAsync(course.No);
    }

    private async void OnClubChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxClub.SelectedItem is not Club club) return;

        _selectedClubNo = club.No;
        TxtStudentInfo.Text = $"{_year}학년도 {_semester}학기  ▸  {club.ClubName}";
        await LoadClubStudentsAsync(club.No);
    }

    private async void OnGradeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxGrade.SelectedItem is not int grade) return;

        _selectedGrade = grade;

        CBoxClass.Items.Clear();
        try
        {
            using var svc = new EnrollmentService();
            var classes = await svc.GetClassListAsync(Settings.SchoolCode.Value, _year, grade);
            foreach (var c in classes)
                CBoxClass.Items.Add(c);

            if (CBoxClass.Items.Count > 0)
                CBoxClass.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StudentLogDialog] 반 목록 실패: {ex.Message}");
        }
    }

    private async void OnClassChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxClass.SelectedItem is not int classNum) return;

        _selectedClass = classNum;
        TxtStudentInfo.Text = $"{_year}학년도 {_semester}학기  ▸  {_selectedGrade}학년 {classNum}반";
        await LoadClassStudentsAsync(_year, _semester, _selectedGrade, _selectedClass);
    }

    #endregion

    #region Event Handlers — LogBox

    private async void OnLogBoxSaved(object? sender, StudentLog log)
    {
        try
        {
            if (_isSingleStudentMode)
                await SaveSingleLogAsync(log);
            else
                await SaveMultipleLogsAsync(log);

            IsSuccess = true;
            this.Close();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("저장 실패", ex.Message);
        }
    }

    private void OnLogBoxCancelled(object? sender, EventArgs e)
    {
        this.Close();
    }

    #endregion

    #region Save Logic

    private async Task SaveSingleLogAsync(StudentLog log)
    {
        using var svc = new StudentLogService();
        if (log.No > 0) await svc.UpdateAsync(log);
        else await svc.InsertAsync(log);

        _logs.Clear();
        _logs.Add(log);
    }

    private async Task SaveMultipleLogsAsync(StudentLog templateLog)
    {
        _logs.Clear();
        var selected = ListStudents.GetSelectedStudents().ToList();

        if (selected.Count == 0)
        {
            await ShowErrorAsync("학생 선택 필요", "로그를 저장할 학생을 선택해주세요.");
            return;
        }

        string teacherId = !string.IsNullOrWhiteSpace(templateLog.TeacherID)
            ? templateLog.TeacherID : Settings.User.Value;

        if (string.IsNullOrWhiteSpace(teacherId))
        {
            await ShowErrorAsync("교사 정보 없음", "Settings에서 사용자 정보를 등록해주세요.");
            return;
        }

        using (var repo = new TeacherRepository(SchoolDatabase.DbPath))
        {
            if (await repo.GetByTeacherIdAsync(teacherId) == null)
            {
                await ShowErrorAsync("교사 ID 오류", $"교사 '{teacherId}'가 등록되어 있지 않습니다.");
                return;
            }
        }

        int courseNo = (_category == LogCategory.교과활동 || _category == LogCategory.개인별세특)
            ? _selectedCourseNo : 0;

        foreach (var enrollment in selected)
        {
            var log = new StudentLog
            {
                StudentID = enrollment.StudentID,
                TeacherID = teacherId,
                Year = enrollment.Year > 0 ? enrollment.Year : _year,
                Semester = enrollment.Semester > 0 ? enrollment.Semester : _semester,
                Date = templateLog.Date,
                Category = templateLog.Category,
                CourseNo = courseNo,
                SubjectName = templateLog.SubjectName,
                Log = templateLog.Log,
                Tag = templateLog.Tag,
                IsImportant = templateLog.IsImportant,
                ActivityName = _category == LogCategory.동아리활동 && CBoxClub.SelectedItem is Club club
                    ? club.ClubName : templateLog.ActivityName,
                Topic = templateLog.Topic,
                Description = templateLog.Description,
                Role = templateLog.Role,
                SkillDeveloped = templateLog.SkillDeveloped,
                StrengthShown = templateLog.StrengthShown,
                ResultOrOutcome = templateLog.ResultOrOutcome
            };

            try
            {
                using var svc = new StudentLogService();
                await svc.InsertAsync(log);
                _logs.Add(log);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[오류] {enrollment.Name} 저장 실패: {ex.Message}");
                await ShowErrorAsync("저장 실패", $"'{enrollment.Name}' 저장 실패:\n{ex.Message}");
                throw;
            }
        }
    }

    #endregion

    #region Helpers

    private async Task ShowErrorAsync(string title, string message)
    {
        await MessageBox.ShowAsync(message, title);
    }

    #endregion
}
