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

    /// <summary>오늘 수업이 없을 때의 안내(XAML 기본값과 같아야 한다)</summary>
    private const string NoLessonsMessage = "오늘은 수업이 없습니다.";

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

        // 섹션 하나가 실패해도 나머지는 보여주되, 실패했다는 사실은 알린다.
        //
        // ⚠ 예전에는 각 로드가 실패를 Debug 로그로만 삼켜서, 과목·시간표·할일을 못 불러와도
        //    화면상 "오늘은 없음"과 구분되지 않았다. 오늘 화면과 같은 방식으로 표면화한다.
        // 순서를 지킨다 — 오늘의 수업이 과목 목록(_courses)에서 과목명을 채운다.
        var failed = new List<string>();

        await SafeLoadAsync("과목", LoadCoursesAsync, failed);
        await SafeLoadAsync("오늘의 수업", LoadTodayLessonsAsync, failed);
        await SafeLoadAsync("최근 수업 일지", LoadJournalsAsync, failed);
        await SafeLoadAsync("내 시간표", LoadTimetableAsync, failed);
        await SafeLoadAsync("수업 할일", LoadLessonTasksAsync, failed);

        // 개설한 수업이 하나도 없으면 빈 화면 대신 다음 할 일을 띄운다.
        // 과목 로드가 실패한 경우(failed 에 "과목")에는 _courses 가 비어 있어도 안내판을
        // 띄우지 않는다 — "아직 안 만든 것" 과 "못 읽은 것" 은 다른 이야기다.
        EmptyState.Visibility = _courses.Count == 0 && !failed.Contains("과목")
            ? Visibility.Visible
            : Visibility.Collapsed;

        ReportFailures(failed);
    }

    /// <summary>
    /// 안내판의 [수업 개설하기] — 수업 관리 화면으로 보낸다.
    /// </summary>
    private void EmptyState_ActionInvoked(object sender, EventArgs e)
    {
        MainWindow.NavigateFromPage(this.Frame, typeof(CourseManagementPage), "CourseManagement");
    }

    private static async Task SafeLoadAsync(string name, Func<Task> load, List<string> failed)
    {
        try
        {
            await load();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonHomePage] ✗ {name} 로드 실패: {ex}");
            failed.Add(name);
        }
    }

    private static void ReportFailures(List<string> failed)
    {
        if (failed.Count == 0 || App.MainWindow is not MainWindow main) return;

        main.ShowGlobalWarning(
            "일부 정보를 불러오지 못했습니다",
            $"{string.Join(", ", failed)} — 새로고침하거나 잠시 후 다시 확인해주세요.");
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
            // 1. 오늘 예정된 수업 (평소 시간표)
            using var lessonSvc = new TeacherTimetableService();
            var todayLessons = await lessonSvc.GetTodayLessonsAsync();

            // 2. 과목 정보 (Subject 매핑)
            var courseDict = new Dictionary<int, Course>();
            foreach (var c in _courses)
            {
                courseDict[c.No] = c;
            }

            // 3. 그날만 걸리는 변경(휴강·교체·보강·대강)을 얹는다.
            //
            // ⚠ 예전에는 이 단계가 없어서 평소 시간표를 그대로 세웠다. 휴강한 수업이
            //    '예정' 으로 남고 아래 "N시간 중 M건" 의 N 에도 들어갔으며, 보강은 아예
            //    나오지 않았다. 바로 옆 [내 시간표] 카드는 변경을 얹고 있었으므로
            //    한 화면이 같은 질문에 두 답을 내놓고 있었다.
            int dayOfWeek = Helpers.SchoolCalendar.ToLessonDayOfWeek(DateTime.Today);
            var slots = todayLessons
                .OrderBy(l => l.Period)
                .Select(l => new TimetableItemViewModel
                {
                    DayOfWeek = dayOfWeek,
                    Period = l.Period,
                    CourseNo = l.Course,
                    SubjectName = courseDict.TryGetValue(l.Course, out var c) ? c.Subject : "",
                    Room = l.Room,
                    IsEmpty = false,
                })
                .ToList();

            slots = await TeacherTimetableService.ApplyDayChangesAsync(slots, DateTime.Today);

            // 4. 오늘 이미 써 둔 수업 일지 (교시별)
            var todayJournals = await LoadTodayJournalsAsync(DateTime.Today);

            // 5. 현재 교시 — 학교 교시 설정을 따르는 계산을 오늘 화면과 함께 쓴다
            int currentPeriod = Functions.GetPeriodNow().Index;

            // 6. TodayLessonItem 빌드
            _todayLessons.Clear();
            foreach (var slot in slots)
            {
                todayJournals.TryGetValue(slot.Period, out var journal);
                _todayLessons.Add(new TodayLessonItem(slot, journal, currentPeriod));
            }

            // 요약 텍스트 — 휴강은 "적을 수업" 이 아니므로 분모에서 뺀다.
            int total = _todayLessons.Count(i => !i.IsCancelled);
            int completed = _todayLessons.Count(i => i.IsCompleted);
            TxtTodaySummary.Text = total > 0 ? $"{total}시간 중 {completed}건 기록" : "";

            // 지난번 실패로 갈아 끼운 문구를 되돌린다 — 아니면 정말 수업이 없는 날에도
            // "수업 정보를 불러올 수 없습니다" 가 계속 남는다.
            // 안내 문구는 <b>줄이 하나도 없을 때</b> 띄운다. 위 total 은 휴강을 뺀 수라서,
            // 전 교시가 휴강인 날 그걸로 판단하면 휴강 줄이 그려진 채로 "수업이 없습니다"
            // 가 겹쳐 뜬다.
            TxtNoLessons.Text = NoLessonsMessage;
            TxtNoLessons.Visibility = _todayLessons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            Debug.WriteLine($"[LessonHomePage] 오늘의 수업: {_todayLessons.Count}줄(휴강 제외 {total}시간), 기록 완료: {completed}건");
        }
        catch
        {
            // 자리 안내 문구는 여기서 갈아 끼우고, 실패 자체는 호출부(SafeLoadAsync)가
            // 모아서 알린다 — 카드 하나가 비었다는 사실이 전역 안내와 어긋나면 안 된다.
            TxtNoLessons.Text = "수업 정보를 불러올 수 없습니다.";
            TxtNoLessons.Visibility = Visibility.Visible;
            throw;
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

        // 휴강 칸은 버튼 자체가 꺼져 있지만, 키보드 경로로 들어올 수 있어 여기서도 막는다.
        if (!item.IsActionable) return;

        bool saved = item.ExistingPost != null
            ? await LessonJournalComposer.OpenPostAsync(item.ExistingPost.No)
            : await LessonJournalComposer.ComposeAsync(new LessonSlotSeed(
                DateTime.Today,
                item.Period,
                item.CourseNo,
                item.Subject,
                item.ClassDisplay));

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
        // 호출부가 전부 async void 라 예외가 새어 나가면 앱이 그대로 죽는다 —
        // 로드 실패는 여기서도 모아서 알린다.
        var failed = new List<string>();

        await SafeLoadAsync("최근 수업 일지", LoadJournalsAsync, failed);
        await SafeLoadAsync("오늘의 수업", LoadTodayLessonsAsync, failed);

        ReportFailures(failed);
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
        using var courseService = new CourseService();
        _courses = await courseService.GetMyCoursesAsync();
        Debug.WriteLine($"[LessonHomePage] 과목 로드 완료: {_courses.Count}개");
    }

    #endregion

    #region 시간표 로드

    private static DateTime MondayOf(DateTime date) => DateTimeHelper.MondayOf(date);

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

        // 한 칸도 없으면 빈 격자 대신 어디서 넣는지 안내한다 —
        // 격자만 남으면 아직 안 넣은 것인지 못 읽어 온 것인지 알 수 없다.
        bool hasLesson = Timetable.HasAnyLesson;
        Timetable.Visibility = hasLesson ? Visibility.Visible : Visibility.Collapsed;
        TxtNoTimetable.Visibility = hasLesson ? Visibility.Collapsed : Visibility.Visible;

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
        await LoadWeekAsync(_weekMonday);
        Debug.WriteLine("[LessonHomePage] 시간표 로드 완료");
    }

    #endregion

    #region 할일 목록

    private async Task LoadLessonTasksAsync()
    {
        // 미완료 할일 + 향후 14일만 표시
        await LessonTaskList.LoadByDateRangeAsync(DateTime.Today, days: 14, showCompleted: false);
        Debug.WriteLine("[LessonHomePage] 수업 할일 로드 완료");
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
/// 오늘의 수업 아이템 (XAML 바인딩용).
///
/// <para>원본은 <see cref="Lesson"/>(평소 시간표 한 줄)이 아니라 <see cref="Slot"/>
/// (<b>변경까지 얹은</b> 그 날의 한 칸)이다. 예전에는 Lesson 을 그대로 안고 있어서
/// 휴강·교체·보강을 표현할 자리가 아예 없었다 — 보강은 Lesson 이 없으므로 이 목록에
/// 들어올 수조차 없었다.</para>
/// </summary>
internal sealed class TodayLessonItem
{
    /// <summary>그 날 실제 내용 — 평소 시간표에 그 날 변경을 얹은 결과.</summary>
    public TimetableItemViewModel Slot { get; }

    public string Subject => Slot.SubjectName;
    public int CourseNo => Slot.CourseNo;
    public int Period => Slot.Period;

    /// <summary>이 교시에 이미 써 둔 수업 일지(게시글). 없으면 null.</summary>
    public Post? ExistingPost { get; }

    public int CurrentPeriod { get; }

    /// <summary>평소와 무엇이 다른가 (휴강·교체·보강·대강)</summary>
    public LessonChangeKind ChangeKind => Slot.ChangeKind;

    /// <summary>휴강 — 적을 수업이 아니므로 집계에서 빠지고 일지도 쓸 수 없다.</summary>
    public bool IsCancelled => Slot.IsCancelled;

    // 계산 프로퍼티
    public bool IsCompleted => ExistingPost != null;
    public bool IsCurrent => !IsCompleted && !IsCancelled && Period == CurrentPeriod;

    // 바인딩용 프로퍼티
    public string PeriodText => $"{Period}교시";

    /// <summary>표식이 붙은 과목명 (예: "(교)영어") — 오늘 화면·주별 표와 같은 문구를 쓴다.</summary>
    public string SubjectDisplay => Slot.SubjectWithPrefix;

    /// <summary>변경 사유 툴팁. 평소대로면 <c>null</c> 이라 툴팁이 아예 붙지 않는다 —
    /// 빈 문자열을 주면 내용 없는 툴팁 상자가 뜬다.</summary>
    public string? ChangeTooltip => Slot.HasChange ? Slot.ChangeTooltip : null;

    /// <summary>강의실/학급 (예: "5-1", "음악실"). 예전에는 <c>Lesson.ClassDisplay</c> 를
    /// 거쳤는데, 그 삼항이 늘 <c>Room</c> 쪽만 타서 직접 읽는다.</summary>
    public string ClassDisplay => Slot.Room;

    /// <summary>일지 본문 첫 줄 — 머리 정보 다이얼로그가 심어 둔 단원이 대개 여기 걸린다.</summary>
    public string TopicText => LessonJournalListHelpers.Summary(ExistingPost?.PlainText);

    public Visibility HasTopic => TopicText.Length > 0
        ? Visibility.Visible : Visibility.Collapsed;

    // 교시 스타일
    public Windows.UI.Text.FontWeight PeriodFontWeight => IsCurrent ? FontWeights.SemiBold : FontWeights.Normal;
    public Brush PeriodForeground => IsCurrent
        ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    // 과목 스타일 — 휴강은 더 흐리게, 기록을 마친 수업은 한 단계 흐리게.
    public Brush SubjectForeground => (Brush)Application.Current.Resources[
        IsCancelled ? "TextFillColorDisabledBrush"
        : IsCompleted ? "TextFillColorSecondaryBrush"
        : "TextFillColorPrimaryBrush"];

    // 행 배경
    public Brush RowBackground => IsCurrent
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x15, 0x42, 0x85, 0xF4))
        : new SolidColorBrush(Colors.Transparent);

    // 상태 버튼
    public string StatusText => IsCancelled ? "휴강" : IsCompleted ? "완료" : IsCurrent ? "기록" : "예정";

    /// <summary>휴강 칸은 누를 수 없다 — 하지 않은 수업의 일지를 쓸 일은 없다
    /// (<c>TimetableControl</c> 의 시간표 칸과 같은 기준).</summary>
    public bool IsActionable => !IsCancelled;

    public Brush StatusForeground
    {
        get
        {
            if (IsCancelled)
                return (Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
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
            if (IsCompleted && !IsCancelled)
                return new SolidColorBrush(Windows.UI.Color.FromArgb(0x15, 0x0F, 0x9D, 0x58));
            if (IsCurrent)
                return (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            return new SolidColorBrush(Colors.Transparent);
        }
    }

    public Thickness StatusBorderThickness =>
        !IsCancelled && (IsCompleted || IsCurrent) ? new(0) : new(1);

    public TodayLessonItem(TimetableItemViewModel slot, Post? existingPost, int currentPeriod)
    {
        Slot = slot;
        ExistingPost = existingPost;
        CurrentPeriod = currentPeriod;
    }
}
