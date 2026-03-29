using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.ViewModels
{
    /// <summary>
    /// 학생 기록 목록 표시용 ViewModel
    /// StudentLog + Student 정보 조합
    /// LogListViewer 컨트롤에서 사용
    /// </summary>
    public class StudentLogViewModel : INotifyPropertyChanged
    {
        #region Fields

        private readonly StudentLogService _logService;
        private readonly EnrollmentService _enrollmentService;
        private readonly StudentService _studentService;

        private bool _isSelected;
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
            _logService = new StudentLogService();
            _enrollmentService = new EnrollmentService();
            _studentService = new StudentService(SchoolDatabase.DbPath);
            _studentlog = new StudentLog() { StudentID = studentId };
        }

        /// <summary>
        /// StudentLog로 초기화 - 생성 후 InitializeAsync() 호출 필요
        /// </summary>
        public StudentLogViewModel(StudentLog log)
        {
            _logService = new StudentLogService();
            _enrollmentService = new EnrollmentService();
            _studentService = new StudentService(SchoolDatabase.DbPath);
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
        /// 비동기 초기화
        /// </summary>
        public async Task InitializeAsync()
        {
            IsLoading = true;
            try
            {
                _enrollment = await _enrollmentService.GetCurrentEnrollmentAsync(_studentlog.StudentID);
                _student = await _studentService.GetBasicInfoAsync(_studentlog.StudentID);

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

        #endregion

        #region Properties - 선택 상태

        /// <summary>체크박스 선택 여부</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>로딩 중 여부</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
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
                    //OnPropertyChanged(nameof(LogByteCount));
                    //OnPropertyChanged(nameof(LogCharCount));
                    //OnPropertyChanged(nameof(LogByteInfo));
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
                    //OnPropertyChanged(nameof(LogByteCount));
                    //OnPropertyChanged(nameof(LogCharCount));
                    //OnPropertyChanged(nameof(LogByteInfo));
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

        ///// <summary>NEIS 바이트 수 (한글 3바이트, 영문/숫자 1바이트)</summary>
        //public int LogByteCount => CalculateNeisByte(Log);

        ///// <summary>글자 수</summary>
        //public int LogCharCount => Log?.Length ?? 0;

        ///// <summary>바이트 정보 표시용</summary>
        //public string LogByteInfo => $"{LogByteCount} Byte / {LogCharCount} 자";

        /// <summary>날짜 표시용 (yyyy-MM-dd)</summary>
        public string DateString => Date.ToString("yyyy-MM-dd");

        /// <summary>학생 정보 (예: 1학년 1반 1번 홍길동)</summary>
        public string StudentInfo => _enrollment != null && _student != null
            ? $"{Grade}학년 {Class}반 {Number}번 {Name}"
            : "학생 정보 로딩 중...";

        /// <summary>카테고리 표시용 짧은 텍스트</summary>
        public string CategoryLabel => Category switch
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

        /// <summary>카테고리별 배경색</summary>
        public string CategoryColor => Category switch
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

        /// <summary>
        /// NEIS 바이트 계산 (한글 3바이트, 영문/숫자/기호 1바이트)
        /// </summary>
        private int CalculateNeisByte(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int byteCount = 0;
            foreach (char c in text)
            {
                // 한글 범위: AC00-D7A3 (가-힣)
                if (c >= 0xAC00 && c <= 0xD7A3)
                {
                    byteCount += 3;
                }
                // 한자 및 기타 유니코드 문자 (2바이트 이상)
                else if (c >= 0x3000)
                {
                    byteCount += 3;
                }
                // ASCII 범위 (영문, 숫자, 기호)
                else
                {
                    byteCount += 1;
                }
            }
            return byteCount;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
