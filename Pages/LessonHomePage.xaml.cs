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
using NewSchool.Board;
using NewSchool.Board.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

/// <summary>
/// 수업 홈 페이지 (대시보드형)
/// - 좌측: 시간표 + 메모 + 할일
/// - 우측: 오늘의 수업 + 최근 수업 일지
///
/// 시간표 칸과 오늘의 수업은 <b>수업 일지로 가는 문</b>이다. 여기서는
/// <see cref="LessonJournalComposer"/> 만 부르고, 일지는 전용 창에서 쓰고 저장된다 —
/// 화면 이동이 없으므로 저장하고 돌아오면 목록과 완료 표시를 직접 다시 읽는다.
/// </summary>
public sealed partial class LessonHomePage : Page
{
    #region Fields

    private List<Course> _courses = [];

    /// <summary>내 시간표에서 보고 있는 주의 월요일</summary>
    private DateTime _weekMonday = DefaultWeekMonday();

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
        await LoadJournalsAsync();
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

            // 3. 오늘 이미 써 둔 수업 일지 (교시별)
            var todayJournals = await LoadTodayJournalsAsync(DateTime.Today);

            // 4. 현재 교시 — 학교 교시 설정을 따르는 계산을 오늘 화면과 함께 쓴다
            int currentPeriod = Functions.GetPeriodNow().Index;

            // 5. TodayLessonItem 빌드
            _todayLessons.Clear();
            foreach (var lesson in todayLessons.OrderBy(l => l.Period))
            {
                if (lesson.IsCancelled) continue;

                var subject = courseDict.TryGetValue(lesson.Course, out var course)
                    ? course.Subject : "";

                todayJournals.TryGetValue(lesson.Period, out var journal);

                _todayLessons.Add(new TodayLessonItem(lesson, subject, lesson.Course, journal, currentPeriod));
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
    /// 그 날짜에 써 둔 수업 일지를 교시별로 모은다.
    ///
    /// 게시글에는 날짜·교시를 담을 칸이 없어서 제목 규칙(<see cref="LessonJournalTitle"/>)을
    /// 되읽는다. 제목을 손으로 고친 글은 못 알아보고 그 교시가 다시 '예정'으로 보이는데,
    /// 글이 사라지는 것은 아니고 목록에는 그대로 남는다.
    /// </summary>
    private static async Task<Dictionary<int, Post>> LoadTodayJournalsAsync(DateTime date)
    {
        var byPeriod = new Dictionary<int, Post>();

        try
        {
            // 제목이 "8/21 " 로 시작하는 글만 추린 뒤 교시를 되읽는다.
            using var service = NewSchool.Board.Board.CreateCachedService();
            var page = await service.GetPostsPagedAsync(
                pageNumber: 1,
                pageSize: 50,
                category: LessonJournalComposer.Category,
                subject: LessonJournalComposer.Subject,
                searchTitle: true,
                searchText: $"{date.Month}/{date.Day} ");

            foreach (var post in page.Items)
            {
                // 해가 바뀌면 "8/21" 이 겹치므로 쓴 해까지 본다.
                if (post.DateTime.Year != date.Year) continue;

                int period = LessonJournalTitle.PeriodOf(post.Title, date);
                if (period > 0) byPeriod.TryAdd(period, post);
            }
        }
        catch (Exception ex)
        {
            // 일지를 못 읽었다고 오늘의 수업까지 버리지는 않는다 — 전부 '예정' 으로 보일 뿐이다.
            Debug.WriteLine($"[LessonHomePage] 오늘 수업 일지 조회 실패: {ex.Message}");
        }

        return byPeriod;
    }

    /// <summary>
    /// 오늘의 수업 아이템 클릭 — 써 둔 일지가 있으면 그 글로, 없으면 새 일지 쓰기로.
    /// </summary>
    private async void TodayLessonItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not TodayLessonItem item) return;

        bool saved = item.ExistingPost != null
            ? await LessonJournalComposer.OpenPostAsync(item.ExistingPost.No)
            : await LessonJournalComposer.ComposeAsync(new LessonSlotSeed(
                DateTime.Today,
                item.Lesson.Period,
                item.CourseNo,
                item.Subject,
                item.Lesson.Room));

        if (saved) await RefreshJournalsAsync();
    }

    #endregion

    #region 수업 일지 쓰기

    /// <summary>
    /// 일지를 쓰거나 고친 뒤 — 창에서 저장하고 돌아오므로 화면 이동이 없다.
    /// 목록과 완료 표시를 직접 다시 읽어야 한다.
    /// </summary>
    private async Task RefreshJournalsAsync()
    {
        await LoadJournalsAsync();
        await LoadTodayLessonsAsync();
    }

    /// <summary>
    /// 내 시간표 칸 클릭 — 그 칸의 날짜·교시·교과·강의실로 일지를 시작한다.
    /// 날짜는 <b>보고 있는 주</b>의 그 요일이다(지난 주를 펼쳐 놓고 눌렀으면 그 날짜).
    /// </summary>
    private async void Timetable_SlotInvoked(object sender, TimetableItemViewModel item)
    {
        var date = _weekMonday.AddDays(item.DayOfWeek - 1);

        if (await LessonJournalComposer.ComposeAsync(new LessonSlotSeed(
                date, item.Period, item.CourseNo, item.SubjectName, item.Room)))
        {
            await RefreshJournalsAsync();
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

    private static DateTime MondayOf(DateTime date)
    {
        var monday = date.Date;
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(-1);
        return monday;
    }

    /// <summary>
    /// 처음 열 때 보여줄 주. <b>주말이면 다가오는 주</b>를 연다 —
    /// 일요일에 이미 끝난 주를 펼쳐 봐야 쓸모가 없다.
    /// </summary>
    private static DateTime DefaultWeekMonday()
    {
        var today = DateTime.Today;

        return today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? MondayOf(today).AddDays(7)
            : MondayOf(today);
    }

    /// <summary>오늘을 기준으로 그 주가 언제인지 (지난 주 · 이번 주 · 다음 주)</summary>
    private static string RelativeWeekLabel(DateTime monday)
    {
        int weeks = (int)Math.Round((monday - MondayOf(DateTime.Today)).TotalDays / 7);

        return weeks switch
        {
            -1 => " · 지난 주",
            0 => " · 이번 주",
            1 => " · 다음 주",
            _ => ""
        };
    }

    /// <summary>
    /// 그 주 시간표를 그린다 — 평소 시간표에 그 주 변경(휴강·교체·보강·대강)이 얹힌다.
    /// 읽기 전용이다: 변경을 넣고 고치는 곳은 수업 관리의 [주별 시간표 확인 및 변경] 탭이다.
    /// </summary>
    private async Task LoadWeekAsync(DateTime monday)
    {
        _weekMonday = monday;

        await Timetable.LoadMyWeekScheduleAsync(monday);

        bool thisWeek = monday == MondayOf(DateTime.Today);

        // 범위 옆에 늘 "이번 주 / 다음 주 / 지난 주" 를 붙인다 —
        // 날짜만 있으면 그게 어느 주인지 매번 머리로 세어야 한다.
        TxtWeekRange.Text = $"{monday:M/d} ~ {monday.AddDays(4):M/d}{RelativeWeekLabel(monday)}";

        BtnThisWeek.Visibility = thisWeek ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnPreviousWeekClick(object sender, RoutedEventArgs e)
        => await MoveWeekAsync(_weekMonday.AddDays(-7));

    private async void OnNextWeekClick(object sender, RoutedEventArgs e)
        => await MoveWeekAsync(_weekMonday.AddDays(7));

    private async void OnThisWeekClick(object sender, RoutedEventArgs e)
        => await MoveWeekAsync(MondayOf(DateTime.Today));

    private async Task MoveWeekAsync(DateTime monday)
    {
        // async void 핸들러라 여기서 새는 예외는 아무 데도 잡히지 않는다.
        try
        {
            await LoadWeekAsync(monday);
        }
        catch (Exception ex)
        {
            await Controls.UserErrorReporter.ReportAsync("주 이동", ex);
        }
    }

    private async Task LoadTimetableAsync()
    {
        try
        {
            await LoadWeekAsync(_weekMonday);
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

    #region 최근 수업 일지

    /// <summary>
    /// 최근 수업 일지 로드 (게시판의 수업일지 글)
    /// </summary>
    private async Task LoadJournalsAsync()
    {
        await JournalList.LoadAsync();
    }

    /// <summary>
    /// 목록에서 고른 일지를 같은 창으로 연다.
    /// </summary>
    private async void JournalList_PostSelected(object sender, int postNo)
    {
        if (await LessonJournalComposer.OpenPostAsync(postNo))
            await RefreshJournalsAsync();
    }

    /// <summary>
    /// 카드의 + 버튼 — 수업 칸을 거치지 않고 바로 쓴다.
    /// 오늘·지금 교시를 시작값으로 주고 교과는 창이 첫 교과로 채운다.
    /// </summary>
    private async void JournalList_AddRequested(object sender, EventArgs e)
    {
        if (await LessonJournalComposer.ComposeAsync(new LessonSlotSeed(
                DateTime.Today, Functions.GetPeriodNow().Index, CourseNo: 0, Subject: "", Room: "")))
        {
            await RefreshJournalsAsync();
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

    /// <summary>이 교시에 이미 써 둔 수업 일지(게시글). 없으면 null.</summary>
    public Post? ExistingPost { get; }

    public int CurrentPeriod { get; }

    // 계산 프로퍼티
    public bool IsCompleted => ExistingPost != null;
    public bool IsCurrent => !IsCompleted && Lesson.Period == CurrentPeriod;

    // 바인딩용 프로퍼티
    public string PeriodText => $"{Lesson.Period}교시";
    public string ClassDisplay => Lesson.ClassDisplay;

    /// <summary>일지 본문 첫 줄 — 머리 정보 다이얼로그가 심어 둔 단원이 대개 여기 걸린다.</summary>
    public string TopicText => LessonJournalListHelpers.Summary(ExistingPost?.PlainText);

    public Visibility HasTopic => TopicText.Length > 0
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

    public TodayLessonItem(Lesson lesson, string subject, int courseNo, Post? existingPost, int currentPeriod)
    {
        Lesson = lesson;
        Subject = subject;
        CourseNo = courseNo;
        ExistingPost = existingPost;
        CurrentPeriod = currentPeriod;
    }
}
