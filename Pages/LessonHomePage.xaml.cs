using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 수업 홈 페이지 (대시보드형)
/// - 좌측: 시간표 + 메모 + 할일
/// - 우측: 오늘의 수업 + 최근 수업 기록
/// </summary>
public sealed partial class LessonHomePage : Page
{
    #region Fields

    private List<Course> _courses = [];

    // 오늘의 수업
    private readonly ObservableCollection<TodayLessonItem> _todayLessons = [];

    #endregion

    #region Constructor

    public LessonHomePage()
    {
        InitializeComponent();
        TodayLessonRepeater.ItemsSource = _todayLessons;
        Loaded += LessonHomePage_Loaded;
    }

    #endregion

    #region Page Events

    private async void LessonHomePage_Loaded(object sender, RoutedEventArgs e)
    {
        // 페이지 헤더 날짜 표시
        TxtPageDate.Text = DateTime.Today.ToString("yyyy년 M월 d일 (ddd)");

        await LoadCoursesAsync();
        await LoadTodayLessonsAsync();
        await LoadLessonLogsAsync();
        await LoadTimetableAsync();
        await LoadLessonTasksAsync();
    }

    #endregion

    #region 오늘의 수업

    /// <summary>
    /// 오늘의 수업 목록 로드
    /// </summary>
    private async Task LoadTodayLessonsAsync()
    {
        try
        {
            // 1. 오늘 예정된 수업 (시간표 기반)
            using var lessonSvc = new LessonService();
            var todayLessons = await lessonSvc.GetTodayLessonsAsync();

            // 2. 과목 정보 (Subject 매핑)
            var courseDict = new Dictionary<int, Course>();
            foreach (var c in _courses)
            {
                courseDict[c.No] = c;
            }

            // 3. 오늘 이미 작성된 기록
            using var logSvc = new LessonLogService();
            var todayLogs = await logSvc.GetTodayLessonsAsync();

            // 4. 현재 교시
            int currentPeriod = LessonLogService.GetCurrentPeriod();

            // 5. TodayLessonItem 빌드
            _todayLessons.Clear();
            foreach (var lesson in todayLessons.OrderBy(l => l.Period))
            {
                if (lesson.IsCancelled) continue;

                var subject = courseDict.TryGetValue(lesson.Course, out var course)
                    ? course.Subject : "";

                // 매칭 기록 찾기: 같은 교시 + 같은 학급
                var matchedLog = todayLogs.FirstOrDefault(log =>
                    log.Period == lesson.Period &&
                    log.Grade == lesson.Grade &&
                    log.Class == lesson.Class);

                _todayLessons.Add(new TodayLessonItem(lesson, subject, lesson.Course, matchedLog, currentPeriod));
            }

            // 요약 텍스트
            int total = _todayLessons.Count;
            int completed = _todayLessons.Count(i => i.IsCompleted);
            TxtTodaySummary.Text = total > 0 ? $"{total}시간 중 {completed}건 기록" : "";
            TxtNoLessons.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

            Debug.WriteLine($"[LessonHomePage] 오늘의 수업: {total}건, 기록 완료: {completed}건");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] 오늘의 수업 로드 실패: {ex.Message}");
            TxtNoLessons.Text = "수업 정보를 불러올 수 없습니다.";
            TxtNoLessons.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 오늘의 수업 아이템 클릭 (기록 작성/편집)
    /// </summary>
    private async void TodayLessonItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not TodayLessonItem item) return;

        ContentDialog dialog;

        if (item.ExistingLog != null)
        {
            // 기존 기록 편집
            dialog = new LessonLogEditDialog(item.ExistingLog, item.CourseNo)
            {
                XamlRoot = XamlRoot
            };
        }
        else
        {
            // 새 기록 생성
            dialog = new LessonLogEditDialog(
                Settings.User.Value,
                item.Subject,
                item.Lesson.Room,
                item.Lesson.Grade,
                item.Lesson.Class,
                item.CourseNo,
                item.Lesson.Period)
            {
                XamlRoot = XamlRoot
            };
        }

        _ = await dialog.ShowAsync();

        var editDialog = (LessonLogEditDialog)dialog;
        if (editDialog.IsDeleted || editDialog.ResultLog != null)
        {
            await LoadTodayLessonsAsync();
            await LoadLessonLogsAsync();
        }
    }

    #endregion

    #region 과목 로드 (오늘의 수업 Subject 매핑용)

    /// <summary>
    /// 교사의 과목 목록 로드 (Course → Subject 매핑용)
    /// </summary>
    private async Task LoadCoursesAsync()
    {
        try
        {
            using var courseService = new CourseService();
            _courses = await courseService.GetMyCoursesAsync();
            Debug.WriteLine($"[LessonHomePage] 과목 로드 완료: {_courses.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] 과목 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region 시간표 로드

    private async Task LoadTimetableAsync()
    {
        try
        {
            await Timetable.LoadMyScheduleAsync();
            Debug.WriteLine("[LessonHomePage] 시간표 로드 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] 시간표 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region 할일 목록

    private async Task LoadLessonTasksAsync()
    {
        try
        {
            // 미완료 할일 + 향후 14일만 표시
            await LessonTaskList.LoadByDateRangeAsync(DateTime.Today, days: 14, showCompleted: false);
            Debug.WriteLine("[LessonHomePage] 수업 할일 로드 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] 수업 할일 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region 수업 기록

    /// <summary>
    /// 최근 수업 기록 로드 (전체 — 필터 없음)
    /// </summary>
    private async Task LoadLessonLogsAsync()
    {
        await LessonLogList.LoadAsync();
    }

    /// <summary>
    /// 수업 기록 선택됨 → 편집 다이얼로그
    /// </summary>
    private async void LessonLogList_LessonSelected(object sender, LessonLog lessonLog)
    {
        var dialog = new LessonLogEditDialog(lessonLog)
        {
            XamlRoot = XamlRoot
        };
        _ = await dialog.ShowAsync();

        if (dialog.IsDeleted || dialog.ResultLog != null)
        {
            await LoadLessonLogsAsync();
            await LoadTodayLessonsAsync();
        }
    }

    /// <summary>
    /// 수업 기록 추가 요청 → 다이얼로그 (과목 미지정)
    /// </summary>
    private async void LessonLogList_AddRequested(object sender, EventArgs e)
    {
        // 과목 필터 없으므로 기본 과목으로 생성
        var subject = _courses.Count > 0 ? _courses[0].Subject : "";
        var dialog = new LessonLogEditDialog(
            Settings.User.Value,
            subject,
            string.Empty)
        {
            XamlRoot = XamlRoot
        };
        _ = await dialog.ShowAsync();

        if (dialog.ResultLog != null)
        {
            await LoadLessonLogsAsync();
            await LoadTodayLessonsAsync();
        }
    }

    #endregion
}

/// <summary>
/// 오늘의 수업 아이템 (XAML 바인딩용)
/// </summary>
internal sealed class TodayLessonItem
{
    // 원본 데이터
    public Lesson Lesson { get; }
    public string Subject { get; }
    public int CourseNo { get; }
    public LessonLog? ExistingLog { get; }
    public int CurrentPeriod { get; }

    // 계산 프로퍼티
    public bool IsCompleted => ExistingLog != null;
    public bool IsCurrent => !IsCompleted && Lesson.Period == CurrentPeriod;

    // 바인딩용 프로퍼티
    public string PeriodText => $"{Lesson.Period}교시";
    public string ClassDisplay => Lesson.ClassDisplay;
    public string TopicText => ExistingLog?.Topic ?? "";
    public Visibility HasTopic => IsCompleted && !string.IsNullOrWhiteSpace(ExistingLog?.Topic)
        ? Visibility.Visible : Visibility.Collapsed;

    // 교시 스타일
    public Windows.UI.Text.FontWeight PeriodFontWeight => IsCurrent ? FontWeights.SemiBold : FontWeights.Normal;
    public Brush PeriodForeground => IsCurrent
        ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    // 과목 스타일
    public Brush SubjectForeground => IsCompleted
        ? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    // 행 배경
    public Brush RowBackground => IsCurrent
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x15, 0x42, 0x85, 0xF4))
        : new SolidColorBrush(Colors.Transparent);

    // 상태 버튼
    public string StatusText => IsCompleted ? "완료" : IsCurrent ? "기록" : "예정";

    public Brush StatusForeground
    {
        get
        {
            if (IsCompleted)
                return new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x9D, 0x58));
            if (IsCurrent)
                return new SolidColorBrush(Colors.White);
            return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
    }

    public Brush StatusBackground
    {
        get
        {
            if (IsCompleted)
                return new SolidColorBrush(Windows.UI.Color.FromArgb(0x15, 0x0F, 0x9D, 0x58));
            if (IsCurrent)
                return (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            return new SolidColorBrush(Colors.Transparent);
        }
    }

    public Thickness StatusBorderThickness => IsCompleted ? new(0) : IsCurrent ? new(0) : new(1);

    public TodayLessonItem(Lesson lesson, string subject, int courseNo, LessonLog? existingLog, int currentPeriod)
    {
        Lesson = lesson;
        Subject = subject;
        CourseNo = courseNo;
        ExistingLog = existingLog;
        CurrentPeriod = currentPeriod;
    }
}
