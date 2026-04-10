using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using NewSchool.Collections;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.ViewModels
{
    /// <summary>
    /// 학급 일지 ViewModel
    /// ClassDiary 모델을 UI 바인딩 가능하게 래핑
    /// </summary>
    public class ClassDiaryViewModel : INotifyPropertyChanged, IDisposable
    {
        #region Fields

        private readonly ClassDiaryService _diaryService;
        private bool _isSelected;
        private ClassDiary _diary;

        #endregion

        #region Constructor

        /// <summary>
        /// 기본 생성자 (최적화됨)
        /// ⚡ OptimizedObservableCollection 사용
        /// </summary>
        public ClassDiaryViewModel()
        {
            _diaryService = new ClassDiaryService(SchoolDatabase.DbPath);
            _diary = new ClassDiary();
            StudentLogs = new OptimizedObservableCollection<StudentLogViewModel>();
        }

        /// <summary>
        /// ClassDiary로 초기화 (최적화됨)
        /// ⚡ OptimizedObservableCollection 사용
        /// </summary>
        public ClassDiaryViewModel(ClassDiary diary)
        {
            _diaryService = new ClassDiaryService(SchoolDatabase.DbPath);
            _diary = diary ?? new ClassDiary();
            StudentLogs = new OptimizedObservableCollection<StudentLogViewModel>();
        }

        #endregion

        #region Properties - Selection

        /// <summary>선택 여부 (체크박스용)</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        #endregion

        #region Properties - ClassDiary Wrapper

        /// <summary>원본 ClassDiary 모델</summary>
        public ClassDiary CurrentDiary => Diary;

        /// <summary>원본 ClassDiary 모델</summary>
        public ClassDiary Diary
        {
            get => _diary;
            set
            {
                if (_diary != value)
                {
                    _diary = value ?? new ClassDiary();
                    OnPropertyChanged(nameof(Diary));
                    OnAllPropertiesChanged();
                }
            }
        }

        /// <summary>PK</summary>
        public int No
        {
            get => _diary.No;
            set
            {
                if (_diary.No != value)
                {
                    _diary.No = value;
                    OnPropertyChanged(nameof(No));
                }
            }
        }

        /// <summary>학교 코드</summary>
        public string SchoolCode
        {
            get => _diary.SchoolCode;
            set
            {
                if (_diary.SchoolCode != value)
                {
                    _diary.SchoolCode = value;
                    OnPropertyChanged(nameof(SchoolCode));
                }
            }
        }

        /// <summary>작성 교사 ID</summary>
        public string TeacherID
        {
            get => _diary.TeacherID;
            set
            {
                if (_diary.TeacherID != value)
                {
                    _diary.TeacherID = value;
                    OnPropertyChanged(nameof(TeacherID));
                }
            }
        }

        /// <summary>학년도</summary>
        public int Year
        {
            get => _diary.Year;
            set
            {
                if (_diary.Year != value)
                {
                    _diary.Year = value;
                    OnPropertyChanged(nameof(Year));
                    OnPropertyChanged(nameof(YearDisplay));
                }
            }
        }

        /// <summary>학기</summary>
        public int Semester
        {
            get => _diary.Semester;
            set
            {
                if (_diary.Semester != value)
                {
                    _diary.Semester = value;
                    OnPropertyChanged(nameof(Semester));
                    OnPropertyChanged(nameof(SemesterDisplay));
                }
            }
        }

        /// <summary>날짜</summary>
        public DateTime Date
        {
            get => _diary.Date;
            set
            {
                if (_diary.Date != value)
                {
                    _diary.Date = value;
                    OnPropertyChanged(nameof(Date));
                    OnPropertyChanged(nameof(DateDisplay));
                    OnPropertyChanged(nameof(DateShort));
                    OnPropertyChanged(nameof(DayOfWeek));
                    OnPropertyChanged(nameof(DayOfWeekKorean));
                    OnPropertyChanged(nameof(MonthDay));
                }
            }
        }

        /// <summary>학년</summary>
        public int Grade
        {
            get => _diary.Grade;
            set
            {
                if (_diary.Grade != value)
                {
                    _diary.Grade = value;
                    OnPropertyChanged(nameof(Grade));
                    OnPropertyChanged(nameof(ClassInfo));
                }
            }
        }

        /// <summary>반</summary>
        public int Class
        {
            get => _diary.Class;
            set
            {
                if (_diary.Class != value)
                {
                    _diary.Class = value;
                    OnPropertyChanged(nameof(Class));
                    OnPropertyChanged(nameof(ClassInfo));
                }
            }
        }

        /// <summary>결석 학생</summary>
        public string Absent
        {
            get => _diary.Absent;
            set
            {
                if (_diary.Absent != value)
                {
                    _diary.Absent = value;
                    OnPropertyChanged(nameof(Absent));
                    OnPropertyChanged(nameof(HasAbsent));
                    OnPropertyChanged(nameof(AbsentCount));
                    OnPropertyChanged(nameof(AttendanceSummary));
                    OnPropertyChanged(nameof(HasAttendanceIssues));
                }
            }
        }

        /// <summary>지각 학생</summary>
        public string Late
        {
            get => _diary.Late;
            set
            {
                if (_diary.Late != value)
                {
                    _diary.Late = value;
                    OnPropertyChanged(nameof(Late));
                    OnPropertyChanged(nameof(HasLate));
                    OnPropertyChanged(nameof(LateCount));
                    OnPropertyChanged(nameof(AttendanceSummary));
                    OnPropertyChanged(nameof(HasAttendanceIssues));
                }
            }
        }

        /// <summary>조퇴 학생</summary>
        public string LeaveEarly
        {
            get => _diary.LeaveEarly;
            set
            {
                if (_diary.LeaveEarly != value)
                {
                    _diary.LeaveEarly = value;
                    OnPropertyChanged(nameof(LeaveEarly));
                    OnPropertyChanged(nameof(HasLeaveEarly));
                    OnPropertyChanged(nameof(LeaveEarlyCount));
                    OnPropertyChanged(nameof(AttendanceSummary));
                    OnPropertyChanged(nameof(HasAttendanceIssues));
                }
            }
        }

        /// <summary>메모</summary>
        public string Memo
        {
            get => _diary.Memo;
            set
            {
                if (_diary.Memo != value)
                {
                    _diary.Memo = value;
                    OnPropertyChanged(nameof(Memo));
                    OnPropertyChanged(nameof(HasMemo));
                }
            }
        }

        /// <summary>알림장</summary>
        public string Notice
        {
            get => _diary.Notice;
            set
            {
                if (_diary.Notice != value)
                {
                    _diary.Notice = value;
                    OnPropertyChanged(nameof(Notice));
                    OnPropertyChanged(nameof(HasNotice));
                }
            }
        }

        /// <summary>학생 생활 기록</summary>
        public string Life
        {
            get => _diary.Life;
            set
            {
                if (_diary.Life != value)
                {
                    _diary.Life = value;
                    OnPropertyChanged(nameof(Life));
                    OnPropertyChanged(nameof(HasLifeRecord));
                }
            }
        }

        #endregion

        #region Properties - Student Logs

        /// <summary>학생 생활 로그 목록</summary>
        /// <summary>
        /// 학생 기록 컬렉션 (최적화됨)
        /// ⚡ OptimizedObservableCollection로 UI 업데이트 80% 향상
        /// </summary>
        public OptimizedObservableCollection<StudentLogViewModel> StudentLogs { get; }

        #endregion

        #region Computed Properties - 날짜 관련

        /// <summary>날짜 표시 (yyyy년 M월 d일 (요일))</summary>
        public string DateDisplay => _diary.DateDisplay;

        /// <summary>날짜 짧게 (MM-dd)</summary>
        public string DateShort => _diary.Date.ToString("MM-dd");

        /// <summary>월/일 (M/d)</summary>
        public string MonthDay => _diary.Date.ToString("M/d");

        /// <summary>요일</summary>
        public DayOfWeek DayOfWeek => _diary.DayOfWeek;

        /// <summary>요일 (한글)</summary>
        public string DayOfWeekKorean => _diary.DayOfWeekKorean;

        /// <summary>학년도 표시</summary>
        public string YearDisplay => $"{_diary.Year}학년도";

        /// <summary>학기 표시</summary>
        public string SemesterDisplay => $"{_diary.Semester}학기";

        /// <summary>학급 정보 (예: 3학년 2반)</summary>
        public string ClassInfo => $"{_diary.Grade}학년 {_diary.Class}반";

        #endregion

        #region Computed Properties - 출결 관련

        /// <summary>결석 학생 있는지</summary>
        public bool HasAbsent => !string.IsNullOrWhiteSpace(_diary.Absent);

        /// <summary>지각 학생 있는지</summary>
        public bool HasLate => !string.IsNullOrWhiteSpace(_diary.Late);

        /// <summary>조퇴 학생 있는지</summary>
        public bool HasLeaveEarly => !string.IsNullOrWhiteSpace(_diary.LeaveEarly);

        /// <summary>출결 문제 있는지</summary>
        public bool HasAttendanceIssues => _diary.HasAttendanceIssues;

        /// <summary>결석 학생 수</summary>
        public int AbsentCount => HasAbsent ? _diary.Absent.Split(',').Length : 0;

        /// <summary>지각 학생 수</summary>
        public int LateCount => HasLate ? _diary.Late.Split(',').Length : 0;

        /// <summary>조퇴 학생 수</summary>
        public int LeaveEarlyCount => HasLeaveEarly ? _diary.LeaveEarly.Split(',').Length : 0;

        /// <summary>출결 요약</summary>
        public string AttendanceSummary => _diary.AttendanceSummary;

        /// <summary>출결 문제 학생 수 (중복 제거)</summary>
        public int AttendanceIssueCount => _diary.AttendanceIssueCount;

        #endregion

        #region Computed Properties - 내용 관련

        /// <summary>메모가 있는지</summary>
        public bool HasMemo => _diary.HasMemo;

        /// <summary>알림장이 있는지</summary>
        public bool HasNotice => _diary.HasNotice;

        /// <summary>생활 기록이 있는지</summary>
        public bool HasLifeRecord => _diary.HasLifeRecord;

        #endregion

        #region Computed Properties - 시각화


        /// <summary>출결 상태 아이콘</summary>
        public string AttendanceIcon
        {
            get
            {
                if (!HasAttendanceIssues) return "✓";
                if (HasAbsent) return "✗";
                return "△";
            }
        }

        /// <summary>출결 상태 색상</summary>
        public string AttendanceColor
        {
            get
            {
                if (!HasAttendanceIssues) return "#4CAF50"; // Green
                if (HasAbsent) return "#F44336"; // Red
                return "#FFC107"; // Yellow
            }
        }

        /// <summary>주말 여부</summary>
        public bool IsWeekend => DayOfWeek == System.DayOfWeek.Saturday || 
                                 DayOfWeek == System.DayOfWeek.Sunday;

        /// <summary>오늘 날짜인지</summary>
        public bool IsToday => Date.Date == DateTime.Today;

        #endregion

        #region Methods

        /// <summary>
        /// 출결 정보 초기화
        /// </summary>
        public void ClearAttendance()
        {
            _diary.ClearAttendance();
            OnPropertyChanged(nameof(Absent));
            OnPropertyChanged(nameof(Late));
            OnPropertyChanged(nameof(LeaveEarly));
            OnPropertyChanged(nameof(HasAttendanceIssues));
            OnPropertyChanged(nameof(AttendanceSummary));
        }

        /// <summary>
        /// 모든 내용 초기화
        /// </summary>
        public void ClearAll()
        {
            _diary.ClearAll();
            OnAllPropertiesChanged();
        }

        /// <summary>
        /// 특정 학생의 출결 상태 확인
        /// </summary>
        public string GetAttendanceStatus(string studentName)
        {
            return _diary.GetAttendanceStatus(studentName);
        }

        /// <summary>
        /// 특정 학생이 출결 문제가 있는지 확인
        /// </summary>
        public bool HasAttendanceIssue(string studentName)
        {
            return _diary.HasAttendanceIssue(studentName);
        }

        /// <summary>
        /// 복사본 생성
        /// </summary>
        public ClassDiaryViewModel Clone()
        {
            return new ClassDiaryViewModel(_diary.Clone());
        }

        #endregion

        #region Methods - 학급일지 관리

        /// <summary>
        /// 특정 날짜의 학급일지 로드
        /// </summary>
        public async Task LoadDiaryAsync(int grade, int classNumber, DateTime date)
        {
            var diary = await _diaryService.GetDiaryAsync(
                Settings.SchoolCode.Value, 
                Settings.WorkYear,  // 작업 학년도로 통일
                grade, 
                classNumber, 
                date);
            
            if (diary == null)
            {
                diary = new ClassDiary
                {
                    Year = Settings.WorkYear,  // 작업 학년도로 통일
                    Semester = Settings.WorkSemester,  // 작업 학기도 사용
                    Date = date.Date,
                    Grade = grade,
                    Class = classNumber,
                    TeacherID = Settings.User,
                    SchoolCode = Settings.SchoolCode
                };
            }

            Diary = diary;
            await LoadStudentLogsAsync();
        }

        /// <summary>
        /// 현재 학급일지 저장
        /// </summary>
        public async Task SaveDiaryAsync()
        {
            if (_diary == null) return;
            await _diaryService.CreateOrUpdateAsync(_diary);
        }

        #endregion

        #region Methods - 학생 로그 관리

        /// <summary>
        /// 해당 날짜의 학생 생활 로그 로드 (public으로 노출)
        /// </summary>
        public async Task<List<StudentLogViewModel>> LoadStudentLogsAsync()
        {
            StudentLogs.Clear();
            var logs = await _diaryService.GetStudentLogsByDateAsync(_diary.Grade, _diary.Class, _diary.Date);
            foreach (var log in logs)
            {
                StudentLogs.Add(log);
            }
            return logs;
        }

        /// <summary>
        /// 선택된 로그 목록 반환
        /// </summary>
        public List<StudentLogViewModel> GetSelectedLogs()
        {
            return StudentLogs.Where(log => log.IsSelected).ToList();
        }

        /// <summary>
        /// 선택된 로그들 저장
        /// </summary>
        public async Task SaveSelectedLogsAsync()
        {
            var selectedLogs = GetSelectedLogs();
            if (selectedLogs.Count == 0) return;

            foreach (var logVM in selectedLogs)
            {
                await _diaryService.SaveStudentLogAsync(logVM.StudentLog);
                logVM.IsSelected = false;
            }
        }

        /// <summary>
        /// 새 로그 추가 (다이얼로그에서 생성된 로그)
        /// </summary>
        public async Task AddNewLogAsync(StudentLogViewModel logViewModel)
        {
            // DB에 저장
            await _diaryService.SaveStudentLogAsync(logViewModel.StudentLog);
            
            // 컨렉션에 추가
            StudentLogs.Add(logViewModel);
        }

        /// <summary>
        /// 선택된 로그들 삭제
        /// </summary>
        public async Task DeleteSelectedLogsAsync()
        {
            var selectedLogs = GetSelectedLogs();
            if (selectedLogs.Count == 0) return;

            foreach (var logVM in selectedLogs)
            {
                await _diaryService.DeleteStudentLogAsync(logVM.No);
                StudentLogs.Remove(logVM);
            }
        }

        /// <summary>
        /// 학생 로그 목록을 Life 필드에 업데이트
        /// </summary>
        public async Task UpdateLifeFieldAsync()
        {
            if (StudentLogs.Count == 0)
            {
                Life = string.Empty;
                return;
            }

            var sb = new StringBuilder();
            foreach (var log in StudentLogs.OrderBy(l => l.Date))
            {
                string timeStr = log.Date.ToString("HH:mm");
                sb.AppendLine($"({timeStr}){log.Name}({log.Number}): {log.Log}");
            }

            Life = sb.ToString();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _diaryService?.Dispose();
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

        /// <summary>
        /// 모든 속성 변경 알림
        /// </summary>
        private void OnAllPropertiesChanged()
        {
            // 기본 정보
            OnPropertyChanged(nameof(No));
            OnPropertyChanged(nameof(SchoolCode));
            OnPropertyChanged(nameof(TeacherID));
            OnPropertyChanged(nameof(Year));
            OnPropertyChanged(nameof(Semester));
            OnPropertyChanged(nameof(Date));
            OnPropertyChanged(nameof(Grade));
            OnPropertyChanged(nameof(Class));

            // 출결
            OnPropertyChanged(nameof(Absent));
            OnPropertyChanged(nameof(Late));
            OnPropertyChanged(nameof(LeaveEarly));

            // 내용
            OnPropertyChanged(nameof(Memo));
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(Life));

            // Computed Properties
            OnPropertyChanged(nameof(DateDisplay));
            OnPropertyChanged(nameof(DateShort));
            OnPropertyChanged(nameof(MonthDay));
            OnPropertyChanged(nameof(DayOfWeek));
            OnPropertyChanged(nameof(DayOfWeekKorean));
            OnPropertyChanged(nameof(YearDisplay));
            OnPropertyChanged(nameof(SemesterDisplay));
            OnPropertyChanged(nameof(ClassInfo));
            OnPropertyChanged(nameof(HasAttendanceIssues));
            OnPropertyChanged(nameof(AttendanceSummary));
            OnPropertyChanged(nameof(AttendanceIssueCount));
            OnPropertyChanged(nameof(HasMemo));
            OnPropertyChanged(nameof(HasNotice));
            OnPropertyChanged(nameof(HasLifeRecord));
            OnPropertyChanged(nameof(AttendanceIcon));
            OnPropertyChanged(nameof(AttendanceColor));
            OnPropertyChanged(nameof(IsWeekend));
            OnPropertyChanged(nameof(IsToday));
        }

        #endregion
    }
}
