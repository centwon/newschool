using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NewSchool.Pages;

public sealed partial class AnnualLessonPlanPage : Page
{
    private AnnualLessonPlanViewModel _viewModel = null!;
    private List<Course> _courses = [];
    private Course? _selectedCourse;
    private List<string> _classColumns = [];
    private List<WeeklyClassHoursRow> _weeklyRows = [];
    private List<SchoolSchedule> _schoolSchedules = [];
    private List<Lesson> _courseSchedules = [];

    // 단원 관리
    private readonly ObservableCollection<CourseSection> _courseSections = [];
    private List<CourseSection>? _pendingImportSections;

    public AnnualLessonPlanPage()
    {
        this.InitializeComponent();
        this.Loaded += AnnualLessonPlanPage_Loaded;
    }

    private async void AnnualLessonPlanPage_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel = new AnnualLessonPlanViewModel(
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(),
            SchoolDatabase.DbPath)
        {
            Year = Settings.WorkYear.Value,
            Semester = Settings.WorkSemester.Value
        };

        UpdateSemesterDisplay();
        await LoadSchoolSchedulesAsync();
        await LoadCoursesAsync();
    }

    private async Task LoadSchoolSchedulesAsync()
    {
        ShowLoading("NEIS 데이터포털에서 학사일정을 불러오는 중...");

        try
        {
            // 필수 설정 값 확인
            string schoolCode = Settings.SchoolCode.Value;
            string provinceCode = Settings.ProvinceCode.Value;
            string apiKey = Settings.NeisApiKey.Value;
            int workYear = Settings.WorkYear.Value;
            int workSemester = Settings.WorkSemester.Value;

            Debug.WriteLine($"[AnnualLessonPlanPage] NEIS API 호출 시작");
            Debug.WriteLine($"  - 학교코드: '{schoolCode}'");
            Debug.WriteLine($"  - 시도코드: '{provinceCode}'");
            Debug.WriteLine($"  - API키: '{(string.IsNullOrEmpty(apiKey) ? "(없음)" : apiKey.Substring(0, Math.Min(8, apiKey.Length)) + "...")}'");
            Debug.WriteLine($"  - 학년도: {workYear}");
            Debug.WriteLine($"  - 학기: {workSemester}");

            // 필수 설정 검증
            if (string.IsNullOrWhiteSpace(schoolCode))
            {
                Debug.WriteLine($"[AnnualLessonPlanPage] 학교코드가 설정되지 않았습니다.");
                _schoolSchedules = [];
                HoursInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                HoursInfoBar.Title = "학교 설정 필요";
                HoursInfoBar.Message = "학교코드가 설정되지 않았습니다. 설정 > 학교 정보에서 학교를 먼저 설정해주세요.";
                HoursInfoBar.IsOpen = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(provinceCode))
            {
                Debug.WriteLine($"[AnnualLessonPlanPage] 시도코드가 설정되지 않았습니다.");
                _schoolSchedules = [];
                HoursInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                HoursInfoBar.Title = "학교 설정 필요";
                HoursInfoBar.Message = "시도교육청 코드가 설정되지 않았습니다. 설정 > 학교 정보에서 학교를 먼저 설정해주세요.";
                HoursInfoBar.IsOpen = true;
                return;
            }

            if (workYear == 0)
            {
                Debug.WriteLine($"[AnnualLessonPlanPage] 학년도가 설정되지 않았습니다.");
                _schoolSchedules = [];
                HoursInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                HoursInfoBar.Title = "학년도 설정 필요";
                HoursInfoBar.Message = "학년도가 설정되지 않았습니다. 설정에서 학년도를 먼저 설정해주세요.";
                HoursInfoBar.IsOpen = true;
                return;
            }

            using var service = new SchoolScheduleService(SchoolDatabase.DbPath);

            var (semesterStart, semesterEnd) = GetSemesterDateRange(workYear, workSemester);
            Debug.WriteLine($"  - 기간: {semesterStart:yyyy-MM-dd} ~ {semesterEnd:yyyy-MM-dd}");

            var result = await service.DownloadFromNeisAsync(
                schoolCode,
                provinceCode,
                workYear,
                semesterStart,
                semesterEnd);

            if (result.Success)
            {
                _schoolSchedules = result.Schedules;
                Debug.WriteLine($"[AnnualLessonPlanPage] NEIS 학사일정 로드 완료: {_schoolSchedules.Count}개");
                
                // 성공 시 InfoBar 숨기기
                HoursInfoBar.IsOpen = false;
            }
            else
            {
                Debug.WriteLine($"[AnnualLessonPlanPage] NEIS API 실패: {result.Message}");
                _schoolSchedules = [];
                
                // 실패 시 경고 표시
                HoursInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                HoursInfoBar.Title = "학사일정 로드 실패";
                HoursInfoBar.Message = result.Message;
                HoursInfoBar.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 학사일정 로드 오류: {ex.Message}");
            _schoolSchedules = [];
            
            // 오류 표시
            HoursInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
            HoursInfoBar.Title = "학사일정 로드 오류";
            HoursInfoBar.Message = $"NEIS API 호출 중 오류가 발생했습니다: {ex.Message}";
            HoursInfoBar.IsOpen = true;
        }
        finally
        {
            HideLoading();
        }
    }

    private void UpdateSemesterDisplay()
    {
        CmbYear.Visibility = Visibility.Collapsed;
        CmbSemester.Visibility = Visibility.Collapsed;
        TxtSubtitle.Text = $"{Settings.WorkYear.Value}학년도 {Settings.WorkSemester.Value}학기";
    }

    /// <summary>
    /// NEIS 학사일정 새로고침 버튼 클릭
    /// </summary>
    private async void OnRefreshScheduleClick(object sender, RoutedEventArgs e)
    {
        await LoadSchoolSchedulesAsync();
        
        // 선택된 수업이 있으면 시수 테이블 재생성
        if (_selectedCourse != null)
        {
            await BuildWeeklyClassTableAsync(_selectedCourse);
        }
    }

    private async Task LoadCoursesAsync()
    {
        ShowLoading("수업 목록을 불러오는 중...");

        try
        {
            using var courseService = new CourseService();
            _courses = await courseService.GetMyCoursesAsync();

            CmbCourse.ItemsSource = _courses;

            Debug.WriteLine($"[AnnualLessonPlanPage] 수업 로드 완료: {_courses.Count}개");

            if (_courses.Count == 0)
            {
                TxtSubtitle.Text = "등록된 수업이 없습니다. 수업 관리에서 수업을 먼저 등록해주세요.";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 수업 로드 실패: {ex.Message}");
            TxtSubtitle.Text = $"수업 목록 로드 실패: {ex.Message}";
        }
        finally
        {
            HideLoading();
        }
    }

    private void ShowLoading(string message)
    {
        TxtLoading.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void HideLoading()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateStatisticsDisplay()
    {
        if (_viewModel == null) return;

        // 주차 정보
        TxtTotalWeeks.Text = $"{_weeklyRows.Count}주";

        // 총 수업일
        int totalClassDays = _weeklyRows.Sum(r => r.ClassDaysCount);
        TxtTotalClassDays.Text = $"{totalClassDays}일";

        // 총 시수 (수동 편집값 우선, 없으면 시간표 기반 자동 계산)
        int totalAutoHours = _weeklyRows.Sum(r =>
            _classColumns.Sum(c => r.GetEffectiveHours(c)));
        TxtAutoHours.Text = $"{totalAutoHours}";

        // 단원 시수 (단원별 예상 차시 합계)
        int totalPlannedHours = _courseSections.Sum(s => s.EstimatedHours);
        TxtPlannedHours.Text = $"{totalPlannedHours}";

        // 단원 요약
        TxtUnitSummary.Text = $"{_courseSections.Count}개 단원 / {totalPlannedHours}시간";

        // 시수 비교 경고
        if (totalPlannedHours > 0 && totalAutoHours > 0)
        {
            int diff = totalAutoHours - totalPlannedHours;
            if (diff > 0)
            {
                HoursInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
                HoursInfoBar.Title = "여유 시수";
                HoursInfoBar.Message = $"총 시수({totalAutoHours})가 단원 시수({totalPlannedHours})보다 {diff}시간 많습니다. 복습/평가 시간으로 활용할 수 있습니다.";
                HoursInfoBar.IsOpen = true;
            }
            else if (diff < 0)
            {
                HoursInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                HoursInfoBar.Title = "시수 부족";
                HoursInfoBar.Message = $"단원 시수({totalPlannedHours})가 총 시수({totalAutoHours})보다 {-diff}시간 많습니다. 단원 차시를 조정하거나 일부 단원을 삭제해주세요.";
                HoursInfoBar.IsOpen = true;
            }
            else
            {
                HoursInfoBar.IsOpen = false;
            }
        }
        else
        {
            HoursInfoBar.IsOpen = false;
        }
    }

    /// <summary>
    /// 시간표 정보 UI 업데이트
    /// </summary>
    private void UpdateTimetableInfo()
    {
        if (_courseSchedules.Count == 0)
        {
            TxtTimetableInfo.Text = "⚠️ 시간표 배치가 없습니다.\n수업 관리에서 '시간표 배치'를 먼저 설정하세요.";
            TimetableItemsControl.ItemsSource = null;
            return;
        }

        // 시간표 요약 생성
        var timetableItems = new List<string>();
        var byRoom = _courseSchedules.GroupBy(l => l.Room);

        foreach (var roomGroup in byRoom)
        {
            var byDay = roomGroup.GroupBy(l => l.DayOfWeek).OrderBy(g => g.Key);
            var dayInfos = new List<string>();

            foreach (var dayGroup in byDay)
            {
                string dayName = dayGroup.Key switch
                {
                    1 => "월",
                    2 => "화",
                    3 => "수",
                    4 => "목",
                    5 => "금",
                    _ => ""
                };
                int periods = dayGroup.Count();
                dayInfos.Add($"{dayName}{periods}");
            }

            timetableItems.Add($"{roomGroup.Key}: {string.Join(", ", dayInfos)}");
        }

        // 총 주당 시수
        int weeklyHours = _courseSchedules.Count;
        TxtTimetableInfo.Text = $"주당 {weeklyHours}시간 ({_classColumns.Count}개 학급)";
        TimetableItemsControl.ItemsSource = timetableItems;
    }

    /// <summary>
    /// 학사일정 정보 UI 업데이트 (NEIS 데이터포털 기반)
    /// </summary>
    private void UpdateScheduleInfo(int grade)
    {
        if (_schoolSchedules.Count == 0)
        {
            TxtScheduleInfo.Text = "⚠️ NEIS 학사일정을 불러올 수 없습니다.\n설정에서 API 키와 학교 정보를 확인해주세요.";
            return;
        }

        var (semesterStart, semesterEnd) = GetSemesterDateRange(
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value);

        // 학기 내 공휴일/휴업일 카운트
        int holidayCount = 0;
        int vacationDays = 0;
        var importantEvents = new List<string>();

        foreach (var schedule in _schoolSchedules)
        {
            if (schedule.AA_YMD < semesterStart || schedule.AA_YMD > semesterEnd)
                continue;

            if (schedule.IsHoliday)
            {
                holidayCount++;
                if (!string.IsNullOrWhiteSpace(schedule.EVENT_NM) && 
                    (schedule.EVENT_NM.Contains("개교") || schedule.EVENT_NM.Contains("종업") || 
                     schedule.EVENT_NM.Contains("시험") || schedule.EVENT_NM.Contains("고사") ||
                     schedule.EVENT_NM.Contains("중간") || schedule.EVENT_NM.Contains("기말")))
                {
                    importantEvents.Add($"{schedule.AA_YMD:M/d} {schedule.EVENT_NM}");
                }
            }

            bool isGradeEvent = grade switch
            {
                1 => schedule.ONE_GRADE_EVENT_YN,
                2 => schedule.TW_GRADE_EVENT_YN,
                3 => schedule.THREE_GRADE_EVENT_YN,
                4 => schedule.FR_GRADE_EVENT_YN,
                5 => schedule.FIV_GRADE_EVENT_YN,
                6 => schedule.SIX_GRADE_EVENT_YN,
                _ => false
            };

            if (isGradeEvent && schedule.EVENT_NM.Contains("방학"))
            {
                vacationDays++;
            }
        }

        var infoBuilder = new StringBuilder();
        infoBuilder.AppendLine($"📱 NEIS 데이터포털 ({_schoolSchedules.Count}개 일정)");
        infoBuilder.AppendLine($"공휴일: {holidayCount}일, 방학: {vacationDays}일");

        if (importantEvents.Count > 0)
        {
            infoBuilder.AppendLine($"주요: {string.Join(", ", importantEvents.Take(3))}");
        }

        TxtScheduleInfo.Text = infoBuilder.ToString().TrimEnd();
    }

    /// <summary>
    /// 시수 관리 탭 UI 전체 업데이트
    /// </summary>
    private void UpdateHoursUI()
    {
        if (_selectedCourse == null)
        {
            TxtTimetableInfo.Text = "수업을 선택하세요";
            TimetableItemsControl.ItemsSource = null;
            TxtScheduleInfo.Text = "학사일정을 불러오는 중...";
            HoursEmptyState.Visibility = Visibility.Visible;
            WeeklyHoursScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateTimetableInfo();
        UpdateScheduleInfo(_selectedCourse.Grade);

        // 테이블 표시 여부
        if (_weeklyRows.Count > 0)
        {
            HoursEmptyState.Visibility = Visibility.Collapsed;
            WeeklyHoursScrollViewer.Visibility = Visibility.Visible;
        }
        else
        {
            HoursEmptyState.Visibility = Visibility.Visible;
            WeeklyHoursScrollViewer.Visibility = Visibility.Collapsed;

            if (_classColumns.Count == 0)
            {
                TxtHoursEmptyMessage.Text = "학급 목록이 없습니다";
            }
            else if (_courseSchedules.Count == 0)
            {
                TxtHoursEmptyMessage.Text = "시간표 배치가 없습니다";
            }
            else
            {
                TxtHoursEmptyMessage.Text = "수업일이 없습니다";
            }
        }

        UpdateStatisticsDisplay();
    }

    #region Course Selection & Data Loading

    private async void OnCourseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        if (CmbCourse.SelectedItem is Course course)
        {
            ShowLoading("시간표와 시수를 계산하는 중...");
            _selectedCourse = course;
            _viewModel.SelectedCourse = course;

            await LoadCourseSchedulesAsync(course.No);
            await BuildWeeklyClassTableAsync(course);
            await LoadCourseSectionsAsync(course.No);

            // 탭3 기간 설정 초기화
            var (semStart, semEnd) = GetSemesterDateRange(Settings.WorkYear.Value, Settings.WorkSemester.Value);
            DpStartDate.Date = new DateTimeOffset(semStart);
            DpEndDate.Date = new DateTimeOffset(semEnd);

            // 탭3 기존 배치 결과 로드 (첫 번째 학급 기준)
            if (_classColumns.Count > 0)
            {
                await LoadPlacementResultsAsync(course.No, _classColumns[0]);
            }

            HideLoading();
            TxtSubtitle.Text = $"{Settings.WorkYear.Value}학년도 {Settings.WorkSemester.Value}학기 - {course.Subject}";
        }
    }

    private async Task LoadCourseSchedulesAsync(int courseNo)
    {
        try
        {
            using var repo = new LessonRepository(SchoolDatabase.DbPath);
            var lessons = await repo.GetByCourseAsync(courseNo);

            _courseSchedules = lessons
                .Where(l => l.IsRecurring && !l.IsCancelled)
                .OrderBy(l => l.Room)
                .ThenBy(l => l.DayOfWeek)
                .ThenBy(l => l.Period)
                .ToList();

            Debug.WriteLine($"[AnnualLessonPlanPage] 시간표 로드 완료: {_courseSchedules.Count}개");
            
            // 디버그: 시간표 상세 정보
            foreach (var lesson in _courseSchedules)
            {
                Debug.WriteLine($"  - Room={lesson.Room}, Day={lesson.DayOfWeek}, Period={lesson.Period}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 시간표 로드 오류: {ex.Message}");
            _courseSchedules = [];
        }
    }

    private async Task LoadCourseSectionsAsync(int courseNo)
    {
        try
        {
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            var sections = await repo.GetByCourseAsync(courseNo);

            _courseSections.Clear();
            foreach (var section in sections)
            {
                Debug.WriteLine($"[LoadCourseSections] 로드: No={section.No}, SortOrder={section.SortOrder}, Name={section.SectionName}");
                _courseSections.Add(section);
            }

            SectionListView.ItemsSource = _courseSections;
            SectionSummaryListView.ItemsSource = _courseSections;
            UpdateSectionUI();

            Debug.WriteLine($"[AnnualLessonPlanPage] 단원 로드 완료: {_courseSections.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 단원 로드 실패: {ex.Message}");
            _courseSections.Clear();
            SectionListView.ItemsSource = null;
            SectionSummaryListView.ItemsSource = null;
        }
    }

    #endregion

    #region Weekly Hours Table

    private async Task BuildWeeklyClassTableAsync(Course course)
    {
        try
        {
            _classColumns = course.RoomList;

            // 학급/강의실 정보 없음
            if (_classColumns.Count == 0)
            {
                ClearTable();
                Debug.WriteLine($"[AnnualLessonPlanPage] Course.Rooms 값이 비어있음: '{course.Rooms}'");
                UpdateHoursUI();
                return;
            }

            // 시간표 데이터 없음
            if (_courseSchedules.Count == 0)
            {
                ClearTable();
                UpdateHoursUI();
                return;
            }

            Debug.WriteLine($"[AnnualLessonPlanPage] 학급 목록: {string.Join(", ", _classColumns)}");

            // 탭3 학급 ComboBox 업데이트
            CmbRoom.ItemsSource = _classColumns;
            if (_classColumns.Count > 0 && CmbRoom.SelectedIndex < 0)
            {
                CmbRoom.SelectedIndex = 0;
            }

            var (semesterStart, semesterEnd) = GetSemesterDateRange(
                Settings.WorkYear.Value,
                Settings.WorkSemester.Value);

            _weeklyRows = await Task.Run(() =>
                BuildWeeklyRowsWithTimetable(course, semesterStart, semesterEnd));

            if (_weeklyRows.Count == 0)
            {
                ClearTable();
                UpdateHoursUI();
                return;
            }

            BuildTableHeader();
            BuildTableBody();
            UpdateHoursUI();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 테이블 생성 실패: {ex.Message}");
            UpdateHoursUI();
        }
    }

    private (DateTime Start, DateTime End) GetSemesterDateRange(int year, int semester)
    {
        if (semester == 1)
        {
            return (new DateTime(year, 3, 1), new DateTime(year, 7, 31));
        }
        else
        {
            return (new DateTime(year, 8, 1), new DateTime(year + 1, 2, DateTime.DaysInMonth(year + 1, 2)));
        }
    }

    private List<WeeklyClassHoursRow> BuildWeeklyRowsWithTimetable(
        Course course,
        DateTime semesterStart,
        DateTime semesterEnd)
    {
        var rows = new List<WeeklyClassHoursRow>();

        var timetableByRoom = _courseSchedules
            .GroupBy(l => l.Room)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(l => l.DayOfWeek)
                      .ToDictionary(
                          dg => dg.Key,
                          dg => dg.Count()));

        var weekStart = semesterStart;
        while (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            weekStart = weekStart.AddDays(1);
        }

        int weekNumber = 1;
        while (weekStart <= semesterEnd)
        {
            var weekEnd = weekStart.AddDays(4);

            if (weekEnd > semesterEnd)
            {
                weekEnd = semesterEnd;
            }

            var classDays = GetClassDaysInWeek(weekStart, weekEnd, course.Grade);
            var weekEvents = GetWeekEvents(weekStart, weekEnd, course.Grade);

            if (classDays.Count > 0)
            {
                var row = new WeeklyClassHoursRow
                {
                    Week = weekNumber,
                    WeekDisplay = $"{weekNumber}주",
                    DateRange = $"{weekStart:MM/dd}~{weekEnd:MM/dd}",
                    StartDate = weekStart,
                    EndDate = weekEnd,
                    ClassDaysCount = classDays.Count,
                    ClassDays = classDays,
                    Remark = weekEvents
                };

                foreach (var room in _classColumns)
                {
                    int roomHours = 0;

                    if (timetableByRoom.TryGetValue(room, out var dayHours))
                    {
                        foreach (var classDay in classDays)
                        {
                            int lessonDayOfWeek = (int)classDay.DayOfWeek;
                            if (lessonDayOfWeek == 0) lessonDayOfWeek = 7;

                            if (dayHours.TryGetValue(lessonDayOfWeek, out var hoursOnDay))
                            {
                                roomHours += hoursOnDay;
                            }
                        }
                    }

                    row.ClassHours[room] = roomHours;
                }

                rows.Add(row);
                weekNumber++;
            }

            weekStart = weekStart.AddDays(7);
        }

        return rows;
    }

    private List<DateTime> GetClassDaysInWeek(DateTime weekStart, DateTime weekEnd, int grade)
    {
        var classDays = new List<DateTime>();

        for (var date = weekStart; date <= weekEnd; date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                continue;

            if (IsHoliday(date, grade))
                continue;

            classDays.Add(date);
        }

        return classDays;
    }

    private bool IsHoliday(DateTime date, int grade)
    {
        var schedules = _schoolSchedules.Where(s => s.AA_YMD.Date == date.Date).ToList();

        foreach (var schedule in schedules)
        {
            if (schedule.IsHoliday)
                return true;

            bool isGradeEvent = grade switch
            {
                1 => schedule.ONE_GRADE_EVENT_YN,
                2 => schedule.TW_GRADE_EVENT_YN,
                3 => schedule.THREE_GRADE_EVENT_YN,
                4 => schedule.FR_GRADE_EVENT_YN,
                5 => schedule.FIV_GRADE_EVENT_YN,
                6 => schedule.SIX_GRADE_EVENT_YN,
                _ => false
            };

            if (isGradeEvent && schedule.EVENT_NM.Contains("방학"))
                return true;
        }

        return false;
    }

    private string GetWeekEvents(DateTime weekStart, DateTime weekEnd, int grade)
    {
        var events = new List<string>();

        var weekSchedules = _schoolSchedules
            .Where(s => s.AA_YMD.Date >= weekStart.Date && s.AA_YMD.Date <= weekEnd.Date)
            .OrderBy(s => s.AA_YMD)
            .ToList();

        foreach (var schedule in weekSchedules)
        {
            if (string.IsNullOrWhiteSpace(schedule.EVENT_NM))
                continue;

            bool isGradeEvent = grade switch
            {
                1 => schedule.ONE_GRADE_EVENT_YN,
                2 => schedule.TW_GRADE_EVENT_YN,
                3 => schedule.THREE_GRADE_EVENT_YN,
                4 => schedule.FR_GRADE_EVENT_YN,
                5 => schedule.FIV_GRADE_EVENT_YN,
                6 => schedule.SIX_GRADE_EVENT_YN,
                _ => true
            };

            bool isAllGradeEvent = !schedule.ONE_GRADE_EVENT_YN && !schedule.TW_GRADE_EVENT_YN &&
                                   !schedule.THREE_GRADE_EVENT_YN && !schedule.FR_GRADE_EVENT_YN &&
                                   !schedule.FIV_GRADE_EVENT_YN && !schedule.SIX_GRADE_EVENT_YN;

            if (isAllGradeEvent || isGradeEvent)
            {
                string eventText = $"{schedule.AA_YMD:M/d} {schedule.EVENT_NM}";
                if (!events.Contains(eventText))
                {
                    events.Add(eventText);
                }
            }
        }

        return string.Join(", ", events);
    }

    private void ClearTable()
    {
        TableHeader.Children.Clear();
        TableHeader.ColumnDefinitions.Clear();
        _weeklyRows.Clear();
        WeeklyHoursTable.Children.Clear();
    }

    private void BuildTableHeader()
    {
        TableHeader.Children.Clear();
        TableHeader.ColumnDefinitions.Clear();

        int columnIndex = 0;

        TableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var weekHeader = new TextBlock
        {
            Text = "주차",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(weekHeader, columnIndex++);
        TableHeader.Children.Add(weekHeader);

        TableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        var dateHeader = new TextBlock
        {
            Text = "기간",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dateHeader, columnIndex++);
        TableHeader.Children.Add(dateHeader);

        TableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
        var daysHeader = new TextBlock
        {
            Text = "일수",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(daysHeader, columnIndex++);
        TableHeader.Children.Add(daysHeader);

        for (int i = 0; i < _classColumns.Count; i++)
        {
            TableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            var classHeader = new TextBlock
            {
                Text = _classColumns[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(classHeader, columnIndex++);
            TableHeader.Children.Add(classHeader);
        }

        TableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var totalHeader = new TextBlock
        {
            Text = "합계",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(totalHeader, columnIndex++);
        TableHeader.Children.Add(totalHeader);

        TableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
        var remarkHeader = new TextBlock
        {
            Text = "비고 (학사일정)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(remarkHeader, columnIndex);
        TableHeader.Children.Add(remarkHeader);
    }

    private void BuildTableBody()
    {
        var tableItems = new List<UIElement>();

        foreach (var row in _weeklyRows)
        {
            var rowBorder = new Border
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 4, 8, 4)
            };

            var rowGrid = new Grid();
            int columnIndex = 0;

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            var weekText = new TextBlock
            {
                Text = row.WeekDisplay,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(weekText, columnIndex++);
            rowGrid.Children.Add(weekText);

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            var dateText = new TextBlock
            {
                Text = row.DateRange,
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dateText, columnIndex++);
            rowGrid.Children.Add(dateText);

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            var daysText = new TextBlock
            {
                Text = row.ClassDaysCount.ToString(),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(daysText, columnIndex++);
            rowGrid.Children.Add(daysText);

            int totalHours = 0;

            for (int i = 0; i < _classColumns.Count; i++)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                int hours = row.GetEffectiveHours(_classColumns[i]);
                totalHours += hours;

                // 수동 편집 여부 확인
                bool isManual = row.ManualHours.TryGetValue(_classColumns[i], out var manual) && manual.HasValue;

                // 편집 가능한 셀 생성
                var cellBorder = new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(2),
                    Tag = new HoursCellData { Row = row, ClassName = _classColumns[i] }
                };
                ToolTipService.SetToolTip(cellBorder, "클릭하여 편집");

                var hoursText = new TextBlock
                {
                    Text = hours.ToString(),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 수동 편집된 값은 강조 표시
                if (isManual)
                {
                    hoursText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                    hoursText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }

                cellBorder.Child = hoursText;

                // 마우스 오버 효과
                cellBorder.PointerEntered += (s, e) =>
                {
                    cellBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
                };
                cellBorder.PointerExited += (s, e) =>
                {
                    cellBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                };

                // 클릭 시 편집 모드 진입
                int classIndex = i;
                cellBorder.Tapped += (s, e) => OnHoursCellTapped(cellBorder, row, _classColumns[classIndex], rowGrid, columnIndex - 1 + classIndex + 1);

                Grid.SetColumn(cellBorder, columnIndex++);
                rowGrid.Children.Add(cellBorder);
            }

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            var totalText = new TextBlock
            {
                Text = totalHours.ToString(),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(totalText, columnIndex++);
            rowGrid.Children.Add(totalText);

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
            var remarkText = new TextBlock
            {
                Text = row.Remark,
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            };
            ToolTipService.SetToolTip(remarkText, row.Remark);
            Grid.SetColumn(remarkText, columnIndex);
            rowGrid.Children.Add(remarkText);

            rowBorder.Child = rowGrid;
            tableItems.Add(rowBorder);
        }

        // 합계 행
        var summaryBorder = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            Padding = new Thickness(8, 6, 8, 6)
        };

        var summaryGrid = new Grid();
        int summaryColIndex = 0;

        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var summaryLabel = new TextBlock
        {
            Text = "합계",
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(summaryLabel, summaryColIndex++);
        summaryGrid.Children.Add(summaryLabel);

        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        summaryColIndex++;

        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
        int totalDays = _weeklyRows.Sum(r => r.ClassDaysCount);
        var totalDaysText = new TextBlock
        {
            Text = totalDays.ToString(),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(totalDaysText, summaryColIndex++);
        summaryGrid.Children.Add(totalDaysText);

        int grandTotal = 0;
        for (int i = 0; i < _classColumns.Count; i++)
        {
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            int classTotal = _weeklyRows.Sum(r => r.GetEffectiveHours(_classColumns[i]));
            grandTotal += classTotal;

            var classTotalText = new TextBlock
            {
                Text = classTotal.ToString(),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(classTotalText, summaryColIndex++);
            summaryGrid.Children.Add(classTotalText);
        }

        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var grandTotalText = new TextBlock
        {
            Text = grandTotal.ToString(),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(grandTotalText, summaryColIndex++);
        summaryGrid.Children.Add(grandTotalText);

        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });

        summaryBorder.Child = summaryGrid;
        tableItems.Add(summaryBorder);

        WeeklyHoursTable.Children.Clear();
        foreach (var item in tableItems)
        {
            WeeklyHoursTable.Children.Add(item);
        }
    }

    /// <summary>
    /// 시수 셀 클릭 시 편집 모드 진입
    /// </summary>
    private void OnHoursCellTapped(Border cellBorder, WeeklyClassHoursRow row, string className, Grid rowGrid, int colIndex)
    {
        // 이미 NumberBox가 있으면 무시
        if (cellBorder.Child is NumberBox) return;

        int currentHours = row.GetEffectiveHours(className);

        var numberBox = new NumberBox
        {
            Value = currentHours,
            Minimum = 0,
            Maximum = 20,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            Width = 44,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 원래 TextBlock 저장
        var originalTextBlock = cellBorder.Child as TextBlock;

        // NumberBox로 교체
        cellBorder.Child = numberBox;
        numberBox.Focus(FocusState.Programmatic);

        // 포커스 잃으면 저장 및 복원
        numberBox.LostFocus += (s, e) =>
        {
            int newValue = (int)numberBox.Value;

            // 수동 값 저장
            row.ManualHours[className] = newValue;

            // TextBlock으로 복원
            var newTextBlock = new TextBlock
            {
                Text = newValue.ToString(),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 수동 편집된 값은 다른 색상으로 표시
            if (row.ManualHours.ContainsKey(className) && row.ManualHours[className].HasValue)
            {
                newTextBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                newTextBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }

            cellBorder.Child = newTextBlock;

            // 합계 업데이트
            UpdateRowTotal(rowGrid, row);
            UpdateSummaryRow();
            UpdateStatisticsDisplay();
        };

        // Enter 키 입력 시 저장
        numberBox.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Escape)
            {
                // 포커스 이동하여 LostFocus 트리거
                WeeklyHoursTable.Focus(FocusState.Programmatic);
            }
        };
    }

    /// <summary>
    /// 행의 합계 업데이트
    /// </summary>
    private void UpdateRowTotal(Grid rowGrid, WeeklyClassHoursRow row)
    {
        // 합계 열 인덱스 계산: 주차(0) + 기간(1) + 일수(2) + 학급들 + 합계
        int totalColIndex = 3 + _classColumns.Count;

        int total = 0;
        foreach (var className in _classColumns)
        {
            total += row.GetEffectiveHours(className);
        }

        // 합계 TextBlock 찾아서 업데이트
        foreach (var child in rowGrid.Children)
        {
            if (child is FrameworkElement fe && Grid.GetColumn(fe) == totalColIndex && child is TextBlock totalText)
            {
                totalText.Text = total.ToString();
                break;
            }
        }
    }

    /// <summary>
    /// 합계 행 업데이트
    /// </summary>
    private void UpdateSummaryRow()
    {
        // 테이블 마지막 요소가 합계 행
        if (WeeklyHoursTable.Children.Count == 0) return;

        var summaryBorder = WeeklyHoursTable.Children[^1] as Border;
        if (summaryBorder?.Child is not Grid summaryGrid) return;

        int grandTotal = 0;
        int summaryColIndex = 3; // 주차, 기간, 일수 건너뛰기

        for (int i = 0; i < _classColumns.Count; i++)
        {
            int classTotal = _weeklyRows.Sum(r => r.GetEffectiveHours(_classColumns[i]));
            grandTotal += classTotal;

            // 합계 행의 학급별 합계 업데이트
            foreach (var child in summaryGrid.Children)
            {
                if (child is FrameworkElement fe && Grid.GetColumn(fe) == summaryColIndex + i && child is TextBlock classTotalText)
                {
                    classTotalText.Text = classTotal.ToString();
                    break;
                }
            }
        }

        // 전체 합계 업데이트
        int grandTotalColIndex = summaryColIndex + _classColumns.Count;
        foreach (var child in summaryGrid.Children)
        {
            if (child is FrameworkElement fe && Grid.GetColumn(fe) == grandTotalColIndex && child is TextBlock grandTotalText)
            {
                grandTotalText.Text = grandTotal.ToString();
                break;
            }
        }
    }

    #endregion

    #region Section Management (단원 관리 - CourseSectionDialog 통합)

    #region CSV Import/Export

    private async void OnImportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null)
        {
            ShowSectionError("먼저 수업을 선택해주세요.");
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".csv");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var content = await FileIO.ReadTextAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            var sections = ParseCsv(content);

            if (sections.Count == 0)
            {
                ShowSectionError("CSV 파일에서 유효한 단원 데이터를 찾을 수 없습니다.\n형식을 확인해주세요.");
                return;
            }

            if (_courseSections.Count > 0)
            {
                _pendingImportSections = sections;
                TxtImportConfirm.Text = $"기존 {_courseSections.Count}개의 단원이 삭제되고 새로 {sections.Count}개의 단원이 추가됩니다.\n계속하시겠습니까?";
                ImportConfirmFlyout.ShowAt(sender as FrameworkElement ?? BtnImportCsv);
                return;
            }

            await ApplyImportSectionsAsync(sections);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] CSV 가져오기 실패: {ex.Message}");
            ShowSectionError($"CSV 파일 가져오기 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private void OnImportCancelClick(object sender, RoutedEventArgs e)
    {
        _pendingImportSections = null;
        ImportConfirmFlyout.Hide();
    }

    private async void OnImportConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_pendingImportSections != null)
        {
            await ApplyImportSectionsAsync(_pendingImportSections);
            _pendingImportSections = null;
        }
        ImportConfirmFlyout.Hide();
    }

    private async Task ApplyImportSectionsAsync(List<CourseSection> sections)
    {
        if (_selectedCourse == null) return;

        try
        {
            // CSV 가져오기는 전체 대체 동작이므로 BulkCreateAsync 사용
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            await repo.BulkCreateAsync(_selectedCourse.No, sections);

            _courseSections.Clear();
            foreach (var section in sections)
            {
                _courseSections.Add(section);
            }

            UpdateSectionUI();
            Debug.WriteLine($"[AnnualLessonPlanPage] CSV 가져오기 완료: {sections.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] CSV 가져오기 저장 실패: {ex.Message}");
            ShowSectionError($"CSV 가져오기 저장 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private async void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_courseSections.Count == 0)
        {
            ShowSectionError("내보낼 단원이 없습니다.");
            return;
        }

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = $"{_selectedCourse?.Subject ?? "단원"}_단원구조";
            picker.FileTypeChoices.Add("CSV 파일", new List<string> { ".csv" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var csv = GenerateCsv();
            await FileIO.WriteTextAsync(file, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

            Debug.WriteLine($"[AnnualLessonPlanPage] CSV 내보내기 완료: {file.Path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] CSV 내보내기 실패: {ex.Message}");
            ShowSectionError($"CSV 파일 내보내기 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private async void OnDownloadTemplateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = "단원구조_템플릿";
            picker.FileTypeChoices.Add("CSV 파일", new List<string> { ".csv" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var template = GenerateCsvTemplate();
            await FileIO.WriteTextAsync(file, template, Windows.Storage.Streams.UnicodeEncoding.Utf8);

            Debug.WriteLine($"[AnnualLessonPlanPage] 템플릿 다운로드 완료: {file.Path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 템플릿 다운로드 실패: {ex.Message}");
            ShowSectionError($"템플릿 다운로드 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private List<CourseSection> ParseCsv(string content)
    {
        var sections = new List<CourseSection>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseCsvLine(line);
            if (fields.Length < 6) continue;

            try
            {
                // 예상차시 파싱 디버깅
                var hoursField = fields.Length > 8 ? fields[8].Trim() : "";
                var hoursParsed = int.TryParse(hoursField, out var hours);
                Debug.WriteLine($"[CSV] 라인 {i}: 예상차시 필드='{hoursField}', 파싱성공={hoursParsed}, 값={hours}");

                var section = new CourseSection
                {
                    UnitNo = int.TryParse(fields[0], out var unitNo) ? unitNo : 0,
                    UnitName = fields[1].Trim(),
                    ChapterNo = int.TryParse(fields[2], out var chapterNo) ? chapterNo : 0,
                    ChapterName = fields[3].Trim(),
                    SectionNo = int.TryParse(fields[4], out var sectionNo) ? sectionNo : 0,
                    SectionName = fields[5].Trim(),
                    StartPage = fields.Length > 6 && int.TryParse(fields[6], out var startPage) ? startPage : 0,
                    EndPage = fields.Length > 7 && int.TryParse(fields[7], out var endPage) ? endPage : 0,
                    EstimatedHours = hoursParsed && hours > 0 ? hours : 1,
                    SectionType = fields.Length > 9 && !string.IsNullOrWhiteSpace(fields[9]) ? fields[9].Trim() : "Normal",
                    LearningObjective = fields.Length > 10 ? fields[10].Trim() : string.Empty,
                    LessonPlan = fields.Length > 11 ? fields[11].Trim() : string.Empty,
                    MaterialPath = fields.Length > 12 ? fields[12].Trim() : string.Empty,
                    MaterialUrl = fields.Length > 13 ? fields[13].Trim() : string.Empty,
                    Memo = fields.Length > 14 ? fields[14].Trim() : string.Empty
                };

                if (fields.Length > 15 && !string.IsNullOrWhiteSpace(fields[15]))
                {
                    if (DateTime.TryParse(fields[15].Trim(), out var pinnedDate))
                    {
                        section.IsPinned = true;
                        section.PinnedDate = pinnedDate;
                    }
                }

                if (section.SectionType == "Exam" || section.SectionType == "Assessment")
                {
                    section.IsPinned = true;
                }

                if (section.UnitNo > 0 && !string.IsNullOrWhiteSpace(section.SectionName))
                {
                    sections.Add(section);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanPage] CSV 라인 파싱 실패 (라인 {i + 1}): {ex.Message}");
            }
        }

        return sections;
    }

    private string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());

        return fields.ToArray();
    }

    private string GenerateCsv()
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("대단원번호,대단원명,중단원번호,중단원명,소단원번호,소단원명,시작페이지,끝페이지,예상차시,유형,학습목표,수업계획,자료파일,자료링크,메모,고정날짜");

        foreach (var section in _courseSections)
        {
            sb.AppendLine(string.Join(",",
                section.UnitNo,
                EscapeCsv(section.UnitName),
                section.ChapterNo,
                EscapeCsv(section.ChapterName),
                section.SectionNo,
                EscapeCsv(section.SectionName),
                section.StartPage,
                section.EndPage,
                section.EstimatedHours,
                section.SectionType,
                EscapeCsv(section.LearningObjective),
                EscapeCsv(section.LessonPlan),
                EscapeCsv(section.MaterialPath),
                EscapeCsv(section.MaterialUrl),
                EscapeCsv(section.Memo),
                section.PinnedDate?.ToString("yyyy-MM-dd") ?? ""
            ));
        }

        return sb.ToString();
    }

    private string GenerateCsvTemplate()
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("대단원번호,대단원명,중단원번호,중단원명,소단원번호,소단원명,시작페이지,끝페이지,예상차시,유형,학습목표,수업계획,자료파일,자료링크,메모,고정날짜");
        sb.AppendLine("1,수와 연산,1,자연수의 혼합 계산,1,덧셈과 뺄셈의 혼합 계산,8,11,2,Normal,덧셈과 뺄셈의 혼합 계산 순서를 안다,개념 도입 → 연습,,,,,");
        sb.AppendLine("1,수와 연산,1,자연수의 혼합 계산,2,곱셈과 나눗셈의 혼합 계산,12,15,2,Normal,곱셈과 나눗셈의 혼합 계산을 할 수 있다,모둠 활동,,,,,");
        sb.AppendLine("0,1학기 중간고사,0,지필평가,1,1단원 평가,0,0,1,Exam,1단원 학습 내용 평가,시험,,,,,2026-04-15");
        sb.AppendLine("0,수행평가,0,포트폴리오,1,수학 탐구 보고서,0,0,2,Assessment,탐구 주제 선정 및 보고서 작성,발표,,,,,2026-05-20");
        return sb.ToString();
    }

    private string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    #endregion

    #region Section Dialog

    /// <summary>
    /// 소단원 추가 버튼 클릭 - CourseSectionDialog 표시
    /// </summary>
    private async void OnAddSectionClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null)
        {
            ShowSectionError("먼저 수업을 선택해주세요.");
            return;
        }

        // 새 단원 추가 (section = null)
        var dialog = new Dialogs.CourseSectionDialog(_selectedCourse, null)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // 다이얼로그에서 저장됨 - 데이터 다시 로드
            await LoadCourseSectionsAsync(_selectedCourse.No);
            Debug.WriteLine("[AnnualLessonPlanPage] 단원 추가 완료 - 데이터 새로고침");
        }
    }

    /// <summary>
    /// 리스트 아이템 클릭 - 해당 단원 편집
    /// </summary>
    private async void OnSectionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CourseSection section || _selectedCourse == null) return;

        // 해당 단원 편집
        var dialog = new Dialogs.CourseSectionDialog(_selectedCourse, section)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            // 다이얼로그에서 저장됨 - 데이터 다시 로드
            await LoadCourseSectionsAsync(_selectedCourse.No);
            Debug.WriteLine("[AnnualLessonPlanPage] 단원 편집 완료 - 데이터 새로고침");
        }
    }

    private async void OnDeleteSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is CourseSection section)
        {
            // 삭제 확인
            if (await MessageBox.ShowConfirmAsync(
                $"\"{section.SectionName}\" 단원을 삭제하시겠습니까?",
                "삭제 확인", "삭제", "취소"))
            {
                try
                {
                    // DB에서 개별 삭제 (연관 데이터 보존)
                    using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
                    await repo.DeleteAsync(section.No);

                    _courseSections.Remove(section);
                    UpdateSectionUI();
                    Debug.WriteLine($"[AnnualLessonPlanPage] 소단원 삭제: {section.FullPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AnnualLessonPlanPage] 소단원 삭제 실패: {ex.Message}");
                    ShowSectionError($"삭제 중 오류가 발생했습니다.\n{ex.Message}");
                }
            }
        }
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        if (_courseSections.Count == 0)
        {
            ShowSectionError("삭제할 단원이 없습니다.");
            return;
        }

        TxtClearConfirm.Text = $"{_courseSections.Count}개의 단원을 모두 삭제하시겠습니까?";
    }

    private void OnClearAllCancelClick(object sender, RoutedEventArgs e)
    {
        ClearAllFlyout.Hide();
    }

    private async void OnClearAllConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null) return;

        try
        {
            using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
            await repo.DeleteByCourseAsync(_selectedCourse.No);

            _courseSections.Clear();
            UpdateSectionUI();
            ClearAllFlyout.Hide();

            Debug.WriteLine("[AnnualLessonPlanPage] 전체 삭제 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 전체 삭제 실패: {ex.Message}");
            ShowSectionError($"전체 삭제 중 오류가 발생했습니다.\n{ex.Message}");
            ClearAllFlyout.Hide();
        }
    }

    #endregion

    #region Section Helpers

    private void UpdateSectionUI()
    {
        bool hasSections = _courseSections.Count > 0;

        SectionEmptyState.Visibility = hasSections ? Visibility.Collapsed : Visibility.Visible;
        SectionListView.Visibility = hasSections ? Visibility.Visible : Visibility.Collapsed;
        SectionListHeader.Visibility = hasSections ? Visibility.Visible : Visibility.Collapsed;

        if (hasSections)
        {
            int unitCount = _courseSections.Select(s => s.UnitNo).Distinct().Count();
            int chapterCount = _courseSections.Select(s => (s.UnitNo, s.ChapterNo)).Distinct().Count();
            int totalHours = _courseSections.Sum(s => s.EstimatedHours);
            int examCount = _courseSections.Count(s => s.SectionType == "Exam");
            int assessmentCount = _courseSections.Count(s => s.SectionType == "Assessment");

            var stats = $"대단원 {unitCount}개 · 중단원 {chapterCount}개 · 소단원 {_courseSections.Count}개 · 총 {totalHours}차시";
            if (examCount > 0 || assessmentCount > 0)
            {
                stats += $" | 지필 {examCount}개 · 수행 {assessmentCount}개";
            }
            TxtSectionStatistics.Text = stats;
            TxtUnitSummary.Text = $"{unitCount}개 대단원 · {_courseSections.Count}개 소단원 · {totalHours}차시";

            UpdateTypeStatistics();
        }
        else
        {
            TxtSectionStatistics.Text = "";
            TxtUnitSummary.Text = "0개 단원 / 0시간";
        }

        // 시수 관리 탭 통계도 업데이트
        UpdateStatisticsDisplay();
    }

    private void UpdateTypeStatistics()
    {
        if (_courseSections == null || _courseSections.Count == 0)
        {
            TxtNormalCount.Text = "일반: 0";
            TxtExamCount.Text = "지필: 0";
            TxtAssessmentCount.Text = "수행: 0";
            TxtPinnedCount.Text = "📌 고정: 0";
            return;
        }

        int normalCount = _courseSections.Count(s => s.SectionType == "Normal");
        int examCount = _courseSections.Count(s => s.SectionType == "Exam");
        int assessmentCount = _courseSections.Count(s => s.SectionType == "Assessment");
        int pinnedCount = _courseSections.Count(s => s.IsPinned);

        TxtNormalCount.Text = $"일반: {normalCount}";
        TxtExamCount.Text = $"지필: {examCount}";
        TxtAssessmentCount.Text = $"수행: {assessmentCount}";
        TxtPinnedCount.Text = $"📌 고정: {pinnedCount}";
    }

    // SaveSectionsAsync 제거됨 (v2 리팩토링)
    // 기존: BulkCreateAsync로 전체 삭제+재생성 → ScheduleUnitMap/LessonProgress 소실 문제
    // 변경: 개별 작업별로 적절한 Repository 메서드 직접 호출
    //   - 삭제: repo.DeleteAsync(no)
    //   - 전체 삭제: repo.DeleteByCourseAsync(courseNo)
    //   - CSV 가져오기: repo.BulkCreateAsync(courseNo, sections)

    private void ShowSectionError(string message)
    {
        SectionErrorInfoBar.Message = message;
        SectionErrorInfoBar.IsOpen = true;
    }



    /// <summary>
    /// 단원 드래그 완료 - SortOrder 업데이트
    /// </summary>
        private async void OnSectionDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (_selectedCourse == null) return;

            try
            {
                Debug.WriteLine($"[AnnualLessonPlanPage] 드래그 완료: {_courseSections.Count}개 단원 정렬");

                // SortOrder 값 갱신 (ObservableCollection은 이미 드래그 순서로 정렬됨)
                for (int i = 0; i < _courseSections.Count; i++)
                {
                    _courseSections[i].SortOrder = i + 1;
                    Debug.WriteLine($"  [{i}] No={_courseSections[i].No}, SortOrder={i + 1}, Name={_courseSections[i].SectionName}");
                }

                // 트랜잭션 일괄 업데이트
                using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
                await repo.BulkUpdateSortOrderAsync(_courseSections.ToList());

                // DB에서 재로드하여 순서와 연번을 확실하게 반영
                await LoadCourseSectionsAsync(_selectedCourse.No);

                Debug.WriteLine($"[AnnualLessonPlanPage] SortOrder 일괄 업데이트 + 재로드 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AnnualLessonPlanPage] 단원 순서 변경 실패: {ex.Message}");
                ShowSectionError($"순서 변경 중 오류가 발생했습니다.\n{ex.Message}");
            }
        }

    /// <summary>
    /// 컨테이너 내용 변경 시 - 연번 업데이트 (1번부터 시작)
    /// </summary>
    private void OnSectionContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
            return;

        // ItemIndex를 사용하여 1부터 시작하는 연번 표시
        int displayIndex = args.ItemIndex + 1;

        // 첫 번째 TextBlock(연번)을 찾아서 업데이트
        if (args.ItemContainer?.ContentTemplateRoot is Grid grid)
        {
            // Grid의 첫 번째 자식 = 연번 TextBlock
            if (grid.Children.Count > 0 && grid.Children[0] is TextBlock indexText)
            {
                indexText.Text = displayIndex.ToString();
            }
        }
    }

    #endregion

    #endregion

    #region Navigation & Plan

    private void OnGoToHoursTabClick(object sender, RoutedEventArgs e)
    {
        MainPivot.SelectedIndex = 1;
    }

    private async void OnRoomSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedCourse == null || CmbRoom.SelectedItem == null) return;

        string room = CmbRoom.SelectedItem.ToString()!;
        await LoadPlacementResultsAsync(_selectedCourse.No, room);
    }

    private async void OnGeneratePlanClick(object sender, RoutedEventArgs e)
    {
        // 1. 유효성 검사
        if (_selectedCourse == null)
        {
            ShowSectionError("먼저 수업을 선택해주세요.");
            return;
        }

        if (_courseSections.Count == 0)
        {
            ShowSectionError("먼저 단원을 추가해주세요. (탭1: 단원 관리)");
            return;
        }

        if (CmbRoom.SelectedItem == null)
        {
            ShowSectionError("학급을 선택해주세요.");
            return;
        }

        if (DpStartDate.Date == null || DpEndDate.Date == null)
        {
            ShowSectionError("배치 기간을 설정해주세요.");
            return;
        }

        string room = CmbRoom.SelectedItem.ToString()!;
        DateTime startDate = DpStartDate.Date!.Value.DateTime;
        DateTime endDate = DpEndDate.Date!.Value.DateTime;

        // 2. 미리보기
        ShowLoading("배치 미리보기 중...");

        try
        {
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var lessonRepo = new LessonRepository(SchoolDatabase.DbPath);
            using var schoolScheduleRepo = new SchoolScheduleRepository(SchoolDatabase.DbPath);

            var engine = new SchedulingEngine(
                scheduleRepo, mapRepo, sectionRepo, lessonRepo, schoolScheduleRepo);

            // 미리보기로 배치 가능 여부 확인
            var preview = await engine.PreviewScheduleAsync(
                _selectedCourse.No, room, startDate, endDate);

            // 3. 확인 다이얼로그
            var confirmContent = $"""
                학급: {room}
                기간: {startDate:yyyy.MM.dd} ~ {endDate:yyyy.MM.dd}
                
                단원: {preview.TotalSections}개 (고정 {preview.PinnedSections} + 일반 {preview.NormalSections})
                필요 시수: {preview.TotalRequiredHours}차시
                가용 슬롯: {preview.TotalAvailableSlots}개
                {(preview.CanComplete ? "✅ 배치 가능" : $"⚠️ 시수 부족: {-preview.ExcessSlots}차시")}
                
                기존 배치 데이터를 삭제하고 새로 배치합니다.
                계속하시겠습니까?
                """;

            HideLoading();

            if (!await MessageBox.ShowConfirmAsync(
                confirmContent, "자동 배치 확인", "배치 실행", "취소"))
                return;

            // 4. 배치 실행
            ShowLoading("단원을 자동 배치하는 중...");

            var result = await engine.GenerateScheduleAsync(
                _selectedCourse.No, room, startDate, endDate, clearExisting: true);

            Debug.WriteLine($"[GenerateSchedule] Success={result.Success}, TotalPlaced={result.TotalPlaced}, " +
                $"Anchored={result.AnchoredCount}, Filled={result.FilledCount}, Unplaced={result.UnplacedCount}, " +
                $"Slots={result.TotalAvailableSlots}, Message={result.Message}");

            // 5. 결과 표시 (ContentDialog)
            var resultMsg = new StringBuilder();
            resultMsg.AppendLine(result.Message);
            resultMsg.AppendLine($"고정 배치: {result.AnchoredCount}개, 순차 배치: {result.FilledCount}개");

            if (result.UnplacedCount > 0)
            {
                resultMsg.AppendLine($"미배치: {result.UnplacedCount}개 " +
                    $"({string.Join(", ", result.UnplacedSections.Take(3).Select(s => s.SectionName))})");
            }

            if (result.AnchorFailures.Count > 0)
            {
                foreach (var failure in result.AnchorFailures.Where(f => f.IsWarning))
                {
                    resultMsg.AppendLine($"⚠️ {failure.Section?.SectionName}: {failure.Reason}");
                }
            }

            resultMsg.AppendLine($"시수: {result.RequiredHours}필요 / {result.AvailableHours}가용 (여유 {result.ExcessHours})");

            await MessageBox.ShowAsync(resultMsg.ToString().TrimEnd(),
                result.Success ? "배치 완료" : "배치 결과");

            // 6. 배치 결과를 ListView에 표시
            await LoadPlacementResultsAsync(_selectedCourse.No, room);

            // Undo/Redo 버튼 상태 갱신
            await RefreshUndoRedoButtonsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 자동 배치 실패: {ex.Message}");
            ShowSectionError($"자동 배치 중 오류가 발생했습니다.\n{ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// 배치 결과를 DB에서 읽어 UnitPlanListView에 표시
    /// 날짜 → 단원 형태로 주차별 그룹화
    /// </summary>
    private async Task LoadPlacementResultsAsync(int courseNo, string room)
    {
        try
        {
            Debug.WriteLine($"[LoadPlacement] === 시작 === courseNo={courseNo}, room='{room}'");

            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);
            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);

            var schedules = await scheduleRepo.GetByCourseAndRoomAsync(courseNo, room);
            schedules = schedules.OrderBy(s => s.Date).ThenBy(s => s.Period).ToList();

            Debug.WriteLine($"[LoadPlacement] 스케줄 조회: {schedules.Count}개");
            foreach (var s in schedules.Take(5))
            {
                Debug.WriteLine($"  Schedule No={s.No}, Date={s.Date:yyyy-MM-dd}, Period={s.Period}, Room='{s.Room}'");
            }

            if (schedules.Count == 0)
            {
                UnitPlanListView.ItemsSource = null;
                Debug.WriteLine($"[LoadPlacement] 배치된 스케줄이 없습니다.");
                return;
            }

            // 단원 정보 로드
            var allSections = await sectionRepo.GetByCourseAsync(courseNo);
            var sectionDict = allSections.ToDictionary(s => s.No);

            // 단원별 누적 카운터 (진도 표시용: 1/3, 2/3, 3/3)
            var sectionProgress = new Dictionary<int, int>();

            // 학기 시작일 기준 주차 계산
            var semesterStart = schedules.First().Date;
            var displayItems = new List<PlacementDisplayItem>();
            int currentWeek = -1;

            // 주차별 그룹화를 위한 임시 수집
            var weekSchedules = new List<(Schedule schedule, List<ScheduleUnitMap> maps)>();

            int totalMaps = 0;
            foreach (var schedule in schedules)
            {
                var maps = await mapRepo.GetByScheduleWithSectionAsync(schedule.No);
                totalMaps += maps.Count;
                weekSchedules.Add((schedule, maps));
            }
            Debug.WriteLine($"[LoadPlacement] 전체 매핑: {totalMaps}개 (schedules={schedules.Count})");

            // 주차별 그룹화
            var weekGroups = weekSchedules
                .GroupBy(ws => GetWeekNumber(ws.schedule.Date, semesterStart))
                .OrderBy(g => g.Key);

            foreach (var weekGroup in weekGroups)
            {
                int weekNum = weekGroup.Key;
                var weekDates = weekGroup.Select(ws => ws.schedule.Date).Distinct().OrderBy(d => d).ToList();
                string weekRange = weekDates.Count > 1
                    ? $"{weekDates.First():M/d}~{weekDates.Last():M/d}"
                    : $"{weekDates.First():M/d}";

                // 주차 헤더
                displayItems.Add(new PlacementDisplayItem
                {
                    IsHeader = true,
                    WeekNumber = weekNum,
                    WeekRange = weekRange,
                    WeekSlotCount = weekGroup.Count()
                });

                // 해당 주차의 수업 슬롯
                foreach (var (schedule, maps) in weekGroup.OrderBy(ws => ws.schedule.Date).ThenBy(ws => ws.schedule.Period))
                {
                    foreach (var map in maps)
                    {
                        var section = sectionDict.GetValueOrDefault(map.CourseSectionId);
                        if (section == null) continue;

                        // 누적 카운트 증가
                        sectionProgress[section.No] = sectionProgress.GetValueOrDefault(section.No) + 1;
                        int current = sectionProgress[section.No];
                        int total = section.EstimatedHours;

                        displayItems.Add(new PlacementDisplayItem
                        {
                            IsHeader = false,
                            Date = schedule.Date,
                            Period = schedule.Period,
                            SectionName = section.SectionName,
                            SectionType = section.SectionType,
                            ProgressDisplay = total > 1 ? $"({current}/{total})" : "",
                            IsPinned = section.IsPinned,
                            ScheduleNo = schedule.No,
                            SectionNo = section.No,
                            MapNo = map.No
                        });
                    }
                }
            }

            UnitPlanListView.ItemsSource = displayItems;
            Debug.WriteLine($"[AnnualLessonPlanPage] 배치 결과 로드: {displayItems.Count}항목 ({schedules.Count}슬롯)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 배치 결과 로드 실패: {ex.Message}");
        }
    }

    #region 수동 편집

    /// <summary>
    /// 배치 항목 선택 변경 — 편집 버튼 활성화
    /// </summary>
    private void OnPlacementSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool isSlotSelected = UnitPlanListView.SelectedItem is PlacementDisplayItem item && !item.IsHeader;
        BtnChangeUnit.IsEnabled = isSlotSelected;
        BtnRemoveUnit.IsEnabled = isSlotSelected;
    }

    /// <summary>
    /// 배치 항목 우클릭 — 컨텍스트 메뉴 표시
    /// </summary>
    private void OnPlacementItemRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (UnitPlanListView.SelectedItem is PlacementDisplayItem item && !item.IsHeader)
        {
            BtnChangeUnit.IsEnabled = true;
            BtnRemoveUnit.IsEnabled = true;
        }
        else
        {
            BtnChangeUnit.IsEnabled = false;
            BtnRemoveUnit.IsEnabled = false;
        }
    }

    /// <summary>
    /// 단원 변경 — 선택된 슬롯의 단원을 다른 단원으로 교체
    /// </summary>
    private async void OnChangeUnitClick(object sender, RoutedEventArgs e)
    {
        if (UnitPlanListView.SelectedItem is not PlacementDisplayItem item || item.IsHeader) return;
        if (_selectedCourse == null || CmbRoom.SelectedItem == null) return;

        // 단원 선택 대화상자
        var sectionPicker = new ComboBox
        {
            PlaceholderText = "단원 선택",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = _courseSections.Select(s => s.SectionName).ToList(),
            Width = 350
        };

        // 현재 단원 선택
        var currentIdx = _courseSections.ToList().FindIndex(s => s.No == item.SectionNo);
        if (currentIdx >= 0) sectionPicker.SelectedIndex = currentIdx;

        var dialog = new ContentDialog
        {
            Title = $"단원 변경 - {item.DateDisplay} {item.PeriodDisplay}",
            Content = sectionPicker,
            PrimaryButtonText = "변경",
            CloseButtonText = "취소",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (sectionPicker.SelectedIndex < 0) return;

        var newSection = _courseSections[sectionPicker.SelectedIndex];
        if (newSection.No == item.SectionNo) return; // 변경 없음

        try
        {
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);

            // 기존 매핑 삭제 후 새 매핑 생성
            await mapRepo.DeleteAsync(item.MapNo);
            await mapRepo.AddUnitToScheduleAsync(item.ScheduleNo, newSection.No);

            Debug.WriteLine($"[수동편집] 단원 변경: Schedule={item.ScheduleNo}, {item.SectionName} → {newSection.SectionName}");

            // 리스트 새로고침
            string room = CmbRoom.SelectedItem.ToString()!;
            await LoadPlacementResultsAsync(_selectedCourse.No, room);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[수동편집] 단원 변경 실패: {ex.Message}");
            ShowSectionError($"단원 변경 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    /// <summary>
    /// 배치 삭제 — 선택된 슬롯에서 단원 제거
    /// </summary>
    private async void OnRemoveUnitClick(object sender, RoutedEventArgs e)
    {
        if (UnitPlanListView.SelectedItem is not PlacementDisplayItem item || item.IsHeader) return;
        if (_selectedCourse == null || CmbRoom.SelectedItem == null) return;

        if (!await MessageBox.ShowConfirmAsync(
            $"{item.DateDisplay} {item.PeriodDisplay}\n'{item.SectionName}' 배치를 삭제하시겠습니까?",
            "배치 삭제", "삭제", "취소")) return;

        try
        {
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);
            await mapRepo.DeleteAsync(item.MapNo);

            Debug.WriteLine($"[수동편집] 배치 삭제: Schedule={item.ScheduleNo}, Section={item.SectionName}");

            // 리스트 새로고침
            string room = CmbRoom.SelectedItem.ToString()!;
            await LoadPlacementResultsAsync(_selectedCourse.No, room);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[수동편집] 배치 삭제 실패: {ex.Message}");
            ShowSectionError($"배치 삭제 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    #endregion

    private static int GetWeekNumber(DateTime date, DateTime startDate)
    {
        return (int)((date.Date - startDate.Date).TotalDays / 7) + 1;
    }

    // OnConfirmClick 제거됨 (v2 리팩토링)
    // SchedulingEngine 기반 배치로 전환되어 구 ViewModel 기반 확정 로직 불필요
    // TODO: 필요 시 SchedulingEngine 결과 기반으로 재구현

    #endregion

    #region Undo/Redo

    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || CmbRoom?.SelectedItem == null)
        {
            ShowSectionError("수업과 학급을 먼저 선택해주세요.");
            return;
        }

        ShowLoading("작업을 취소하는 중...");

        try
        {
            string room = CmbRoom.SelectedItem.ToString()!;

            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);
            using var undoRepo = new UndoHistoryRepository(SchoolDatabase.DbPath);
            using var lessonRepo = new LessonRepository(SchoolDatabase.DbPath);
            using var schoolScheduleRepo = new SchoolScheduleRepository(SchoolDatabase.DbPath);

            var shiftService = new ScheduleShiftService(
                scheduleRepo, mapRepo, undoRepo, lessonRepo, schoolScheduleRepo);

            var result = await shiftService.UndoLastActionAsync(_selectedCourse.No, room);

            await MessageBox.ShowAsync(result.Message,
                result.Success ? "취소 완료" : "취소 실패");

            if (result.Success)
            {
                await RefreshUndoRedoButtonsAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] Undo 실패: {ex.Message}");
            ShowSectionError($"취소 중 오류가 발생했습니다.\n{ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async void OnRedoClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null || CmbRoom?.SelectedItem == null)
        {
            ShowSectionError("수업과 학급을 먼저 선택해주세요.");
            return;
        }

        ShowLoading("작업을 다시 실행하는 중...");

        try
        {
            string room = CmbRoom.SelectedItem.ToString()!;

            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);
            using var undoRepo = new UndoHistoryRepository(SchoolDatabase.DbPath);
            using var lessonRepo = new LessonRepository(SchoolDatabase.DbPath);
            using var schoolScheduleRepo = new SchoolScheduleRepository(SchoolDatabase.DbPath);

            var shiftService = new ScheduleShiftService(
                scheduleRepo, mapRepo, undoRepo, lessonRepo, schoolScheduleRepo);

            var result = await shiftService.RedoLastActionAsync(_selectedCourse.No, room);

            await MessageBox.ShowAsync(result.Message,
                result.Success ? "다시 실행 완료" : "다시 실행 실패");

            if (result.Success)
            {
                await RefreshUndoRedoButtonsAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] Redo 실패: {ex.Message}");
            ShowSectionError($"다시 실행 중 오류가 발생했습니다.\n{ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async Task RefreshUndoRedoButtonsAsync()
    {
        if (_selectedCourse == null || CmbRoom?.SelectedItem == null)
        {
            BtnUndo.IsEnabled = false;
            BtnRedo.IsEnabled = false;
            return;
        }

        try
        {
            string room = CmbRoom.SelectedItem.ToString()!;

            using var undoRepo = new UndoHistoryRepository(SchoolDatabase.DbPath);
            BtnUndo.IsEnabled = await undoRepo.CanUndoAsync(_selectedCourse.No, room);
            BtnRedo.IsEnabled = await undoRepo.CanRedoAsync(_selectedCourse.No, room);
        }
        catch
        {
            BtnUndo.IsEnabled = false;
            BtnRedo.IsEnabled = false;
        }
    }

    #endregion

    #region Excel Export

    private async void OnExportExcelClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCourse == null)
        {
            ShowSectionError("먼저 수업을 선택해주세요.");
            return;
        }

        if (_courseSections.Count == 0)
        {
            ShowSectionError("내보낼 단원이 없습니다.");
            return;
        }

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = $"연간수업계획_{_selectedCourse.Subject}_{DateTime.Now:yyyyMMdd}";
            picker.FileTypeChoices.Add("Excel 파일", new List<string> { ".xlsx" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            ShowLoading("엑셀 내보내는 중...");

            using var sectionRepo = new CourseSectionRepository(SchoolDatabase.DbPath);
            using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
            using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);
            using var progressRepo = new LessonProgressRepository(SchoolDatabase.DbPath);

            var exportService = new ReportExportService(sectionRepo, scheduleRepo, mapRepo, progressRepo);

            var result = await exportService.ExportYearPlanToExcelAsync(
                _selectedCourse,
                file.Path,
                Settings.WorkYear.Value,
                Settings.WorkSemester.Value);

            if (result.Success)
            {
                await MessageBox.ShowAsync(result.Message, "내보내기 완료");
            }
            else
            {
                ShowSectionError(result.Message);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnnualLessonPlanPage] 엑셀 내보내기 실패: {ex.Message}");
            ShowSectionError($"엑셀 내보내기 중 오류가 발생했습니다.\n{ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    #endregion

}

/// <summary>
/// 주차별 학급 시수 행 데이터
/// </summary>
public class WeeklyClassHoursRow
{
    public int Week { get; set; }
    public string WeekDisplay { get; set; } = string.Empty;
    public string DateRange { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int ClassDaysCount { get; set; }
    public List<DateTime> ClassDays { get; set; } = [];
    public Dictionary<string, int> ClassHours { get; set; } = [];
    /// <summary>
    /// 사용자가 수정한 시수 (null이면 자동 계산값 사용)
    /// </summary>
    public Dictionary<string, int?> ManualHours { get; set; } = [];
    public string Remark { get; set; } = string.Empty;

    /// <summary>
    /// 특정 학급의 실제 사용할 시수 반환 (수동 > 자동)
    /// </summary>
    public int GetEffectiveHours(string className)
    {
        if (ManualHours.TryGetValue(className, out var manual) && manual.HasValue)
            return manual.Value;
        return ClassHours.TryGetValue(className, out var auto) ? auto : 0;
    }
}

/// <summary>
/// 시수 셀 데이터
/// </summary>
public class HoursCellData
{
    public WeeklyClassHoursRow Row { get; set; } = null!;
    public string ClassName { get; set; } = string.Empty;
}

/// <summary>
/// 배치 결과 표시용 항목 (날짜 → 단원)
/// </summary>
public class PlacementDisplayItem
{
    /// <summary>표시 유형: Header(주차 구분선) / Item(수업 슬롯)</summary>
    public bool IsHeader { get; set; }

    /// <summary>수업 슬롯 여부 (Header가 아닌 경우)</summary>
    public bool IsSlot => !IsHeader;

    // x:Bind에서 Visibility 바인딩용
    public Visibility HeaderVisibility => IsHeader ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SlotVisibility => IsSlot ? Visibility.Visible : Visibility.Collapsed;

    // 주차 헤더용
    public int WeekNumber { get; set; }
    public string WeekRange { get; set; } = string.Empty;
    public int WeekSlotCount { get; set; }

    // 수업 슬롯용
    public DateTime Date { get; set; }
    public int Period { get; set; }
    public string SectionName { get; set; } = string.Empty;
    public string SectionType { get; set; } = string.Empty;
    public string ProgressDisplay { get; set; } = string.Empty;
    public bool IsPinned { get; set; }

    // DB 참조 (수동 편집용)
    public int ScheduleNo { get; set; }
    public int SectionNo { get; set; }
    public int MapNo { get; set; }

    // 표시 속성
    public string DateDisplay => IsHeader ? "" : $"{Date:M/d}({DayName})";
    public string PeriodDisplay => IsHeader ? "" : $"{Period}교시";
    public string HeaderDisplay => IsHeader ? $"[{WeekNumber}주차] {WeekRange}  ({WeekSlotCount}차시)" : "";
    public string TypeIcon => SectionType switch
    {
        "Exam" => "\uE82D",        // 지필고사
        "Assessment" => "\uE8A1",   // 수행평가
        "Event" => "\uE8E3",        // 행사
        _ => "\uE7C3"               // 일반
    };

    private string DayName => Date.DayOfWeek switch
    {
        DayOfWeek.Monday => "월",
        DayOfWeek.Tuesday => "화",
        DayOfWeek.Wednesday => "수",
        DayOfWeek.Thursday => "목",
        DayOfWeek.Friday => "금",
        DayOfWeek.Saturday => "토",
        DayOfWeek.Sunday => "일",
        _ => ""
    };
}
