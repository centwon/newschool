using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 수업 홈 페이지 (대시보드형)
/// - 좌측: 내 시간표 + 할일
/// - 우측: 최근 수업 일지 + 수업 메모
///
/// 내 시간표 칸이 <b>수업 일지 · 진도 · 수업 변경으로 가는 문</b>이다(<c>WeeklyTimetableView</c> 의
/// 칸 메뉴). 일지는 전용 창에서 쓰고 저장된다 — 화면 이동이 없으므로 저장하고 돌아오면
/// 목록과 시간표의 공책 표시를 직접 다시 읽는다.
///
/// <para>예전에는 우측 맨 위에 [오늘의 수업] 목록(오늘 수업을 교시 순으로, 일지를 쓴 교시는 완료)이
/// 있었다. 시간표 칸에 공책 표시와 메뉴가 붙으면서 같은 일을 두 번 하게 되어 걷어냈다(2026-09-28).</para>
/// </summary>
public sealed partial class LessonHomePage : Page
{
    #region Fields

    private List<Course> _courses = [];

    /// <summary>내 시간표에서 보고 있는 주의 월요일</summary>
    private DateTime _weekMonday = DefaultWeekMonday(DateTime.Today);

    /// <summary>
    /// 화면을 불러온 날. 켜 둔 채 밤을 넘기면 머리 날짜·시간표의 "오늘" 이 전날에 멈춰 있으므로
    /// 날이 바뀌면 화면을 다시 읽는다(<see cref="OnMinuteTick"/>).
    /// </summary>
    private DateTime _loadedDate = DateTime.Today;

    /// <summary>날짜를 따라가는 1분 타이머(오늘 화면과 같은 방식).</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _minuteTimer;

    #endregion

    #region Constructor

    public LessonHomePage()
    {
        InitializeComponent();
        Loaded += LessonHomePage_Loaded;
        Unloaded += (_, _) => _minuteTimer?.Stop();
    }

    #endregion

    #region Page Events

    private async void LessonHomePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_minuteTimer == null)
        {
            _minuteTimer = DispatcherQueue.CreateTimer();
            _minuteTimer.Interval = TimeSpan.FromMinutes(1);
            // 약하게 잇는다 — 람다로 이으면 수업홈이 수거되지 않는다(TimerTick 주석).
            _minuteTimer.TickWeakly(this, static page => page.OnMinuteTick());
        }
        _minuteTimer.Start();

        await LoadAllAsync();
    }

    /// <summary>1분마다 — 날이 바뀌었으면 화면을 통째로 다시 읽는다.</summary>
    private async void OnMinuteTick()
    {
        try
        {
            if (_loadedDate == DateTime.Today) return;

            // 보던 주가 "처음 열 때의 주" 였으면 새 날의 기본 주로 따라간다.
            // 사용자가 다른 주로 옮겨 둔 것은 그대로 둔다.
            if (_weekMonday == DefaultWeekMonday(_loadedDate))
                _weekMonday = DefaultWeekMonday(DateTime.Today);

            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            // async void — 새면 앱이 죽는다. 다음 틱에 다시 해 본다.
            NewSchool.Logging.Log.Error("LessonHomePage", "날짜 갱신 실패", ex);
        }
    }

    private async Task LoadAllAsync()
    {
        _loadedDate = DateTime.Today;

        // 페이지 헤더 날짜 표시
        TxtPageDate.Text = _loadedDate.ToString("yyyy년 M월 d일 (ddd)");

        // 섹션 하나가 실패해도 나머지는 보여주되, 실패했다는 사실은 알린다.
        //
        // ⚠ 예전에는 각 로드가 실패를 Debug 로그로만 삼켜서, 과목·시간표·할일을 못 불러와도
        //    화면상 "오늘은 없음"과 구분되지 않았다. 오늘 화면과 같은 방식으로 표면화한다.
        // 순서를 지킨다 — 내 시간표가 과목 목록(_courses)으로 수업을 거른다.
        var failed = new List<string>();

        await SafeLoadAsync("과목", LoadCoursesAsync, failed);
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
            // 사용자에게는 이름만 알린다(ReportFailures). 이유는 여기서만 남는다.
            NewSchool.Logging.Log.Error("LessonHomePage", $"{name} 을(를) 불러오지 못했다", ex);
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

    #region 수업 일지 쓰기

    /// <summary>
    /// 목록에서 일지를 쓰거나 고친 뒤 — 창에서 저장하고 돌아오므로 화면 이동이 없다.
    /// 목록과 시간표의 공책 표시를 직접 다시 읽어야 한다.
    /// </summary>
    private async Task RefreshJournalsAsync()
    {
        // 호출부가 전부 async void 라 예외가 새어 나가면 앱이 그대로 죽는다 —
        // 로드 실패는 여기서도 모아서 알린다.
        var failed = new List<string>();

        await SafeLoadAsync("최근 수업 일지", LoadJournalsAsync, failed);
        await SafeLoadAsync("내 시간표", Timetable.RefreshAsync, failed);

        ReportFailures(failed);
    }

    /// <summary>
    /// 내 시간표 칸 메뉴에서 수업 일지를 쓰거나 진도를 표시했다 — 최근 수업 일지 목록을 다시 읽는다
    /// (시간표의 공책 표시는 시간표가 스스로 고친다). 메뉴 자체는 <c>WeeklyTimetableView</c> 가 연다.
    /// </summary>
    private async void Timetable_LessonRecordChanged(object? sender, EventArgs e)
    {
        var failed = new List<string>();
        await SafeLoadAsync("최근 수업 일지", LoadJournalsAsync, failed);
        ReportFailures(failed);
    }

    #endregion

    #region 과목 로드

    /// <summary>
    /// 교사의 과목 목록 로드 — 내 시간표가 이 수업들로 배치를 거른다.
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
    private static DateTime DefaultWeekMonday(DateTime today)
    {
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
    /// 칸을 누르면 수업 일지 · 진도 · 수업 변경 메뉴가 뜬다.
    /// </summary>
    private async Task LoadWeekAsync(DateTime monday)
    {
        _weekMonday = monday;

        await Timetable.LoadAsync(Settings.WorkYear.Value, Settings.WorkSemester.Value, _courses, monday);

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
