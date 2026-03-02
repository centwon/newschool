using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.ViewModels
{
    /// <summary>
    /// 연간수업계획 페이지 ViewModel
    /// </summary>
    public class AnnualLessonPlanViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly string _dbPath;

        #region Collections

        /// <summary>교사의 Course 목록</summary>
        public ObservableCollection<Course> Courses { get; } = new();

        /// <summary>선택된 Course의 연간계획 목록</summary>
        public ObservableCollection<SubjectYearPlan> YearPlans { get; } = new();

        /// <summary>주차별 시수 목록</summary>
        public ObservableCollection<WeeklyLessonHours> WeeklyHours { get; } = new();

        /// <summary>단원 목록 (CourseSection 기반)</summary>
        public ObservableCollection<CourseSection> Units { get; } = new();

        /// <summary>주차별 단원 배치 (CourseSection 기반)</summary>
        public ObservableCollection<WeeklyUnitPlan> UnitPlans { get; } = new();

        /// <summary>대상 학급 목록 (ClassTimetable에서 추출)</summary>
        public ObservableCollection<TargetClassInfo> TargetClasses { get; } = new();

        #endregion

        #region Properties

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        private string _loadingMessage = string.Empty;
        public string LoadingMessage
        {
            get => _loadingMessage;
            set { _loadingMessage = value; OnPropertyChanged(); }
        }

        private Course? _selectedCourse;
        public Course? SelectedCourse
        {
            get => _selectedCourse;
            set
            {
                if (_selectedCourse != value)
                {
                    _selectedCourse = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedCourse));
                    _ = OnCourseSelectedAsync();
                }
            }
        }

        public bool HasSelectedCourse => _selectedCourse != null;

        private TargetClassInfo? _selectedTargetClass;
        public TargetClassInfo? SelectedTargetClass
        {
            get => _selectedTargetClass;
            set
            {
                if (_selectedTargetClass != value)
                {
                    _selectedTargetClass = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedTargetClass));
                    _ = OnTargetClassSelectedAsync();
                }
            }
        }

        public bool HasSelectedTargetClass => _selectedTargetClass != null;

        private SubjectYearPlan? _currentPlan;
        public SubjectYearPlan? CurrentPlan
        {
            get => _currentPlan;
            set
            {
                _currentPlan = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentPlan));
                OnPropertyChanged(nameof(CanConfirm));
            }
        }

        public bool HasCurrentPlan => _currentPlan != null;

        private int _currentStep = 1;
        public int CurrentStep
        {
            get => _currentStep;
            set { _currentStep = value; OnPropertyChanged(); }
        }

        // 통계
        private int _totalAutoCalculatedHours;
        public int TotalAutoCalculatedHours
        {
            get => _totalAutoCalculatedHours;
            set { _totalAutoCalculatedHours = value; OnPropertyChanged(); }
        }

        private int _totalPlannedHours;
        public int TotalPlannedHours
        {
            get => _totalPlannedHours;
            set { _totalPlannedHours = value; OnPropertyChanged(); }
        }

        private int _totalUnitHours;
        public int TotalUnitHours
        {
            get => _totalUnitHours;
            set { _totalUnitHours = value; OnPropertyChanged(); }
        }

        private int _totalWeeks;
        public int TotalWeeks
        {
            get => _totalWeeks;
            set { _totalWeeks = value; OnPropertyChanged(); }
        }

        public bool CanConfirm => HasCurrentPlan &&
                                  CurrentPlan?.Status == "DRAFT" &&
                                  TotalPlannedHours > 0 &&
                                  Units.Count > 0;

        // 학기 정보
        public int Year { get; set; } = DateTime.Today.Year;
        public int Semester { get; set; } = DateTime.Today.Month <= 7 ? 1 : 2;

        private DateTime _semesterStart;
        public DateTime SemesterStart
        {
            get => _semesterStart;
            set { _semesterStart = value; OnPropertyChanged(); }
        }

        private DateTime _semesterEnd;
        public DateTime SemesterEnd
        {
            get => _semesterEnd;
            set { _semesterEnd = value; OnPropertyChanged(); }
        }

        #endregion

        #region Constructor

        public AnnualLessonPlanViewModel(DispatcherQueue dispatcherQueue, string dbPath)
        {
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));

            // 기본 학기 기간 설정
            SetDefaultSemesterDates();
        }

        private void SetDefaultSemesterDates()
        {
            if (Semester == 1)
            {
                SemesterStart = new DateTime(Year, 3, 2);
                SemesterEnd = new DateTime(Year, 7, 19);
            }
            else
            {
                SemesterStart = new DateTime(Year, 8, 14);
                SemesterEnd = new DateTime(Year, 12, 31);
            }
        }

        #endregion

        #region Load Data

        /// <summary>
        /// 초기 데이터 로드 (Course 목록)
        /// </summary>
        public async Task LoadCoursesAsync(string teacherId)
        {
            IsLoading = true;
            LoadingMessage = "수업 목록을 불러오는 중...";

            try
            {
                using var courseRepo = new CourseRepository(_dbPath);
                var courses = await courseRepo.GetByTeacherAsync(teacherId, Year, Semester);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    Courses.Clear();
                    foreach (var course in courses)
                    {
                        Courses.Add(course);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] Course 로드 오류: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Course 선택 시 대상 학급 목록 로드
        /// </summary>
        private async Task OnCourseSelectedAsync()
        {
            if (SelectedCourse == null) return;

            IsLoading = true;
            LoadingMessage = "학급 정보를 불러오는 중...";

            try
            {
                // ClassTimetable에서 해당 과목을 가르치는 학급 목록 추출
                using var timetableRepo = new ClassTimetableRepository(_dbPath);
                var timetables = await timetableRepo.GetByGradeAsync(
                    SelectedCourse.SchoolCode,
                    SelectedCourse.Year,
                    SelectedCourse.Semester,
                    SelectedCourse.Grade);

                // 해당 과목의 학급들 추출
                var classGroups = timetables
                    .Where(t => t.SubjectName == SelectedCourse.Subject)
                    .GroupBy(t => new { t.Grade, t.Class })
                    .Select(g => new TargetClassInfo
                    {
                        Grade = g.Key.Grade,
                        Class = g.Key.Class,
                        WeeklyHours = g.Count(),
                        TimetableInfo = string.Join(", ", g.Select(t => $"{t.DayName}{t.Period}"))
                    })
                    .OrderBy(c => c.Grade)
                    .ThenBy(c => c.Class)
                    .ToList();

                _dispatcherQueue.TryEnqueue(() =>
                {
                    TargetClasses.Clear();

                    // 학년 전체 옵션 추가
                    TargetClasses.Add(new TargetClassInfo
                    {
                        Grade = SelectedCourse.Grade,
                        Class = null,
                        WeeklyHours = SelectedCourse.Unit,
                        TimetableInfo = "학년 전체"
                    });

                    foreach (var classInfo in classGroups)
                    {
                        TargetClasses.Add(classInfo);
                    }
                });

                // CourseSection 로드
                await LoadCourseSectionsAsync(SelectedCourse.No);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] 학급 목록 로드 오류: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// CourseSection 로드
        /// </summary>
        private async Task LoadCourseSectionsAsync(int courseNo)
        {
            try
            {
                using var sectionRepo = new CourseSectionRepository(_dbPath);
                var sections = await sectionRepo.GetByCourseAsync(courseNo);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    Units.Clear();
                    foreach (var section in sections)
                    {
                        Units.Add(section);
                    }
                    UpdateStatistics();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] CourseSection 로드 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 대상 학급 선택 시 연간계획 로드 또는 생성
        /// </summary>
        private async Task OnTargetClassSelectedAsync()
        {
            if (SelectedCourse == null || SelectedTargetClass == null) return;

            IsLoading = true;
            LoadingMessage = "연간계획을 불러오는 중...";

            try
            {
                using var planRepo = new SubjectYearPlanRepository(_dbPath);

                // 기존 계획 조회
                var existingPlan = await planRepo.GetByCourseAsync(
                    SelectedCourse.No,
                    SelectedTargetClass.Grade,
                    SelectedTargetClass.Class);

                if (existingPlan != null)
                {
                    CurrentPlan = existingPlan;
                    await LoadPlanDetailsAsync(existingPlan.No);
                }
                else
                {
                    // 새 계획 생성
                    var newPlan = new SubjectYearPlan
                    {
                        CourseNo = SelectedCourse.No,
                        TargetGrade = SelectedTargetClass.Grade,
                        TargetClass = SelectedTargetClass.Class,
                        Year = Year,
                        Semester = Semester,
                        WeeklyHours = SelectedCourse.Unit,
                        Status = "DRAFT"
                    };

                    int planNo = await planRepo.CreateAsync(newPlan);
                    newPlan.No = planNo;
                    CurrentPlan = newPlan;

                    // 주차별 시수 자동 계산
                    await CalculateWeeklyHoursAsync();
                }

                CurrentStep = 3; // Step 3: 주차별 시수 관리
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] 연간계획 로드/생성 오류: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 연간계획 상세 데이터 로드
        /// </summary>
        private async Task LoadPlanDetailsAsync(int yearPlanNo)
        {
            try
            {
                // 주차별 시수 로드
                using var hoursRepo = new WeeklyLessonHoursRepository(_dbPath);
                var hours = await hoursRepo.GetByYearPlanAsync(yearPlanNo);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    WeeklyHours.Clear();
                    foreach (var h in hours)
                    {
                        WeeklyHours.Add(h);
                    }
                    UpdateStatistics();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] 계획 상세 로드 오류: {ex.Message}");
            }
        }

        #endregion

        #region Calculate Weekly Hours

        /// <summary>
        /// 주차별 시수 자동 계산
        /// </summary>
        public async Task CalculateWeeklyHoursAsync()
        {
            if (SelectedCourse == null || CurrentPlan == null) return;

            IsLoading = true;
            LoadingMessage = "주차별 시수를 계산하는 중...";

            try
            {
                using var timetableRepo = new ClassTimetableRepository(_dbPath);
                using var scheduleRepo = new SchoolScheduleRepository(_dbPath);
                using var courseRepo = new CourseRepository(_dbPath);
                using var hoursRepo = new WeeklyLessonHoursRepository(_dbPath);

                var calculator = new WeeklyHoursCalculator(timetableRepo, scheduleRepo, courseRepo);

                var calculatedHours = await calculator.CalculateAsync(
                    SelectedCourse.No,
                    CurrentPlan.No,
                    CurrentPlan.TargetGrade,
                    CurrentPlan.TargetClass,
                    SemesterStart,
                    SemesterEnd);

                // 기존 데이터 삭제 후 새로 저장
                await hoursRepo.DeleteByYearPlanAsync(CurrentPlan.No);
                await hoursRepo.CreateBatchAsync(calculatedHours);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    WeeklyHours.Clear();
                    foreach (var h in calculatedHours)
                    {
                        WeeklyHours.Add(h);
                    }
                    UpdateStatistics();
                });

                // 총 시수 업데이트
                using var planRepo = new SubjectYearPlanRepository(_dbPath);
                int totalHours = calculatedHours.Sum(h => h.PlannedHours);
                await planRepo.UpdateTotalPlannedHoursAsync(CurrentPlan.No, totalHours);
                CurrentPlan.TotalPlannedHours = totalHours;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] 시수 계산 오류: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Generate Unit Plan (SchedulingEngine 기반)

        /// <summary>
        /// 단원 자동 배치 (SchedulingEngine 사용)
        /// </summary>
        public async Task GenerateUnitPlanAsync()
        {
            if (SelectedCourse == null || Units.Count == 0) return;

            IsLoading = true;
            LoadingMessage = "단원을 배치하는 중...";

            try
            {
                // SchedulingEngine은 AnnualLessonPlanPage.xaml.cs에서 직접 사용
                // ViewModel에서는 결과를 표시하는 역할만 수행
                Debug.WriteLine("[AnnualLessonPlanVM] GenerateUnitPlanAsync - SchedulingEngine은 Page에서 직접 호출");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] 단원 배치 오류: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Update Weekly Hours

        /// <summary>
        /// 특정 주 시수 수정
        /// </summary>
        public async Task UpdateWeekHoursAsync(WeeklyLessonHours week, int newHours, string? notes)
        {
            try
            {
                using var hoursRepo = new WeeklyLessonHoursRepository(_dbPath);
                using var planRepo = new SubjectYearPlanRepository(_dbPath);

                var helper = new WeeklyHoursHelper(hoursRepo, planRepo);
                await helper.UpdateWeekHoursAsync(week.No, newHours, notes);

                week.PlannedHours = newHours;
                week.IsModified = 1;
                if (notes != null) week.Notes = notes;

                _dispatcherQueue.TryEnqueue(() =>
                {
                    UpdateStatistics();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] 시수 수정 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 시수 일괄 추가
        /// </summary>
        public async Task AddHoursEvenlyAsync(int additionalHours, int? fromWeek = null)
        {
            if (CurrentPlan == null) return;

            try
            {
                using var hoursRepo = new WeeklyLessonHoursRepository(_dbPath);
                using var planRepo = new SubjectYearPlanRepository(_dbPath);

                var helper = new WeeklyHoursHelper(hoursRepo, planRepo);
                await helper.AddHoursEvenlyAsync(CurrentPlan.No, additionalHours, fromWeek);

                // 새로고침
                await LoadPlanDetailsAsync(CurrentPlan.No);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] 시수 일괄 추가 오류: {ex.Message}");
            }
        }

        #endregion

        #region Confirm Plan

        /// <summary>
        /// 계획 확정
        /// </summary>
        public async Task ConfirmPlanAsync()
        {
            if (CurrentPlan == null) return;

            try
            {
                using var planRepo = new SubjectYearPlanRepository(_dbPath);
                await planRepo.UpdateStatusAsync(CurrentPlan.No, "CONFIRMED");

                CurrentPlan.Status = "CONFIRMED";
                OnPropertyChanged(nameof(CurrentPlan));
                OnPropertyChanged(nameof(CanConfirm));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanVM] 계획 확정 오류: {ex.Message}");
            }
        }

        #endregion

        #region Statistics

        private void UpdateStatistics()
        {
            TotalWeeks = WeeklyHours.Count;
            TotalAutoCalculatedHours = WeeklyHours.Sum(w => w.AutoCalculatedHours);
            TotalPlannedHours = WeeklyHours.Sum(w => w.PlannedHours);
            TotalUnitHours = Units.Sum(u => u.EstimatedHours);
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// 대상 학급 정보
    /// </summary>
    public class TargetClassInfo
    {
        public int Grade { get; set; }
        public int? Class { get; set; }
        public int WeeklyHours { get; set; }
        public string TimetableInfo { get; set; } = string.Empty;

        public string Display => Class.HasValue
            ? $"{Grade}학년 {Class}반 ({WeeklyHours}시간/주)"
            : $"{Grade}학년 전체 ({WeeklyHours}시간/주)";

        public string DetailDisplay => Class.HasValue
            ? $"{Grade}-{Class} ({TimetableInfo})"
            : "학년 전체";
    }
}
