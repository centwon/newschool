using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

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

        this.Activated += OnActivatedOnce;
    }

    private async void OnActivatedOnce(object sender, WindowActivatedEventArgs e)
    {
        this.Activated -= OnActivatedOnce;
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

        _ = LoadClassStudentsAsync(year, semester, grade, classNum).ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[StudentLogDialog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);

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

        _ = InitCourseAsync(year, semester, teacherId).ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[StudentLogDialog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
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

        _ = InitClubAsync(year, schoolCode).ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[StudentLogDialog] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    #endregion

    #region Initialization

    private void InitializeCommon()
    {
        // 안내·오류 대화상자가 메인 창이 아니라 이 창 위에 뜨도록 등록한다.
        Controls.MessageBox.TrackWindow(this);

        // 메인 창이 '항상 위에'면 이 창도 같은 topmost 레벨로 올려 뒤로 숨지 않게 함
        if (Settings.TopMost.Value)
            MainWindow.SetAlwaysOnTop(this, true);

        // 메인 창과 같은 테마로 연다
        NewSchool.Helpers.ThemeHelper.Apply(this);

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
            var list = await svc.GetEnrollmentsAsync(Settings.SchoolCode.Value, year, grade, classNum);
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
            _ = FillGradeComboAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"[StudentLogDialog] {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
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

    /// <summary>
    /// ⚠ <b>저장에 실패했으면 창을 닫지 않는다.</b> 예전에는 저장 함수가 안내만 띄우고
    /// 돌아와도 여기서 무조건 <c>IsSuccess = true; Close();</c> 를 실행했다 — 학급 일괄
    /// 입력에서 학생을 안 고르고 [저장]을 누르면 "학생을 선택해주세요"가 뜬 뒤 창이 그대로
    /// 닫히면서 입력한 내용이 통째로 사라졌다(교사 미등록 경로도 같았다).
    /// </summary>
    private async void OnLogBoxSaved(object? sender, StudentLog log)
    {
        try
        {
            bool ok = _isSingleStudentMode
                ? await SaveSingleLogAsync(log)
                : await SaveMultipleLogsAsync(log);

            if (!ok) return;   // 안내는 저장 함수가 이미 띄웠다

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

    /// <returns>실제로 반영됐으면 true. false 면 안내를 띄운 뒤이므로 창을 닫지 않는다.</returns>
    private async Task<bool> SaveSingleLogAsync(StudentLog log)
    {
        // 학교를 떠난 학생에게 그 뒤 날짜로 기록을 남기려는 것이면 먼저 알린다.
        if (!await Services.EnrollmentGuard.ConfirmRecordsAfterLeavingAsync(
                [((string?)log.StudentID, log.Year, log.Date)]))
            return false;

        using var svc = new StudentLogService();

        // 반영 여부를 확인한다 — 예전에는 결과를 버려서, 이미 지워진 기록을 고쳐도
        // 창이 "저장됨"으로 닫히고 편집 내용이 조용히 사라졌다.
        bool ok = log.No > 0
            ? await svc.UpdateAsync(log)
            : (log.No = await svc.InsertAsync(log)) > 0;

        if (!ok)
        {
            await ShowErrorAsync("저장 실패",
                "저장되지 않았습니다. 이미 지워진 기록일 수 있습니다.");
            return false;
        }

        _logs.Clear();
        _logs.Add(log);
        return true;
    }

    /// <returns>전원 저장에 성공했으면 true. false 면 안내를 띄운 뒤이므로 창을 닫지 않는다.</returns>
    private async Task<bool> SaveMultipleLogsAsync(StudentLog templateLog)
    {
        _logs.Clear();
        var selected = ListStudents.GetSelectedStudents().ToList();

        if (selected.Count == 0)
        {
            await ShowErrorAsync("학생 선택 필요", "로그를 저장할 학생을 선택해주세요.");
            return false;
        }

        string teacherId = !string.IsNullOrWhiteSpace(templateLog.TeacherID)
            ? templateLog.TeacherID : Settings.User.Value;

        if (string.IsNullOrWhiteSpace(teacherId))
        {
            await ShowErrorAsync("교사 정보 없음", "Settings에서 사용자 정보를 등록해주세요.");
            return false;
        }

        using (var repo = new TeacherRepository(SchoolDatabase.DbPath))
        {
            if (await repo.GetByTeacherIdAsync(teacherId) == null)
            {
                await ShowErrorAsync("교사 ID 오류", $"교사 '{teacherId}'가 등록되어 있지 않습니다.");
                return false;
            }
        }

        // 학교를 떠난 학생에게 그 뒤 날짜로 기록을 남기려는 것이면 먼저 알린다.
        if (!await Services.EnrollmentGuard.ConfirmRecordsAfterLeavingAsync(
                selected.Select(en => ((string?)en.StudentID,
                                       en.Year > 0 ? en.Year : _year,
                                       templateLog.Date))))
            return false;

        int courseNo = (_category == LogCategory.교과활동 || _category == LogCategory.개인별세특)
            ? _selectedCourseNo : 0;

        // 동아리활동은 동아리를 전용 칸(ClubNo·ClubName)에 넣는다. 예전에는 활동명 칸에
        // 동아리명을 덮어써서 교사가 적은 활동명이 사라졌고, 목록·내보내기가 읽는
        // 칸은 또 달라 "동아리" 열이 늘 비어 있었다.
        var selectedClub = CBoxClub.SelectedItem as Club;

        var toSave = new List<StudentLog>(selected.Count);

        foreach (var enrollment in selected)
        {
            var log = new StudentLog
            {
                StudentID = enrollment.StudentID,
                TeacherID = teacherId,
                Year = enrollment.Year > 0 ? enrollment.Year : _year,
                // 학기는 기록 쪽에만 있다 — 학적(Enrollment)은 학년 단위라 학기를 들고 있지 않다.
                Semester = _semester,
                Date = templateLog.Date,
                Category = templateLog.Category,
                CourseNo = courseNo,
                SubjectName = templateLog.SubjectName,
                ClubNo = _category == LogCategory.동아리활동 ? (selectedClub?.No ?? _selectedClubNo) : 0,
                ClubName = _category == LogCategory.동아리활동
                    ? (selectedClub?.ClubName ?? string.Empty) : string.Empty,
                Log = templateLog.Log,
                Tag = templateLog.Tag,
                IsImportant = templateLog.IsImportant,
                ActivityName = templateLog.ActivityName,
                Topic = templateLog.Topic,
                Description = templateLog.Description,
                Role = templateLog.Role,
                SkillDeveloped = templateLog.SkillDeveloped,
                StrengthShown = templateLog.StrengthShown,
                ResultOrOutcome = templateLog.ResultOrOutcome
            };

            toSave.Add(log);
        }

        // 한 트랜잭션으로 넣는다 — 하나라도 실패하면 전부 되돌린다. 예전에는 학생마다
        // 따로 넣어서, 열 번째에서 실패하면 앞의 아홉 명만 남았다. 사용자가 다시 저장을
        // 누르면 그 아홉 명에게 같은 기록이 한 벌 더 생겼다.
        try
        {
            using var svc = new StudentLogService();
            await svc.InsertManyAsync(toSave);
            _logs.AddRange(toSave);
            return true;
        }
        catch (Exception ex)
        {
            // 여기서 다시 던지면 호출부가 같은 내용을 한 번 더 띄운다 — 안내는 한 번만 하고
            // 창은 닫지 않아 입력한 내용을 지킨다.
            Debug.WriteLine($"[오류] 일괄 저장 실패: {ex.Message}");
            await ShowErrorAsync("저장 실패",
                $"저장하지 못했습니다. 한 건도 저장되지 않았습니다.\n{ex.Message}\n\n" +
                "창을 닫지 않았으니 다시 저장해 주세요.");
            return false;
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
