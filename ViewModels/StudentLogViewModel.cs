using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.ViewModels;

/// <summary>
/// 학생 기록 목록 표시용 ViewModel
/// StudentLog + Student 정보 조합
/// LogListViewer 컨트롤에서 사용
/// </summary>
public sealed class StudentLogViewModel : NotifyPropertyChangedBase
{
    #region Fields

    private bool _isSelected;
    private double _contentFontSize = DefaultContentFontSize;
    private bool _isLoading;
    private StudentLog _studentlog;
    private Enrollment? _enrollment;
    private Student? _student;

    #endregion

    #region Constructor

    /// <summary>
    /// 기본 생성자 - 생성 후 InitializeAsync() 호출 필요
    /// </summary>
    public StudentLogViewModel(string studentId)
    {
        _studentlog = new StudentLog() { StudentID = studentId };
    }

    /// <summary>
    /// StudentLog로 초기화 - 생성 후 InitializeAsync() 호출 필요
    /// </summary>
    public StudentLogViewModel(StudentLog log)
    {
        _studentlog = log ?? new StudentLog();
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// 학생 ID로 비동기 생성 (권장)
    /// </summary>
    public static async Task<StudentLogViewModel> CreateAsync(string studentId)
    {
        var vm = new StudentLogViewModel(studentId);
        await vm.InitializeAsync();
        return vm;
    }

    /// <summary>
    /// StudentLog로 비동기 생성 (권장)
    /// </summary>
    public static async Task<StudentLogViewModel> CreateAsync(StudentLog log)
    {
        var vm = new StudentLogViewModel(log);
        await vm.InitializeAsync();
        return vm;
    }

    /// <summary>
    /// 여러 StudentLog 를 한 번에 ViewModel 로 변환한다 (N+1 방지).
    ///
    /// CreateAsync 를 루프로 돌리면 기록 1건마다 학적·기본정보 조회가 2회씩 발생한다.
    /// 특히 한 학생의 기록 목록에서는 매번 "같은 학생"을 다시 읽어 결과가 전부 동일한
    /// 순수 낭비였다(기록 50건 → 쿼리 100회). 여기서는 등장하는 학생 ID 를 모아
    /// 배치 조회 2회로 끝낸다.
    /// </summary>
    public static async Task<List<StudentLogViewModel>> CreateManyAsync(
        IEnumerable<StudentLog> logs)
    {
        var logList = logs?.ToList() ?? new List<StudentLog>();
        var result = new List<StudentLogViewModel>(logList.Count);
        if (logList.Count == 0) return result;

        var studentIds = logList
            .Select(l => l.StudentID)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        var enrollmentsById = new Dictionary<string, Enrollment>();
        var studentsById = new Dictionary<string, Student>();

        try
        {
            using var enrollmentService = new EnrollmentService();
            using var studentService = new StudentService(SchoolDatabase.DbPath);

            var enrollments = await enrollmentService.GetCurrentEnrollmentsAsync(studentIds);
            foreach (var e in enrollments)
                enrollmentsById[e.StudentID] = e;

            var students = await studentService.GetStudentsByIdsAsync(studentIds);
            foreach (var s in students)
                studentsById[s.StudentID] = s;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[StudentLogViewModel] 학생 정보 배치 로드 실패: {ex.Message}");
            // 실패해도 기본값으로 진행 (개별 InitializeAsync 와 동일한 정책)
        }

        foreach (var log in logList)
        {
            var vm = new StudentLogViewModel(log);
            enrollmentsById.TryGetValue(log.StudentID ?? string.Empty, out var enrollment);
            studentsById.TryGetValue(log.StudentID ?? string.Empty, out var student);
            vm.ApplyStudentInfo(enrollment, student);
            result.Add(vm);
        }

        return result;
    }

    /// <summary>
    /// 비동기 초기화.
    ///
    /// <para>서비스는 이 메서드 안에서만 열고 바로 닫는다. 예전에는 생성자가
    /// <c>StudentLogService</c>·<c>EnrollmentService</c>·<c>StudentService</c> 를 필드로 들고 있었는데,
    /// 이 ViewModel 은 기록 <b>한 줄마다</b> 만들어지므로(<c>logs.Select(l => new StudentLogViewModel(l))</c>)
    /// 목록을 한 번 여는 것만으로 SQLite 연결이 기록 수 × 3 개 열렸다. 게다가
    /// <c>_logService</c> 는 어디서도 쓰이지 않았다. 목록 경로는 <see cref="CreateManyAsync"/> 가
    /// 배치 조회로 처리하므로 대부분의 인스턴스는 연결을 하나도 열지 않는다.</para>
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            using var enrollmentService = new EnrollmentService();
            using var studentService = new StudentService(SchoolDatabase.DbPath);

            _enrollment = await enrollmentService.GetCurrentEnrollmentAsync(_studentlog.StudentID);
            _student = await studentService.GetBasicInfoAsync(_studentlog.StudentID);

            // 학생 정보 로드 완료 알림
            OnPropertyChanged(nameof(Grade));
            OnPropertyChanged(nameof(Class));
            OnPropertyChanged(nameof(Number));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(StudentInfo));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentLogViewModel] 학생 정보 로드 실패: {ex.Message}");
            // 실패해도 기본값으로 진행
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 배치 조회로 이미 확보한 학생 정보를 주입한다 (DB 재조회 없음).
    /// </summary>
    private void ApplyStudentInfo(Enrollment? enrollment, Student? student)
    {
        _enrollment = enrollment;
        _student = student;

        OnPropertyChanged(nameof(Grade));
        OnPropertyChanged(nameof(Class));
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StudentInfo));
    }

    #endregion

    #region Properties - 선택 상태

    /// <summary>체크박스 선택 여부</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// 기록 내용 칸의 기본 글자 크기. <b>각 페이지의 글자 크기 슬라이더 기본값(Value)도
    /// 이 값과 같아야 한다</b> — 다르면 슬라이더를 처음 건드리는 순간 글자가 한 번 튄다.
    /// </summary>
    public const double DefaultContentFontSize = 14.0;

    /// <summary>
    /// 기록 내용 칸의 글자 크기. 툴바의 "글자 크기" 슬라이더가 이 값만 바꾼다.
    ///
    /// ⚠ 목록 컨트롤의 <c>FontSize</c> 를 직접 바꾸면 안 된다. 행의 각 칸(학년도·이름·일시…)에
    /// <c>FontSize="12"</c> 가 명시돼 있어 상속값이 먹히지 않고, 크기가 명시되지 않은
    /// <b>헤더 라벨만</b> 커진다 — 실제로 그렇게 동작해서 "기록은 그대로인데 엉뚱한 데가 커진다"는
    /// 문제가 있었다(2026-07-30 수정). 그래서 기록 내용 칸이 이 속성을 직접 바인딩한다.
    /// </summary>
    public double ContentFontSize
    {
        get => _contentFontSize;
        set => SetProperty(ref _contentFontSize, value);
    }

    /// <summary>로딩 중 여부</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    #endregion

    #region Properties - Column Visibility (x:Bind용)

    private Visibility _yearColumnVisibility = Visibility.Visible;
    private Visibility _semesterColumnVisibility = Visibility.Visible;
    private Visibility _categoryColumnVisibility = Visibility.Visible;
    private Visibility _subjectColumnVisibility = Visibility.Visible;
    private Visibility _gradeColumnVisibility = Visibility.Visible;
    private Visibility _classColumnVisibility = Visibility.Visible;
    private Visibility _numberColumnVisibility = Visibility.Visible;
    private Visibility _nameColumnVisibility = Visibility.Visible;

    /// <summary>학년도 컬럼 표시 여부</summary>
    public Visibility YearColumnVisibility
    {
        get => _yearColumnVisibility;
        set => SetProperty(ref _yearColumnVisibility, value);
    }

    /// <summary>학기 컬럼 표시 여부</summary>
    public Visibility SemesterColumnVisibility
    {
        get => _semesterColumnVisibility;
        set => SetProperty(ref _semesterColumnVisibility, value);
    }

    /// <summary>카테고리 컬럼 표시 여부</summary>
    public Visibility CategoryColumnVisibility
    {
        get => _categoryColumnVisibility;
        set => SetProperty(ref _categoryColumnVisibility, value);
    }

    /// <summary>과목 컬럼 표시 여부</summary>
    public Visibility SubjectColumnVisibility
    {
        get => _subjectColumnVisibility;
        set => SetProperty(ref _subjectColumnVisibility, value);
    }

    /// <summary>학년 컬럼 표시 여부</summary>
    public Visibility GradeColumnVisibility
    {
        get => _gradeColumnVisibility;
        set => SetProperty(ref _gradeColumnVisibility, value);
    }

    /// <summary>반 컬럼 표시 여부</summary>
    public Visibility ClassColumnVisibility
    {
        get => _classColumnVisibility;
        set => SetProperty(ref _classColumnVisibility, value);
    }

    /// <summary>번호 컬럼 표시 여부</summary>
    public Visibility NumberColumnVisibility
    {
        get => _numberColumnVisibility;
        set => SetProperty(ref _numberColumnVisibility, value);
    }

    /// <summary>이름 컬럼 표시 여부</summary>
    public Visibility NameColumnVisibility
    {
        get => _nameColumnVisibility;
        set => SetProperty(ref _nameColumnVisibility, value);
    }

    #endregion

    #region Properties - StudentLog 정보

    /// <summary>StudentLog 전체 교체</summary>
    public StudentLog StudentLog
    {
        get => _studentlog;
        set
        {
            if (_studentlog != value)
            {
                _studentlog = value ?? new StudentLog();

                // StudentLog 관련 모든 속성 알림
                OnPropertyChanged(nameof(StudentLog));
                OnPropertyChanged(nameof(No));
                OnPropertyChanged(nameof(StudentID));
                OnPropertyChanged(nameof(TeacherID));
                OnPropertyChanged(nameof(Year));
                OnPropertyChanged(nameof(Semester));
                OnPropertyChanged(nameof(Date));
                OnPropertyChanged(nameof(Category));
                OnPropertyChanged(nameof(CategoryLabel));
                OnPropertyChanged(nameof(CategoryColor));
                OnPropertyChanged(nameof(CourseNo));
                OnPropertyChanged(nameof(SubjectName));
                OnPropertyChanged(nameof(Log));
                OnPropertyChanged(nameof(Tag));
                OnPropertyChanged(nameof(IsImportant));
                OnPropertyChanged(nameof(ActivityName));
                OnPropertyChanged(nameof(Topic));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(Role));
                OnPropertyChanged(nameof(SkillDeveloped));
                OnPropertyChanged(nameof(StrengthShown));
                OnPropertyChanged(nameof(ResultOrOutcome));
                OnPropertyChanged(nameof(DateString));
            }
        }
    }

    /// <summary>StudentLog PK</summary>
    public int No
    {
        get => _studentlog.No;
        set
        {
            if (_studentlog.No != value)
            {
                _studentlog.No = value;
                OnPropertyChanged(nameof(No));
            }
        }
    }

    /// <summary>학생 ID</summary>
    public string StudentID
    {
        get => _studentlog.StudentID;
        set
        {
            if (_studentlog.StudentID != value)
            {
                _studentlog.StudentID = value;
                OnPropertyChanged(nameof(StudentID));
            }
        }
    }

    /// <summary>작성 교사 ID</summary>
    public string TeacherID
    {
        get => _studentlog.TeacherID;
        set
        {
            if (_studentlog.TeacherID != value)
            {
                _studentlog.TeacherID = value;
                OnPropertyChanged(nameof(TeacherID));
            }
        }
    }

    /// <summary>학년도</summary>
    public int Year
    {
        get => _studentlog.Year;
        set
        {
            if (_studentlog.Year != value)
            {
                _studentlog.Year = value;
                OnPropertyChanged(nameof(Year));
            }
        }
    }

    /// <summary>학기</summary>
    public int Semester
    {
        get => _studentlog.Semester;
        set
        {
            if (_studentlog.Semester != value)
            {
                _studentlog.Semester = value;
                OnPropertyChanged(nameof(Semester));
            }
        }
    }

    /// <summary>작성일</summary>
    public DateTimeOffset Date
    {
        get => new DateTimeOffset(_studentlog.Date);
        set
        {
            var localDate = value.LocalDateTime;
            if (_studentlog.Date != localDate)
            {
                _studentlog.Date = localDate;
                OnPropertyChanged(nameof(Date));
                OnPropertyChanged(nameof(DateString));
            }
        }
    }

    /// <summary>카테고리</summary>
    public LogCategory Category
    {
        get => _studentlog.Category;
        set
        {
            if (_studentlog.Category != value)
            {
                _studentlog.Category = value;
                OnPropertyChanged(nameof(Category));
                OnPropertyChanged(nameof(CategoryLabel));
                OnPropertyChanged(nameof(CategoryColor));
            }
        }
    }

    /// <summary>수업 번호 (Course.No)</summary>
    public int CourseNo
    {
        get => _studentlog.CourseNo;
        set
        {
            if (_studentlog.CourseNo != value)
            {
                _studentlog.CourseNo = value;
                OnPropertyChanged(nameof(CourseNo));
            }
        }
    }

    /// <summary>과목명</summary>
    public string SubjectName
    {
        get => _studentlog.SubjectName;
        set
        {
            if (_studentlog.SubjectName != value)
            {
                _studentlog.SubjectName = value;
                OnPropertyChanged(nameof(SubjectName));
            }
        }
    }

    /// <summary>기록 내용</summary>
    public string Log
    {
        get => _studentlog.Log;
        set
        {
            if (_studentlog.Log != value)
            {
                _studentlog.Log = value;
                OnPropertyChanged(nameof(Log));
            }
        }
    }

    /// <summary>태그</summary>
    public string Tag
    {
        get => _studentlog.Tag;
        set
        {
            if (_studentlog.Tag != value)
            {
                _studentlog.Tag = value;
                OnPropertyChanged(nameof(Tag));
            }
        }
    }

    /// <summary>중요 표시</summary>
    public bool IsImportant
    {
        get => _studentlog.IsImportant;
        set
        {
            if (_studentlog.IsImportant != value)
            {
                _studentlog.IsImportant = value;
                OnPropertyChanged(nameof(IsImportant));
            }
        }
    }

    /// <summary>활동명</summary>
    public string ActivityName
    {
        get => _studentlog.ActivityName;
        set
        {
            if (_studentlog.ActivityName != value)
            {
                _studentlog.ActivityName = value;
                OnPropertyChanged(nameof(ActivityName));
            }
        }
    }

    /// <summary>활동 주제</summary>
    public string Topic
    {
        get => _studentlog.Topic;
        set
        {
            if (_studentlog.Topic != value)
            {
                _studentlog.Topic = value;
                OnPropertyChanged(nameof(Topic));
            }
        }
    }

    /// <summary>구체적 활동 내용</summary>
    public string Description
    {
        get => _studentlog.Description;
        set
        {
            if (_studentlog.Description != value)
            {
                _studentlog.Description = value;
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    /// <summary>역할</summary>
    public string Role
    {
        get => _studentlog.Role;
        set
        {
            if (_studentlog.Role != value)
            {
                _studentlog.Role = value;
                OnPropertyChanged(nameof(Role));
            }
        }
    }

    /// <summary>기른 능력</summary>
    public string SkillDeveloped
    {
        get => _studentlog.SkillDeveloped;
        set
        {
            if (_studentlog.SkillDeveloped != value)
            {
                _studentlog.SkillDeveloped = value;
                OnPropertyChanged(nameof(SkillDeveloped));
            }
        }
    }

    /// <summary>장점</summary>
    public string StrengthShown
    {
        get => _studentlog.StrengthShown;
        set
        {
            if (_studentlog.StrengthShown != value)
            {
                _studentlog.StrengthShown = value;
                OnPropertyChanged(nameof(StrengthShown));
            }
        }
    }

    /// <summary>성취 및 결과</summary>
    public string ResultOrOutcome
    {
        get => _studentlog.ResultOrOutcome;
        set
        {
            if (_studentlog.ResultOrOutcome != value)
            {
                _studentlog.ResultOrOutcome = value;
                OnPropertyChanged(nameof(ResultOrOutcome));
            }
        }
    }

    #endregion

    #region Properties - 학생 정보 (조인)

    /// <summary>학년</summary>
    public int Grade
    {
        get => _enrollment?.Grade ?? 0;
        set
        {
            if (_enrollment != null && _enrollment.Grade != value)
            {
                _enrollment.Grade = value;
                OnPropertyChanged(nameof(Grade));
                OnPropertyChanged(nameof(StudentInfo));
            }
        }
    }

    /// <summary>반</summary>
    public int Class
    {
        get => _enrollment?.Class ?? 0;
        set
        {
            if (_enrollment != null && _enrollment.Class != value)
            {
                _enrollment.Class = value;
                OnPropertyChanged(nameof(Class));
                OnPropertyChanged(nameof(StudentInfo));
            }
        }
    }

    /// <summary>번호</summary>
    public int Number
    {
        get => _enrollment?.Number ?? 0;
        set
        {
            if (_enrollment != null && _enrollment.Number != value)
            {
                _enrollment.Number = value;
                OnPropertyChanged(nameof(Number));
                OnPropertyChanged(nameof(StudentInfo));
            }
        }
    }

    /// <summary>이름</summary>
    public string Name
    {
        get => _student?.Name ?? string.Empty;
        set
        {
            if (_student != null && _student.Name != value)
            {
                _student.Name = value;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(StudentInfo));
            }
        }
    }

    #endregion

    #region Computed Properties

    /// <summary>날짜 표시용 (yyyy-MM-dd)</summary>
    public string DateString => Date.ToString("yyyy-MM-dd");

    /// <summary>학생 정보 (예: 1학년 1반 1번 홍길동)</summary>
    public string StudentInfo => _enrollment != null && _student != null
        ? $"{Grade}학년 {Class}반 {Number}번 {Name}"
        : "학생 정보 로딩 중...";

    /// <summary>카테고리 표시용 짧은 텍스트</summary>
    public string CategoryLabel => ToCategoryLabel(Category);

    /// <summary>카테고리별 배경색</summary>
    public string CategoryColor => ToCategoryColor(Category);

    /// <summary>카테고리 → 짧은 라벨 (순수 함수, 테스트 가능)</summary>
    public static string ToCategoryLabel(LogCategory category) => category switch
    {
        LogCategory.교과활동 => "교과",
        LogCategory.동아리활동 => "동아리",
        LogCategory.봉사활동 => "봉사",
        LogCategory.진로활동 => "진로",
        LogCategory.자율활동 => "자율",
        LogCategory.개인별세특 => "세특",
        LogCategory.종합의견 => "행특",
        LogCategory.상담기록 => "상담",
        LogCategory.기타 => "기타",
        _ => "전체"
    };

    /// <summary>카테고리 → 배경색 (순수 함수, 테스트 가능)</summary>
    public static string ToCategoryColor(LogCategory category) => category switch
    {
        LogCategory.교과활동 => "#FF6B9BD1",      // 파란색
        LogCategory.동아리활동 => "#FF9B59B6",    // 보라색
        LogCategory.봉사활동 => "#FF27AE60",      // 녹색
        LogCategory.진로활동 => "#FFFF9800",      // 주황색
        LogCategory.자율활동 => "#FF3498DB",      // 하늘색
        LogCategory.개인별세특 => "#FFE74C3C",    // 빨간색
        LogCategory.종합의견 => "#FF95A5A6",      // 회색
        LogCategory.상담기록 => "#FFF39C12",      // 노란색
        LogCategory.기타 => "#FF7F8C8D",          // 어두운 회색
        _ => "#FFBDC3C7"                          // 밝은 회색
    };

    #endregion

    #region Methods

    /// <summary>
    /// 다이얼로그에서 편집 후 UI 갱신용 — 모든 속성 PropertyChanged 발생
    /// </summary>
    public void RefreshFromLog()
    {
        OnPropertyChanged(nameof(No));
        OnPropertyChanged(nameof(Year));
        OnPropertyChanged(nameof(Semester));
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(DateString));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(CategoryLabel));
        OnPropertyChanged(nameof(CategoryColor));
        OnPropertyChanged(nameof(SubjectName));
        OnPropertyChanged(nameof(Log));
        OnPropertyChanged(nameof(Tag));
        OnPropertyChanged(nameof(IsImportant));
        OnPropertyChanged(nameof(ActivityName));
        OnPropertyChanged(nameof(Topic));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Role));
        OnPropertyChanged(nameof(SkillDeveloped));
        OnPropertyChanged(nameof(StrengthShown));
        OnPropertyChanged(nameof(ResultOrOutcome));
    }

    #endregion

    #region INotifyPropertyChanged




    #endregion
}
